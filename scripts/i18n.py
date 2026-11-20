#!/usr/bin/env python3
"""介面文字的語言檔工具。

正本是 i18n/<語言>.json（繁體中文 zh-Hant.json 是原稿）。
- Mac 版直接讀這些 json（組裝時會放進 Tietie.app）。
- Windows 版用 .NET 資源檔，這支程式把 json 轉成 windows/Tietie/Strings*.resx。

改了任何一個 json 之後，執行：python3 scripts/i18n.py
它會先檢查每個語言的項目和 {0} 這類位置符號是否跟原稿一致，有問題就停下來。
"""
import json
import re
import sys
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parent.parent
I18N = ROOT / "i18n"
WIN = ROOT / "windows" / "Tietie"
SOURCE = "zh-Hant"
LANGUAGES = ["zh-Hant", "en", "zh-Hans", "ja", "ko", "pt", "es"]
NEUTRAL = "en"  # Windows 找不到對應語言時用的預設


def load(code):
    with open(I18N / f"{code}.json", encoding="utf-8") as f:
        return json.load(f)


def placeholders(text):
    return sorted(set(re.findall(r"\{\d+\}", text)))


def check(tables):
    errors = []
    base = tables[SOURCE]
    for code, table in tables.items():
        missing = set(base) - set(table)
        extra = set(table) - set(base)
        for key in sorted(missing):
            errors.append(f"{code}: 少了 {key}")
        for key in sorted(extra):
            errors.append(f"{code}: 多了 {key}（原稿沒有）")
        for key in base:
            if key not in table:
                continue
            value = table[key]
            if not isinstance(value, str) or not value.strip():
                errors.append(f"{code}: {key} 是空的")
                continue
            if placeholders(value) != placeholders(base[key]):
                errors.append(f"{code}: {key} 的位置符號 {placeholders(value)} 跟原稿 {placeholders(base[key])} 不一樣")
    return errors


def resx(entries):
    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        "<!-- 這個檔案由 scripts/i18n.py 從 i18n/*.json 產生，請改 json 再重新產生，不要直接改這裡。 -->",
        "<root>",
        '  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>',
        '  <resheader name="version"><value>2.0</value></resheader>',
        '  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>',
        '  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>',
    ]
    for key, value in entries:
        lines.append(f'  <data name="{escape(key, {chr(34): "&quot;"})}" xml:space="preserve"><value>{escape(value)}</value></data>')
    lines.append("</root>")
    return "\n".join(lines) + "\n"


def main():
    tables = {code: load(code) for code in LANGUAGES}
    errors = check(tables)
    if errors:
        print("語言檔有問題：")
        for e in errors:
            print("  " + e)
        sys.exit(1)
    order = list(tables[SOURCE].keys())
    for code in LANGUAGES:
        entries = [(k, tables[code][k]) for k in order if not k.startswith("mac.")]
        name = "Strings.resx" if code == NEUTRAL else f"Strings.{code}.resx"
        (WIN / name).write_text(resx(entries), encoding="utf-8")
    count = len([k for k in order if not k.startswith("_")])
    print(f"檢查通過：{len(LANGUAGES)} 種語言，每種 {count} 項。已更新 windows/Tietie/Strings*.resx")


if __name__ == "__main__":
    main()
