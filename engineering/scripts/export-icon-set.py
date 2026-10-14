#!/usr/bin/env python3
"""Lay out Wheelhouse's icon set from the approved brand exports.

Copies four approved PNGs, byte for byte, to product/brand/icons/ under the wow-two product icons convention
(conventions/design/identity/product-icons.md). Each source must match its SHA-256 in product/brand/manifest.json.
Needs only the standard library; use --output-dir for an isolated replay.
"""

import argparse
import hashlib
import json
from pathlib import Path


REPO = Path(__file__).resolve().parents[2]
BRAND = REPO / 'product/brand'

# Icon file -> approved export. Monochrome variants are not derived: the gray lower facet stays in every approved
# variant, so a one-ink export needs a brand decision.
OUTPUTS = {
    'short-colored-light.png': 'wheelhouse-mark.png',
    'short-colored-dark.png': 'wheelhouse-mark-white.png',
    'full-colored-light.png': 'wheelhouse-wordmark.png',
    'full-colored-dark.png': 'wheelhouse-wordmark-white.png',
}


def source(name):
    """Reads an approved export after checking it against the brand manifest."""
    manifest = json.loads((BRAND / 'manifest.json').read_text())
    record = next(entry for entry in manifest['exports'] if entry['file'] == f'soft-folds/{name}')
    data = (BRAND / record['file']).read_bytes()
    if hashlib.sha256(data).hexdigest() != record['sha256']:
        raise SystemExit(f'{BRAND / record["file"]} does not match product/brand/manifest.json')
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', type=Path, default=BRAND / 'icons', help='the folder to write the icons into')
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    for name, export in OUTPUTS.items():
        target = args.output_dir / name
        target.write_bytes(source(export))
        print(f'{name}  {hashlib.sha256(target.read_bytes()).hexdigest()}')


if __name__ == '__main__':
    main()
