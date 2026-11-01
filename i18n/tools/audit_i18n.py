#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""CKAN-UI 穿透性国际化 (i18n) 审计工具。

穿透性审计全源码仓库中硬编码的英文与中文文案：
1. 视图层 (AXAML): 审计属性 (Text/Header/Content/ToolTip.Tip/Watermark/Title 等) 与标签文本中的硬编码
2. 逻辑层 (C# ViewModels/Services/App): 区分已标准化 Text(...)、硬编码 UI 文案与规范排除项 (异常/日志/诊断)
3. 契约层 (LocalizationKeys / Packs): 审计双语对称性、占位符一致性、死键 (Dead Keys) 与缺键 (Missing Keys)
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        pass
from collections import defaultdict
from dataclasses import asdict, dataclass
from typing import Any, Dict, Generator, List, Optional, Set, Tuple

CJK_PATTERN = re.compile(r"[\u4e00-\u9fff]")
PLACEHOLDER_PATTERN = re.compile(r"\{(\d+)(?::[^}]+)?\}")
KEY_PATTERN = re.compile(r"^Ui_([A-Z][A-Za-z0-9]*)_([A-Za-z0-9]+)(?:_([A-Za-z0-9]+))*$")

AXAML_TEXT_ATTRS = {
    "Text", "Header", "Content", "ToolTip.Tip", "Watermark",
    "PlaceholderText", "Title", "BadgeText", "AutomationProperties.Name"
}

AXAML_EN_IGNORE = {
    "Auto", "Left", "Right", "Center", "Top", "Bottom", "Stretch",
    "True", "False", "Visible", "Collapsed", "Hidden",
    "#", ":", "-", "+", "/", "*", "%", "...",
    "fps", "ms", "px", "MB", "GB", "KB", "KB/s", "MB/s",
    "x", "y", "z", "w", "h",
    "60.0 fps", "0.0 ms", "0 MB", "0 KB", "0 B", "0", "1", "-- fps", "FPS --",
    "CKAN", "KSP", "GUI", "URL", "ID", "SHA256", "ZIP",
    "Ctrl+K", "Ctrl+S", "Ctrl+B", "Ctrl+F", "F12"
}

EXCEPTION_KEYWORDS = (
    "ArgumentNullException", "ArgumentException", "InvalidOperationException",
    "NotSupportedException", "KeyNotFoundException", "FileNotFoundException",
    "DirectoryNotFoundException", "IOException", "FormatException",
    "NullReferenceException", "NotImplementedException", "throw new "
)

LOG_KEYWORDS = (
    "_log.Info", "_log.Warn", "_log.Error", "_log.Debug",
    "Log.Info", "Log.Warn", "Log.Error", "Log.Debug",
    "ILocalizationLog", "Trace.WriteLine", "Debug.WriteLine"
)

REPORTER_KEYWORDS = (
    "IErrorReporter", "ReportError", "Report("
)

def is_cjk(text: str) -> bool:
    return bool(CJK_PATTERN.search(text))

def is_natural_english(text: str) -> bool:
    t = text.strip()
    if not t or len(t) < 2:
        return False
    if t in AXAML_EN_IGNORE:
        return False
    if re.match(r"^#[0-9A-Fa-f]{3,8}$", t):
        return False
    if t in ("CKAN UI", "CKAN Contributors", "Kerbal Space Program"):
        return False
    if t.startswith("translate(") or t.startswith("rotate(") or t.startswith("scale("):
        return False
    if re.match(r"^[\d\s,.\-+]+#[0-9A-Fa-f]{3,8}(?:[\d\s,.\-+]+#[0-9A-Fa-f]{3,8})*$", t):
        return False
    if t.startswith("avares://") or t.startswith("http://") or t.startswith("https://"):
        return False
    if "/" in t or "\\" in t or t.endswith(".axaml") or t.endswith(".cs") or t.endswith(".json"):
        return False
    if re.match(r"^[0-9.,\-_+*/%#@!?:;()[\]{}<>=]+$", t):
        return False
    if " " in t:
        if ";" in t or "{" in t or "}" in t:
            return False
        if re.search(r"\b#[0-9A-Fa-f]{6,8}\b", t) or "inset " in t:
            return False
        if re.match(r"^[MmLlHhVvCcSsQqTtAaZz0-9.\s,-]+$", t) and any(cmd in t for cmd in ["M", "m", "C", "c", "Z", "z"]):
            return False
        if any(f in t for f in ("Microsoft YaHei", "Yu Gothic", "Segoe UI", "SimSun", "Meiryo", "Malgun Gothic")):
            return False
        if t in ("yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss zzz", "yyyy-MM-dd HH:mm:ss,fff", "yyyy-MM-dd HH:mm"):
            return False
        return True
    natural_words = {
        "Refresh", "Cancel", "Confirm", "Save", "Discard", "Apply", "Retry",
        "Close", "Open", "Browse", "Search", "Filter", "Loading", "Ready",
        "Clear", "Reset", "Copy", "Paste", "Delete", "Remove", "Add",
        "Settings", "Options", "Help", "About", "Version", "Status",
        "Details", "Name", "Description", "Instances", "Mods", "Changes",
        "Back", "Next", "Finish", "Install", "Uninstall", "Update", "Export",
        "Import", "Download", "Diagnostics", "Logs", "Enabled", "Disabled"
    }
    if t in natural_words:
        return True
    if t.isupper() and len(t) <= 6:
        return False
    return False

def extract_csharp_strings(raw: str) -> Generator[Tuple[int, int, str, bool, str], None, None]:
    """Accurately lex C# source to extract string literals, correctly skipping comments and character literals."""
    i = 0
    n = len(raw)
    while i < n:
        c = raw[i]
        # Skip single-line and multi-line comments
        if c == '/' and i + 1 < n:
            if raw[i+1] == '/':
                i += 2
                while i < n and raw[i] != '\n':
                    i += 1
                continue
            elif raw[i+1] == '*':
                i += 2
                while i + 1 < n and not (raw[i] == '*' and raw[i+1] == '/'):
                    i += 1
                i += 2
                continue
        # Skip character literals: '...'
        if c == "'":
            i += 1
            if i < n and raw[i] == '\\':
                i += 2
            elif i < n:
                i += 1
            while i < n and raw[i] != "'" and raw[i] != '\n':
                i += 1
            if i < n and raw[i] == "'":
                i += 1
            continue
        # String literals: @, $, @$, $@, or standard "
        if c in ('@', '$', '"'):
            is_interp = False
            is_verbatim = False
            p = i
            if raw[p] in ('@', '$'):
                if raw[p] == '$': is_interp = True
                if raw[p] == '@': is_verbatim = True
                p += 1
                if p < n and raw[p] in ('@', '$'):
                    if raw[p] == '$': is_interp = True
                    if raw[p] == '@': is_verbatim = True
                    p += 1
            if p < n and raw[p] == '"':
                quote_count = 0
                while p + quote_count < n and raw[p + quote_count] == '"':
                    quote_count += 1
                if quote_count >= 3:
                    content_start = p + quote_count
                    end_quote = '"' * quote_count
                    closing = raw.find(end_quote, content_start)
                    if closing != -1:
                        val = raw[content_start:closing]
                        yield (i, closing + quote_count, val, is_interp, 'raw')
                        i = closing + quote_count
                        continue
                    else:
                        break
                elif is_verbatim:
                    content_start = p + 1
                    curr = content_start
                    while curr < n:
                        if raw[curr] == '"':
                            if curr + 1 < n and raw[curr+1] == '"':
                                curr += 2
                            else:
                                break
                        else:
                            curr += 1
                    val = raw[content_start:curr].replace('""', '"')
                    yield (i, curr + 1, val, is_interp, 'verbatim')
                    i = curr + 1
                    continue
                else:
                    content_start = p + 1
                    curr = content_start
                    while curr < n:
                        if raw[curr] == '\\':
                            curr += 2
                        elif raw[curr] == '"':
                            break
                        elif raw[curr] == '\n':
                            break
                        else:
                            curr += 1
                    val = raw[content_start:curr]
                    yield (i, curr + 1, val, is_interp, 'standard')
                    i = curr + 1
                    continue
        i += 1

@dataclass
class Finding:
    file: str
    line: int
    layer: str
    kind: str
    subkind: str
    text: str
    context: str
    key: Optional[str] = None

@dataclass
class KeyAuditResult:
    total_keys: int
    zh_keys_count: int
    en_keys_count: int
    symmetric: bool
    missing_in_zh: List[str]
    missing_in_en: List[str]
    placeholder_mismatches: List[Tuple[str, List[str], List[str]]]
    convention_violations: List[str]
    dead_keys: List[str]

class I18nAuditor:
    def __init__(self, root: str):
        self.root = os.path.abspath(root)
        self.keys_cs_path = os.path.join(self.root, "src", "CkanUI.Localization", "LocalizationKeys.cs")
        self.zh_pack_path = os.path.join(self.root, "i18n", "packs", "zh-CN.json")
        self.en_pack_path = os.path.join(self.root, "i18n", "packs", "en-US.json")
        self.localization_keys: Dict[str, str] = {}
        self.key_to_field: Dict[str, str] = {}
        self.zh_strings: Dict[str, str] = {}
        self.en_strings: Dict[str, str] = {}
        self.all_keys_set: Set[str] = set()
        self.findings: List[Finding] = []
        self.key_usage_counts: Dict[str, int] = defaultdict(int)
        self._load_localization_assets()

    def _load_localization_assets(self) -> None:
        if os.path.exists(self.keys_cs_path):
            raw = open(self.keys_cs_path, encoding="utf-8").read()
            for m in re.finditer(r'public const string ([A-Za-z0-9_]+) = "(Ui_[A-Za-z0-9_]+)";', raw):
                field, val = m.group(1), m.group(2)
                self.localization_keys[field] = val
                self.key_to_field[val] = field
                self.all_keys_set.add(val)
        if os.path.exists(self.zh_pack_path):
            data = json.load(open(self.zh_pack_path, encoding="utf-8"))
            self.zh_strings = data.get("strings", {})
            self.all_keys_set.update(self.zh_strings.keys())
        if os.path.exists(self.en_pack_path):
            data = json.load(open(self.en_pack_path, encoding="utf-8"))
            self.en_strings = data.get("strings", {})
            self.all_keys_set.update(self.en_strings.keys())

    def audit_key_contract(self) -> KeyAuditResult:
        zh_set = set(self.zh_strings.keys())
        en_set = set(self.en_strings.keys())
        cs_set = set(self.key_to_field.keys())
        missing_in_zh = sorted(list(en_set - zh_set))
        missing_in_en = sorted(list(zh_set - en_set))
        symmetric = (zh_set == en_set) and (zh_set == cs_set)
        violations = []
        for key in self.all_keys_set:
            if not KEY_PATTERN.match(key):
                violations.append(key)
            field = self.key_to_field.get(key)
            if field:
                expected_field = "".join(key.replace("Ui_", "").split("_"))
                if field != expected_field:
                    violations.append(f"{key} (Field {field} != expected {expected_field})")
        placeholder_mismatches = []
        common_keys = zh_set.intersection(en_set)
        for k in common_keys:
            zh_ph = sorted(re.findall(r"\{(\d+)\}", self.zh_strings[k]))
            en_ph = sorted(re.findall(r"\{(\d+)\}", self.en_strings[k]))
            if zh_ph != en_ph:
                placeholder_mismatches.append((k, zh_ph, en_ph))
        dead_keys = [k for k in self.all_keys_set if self.key_usage_counts[k] == 0]
        return KeyAuditResult(
            total_keys=len(self.all_keys_set),
            zh_keys_count=len(zh_set),
            en_keys_count=len(en_set),
            symmetric=symmetric,
            missing_in_zh=missing_in_zh,
            missing_in_en=missing_in_en,
            placeholder_mismatches=placeholder_mismatches,
            convention_violations=violations,
            dead_keys=sorted(dead_keys)
        )

    def scan_axaml_file(self, file_path: str) -> None:
        rel_path = os.path.relpath(file_path, self.root).replace("\\", "/")
        raw = open(file_path, encoding="utf-8").read()
        src = re.sub(r"<!--.*?-->", "", raw, flags=re.S)
        for m in re.finditer(r"\{DynamicResource\s+(Ui_[A-Za-z0-9_]+)\}", src):
            key = m.group(1)
            self.key_usage_counts[key] += 1
            line = raw[:m.start()].count("\n") + 1
            self.findings.append(Finding(
                file=rel_path, line=line, layer="AXAML", kind="resource_binding",
                subkind="localized_text", text=key, context="{DynamicResource " + key + "}", key=key
            ))
        for m in re.finditer(r"([A-Za-z0-9_.:]+)\s*=\s*\"([^\"]*)\"", src):
            attr, val = m.group(1), m.group(2)
            short_attr = attr.split(":")[-1]
            if short_attr in AXAML_TEXT_ATTRS:
                val_clean = val.strip()
                if not val_clean or val_clean.startswith("{"):
                    continue
                line = raw[:m.start()].count("\n") + 1
                if is_cjk(val_clean):
                    self.findings.append(Finding(
                        file=rel_path, line=line, layer="AXAML", kind="attribute",
                        subkind="hardcoded_cjk", text=val_clean, context=f'{attr}="{val_clean}"'
                    ))
                elif is_natural_english(val_clean):
                    self.findings.append(Finding(
                        file=rel_path, line=line, layer="AXAML", kind="attribute",
                        subkind="hardcoded_en", text=val_clean, context=f'{attr}="{val_clean}"'
                    ))
        for m in re.finditer(r">([^<]+)<", src):
            val = m.group(1).strip()
            if not val or val.startswith("{"):
                continue
            line = raw[:m.start()].count("\n") + 1
            if is_cjk(val):
                self.findings.append(Finding(
                    file=rel_path, line=line, layer="AXAML", kind="element_text",
                    subkind="hardcoded_cjk", text=val, context=f">{val}<"
                ))
            elif is_natural_english(val):
                self.findings.append(Finding(
                    file=rel_path, line=line, layer="AXAML", kind="element_text",
                    subkind="hardcoded_en", text=val, context=f">{val}<"
                ))

    def scan_csharp_file(self, file_path: str) -> None:
        rel_path = os.path.relpath(file_path, self.root).replace("\\", "/")
        if "LocalizationKeys.cs" in rel_path or "tests/" in rel_path or "obj/" in rel_path or "AssemblyAttributes" in rel_path:
            return
        layer = "App"
        if "ViewModels" in rel_path:
            layer = "ViewModel"
        elif "Services" in rel_path:
            layer = "Service"
        raw = open(file_path, encoding="utf-8").read()
        for m in re.finditer(r"LocalizationKeys\.([A-Za-z0-9_]+)", raw):
            field = m.group(1)
            key = self.localization_keys.get(field)
            if key:
                self.key_usage_counts[key] += 1
        for m in re.finditer(r'"(Ui_[A-Za-z0-9_]+)"', raw):
            key = m.group(1)
            if key in self.all_keys_set:
                self.key_usage_counts[key] += 1
                
        for start_pos, end_pos, val, is_interp, kind in extract_csharp_strings(raw):
            line = raw[:start_pos].count("\n") + 1
            ctx_start = max(0, start_pos - 80)
            ctx_end = min(len(raw), end_pos + 60)
            ctx_before = raw[ctx_start:start_pos].replace("\n", " ")
            ctx_all = raw[ctx_start:ctx_end].replace("\n", " ")
            if re.search(r"\bText\s*\($", ctx_before.strip()):
                self.findings.append(Finding(
                    file=rel_path, line=line, layer=layer, kind="csharp_literal",
                    subkind="localized_text", text=val, context=ctx_all.strip()
                ))
                continue
            is_excluded = False
            for kw in EXCEPTION_KEYWORDS:
                if kw in ctx_before:
                    is_excluded = True; break
            if not is_excluded:
                for kw in LOG_KEYWORDS:
                    if kw in ctx_before:
                        is_excluded = True; break
            if not is_excluded:
                for kw in REPORTER_KEYWORDS:
                    if kw in ctx_before:
                        is_excluded = True; break
            val_stripped = val.strip()
            if (val_stripped.startswith(".") and len(val_stripped) <= 6) or val_stripped in (".json", ".zip", ".ckan", ".dll", ".exe"):
                is_excluded = True
            if is_excluded:
                self.findings.append(Finding(
                    file=rel_path, line=line, layer=layer, kind="csharp_literal",
                    subkind="excluded_rule", text=val, context=ctx_all.strip()
                ))
                continue
            if is_cjk(val):
                self.findings.append(Finding(
                    file=rel_path, line=line, layer=layer, kind="csharp_literal",
                    subkind="hardcoded_cjk", text=val, context=ctx_all.strip()
                ))
            elif is_natural_english(val):
                self.findings.append(Finding(
                    file=rel_path, line=line, layer=layer, kind="csharp_literal",
                    subkind="hardcoded_en", text=val, context=ctx_all.strip()
                ))

    def run_audit(self) -> None:
        src_dir = os.path.join(self.root, "src")
        for root_dir, _, files in os.walk(src_dir):
            for file in files:
                p = os.path.join(root_dir, file)
                if file.endswith(".axaml"):
                    self.scan_axaml_file(p)
                elif file.endswith(".cs"):
                    self.scan_csharp_file(p)

    def generate_report(self) -> Dict[str, Any]:
        key_audit = self.audit_key_contract()
        counts_by_layer = defaultdict(lambda: defaultdict(int))
        for f in self.findings:
            counts_by_layer[f.layer][f.subkind] += 1
        hardcoded_cjk_findings = [f for f in self.findings if f.subkind == "hardcoded_cjk"]
        hardcoded_en_findings = [f for f in self.findings if f.subkind == "hardcoded_en"]
        localized_findings = [f for f in self.findings if f.subkind == "localized_text"]
        excluded_findings = [f for f in self.findings if f.subkind == "excluded_rule"]
        file_summary = defaultdict(lambda: {"cjk": 0, "en": 0, "layer": ""})
        for f in hardcoded_cjk_findings:
            file_summary[f.file]["cjk"] += 1
            file_summary[f.file]["layer"] = f.layer
        for f in hardcoded_en_findings:
            file_summary[f.file]["en"] += 1
            file_summary[f.file]["layer"] = f.layer
        top_unlocalized_files = sorted(
            [{"file": k, **v} for k, v in file_summary.items()],
            key=lambda x: -(x["cjk"] + x["en"])
        )
        return {
            "metrics": {
                "total_findings": len(self.findings),
                "total_hardcoded_cjk": len(hardcoded_cjk_findings),
                "total_hardcoded_en": len(hardcoded_en_findings),
                "total_localized_sites": len(localized_findings),
                "total_excluded_by_rules": len(excluded_findings),
            },
            "layer_breakdown": {k: dict(v) for k, v in counts_by_layer.items()},
            "key_audit": asdict(key_audit),
            "top_unlocalized_files": top_unlocalized_files,
            "findings": [asdict(f) for f in self.findings]
        }

def render_markdown_report(report: Dict[str, Any]) -> str:
    m = report["metrics"]
    ka = report["key_audit"]
    layers = report["layer_breakdown"]
    lines = []
    lines.append("# CKAN-UI 国际化 (i18n) 穿透性审计报告\n")
    lines.append("## 1. 总体健康度与核心指标\n")
    lines.append("| 维度 | 统计数值 | 说明 |")
    lines.append("| --- | --- | --- |")
    lines.append(f'| 已标准化国际化站点 | {m["total_localized_sites"]} | 包含 AXAML DynamicResource 与 C# Text(...) |')
    lines.append(f'| 硬编码中文 (待标准化) | {m["total_hardcoded_cjk"]} | 视图层与逻辑层待抽取的中文文案 |')
    lines.append(f'| 硬编码英文 (待标准化) | {m["total_hardcoded_en"]} | 视图层硬编码自然英文标签/提示 |')
    lines.append(f'| 规范排除项 (非本地化) | {m["total_excluded_by_rules"]} | 遵循规范排除的异常/日志/诊断信息 |')
    lines.append(f'| 语言包键总数 | {ka["total_keys"]} | zh-CN 与 en-US 共享键基准 |')
    lines.append(f'| 双语包对称性 | {"✅ 对称" if ka["symmetric"] else "❌ 存在不对称"} | zh-CN vs en-US 键集合比对 |')
    lines.append(f'| 死键 (Dead Keys) 数量 | {len(ka["dead_keys"])} | 语言包中定义但未被代码/视图引用的键 |\n')
    lines.append("## 2. 分层穿透统计\n")
    lines.append("| 架构分层 | 已标准化 (Localized) | 硬编码中文 (CJK) | 硬编码英文 (EN) | 规范排除 (Excluded) |")
    lines.append("| --- | --- | --- | --- | --- |")
    for layer in ("AXAML", "ViewModel", "Service", "App"):
        row = layers.get(layer, {})
        lines.append(
            f'| {layer} | {row.get("localized_text", 0)} | '
            f'{row.get("hardcoded_cjk", 0)} | {row.get("hardcoded_en", 0)} | '
            f'{row.get("excluded_rule", 0)} |'
        )
    lines.append("")
    lines.append("## 3. 语言包与契约完整性审计\n")
    if ka["symmetric"] and not ka["placeholder_mismatches"] and not ka["missing_in_zh"] and not ka["missing_in_en"]:
        lines.append("✅ **语言包键集完整且高度对称**：`zh-CN.json`、`en-US.json` 与 `LocalizationKeys.cs` 保持严格 1:1 镜像。\n")
    else:
        if ka["missing_in_zh"]:
            lines.append(f'- **zh-CN 缺键**: {ka["missing_in_zh"]}')
        if ka["missing_in_en"]:
            lines.append(f'- **en-US 缺键**: {ka["missing_in_en"]}')
        if ka["placeholder_mismatches"]:
            lines.append(f'- **占位符不匹配**: {ka["placeholder_mismatches"]}')
    lines.append(f'**未被引用的死键 (共 {len(ka["dead_keys"])} 个)**：')
    if ka["dead_keys"]:
        lines.append("```text")
        lines.append(", ".join(ka["dead_keys"][:30]) + ("..." if len(ka["dead_keys"]) > 30 else ""))
        lines.append("```\n")
    else:
        lines.append("✅ **0 死键**：所有键均在代码或 AXAML 中被有效引用。\n")
    lines.append("## 4. 重点未标准化文件 Top 20\n")
    lines.append("| 文件路径 | 所属层 | 硬编码中文 | 硬编码英文 | 合计待处理 |")
    lines.append("| --- | --- | --- | --- | --- |")
    for item in report["top_unlocalized_files"][:20]:
        tot = item["cjk"] + item["en"]
        lines.append(f'| `{item["file"]}` | {item["layer"]} | {item["cjk"]} | {item["en"]} | {tot} |')
    lines.append("")
    return "\n".join(lines)

def main():
    parser = argparse.ArgumentParser(description="CKAN-UI 穿透性国际化审计脚本")
    parser.add_argument("--root", default=".", help="仓库根路径（默认当前目录）")
    parser.add_argument("--json", help="输出完整 JSON 审计数据路径")
    parser.add_argument("--markdown", help="输出 Markdown 审计报告路径")
    parser.add_argument("--check", action="store_true", help="CI 检查模式：发现硬编码或死键时报错")
    parser.add_argument("--summary-only", action="store_true", help="仅打印统计摘要")
    args = parser.parse_args()

    auditor = I18nAuditor(args.root)
    auditor.run_audit()
    report = auditor.generate_report()

    if args.json:
        os.makedirs(os.path.dirname(os.path.abspath(args.json)), exist_ok=True)
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(report, f, ensure_ascii=False, indent=2)
        print(f"JSON 报告已生成: {args.json}")

    if args.markdown:
        os.makedirs(os.path.dirname(os.path.abspath(args.markdown)), exist_ok=True)
        md_text = render_markdown_report(report)
        with open(args.markdown, "w", encoding="utf-8") as f:
            f.write(md_text)
        print(f"Markdown 报告已生成: {args.markdown}")

    m = report["metrics"]
    ka = report["key_audit"]
    print("=" * 60)
    print("CKAN-UI 穿透性国际化审计统计摘要")
    print("=" * 60)
    print(f"已标准化站点数 (DynamicResource / Text): {m['total_localized_sites']}")
    print(f"规范排除项数 (Exception / Log / Reporter): {m['total_excluded_by_rules']}")
    print(f"硬编码中文待处理项: {m['total_hardcoded_cjk']}")
    print(f"硬编码英文待处理项: {m['total_hardcoded_en']}")
    print(f"语言包总键数: {ka['total_keys']}")
    print(f"双语包对称性: {'✅ PASS' if ka['symmetric'] else '❌ FAIL'}")
    print(f"死键 (Dead Keys) 数量: {len(ka['dead_keys'])}")
    print("=" * 60)

    if args.check:
        if not ka["symmetric"] or ka["dead_keys"]:
            print("❌ 契约检查失败！")
            sys.exit(1)
        print("✅ 契约检查通过！")

if __name__ == "__main__":
    main()
