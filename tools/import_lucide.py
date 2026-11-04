"""Imports Lucide icons (ISC licence) as WPF stroke geometries.

Usage: python -I tools/import_lucide.py
Downloads one pinned lucide-static package and reads only known SVG members, converts
<path>, <circle>, <rect>, <line>, <polyline>, <polygon> and <ellipse> into path markup, and writes
src/DynamicIsland.UI/Icons/Lucide.xaml. Icons are 24x24, drawn with a round-capped stroke by the
Icon control. Swap the whole icon set by regenerating this file (see IconProvider).
"""
import io
import json
import re
import tarfile
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

VERSION = "1.61.0"
PACKAGE_URL = f"https://registry.npmjs.org/lucide-static/-/lucide-static-{VERSION}.tgz"
ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "DynamicIsland.UI" / "Icons" / "Lucide.xaml"

# Icon key (used in code) -> Lucide name.
ICONS = {
    "Play": "play",
    "Pause": "pause",
    "Previous": "skip-back",
    "Next": "skip-forward",
    "Repeat": "repeat",
    "Repeat1": "repeat-1",
    "Shuffle": "shuffle",
    "ChevronLeft": "chevron-left",
    "ChevronRight": "chevron-right",
    "Battery": "battery",
    "BatteryCharging": "battery-charging",
    "Zap": "zap",
    "Target": "target",
    "Timer": "timer",
    "Coffee": "coffee",
    "Brain": "brain",
    "Reset": "rotate-ccw",
    "Droplet": "droplet",
    "GlassWater": "glass-water",
    "Undo": "undo-2",
    "Bell": "bell",
    "BellOff": "bell-off",
    "Plus": "plus",
    "Minus": "minus",
    "Check": "check",
    "CircleCheck": "circle-check",
    "Circle": "circle",
    "Close": "x",
    "Trash": "trash-2",
    "Pencil": "pencil",
    "GripVertical": "grip-vertical",
    "ListTodo": "list-todo",
    "ClipboardList": "clipboard-list",
    "Inbox": "inbox",
    "Upload": "upload",
    "File": "file",
    "Folder": "folder",
    "FolderOpen": "folder-open",
    "ExternalLink": "external-link",
    "Copy": "copy",
    "Image": "image",
    "FileText": "file-text",
    "Music": "music",
    "Headphones": "headphones",
    "Settings": "settings",
    "SlidersHorizontal": "sliders-horizontal",
    "Mic": "mic",
    "Video": "video",
    "Camera": "camera",
    "Monitor": "monitor",
    "ScreenShare": "screen-share",
    "Info": "info",
    "Keyboard": "keyboard",
    "Languages": "languages",
    "Power": "power",
    "RefreshCw": "refresh-cw",
    "GitBranch": "git-branch",
    "Download": "download",
    "LayoutGrid": "layout-grid",
    "Sparkles": "sparkles",
    "Globe": "globe",
    "Record": "circle-dot",
    "Stop": "square",
    "AudioLines": "audio-lines",
    "Cpu": "cpu",
    "Activity": "activity",
    "Gauge": "gauge",
    "BellRing": "bell-ring",
    "Wrench": "wrench",
    "Volume": "volume-2",
    "MicOff": "mic-off",
    "Cast": "cast",
}

NUM = r"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?"


def fmt(v: float) -> str:
    s = f"{v:.3f}".rstrip("0").rstrip(".")
    return s if s not in ("-0", "") else "0"


def f(el, name, default=0.0):
    return float(el.get(name, default))


def circle(cx, cy, rx, ry):
    # Two arcs make a full ellipse.
    return (f"M{fmt(cx - rx)},{fmt(cy)} A{fmt(rx)},{fmt(ry)} 0 1 0 {fmt(cx + rx)},{fmt(cy)} "
            f"A{fmt(rx)},{fmt(ry)} 0 1 0 {fmt(cx - rx)},{fmt(cy)} Z")


def rect(el):
    x, y, w, h = f(el, "x"), f(el, "y"), f(el, "width"), f(el, "height")
    rx = float(el.get("rx", el.get("ry", 0)))
    ry = float(el.get("ry", rx))
    if rx <= 0:
        return f"M{fmt(x)},{fmt(y)} H{fmt(x + w)} V{fmt(y + h)} H{fmt(x)} Z"
    return (f"M{fmt(x + rx)},{fmt(y)} H{fmt(x + w - rx)} A{fmt(rx)},{fmt(ry)} 0 0 1 {fmt(x + w)},{fmt(y + ry)} "
            f"V{fmt(y + h - ry)} A{fmt(rx)},{fmt(ry)} 0 0 1 {fmt(x + w - rx)},{fmt(y + h)} "
            f"H{fmt(x + rx)} A{fmt(rx)},{fmt(ry)} 0 0 1 {fmt(x)},{fmt(y + h - ry)} "
            f"V{fmt(y + ry)} A{fmt(rx)},{fmt(ry)} 0 0 1 {fmt(x + rx)},{fmt(y)} Z")


def points(el, close):
    nums = re.findall(NUM, el.get("points", ""))
    pairs = [(nums[i], nums[i + 1]) for i in range(0, len(nums) - 1, 2)]
    d = "M" + " L".join(f"{x},{y}" for x, y in pairs)
    return d + (" Z" if close else "")


def independent_path(data: str) -> str:
    """Reset each SVG path's initial origin while preserving implicit relative lines.

    SVG starts every element at (0, 0), but WPF concatenated path figures share
    the previous current point. Only the *first pair* of an initial relative
    moveto becomes absolute. Subsequent implicit pairs are relative lineto,
    so changing the command letter alone would distort chevrons and crosses.
    Later movetos within this same SVG path keep their original semantics.
    """
    data = data.strip()
    if not data.startswith("m"):
        return data
    # Ignore e/E inside numeric exponents when locating the next path command.
    command = re.search(r"[A-DF-Za-df-z]", data[1:])
    end = command.start() + 1 if command else len(data)
    coordinates = data[1:end]
    numbers = list(re.finditer(NUM, coordinates))
    if len(numbers) < 2 or len(numbers) % 2:
        raise ValueError("Initial relative moveto requires complete coordinate pairs")
    x, y = numbers[:2]
    remainder = coordinates[y.end():].strip(" ,\t\r\n")
    result = f"M{x.group()},{y.group()}"
    if remainder:
        result += " l" + remainder
    return result + data[end:]


def convert(svg_text: str) -> str:
    root = ET.fromstring(svg_text)
    parts = []
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag == "path":
            parts.append(independent_path(el.get("d", "")))
        elif tag == "circle":
            parts.append(circle(f(el, "cx"), f(el, "cy"), f(el, "r"), f(el, "r")))
        elif tag == "ellipse":
            parts.append(circle(f(el, "cx"), f(el, "cy"), f(el, "rx"), f(el, "ry")))
        elif tag == "rect":
            parts.append(rect(el))
        elif tag == "line":
            parts.append(f"M{el.get('x1')},{el.get('y1')} L{el.get('x2')},{el.get('y2')}")
        elif tag == "polyline":
            parts.append(points(el, close=False))
        elif tag == "polygon":
            parts.append(points(el, close=True))
    return " ".join(p.strip() for p in parts if p.strip())


def read_package(archive: bytes) -> dict[str, str]:
    """Read known regular members only; never extract archive paths to disk."""
    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as package:
        def read(member_name: str, limit: int) -> bytes:
            member = package.getmember(member_name)
            if not member.isfile() or member.size > limit:
                raise ValueError(f"Unexpected package member: {member_name}")
            stream = package.extractfile(member)
            if stream is None:
                raise ValueError(f"Missing package member: {member_name}")
            with stream:
                return stream.read(limit + 1)

        metadata = json.loads(read("package/package.json", 65536))
        if metadata.get("name") != "lucide-static" or metadata.get("version") != VERSION:
            raise ValueError("Icon package does not match the pinned name and version")
        return {name: read(f"package/icons/{name}.svg", 1048576).decode("utf-8")
                for name in ICONS.values()}


def download_icons() -> dict[str, str]:
    # A single immutable-version npm archive avoids dozens of serial CDN requests.
    with urllib.request.urlopen(PACKAGE_URL, timeout=60) as response:
        archive = response.read(64 * 1024 * 1024 + 1)
    if len(archive) > 64 * 1024 * 1024:
        raise ValueError("Icon package exceeds the download limit")
    return read_package(archive)


def main() -> None:
    lines = [
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        f"    <!-- Generated by tools/import_lucide.py from lucide-static {VERSION}. Lucide: ISC licence, see THIRD_PARTY_NOTICES.md. -->",
    ]
    svgs = download_icons()
    for key, name in sorted(ICONS.items()):
        data = convert(svgs[name])
        if not data:
            raise SystemExit(f"Empty icon: {name}")
        lines.append(f'    <Geometry x:Key="Icon.{key}">F0 {data}</Geometry>')
    lines.append("</ResourceDictionary>")
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Wrote {len(ICONS)} icons to {OUT}")


if __name__ == "__main__":
    main()
