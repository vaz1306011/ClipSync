import re
import sys
from pathlib import Path

import qrcode
from PIL import Image
from qrcode.constants import ERROR_CORRECT_M

ROOT = Path(__file__).resolve().parent.parent
README = ROOT / "README.md"
OUTPUT = ROOT / "docs" / "shortcut-qr.png"
URL_PATTERN = re.compile(r"https://www\.icloud\.com/shortcuts/[0-9a-f]+")

match = URL_PATTERN.search(README.read_text(encoding="utf-8"))
if not match:
    sys.exit("README.md に iCloud ショートカットの URL が見つかりません")

image = (
    qrcode.make(
        match.group(0), box_size=10, border=4, error_correction=ERROR_CORRECT_M
    )
    .get_image()
    .convert("L")
)

# PNG のバイト列はライブラリのバージョンで変わるため、見た目が同じなら書き込まない
if OUTPUT.exists():
    with Image.open(OUTPUT) as existing:
        if existing.size == image.size and existing.convert("L").tobytes() == image.tobytes():
            print(f"{OUTPUT.relative_to(ROOT)} is up to date")
            sys.exit(0)

OUTPUT.parent.mkdir(exist_ok=True)
image.save(OUTPUT)
print(f"Generated {OUTPUT.relative_to(ROOT)} for {match.group(0)}")
