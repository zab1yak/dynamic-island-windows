"""Draws the app icon (a black island pill) and writes a multi-size .ico.

Pure Python (zlib + struct), no image libraries. Usage:
    python -I tools/gen_icon.py src/DynamicIsland.App/Assets/island.ico
"""
import struct
import sys
import zlib

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 4  # supersampling per axis


def pill_coverage(x, y, cx, cy, half_w, half_h):
    """Signed distance to a capsule; returns negative inside."""
    r = half_h
    dx = max(abs(x - cx) - (half_w - r), 0.0)
    dy = abs(y - cy)
    return (dx * dx + dy * dy) ** 0.5 - r


def render(size):
    cx, cy = size / 2, size / 2
    half_w, half_h = size * 0.46, size * 0.21
    stroke = max(size / 32, 0.75)
    dot_r = half_h * 0.42
    dot_cx = cx + half_w - half_h
    rows = []
    for py in range(size):
        row = bytearray([0])  # PNG filter: none
        for px in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(SS):
                for sx in range(SS):
                    x = px + (sx + 0.5) / SS
                    y = py + (sy + 0.5) / SS
                    d = pill_coverage(x, y, cx, cy, half_w, half_h)
                    if d > 0:
                        continue
                    if d > -stroke:
                        color = (72, 72, 74)  # outline so the pill reads on a dark taskbar
                    elif ((x - dot_cx) ** 2 + (y - cy) ** 2) ** 0.5 <= dot_r:
                        color = (48, 209, 88)  # iOS green activity dot
                    else:
                        color = (0, 0, 0)
                    acc[0] += color[0]
                    acc[1] += color[1]
                    acc[2] += color[2]
                    acc[3] += 1
            n = acc[3]
            if n:
                row += bytes((round(acc[0] / n), round(acc[1] / n), round(acc[2] / n), round(255 * n / (SS * SS))))
            else:
                row += b"\0\0\0\0"
        rows.append(bytes(row))
    return png(size, b"".join(rows))


def png(size, raw):
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def main():
    images = [render(s) for s in SIZES]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for s, data in zip(SIZES, images):
        dim = 0 if s >= 256 else s
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(sys.argv[1], "wb") as f:
        f.write(header + entries + b"".join(images))


if __name__ == "__main__":
    main()
