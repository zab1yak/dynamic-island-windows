"""Generates Strings.resx / Strings.ru.resx from a two-language JSON table.

Usage: python -I tools/gen_resx.py <strings.json> <output folder>
The JSON is {"Key": {"en": "...", "ru": "..."}}. Keeping one table guarantees both
languages always have the same keys.
"""
import json
import sys
from pathlib import Path
from xml.sax.saxutils import escape

HEADER = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
"""


def write(path: Path, table: dict, lang: str) -> None:
    lines = [HEADER]
    for key in sorted(table):
        value = table[key][lang]
        lines.append(f'  <data name="{escape(key)}" xml:space="preserve"><value>{escape(value)}</value></data>\n')
    lines.append("</root>\n")
    path.write_text("".join(lines), encoding="utf-8")


def main() -> None:
    source, out = Path(sys.argv[1]), Path(sys.argv[2])
    table = json.loads(source.read_text(encoding="utf-8"))
    missing = [k for k, v in table.items() if not v.get("en") or not v.get("ru")]
    if missing:
        sys.exit(f"Missing translations: {missing}")
    write(out / "Strings.resx", table, "en")
    write(out / "Strings.ru.resx", table, "ru")


if __name__ == "__main__":
    main()
