"""Extract the official post-2025 commune catalogue from Decision 19/2025/QD-TTg PDFs."""

from __future__ import annotations

import json
import re
import sys
from collections import OrderedDict
from pathlib import Path

import pdfplumber


PROVINCE_CODES = [
    "hanoi", "caobang", "tuyenquang", "dienbien", "laichau", "sonla", "laocai",
    "thainguyen", "langson", "quangninh", "bacninh", "phutho", "haiphong",
    "hungyen", "ninhbinh", "thanhhoa", "nghean", "hatinh", "quangtri", "hue",
    "danang", "quangngai", "gialai", "khanhhoa", "daklak", "lamdong", "dongnai",
    "hochiminh", "tayninh", "dongthap", "vinhlong", "angiang", "cantho", "camau",
]

HEADER = re.compile(r"^\s*\d{2}\.\s+(?:THÀNH PHỐ|TỈNH)\s+(.+?)\s*$")
AREA = re.compile(r"^\s*(\d{5})\s+((Phường|Xã|Đặc khu)\s+.+?)\s*$")


def extract(pdf_paths: list[Path]) -> OrderedDict[str, list[dict[str, str]]]:
    by_province_name: OrderedDict[str, list[dict[str, str]]] = OrderedDict()
    current: str | None = None

    for pdf_path in pdf_paths:
        with pdfplumber.open(pdf_path) as document:
            for page in document.pages:
                for line in (page.extract_text() or "").splitlines():
                    header = HEADER.match(line)
                    if header:
                        current = header.group(1).strip()
                        by_province_name.setdefault(current, [])
                        continue
                    area = AREA.match(line)
                    if area and current:
                        by_province_name[current].append({
                            "code": area.group(1),
                            "name": area.group(2),
                            "type": area.group(3),
                        })

    if len(by_province_name) != 34:
        raise ValueError(f"Expected 34 provinces, extracted {len(by_province_name)}")
    if sum(map(len, by_province_name.values())) != 3321:
        raise ValueError("Expected exactly 3,321 commune-level units")
    if len({area["code"] for areas in by_province_name.values() for area in areas}) != 3321:
        raise ValueError("Administrative area codes must be unique")

    return OrderedDict(zip(PROVINCE_CODES, by_province_name.values(), strict=True))


if __name__ == "__main__":
    if len(sys.argv) < 4:
        raise SystemExit("Usage: extract_administrative_areas.py PART1.pdf PART2.pdf OUTPUT.json")
    result = extract([Path(value) for value in sys.argv[1:-1]])
    output = Path(sys.argv[-1])
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"Wrote {sum(map(len, result.values()))} areas across {len(result)} provinces to {output}")
