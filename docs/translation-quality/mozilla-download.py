"""Research-only Mozilla artifact downloader. Python/zstandard are not product dependencies.
Usage: python mozilla-download.py CACHE_DIR
Downloads pinned official artifacts and validates compressed AND decompressed SHA-256.
"""
import hashlib
import json
from pathlib import Path
import sys
import urllib.request
import zstandard

root = Path(__file__).resolve().parent
lock = json.loads((root / 'mozilla-artifact-lock.json').read_text())
cache = Path(sys.argv[1]); cache.mkdir(parents=True, exist_ok=True)
def checked(data, expected):
    if hashlib.sha256(data).hexdigest() != expected:
        raise ValueError('SHA-256 mismatch')
    return data

def artifact(record, name):
    output = cache / name
    if output.exists():
        checked(output.read_bytes(), record['decompressedHash'])
        return
    attachment = record['attachment']
    with urllib.request.urlopen(lock['attachmentBase'] + attachment['location'], timeout=120) as response:
        compressed = checked(response.read(), attachment['hash'])
    data = zstandard.decompress(compressed, max_output_size=record['decompressedSize'])
    checked(data, record['decompressedHash'])
    if len(data) != record['decompressedSize']:
        raise ValueError('Size mismatch')
    output.write_bytes(data)
    print(name, len(data), flush=True)

for record in lock['models']:
    artifact(record, record['sourceLanguage'] + record['targetLanguage'] + '-' + record['fileType'])
artifact(lock['runtime'], 'bergamot-translator.wasm')
glue = cache / 'bergamot-translator.js'
if glue.exists():
    checked(glue.read_bytes(), lock['glue']['sha256'])
else:
    with urllib.request.urlopen(lock['glue']['url'], timeout=120) as response:
        glue.write_bytes(checked(response.read(), lock['glue']['sha256']))
