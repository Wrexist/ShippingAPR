#!/usr/bin/env python3
"""Add localization keys to Strings.resx, Strings.sv.resx and Strings.Designer.cs.

Reads a JSON object {key: [english, swedish], ...} from stdin. Idempotent:
keys already present in a file are skipped, so re-running is safe.
"""
import sys, json, html, pathlib

RES = pathlib.Path("/home/user/ShippingAPR/src/ShippingAPR.App/Resources")
EN = RES / "Strings.resx"
SV = RES / "Strings.sv.resx"
DESIGNER = RES / "Strings.Designer.cs"

KEYS = json.load(sys.stdin)

def add_resx(path, idx):
    text = path.read_text(encoding="utf-8")
    additions = []
    for key, vals in KEYS.items():
        if f'name="{key}">' in text:
            continue
        val = html.escape(vals[idx], quote=False)
        additions.append(f'  <data name="{key}"><value>{val}</value></data>')
    if not additions:
        return 0
    text = text.replace("</root>", "\n".join(additions) + "\n</root>", 1)
    path.write_text(text, encoding="utf-8")
    return len(additions)

def add_designer():
    text = DESIGNER.read_text(encoding="utf-8")
    additions = []
    for key in KEYS:
        if f'GetString("{key}")' in text or f' {key} =>' in text:
            continue
        additions.append(f'    public static string {key} => GetString("{key}");')
    if not additions:
        return 0
    assert text.rstrip().endswith("}")
    text = text.rstrip()[:-1].rstrip() + "\n\n" + "\n".join(additions) + "\n}\n"
    DESIGNER.write_text(text, encoding="utf-8")
    return len(additions)

print(f"Added: en={add_resx(EN, 0)} sv={add_resx(SV, 1)} designer={add_designer()} "
      f"(requested {len(KEYS)})")
