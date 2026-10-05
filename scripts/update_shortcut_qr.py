import re
import sys
from pathlib import Path

import qrcode
from qrcode.constants import ERROR_CORRECT_M

ROOT = Path(__file__).resolve().parent.parent
README = ROOT / "README.md"
OUTPUT = ROOT / "docs" / "shortcut-qr.png"
URL_PATTERN = re.compile(r"https://www\.icloud\.com/shortcuts/[0-9a-f]+")

match = URL_PATTERN.search(README.read_text(encoding="utf-8"))
if not match:
    sys.exit("README.md に iCloud ショートカットの URL が見つかりません")

OUTPUT.parent.mkdir(exist_ok=True)
qrcode.make(
    match.group(0), box_size=10, border=4, error_correction=ERROR_CORRECT_M
).save(OUTPUT)
print(f"Generated {OUTPUT.relative_to(ROOT)} for {match.group(0)}")
