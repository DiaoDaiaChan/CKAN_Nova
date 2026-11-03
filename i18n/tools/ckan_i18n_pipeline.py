#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""CKAN 元数据 → UniversalI18n → ckan-ui 全链路编排与验收（单文件、零第三方依赖）。

链路（与 docs/design/mod-metadata-i18n.md「全链路」一节一致）：
    CKAN-meta / %LOCALAPPDATA%/CKAN/repos
      → i18n/tools/extract_ckan_meta.py            候选 + pool（ckan:<identifier>:<field>）
      → UniversalI18n i18n_tools.translate         api 门禁 / journal / 合并
      → <UI18n>/packs/zh-cn/CKANMeta.jsonl         源包
      → 本脚本 sync（.tmp + os.replace，sha256 前后核对）
      → %LOCALAPPDATA%/CKAN/ckan-ui/i18n/packs/<lang>/*.jsonl（CKANMeta + UI18n 全量语言包）
      → ckan-ui 运行时（src/CkanUI.ModText）

退出码（CI 用来定位究竟是哪一段坏了）：
    0   成功
    2   参数错误（argparse）
    10  环境/路径解析失败（ckan-ui 根、UniversalI18n 根、抽取脚本缺失）
    11  extract 阶段失败
    12  translate 阶段失败
    13  sync 阶段失败（源包契约不过、sha256 不符、写盘失败）
    14  --dotnet-test 失败
    20  --check 不合格（契约错误；--strict 下 warn 也判红）
    21  --selftest 断言失败

零硬编码：仓库根从 __file__ 推导（i18n/tools 上两级）；
UniversalI18n 根按 --ui18n-root → UNIVERSAL_I18N_ROOT → <root>/external/UniversalI18n
→ <root>/../UniversalI18n 顺序解析，全部落空时给可操作错误。

不联网：extract / translate 是否联网由被调工具决定；--check / --selftest 全程零网络、零写盘。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

# 导入同目录的 extract_ckan_meta 会写 __pycache__（本仓该目录被 git 跟踪），一律不写字节码。
sys.dont_write_bytecode = True

THIS_FILE = Path(__file__).resolve()
TOOLS_DIR = THIS_FILE.parent                 # <root>/i18n/tools
I18N_DIR = TOOLS_DIR.parent                   # <root>/i18n
REPO_ROOT = I18N_DIR.parent                   # <root>
DEFAULT_EXTRACT_SCRIPT = TOOLS_DIR / "extract_ckan_meta.py"

MOD_NAME = "CKANMeta"
LANG = "zh-cn"
PACK_NAME = "CKANMeta.jsonl"
POOL_NAME = "ckan_pool.jsonl"
INDEX_NAME = "index.json"
ENV_UI18N = "UNIVERSAL_I18N_ROOT"

# 退出码
EXIT_OK = 0
EXIT_USAGE = 2
EXIT_ENV = 10
EXIT_EXTRACT = 11
EXIT_TRANSLATE = 12
EXIT_SYNC = 13
EXIT_DOTNET = 14
EXIT_CHECK = 20
EXIT_SELFTEST = 21

# sync 搬运运行时真正需要的全部语言包：CKAN 元数据只是其中一包，游戏内 UI 与部件描述
# 的译文同样要进 ckan-ui，否则 i18n 页只能看到元数据、其余模组的译文一律缺位。
SYNC_ROOT_COPY = ("glossary.jsonl", "blocklist.json")   # 术语表/黑名单：整体替换（非用户数据）
SYNC_ROOT_MERGE = ("overrides.jsonl",)                  # 用户覆盖层：只增不减，绝不覆盖
SYNC_LANG_PACK_SUFFIX = ".jsonl"
SYNC_SKIP_LANG_DIRS = ("journal", "submission")

KEY_RE = re.compile(r"^ckan:.+:[^:]+$")
HASH_RE = re.compile(r"^[0-9a-f]{16}$")


# --------------------------------------------------------------------------- #
# 基础工具
# --------------------------------------------------------------------------- #

class PipelineError(Exception):
    """带退出码的可预期失败。"""

    def __init__(self, message: str, code: int) -> None:
        super().__init__(message)
        self.code = code


def _configure_stdout() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass


def p(value) -> str:
    return "" if value is None else str(value)


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


_contract_module = None


def contract():
    """复用 extract_ckan_meta 的 normalize/fnv1a64，杜绝两套哈希实现漂移。"""
    global _contract_module
    if _contract_module is not None:
        return _contract_module
    if not DEFAULT_EXTRACT_SCRIPT.is_file():
        raise PipelineError(
            "未找到哈希契约实现：%s\n"
            "本编排器要求与抽取器共用同一套 normalize/fnv1a64，不允许各写一套。"
            % DEFAULT_EXTRACT_SCRIPT,
            EXIT_ENV,
        )
    sys.path.insert(0, str(TOOLS_DIR))
    import extract_ckan_meta as core  # 延迟导入：--help 不该触发
    _contract_module = core
    return core


def read_jsonl(path: Path):
    """返回 (行对象, 无法解析的行说明)；空行不计。"""
    rows = []
    errors = []
    with path.open("r", encoding="utf-8-sig", newline=None) as handle:
        for lineno, line in enumerate(handle, 1):
            if not line.strip():
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError as exc:
                errors.append("第 %d 行无法解析：%s" % (lineno, exc))
                continue
            if not isinstance(obj, dict):
                errors.append("第 %d 行不是 JSON 对象" % lineno)
                continue
            rows.append(obj)
    return rows, errors


def run_command(cmd, cwd=None, sink=None) -> int:
    """跑子进程；子进程输出透传到 sink（--json 模式下改走 stderr，保持 stdout 干净）。"""
    sink = sink or sys.stdout
    env = dict(os.environ)
    env["PYTHONIOENCODING"] = "utf-8"
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    rendered = " ".join(str(part) for part in cmd)
    print("[RUN] %s%s" % (rendered, " (cwd=%s)" % cwd if cwd else ""), file=sink, flush=True)
    proc = subprocess.run(
        [str(part) for part in cmd],
        cwd=str(cwd) if cwd else None,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        env=env,
    )
    if proc.stdout:
        sink.write(proc.stdout)
        sink.flush()
    if proc.stderr:
        sys.stderr.write(proc.stderr)
        sys.stderr.flush()
    return proc.returncode


def capture_command(cmd, cwd=None):
    env = dict(os.environ)
    env["PYTHONIOENCODING"] = "utf-8"
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    proc = subprocess.run(
        [str(part) for part in cmd],
        cwd=str(cwd) if cwd else None,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        env=env,
    )
    return proc.returncode, proc.stdout or "", proc.stderr or ""


# --------------------------------------------------------------------------- #
# 根目录解析（零硬编码）
# --------------------------------------------------------------------------- #

def resolve_ckan_ui_root(explicit):
    if explicit:
        root = Path(explicit).expanduser().resolve()
        if not (root / "i18n" / "tools").is_dir():
            raise PipelineError(
                "--ckan-ui-root 不像 ckan-ui 仓库根：%s\n"
                "该目录下应有 i18n/tools/。不传此参数时自动使用 %s。"
                % (root, REPO_ROOT),
                EXIT_ENV,
            )
        return root
    if not (REPO_ROOT / "i18n" / "tools").is_dir():
        raise PipelineError(
            "无法从脚本位置推导 ckan-ui 仓库根：%s\n"
            "请把本脚本放在 <ckan-ui>/i18n/tools/ 下，或显式传 --ckan-ui-root。"
            % REPO_ROOT,
            EXIT_ENV,
        )
    return REPO_ROOT


def resolve_extract_script(explicit, ckan_ui_root):
    if explicit:
        script = Path(explicit).expanduser().resolve()
    else:
        script = ckan_ui_root / "i18n" / "tools" / "extract_ckan_meta.py"
    if not script.is_file():
        raise PipelineError(
            "未找到抽取器：%s\n"
            "它是哈希契约的唯一实现（--verify-vectors 也由它提供）。\n"
            "修复：确认在 ckan-ui 仓库内运行，或传 --ckan-ui-root / --extract-script。"
            % script,
            EXIT_ENV,
        )
    return script


def resolve_ui18n_root(explicit, ckan_ui_root):
    """按 --ui18n-root → 环境变量 → <root>/external/UniversalI18n → <root>/../UniversalI18n 解析。"""
    tried = []
    candidates = []

    if explicit:
        candidates.append(("--ui18n-root", Path(explicit).expanduser()))
    else:
        candidates.append(("--ui18n-root", None))

    env_value = os.environ.get(ENV_UI18N, "").strip()
    candidates.append(("环境变量 %s" % ENV_UI18N, Path(env_value).expanduser() if env_value else None))
    candidates.append(("仓库内联接 external/UniversalI18n", ckan_ui_root / "external" / "UniversalI18n"))
    candidates.append(("相邻仓库 ../UniversalI18n", ckan_ui_root.parent / "UniversalI18n"))

    for label, candidate in candidates:
        if candidate is None:
            tried.append("%s：未提供" % label)
            continue
        resolved = candidate.resolve() if candidate.exists() else candidate
        ok = resolved.is_dir() and ((resolved / "packs").is_dir() or (resolved / "tools").is_dir())
        tried.append("%s：%s%s" % (label, resolved, "" if ok else "（不存在或不像 UniversalI18n 仓库）"))
        if ok:
            return resolved, label, tried

    raise PipelineError(
        "无法定位 UniversalI18n 仓库根。已尝试：\n  - "
        + "\n  - ".join(tried)
        + "\n修复任一即可：\n"
        "  1) 设环境变量 %s=<UniversalI18n 仓库根>；\n"
        "  2) 在 <ckan-ui>/external/ 下建目录联接：mklink /J external\\UniversalI18n <UniversalI18n>；\n"
        "  3) 把 UniversalI18n 克隆到 <ckan-ui 的上级目录>/UniversalI18n；\n"
        "  4) 直接传 --ui18n-root <路径>。" % ENV_UI18N,
        EXIT_ENV,
    )


def default_dest_base():
    local = os.environ.get("LOCALAPPDATA", "").strip()
    if local:
        return Path(local) / "CKAN" / "ckan-ui" / "i18n" / "packs"
    profile = os.environ.get("USERPROFILE", "").strip()
    if profile:
        return Path(profile) / "AppData" / "Local" / "CKAN" / "ckan-ui" / "i18n" / "packs"
    return None


def default_repo_dir():
    local = os.environ.get("LOCALAPPDATA", "").strip()
    if not local:
        return None
    candidate = Path(local) / "CKAN" / "repos"
    return candidate if candidate.is_dir() else None


class Paths:
    """一次解析、各阶段共用，避免每处都重复回落逻辑。"""

    def __init__(self, args) -> None:
        self.ckan_ui_root = resolve_ckan_ui_root(args.ckan_ui_root)
        self.extract_script = resolve_extract_script(args.extract_script, self.ckan_ui_root)
        self.lang = args.lang
        self.pool_explicit = bool(args.pool)
        self.pack_explicit = bool(args.pack)
        self.index_explicit = bool(args.index)
        self.ui18n = None
        self.ui18n_source = ""
        self.ui18n_tried = []
        if not (self.pool_explicit and self.pack_explicit):
            self.ui18n, self.ui18n_source, self.ui18n_tried = resolve_ui18n_root(
                args.ui18n_root, self.ckan_ui_root
            )
        self.pool = (
            Path(args.pool).expanduser().resolve()
            if self.pool_explicit
            else (self.ui18n / "packs" / POOL_NAME)
        )
        self.pack = (
            Path(args.pack).expanduser().resolve()
            if self.pack_explicit
            else (self.ui18n / "packs" / self.lang / PACK_NAME)
        )
        self.index = (
            Path(args.index).expanduser().resolve()
            if self.index_explicit
            else (self.ui18n / "packs" / INDEX_NAME)
        )
        if args.dest:
            self.dest = Path(args.dest).expanduser().resolve()
        else:
            self.dest = default_dest_base()
        self.repo_dir = Path(args.repo_dir).expanduser().resolve() if args.repo_dir else default_repo_dir()
        self.meta_root = Path(args.meta_root).expanduser().resolve() if args.meta_root else None

    def as_json(self) -> dict:
        return {
            "ckanUi": p(self.ckan_ui_root),
            "ui18n": p(self.ui18n),
            "ui18nSource": self.ui18n_source,
            "pool": p(self.pool),
            "pack": p(self.pack),
            "index": p(self.index),
            "dest": p(self.dest),
            "lang": self.lang,
        }


# --------------------------------------------------------------------------- #
# 统一的检查条目模型
# --------------------------------------------------------------------------- #

class Report:
    """severity: error（契约违约，必红）/ warn（外部状态，--strict 才红）/ info（提示）。"""

    def __init__(self, strict: bool) -> None:
        self.strict = strict
        self.items = []

    def add(self, item_id, ok, message, severity="error", detail=None) -> None:
        self.items.append({
            "id": item_id,
            "severity": severity,
            "ok": bool(ok),
            "message": message,
            "detail": detail or {},
        })

    def info(self, item_id, message, detail=None) -> None:
        self.add(item_id, True, message, "info", detail)

    def warn(self, item_id, ok, message, detail=None) -> None:
        self.add(item_id, ok, message, "warn", detail)

    def failed(self):
        return [
            item for item in self.items
            if not item["ok"] and (item["severity"] == "error" or (self.strict and item["severity"] == "warn"))
        ]

    def counts(self) -> dict:
        """只统计失败的条目，避免"通过的错误级检查"把 error 计数撑大。"""
        counts = {"total": len(self.items), "failedError": 0, "failedWarn": 0,
                  "info": 0, "failed": 0}
        for item in self.items:
            if item["severity"] == "info":
                counts["info"] += 1
            if item["ok"]:
                continue
            if item["severity"] == "error":
                counts["failedError"] += 1
            elif item["severity"] == "warn":
                counts["failedWarn"] += 1
        counts["failed"] = len(self.failed())
        return counts

    def render(self, sink=None) -> None:
        sink = sink or sys.stdout
        for item in self.items:
            if item["ok"]:
                tag = "[ OK ]"
            elif item["severity"] == "error":
                tag = "[FAIL]"
            elif item["severity"] == "warn":
                tag = "[WARN]"
            else:
                tag = "[INFO]"
            print("%s %-22s %s" % (tag, item["id"], item["message"]), file=sink)


# --------------------------------------------------------------------------- #
# 各检查项
# --------------------------------------------------------------------------- #

def _sample(items, limit=3) -> str:
    shown = items[:limit]
    suffix = "" if len(items) <= limit else " …（共 %d 条）" % len(items)
    return "；".join(shown) + suffix


def check_vectors(report: Report, extract_script: Path) -> bool:
    code, out, err = capture_command([sys.executable, str(extract_script), "--verify-vectors"])
    if code == 0:
        first = next((line.strip() for line in out.splitlines() if line.strip()), "官方向量全部通过。")
        report.add("vectors", True, first, "error", {"exitCode": code})
        return True
    detail = (err.strip() or out.strip() or "无输出").splitlines()
    report.add(
        "vectors", False,
        "跨语言哈希向量不符（C# UI18n.Core 与 Python 已漂移）：%s" % (detail[0] if detail else "无输出"),
        "error",
        {"exitCode": code, "stderr": "\n".join(detail[:5]), "script": p(extract_script)},
    )
    return False


def validate_pool(rows, core):
    """返回 (stats, hash 违约, key 违约, (by_h, by_key))。"""
    bad_hash = []
    bad_key = []
    seen = {}
    by_h = {}
    by_key = {}
    for position, row in enumerate(rows, 1):
        text = row.get("text")
        h = row.get("h")
        key = row.get("key")
        if not isinstance(text, str) or not text.strip():
            bad_hash.append("第 %d 行 text 为空" % position)
            continue
        if not isinstance(h, str) or not HASH_RE.match(h):
            bad_hash.append("第 %d 行 h 不是 16 位小写 hex：%r" % (position, h))
            continue
        expected = core.hash_hex(text)
        if h != expected:
            bad_hash.append("第 %d 行 h=%s 与 fnv1a64(normalize(text))=%s 不符" % (position, h, expected))
        if h in seen:
            bad_hash.append("第 %d 行 h 重复：%s" % (position, h))
        seen[h] = True
        by_h[h] = row
        if not isinstance(key, str) or not KEY_RE.match(key):
            bad_key.append("第 %d 行 key 形状非法：%r" % (position, key))
        else:
            # 同一个 ckan:<identifier>:<field> 在 pool 里可能有多条历史文本（key 不是主键，h 才是）。
            by_key.setdefault(key, {})[h] = row
    stats = {"rows": len(rows), "distinctH": len(seen), "distinctKeys": len(by_key)}
    return stats, bad_hash, bad_key, (by_h, by_key)


def validate_pack(rows, pool_by_h, pool_by_key, core):
    """按契约校验源包；返回 (stats, [(item_id, violations), ...])。"""
    bad_hash = []
    bad_norm = []
    bad_text = []
    bad_dup = []
    bad_kshape = []
    bad_korphan = []
    bad_khash = []
    seen = {}
    with_k = 0
    for position, row in enumerate(rows, 1):
        source = row.get("s")
        h = row.get("h")
        normalized = row.get("n")
        target = row.get("t")
        if not isinstance(source, str) or not source.strip():
            bad_text.append("第 %d 行 s 为空" % position)
            continue
        if not isinstance(target, str) or not target.strip():
            bad_text.append("第 %d 行 t 为空（不允许写空译文）" % position)
        if not isinstance(h, str) or not HASH_RE.match(h):
            bad_hash.append("第 %d 行 h 不是 16 位小写 hex：%r" % (position, h))
        else:
            expected = core.hash_hex(source)
            if h != expected:
                bad_hash.append("第 %d 行 h=%s ≠ fnv1a64(normalize(s))=%s" % (position, h, expected))
            if h in seen:
                bad_dup.append("第 %d 行 h 重复：%s（同包 h 必须唯一）" % (position, h))
            seen[h] = True
        expected_n = core.normalize(source)
        if normalized != expected_n:
            bad_norm.append("第 %d 行 n ≠ normalize(s)（n=%r）" % (position, normalized))
        if row.get("k") is not None:
            with_k += 1
            key = row.get("k")
            if not isinstance(key, str) or not KEY_RE.match(key):
                bad_kshape.append("第 %d 行 k 形状非法（应为 ckan:<identifier>:<field>）：%r" % (position, key))
            else:
                hits = pool_by_key.get(key)
                if not hits:
                    bad_korphan.append("第 %d 行 k=%s 在 pool 中不存在" % (position, key))
                elif h not in hits:
                    bad_khash.append(
                        "第 %d 行 k=%s 的 h 在 pool 里找不到（包 h=%s；pool 同 key 的 h=%s）"
                        % (position, key, h, "/".join(sorted(hits)[:3]))
                    )
    stats = {
        "rows": len(rows),
        "kRows": with_k,
        "kMissing": len(rows) - with_k,
        "inPool": sum(1 for row in rows if row.get("h") in pool_by_h),
    }
    groups = [
        ("pack.hash", bad_hash),
        ("pack.normalized", bad_norm),
        ("pack.text", bad_text),
        ("pack.duplicate-h", bad_dup),
        ("pack.k-shape", bad_kshape),
        ("pack.k-orphan", bad_korphan),
        ("pack.k-hash", bad_khash),
    ]
    return stats, groups


PACK_OK_MESSAGES = {
    "pack.hash": "每行 h == fnv1a64(normalize(s))",
    "pack.normalized": "每行 n == normalize(s)",
    "pack.text": "每行 s/t 非空",
    "pack.duplicate-h": "同包 h 唯一",
    "pack.k-shape": "%d 行带 k 且形状合法",
    "pack.k-orphan": "k 均能在 pool 命中",
    "pack.k-hash": "k 与 pool 的 h 全部对齐",
}

PACK_BAD_MESSAGES = {
    "pack.hash": "行级哈希契约违约 %d 处：%s",
    "pack.normalized": "n 与 normalize(s) 不一致 %d 处：%s",
    "pack.text": "s/t 为空 %d 处：%s",
    "pack.duplicate-h": "同包 h 重复 %d 处：%s",
    "pack.k-shape": "k 形状非法 %d 处：%s",
    "pack.k-orphan": "k 在 pool 中找不到 %d 处：%s",
    "pack.k-hash": "k 与 pool 的 h 不对齐 %d 处：%s",
}


def check_pool(report: Report, pool_path: Path, core):
    stats = {"path": p(pool_path), "rows": 0, "distinctH": 0, "distinctKeys": 0,
             "translated": 0, "missing": 0, "present": pool_path.is_file()}
    if not pool_path.is_file():
        report.add("pool.file", False,
                   "pool 不存在：%s（先跑 --stage extract 生成）" % pool_path, "error")
        return stats, {}, {}
    rows, errors = read_jsonl(pool_path)
    if errors:
        report.add("pool.parse", False, "pool 有 %d 行无法解析：%s" % (len(errors), _sample(errors)), "error")
    else:
        report.add("pool.parse", True, "%d 行全部可解析" % len(rows), "error")

    pool_stats, bad_hash, bad_key, indexes = validate_pool(rows, core)
    stats.update(pool_stats)
    if bad_hash:
        report.add("pool.hash", False,
                   "pool 哈希契约违约 %d 处：%s" % (len(bad_hash), _sample(bad_hash)), "error")
    else:
        report.add("pool.hash", True, "pool 每行 h == fnv1a64(normalize(text))，无重复", "error")
    if bad_key:
        report.add("pool.key", False,
                   "pool key 形状违约 %d 处：%s" % (len(bad_key), _sample(bad_key)), "error")
    else:
        report.add("pool.key", True, "%d 个 key 均为 ckan:<identifier>:<field>" % pool_stats["distinctKeys"],
                   "error")
    by_h, by_key = indexes
    return stats, by_h, by_key


def check_pack(report: Report, pack_path: Path, pool_by_h: dict, pool_by_key: dict, core):
    stats = {"path": p(pack_path), "rows": 0, "bytes": 0, "sha256": "", "kRows": 0,
             "kMissing": 0, "inPool": 0, "present": pack_path.is_file()}
    if not pack_path.is_file():
        report.warn("pack.file", False,
                    "源包尚未产出：%s（跑 --stage all 生成；本节其余检查跳过）" % pack_path)
        return stats
    stats["bytes"] = pack_path.stat().st_size
    stats["sha256"] = sha256_file(pack_path)
    rows, errors = read_jsonl(pack_path)
    report.add("pack.file", True,
               "%d 行 / %d 字节 / sha256=%s…" % (len(rows) + len(errors), stats["bytes"], stats["sha256"][:16]),
               "error", {"sha256": stats["sha256"], "bytes": stats["bytes"]})
    if errors:
        report.add("pack.parse", False, "源包有 %d 行无法解析：%s" % (len(errors), _sample(errors)), "error")
    pack_stats, groups = validate_pack(rows, pool_by_h, pool_by_key, core)
    stats.update(pack_stats)
    for item_id, violations in groups:
        if violations:
            report.add(item_id, False,
                       PACK_BAD_MESSAGES[item_id] % (len(violations), _sample(violations)), "error")
            continue
        if pack_stats["kRows"] == 0 and item_id.startswith("pack.k-"):
            if item_id == "pack.k-shape":
                report.info(item_id, "包内无 k 字段（旧格式），按跨仓契约跳过形状校验，不判红")
            continue
        template = PACK_OK_MESSAGES[item_id]
        message = template % pack_stats["kRows"] if "%d" in template else template
        report.add(item_id, True, message, "error")
    return stats


def check_index(report: Report, index_path: Path, pack_path: Path, lang: str) -> dict:
    info = {"path": p(index_path), "registered": False, "mod": MOD_NAME,
            "rows": None, "bytes": None, "sha256": None}
    if not index_path.is_file():
        report.warn("index.file", False,
                    "清单不存在：%s（UniversalI18n 侧 python -m i18n_tools.packindex 生成）" % index_path)
        return info
    try:
        payload = json.loads(index_path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as exc:
        report.add("index.file", False, "清单无法解析：%s（%s）" % (index_path, exc), "error")
        return info
    entries = payload.get("packs") if isinstance(payload, dict) else None
    if not isinstance(entries, list):
        report.add("index.file", False, "清单缺少 packs[] 数组：%s" % index_path, "error")
        return info

    wanted = ("%s/%s" % (lang, PACK_NAME)).lower()
    entry = None
    for candidate in entries:
        if not isinstance(candidate, dict):
            continue
        file_field = str(candidate.get("file") or "").replace("\\", "/").lower()
        if file_field == wanted:
            entry = candidate
            break
    if entry is None:
        for candidate in entries:
            if isinstance(candidate, dict) and candidate.get("mod") == MOD_NAME:
                entry = candidate
                break

    if entry is None:
        report.warn(
            "index.registered", False,
            "%s 未登记 %s（磁盘已登记 %d 个包）。清单由 UniversalI18n 的 packindex 生成，"
            "本检查只读不改；缺登记时增量分发拿不到这个包。" % (index_path.name, PACK_NAME, len(entries)),
            {"entries": len(entries), "file": wanted},
        )
        return info

    info.update({"registered": True, "rows": entry.get("rows"), "bytes": entry.get("bytes"),
                 "sha256": entry.get("sha256")})
    if not pack_path.is_file():
        report.info("index.rows", "清单已登记 %s，但源包不在磁盘，跳过行数/字节比对" % PACK_NAME)
        return info

    rows, errors = read_jsonl(pack_path)
    disk_rows = len(rows) + len(errors)
    disk_bytes = pack_path.stat().st_size
    disk_sha = sha256_file(pack_path)
    problems = []
    if entry.get("rows") != disk_rows:
        problems.append("rows 清单=%r 磁盘=%d" % (entry.get("rows"), disk_rows))
    if entry.get("bytes") != disk_bytes:
        problems.append("bytes 清单=%r 磁盘=%d" % (entry.get("bytes"), disk_bytes))
    if isinstance(entry.get("sha256"), str) and entry["sha256"] != disk_sha:
        problems.append("sha256 清单=%s… 磁盘=%s…" % (entry["sha256"][:16], disk_sha[:16]))
    if problems:
        report.add(
            "index.rows", False,
            "清单与磁盘不一致（%s）：旧清单会让分发/校验对不上，"
            "跑 python -m i18n_tools.packindex 重新生成（UniversalI18n 侧）" % "；".join(problems),
            "error",
            {"entry": entry, "diskRows": disk_rows, "diskBytes": disk_bytes, "diskSha256": disk_sha},
        )
    else:
        report.add("index.rows", True,
                   "清单已登记 %s：rows=%d / bytes=%d / sha256 一致" % (PACK_NAME, disk_rows, disk_bytes),
                   "error")
    return info


def check_runtime(report: Report, dest_base, lang: str, source_pack: Path,
                  source_lang_dir: Path | None = None) -> dict:
    info = {"path": "", "present": False, "sha256": "", "sourceSha256": "", "match": None}
    if dest_base is None:
        report.info("runtime.path", "无法定位运行时目录（未设置 LOCALAPPDATA/USERPROFILE）；跳过运行时对账")
        return info
    runtime_pack = dest_base / lang / PACK_NAME
    info["path"] = p(runtime_pack)
    if not runtime_pack.is_file():
        report.info("runtime.pack", "运行时目录尚无 %s（首次运行前正常）：%s" % (PACK_NAME, runtime_pack))
        return info
    info["present"] = True
    info["sha256"] = sha256_file(runtime_pack)
    if not source_pack.is_file():
        report.info("runtime.pack", "运行时包存在但源包缺失，无法比对：%s" % runtime_pack)
        return info
    info["sourceSha256"] = sha256_file(source_pack)
    info["match"] = info["sha256"] == info["sourceSha256"]
    if source_lang_dir is not None and Path(source_lang_dir).is_dir() and dest_base is not None:
        src_names = {item.name for item in Path(source_lang_dir).glob("*.jsonl")}
        dst_dir = dest_base / lang
        dst_names = {item.name for item in dst_dir.glob("*.jsonl")} if dst_dir.is_dir() else set()
        missing = sorted(src_names - dst_names)
        if missing:
            report.warn("runtime.packs", False,
                        "运行时缺少 %d 个语言包（如 %s）→ 跑 --stage sync 补齐"
                        % (len(missing), "、".join(missing[:3])),
                        {"missingCount": len(missing), "missing": missing[:20]})
        else:
            report.info("runtime.packs", "运行时语言包与源目录一致（%d 个文件）" % len(src_names))
    if info["match"]:
        report.add("runtime.sha256", True, "运行时包与源包一致（sha256=%s…）" % info["sha256"][:16], "warn",
                   {"sha256": info["sha256"]})
    else:
        report.warn(
            "runtime.sha256", False,
            "运行时包与源包不一致：dest=%s… src=%s… → 该跑 --stage sync（无 --strict 时只告警）"
            % (info["sha256"][:16], info["sourceSha256"][:16]),
            {"destSha256": info["sha256"], "srcSha256": info["sourceSha256"], "path": p(runtime_pack)},
        )
    return info


# --------------------------------------------------------------------------- #
# --check
# --------------------------------------------------------------------------- #

def run_check(args) -> int:
    report = Report(strict=args.strict)
    summary = {
        "tool": "ckan_i18n_pipeline",
        "schema": 1,
        "action": "check",
        "strict": bool(args.strict),
        "roots": {},
        "vectors": {},
        "pool": {},
        "pack": {},
        "index": {},
        "runtime": {},
        "counts": {},
        "items": [],
    }
    try:
        paths = Paths(args)
    except PipelineError as exc:
        print(exc, file=sys.stderr)
        return exc.code
    summary["roots"] = paths.as_json()

    core = contract()
    vectors_ok = check_vectors(report, paths.extract_script)
    summary["vectors"] = {"script": p(paths.extract_script), "ok": vectors_ok}

    pool_stats, pool_by_h, pool_by_key = check_pool(report, paths.pool, core)
    pack_stats = check_pack(report, paths.pack, pool_by_h, pool_by_key, core)
    index_info = check_index(report, paths.index, paths.pack, paths.lang)
    runtime_info = check_runtime(report, paths.dest, paths.lang, paths.pack,
                                 paths.pack.parent if paths.pack.is_file() else None)

    if paths.pool.is_file() and pack_stats.get("present"):
        translated = pack_stats.get("inPool", 0)
        pool_stats["translated"] = translated
        pool_stats["missing"] = max(0, pool_stats.get("distinctH", 0) - translated)
        report.info(
            "pool.gap",
            "pool distinct h=%d，已译（命中源包）%d，缺口 %d"
            % (pool_stats["distinctH"], translated, pool_stats["missing"]),
            {"distinctH": pool_stats["distinctH"], "translated": translated, "missing": pool_stats["missing"]},
        )
    summary["pool"] = pool_stats
    summary["pack"] = pack_stats
    summary["index"] = index_info
    summary["runtime"] = runtime_info

    failed = report.failed()
    counts = report.counts()
    summary["counts"] = counts
    summary["items"] = report.items
    summary["ok"] = not failed
    summary["exitCode"] = EXIT_OK if not failed else EXIT_CHECK

    if args.json:
        print(json.dumps(summary, ensure_ascii=False))
        return summary["exitCode"]

    print("=== [check] 离线验收（零网络 / 零写盘） ===")
    report.render()
    print("")
    print("ckan-ui      : %s" % paths.ckan_ui_root)
    print("UniversalI18n: %s（来源：%s）" % (paths.ui18n, paths.ui18n_source))
    print("pool         : %s（%d 行 / distinct h %d / 已译 %d / 缺口 %d）"
          % (paths.pool, pool_stats.get("rows", 0), pool_stats.get("distinctH", 0),
             pool_stats.get("translated", 0), pool_stats.get("missing", 0)))
    print("源包         : %s（%d 行）" % (paths.pack, pack_stats.get("rows", 0)))
    print("清单         : %s（%s）"
          % (paths.index, "已登记" if index_info.get("registered") else "未登记 %s" % PACK_NAME))
    runtime_label = "不存在" if not runtime_info.get("present") else (
        "与源包一致" if runtime_info.get("match") else "与源包不一致 → 该跑 --stage sync")
    print("运行时       : %s（%s）" % (runtime_info.get("path") or "未定位", runtime_label))
    print("")
    verdict = "PASS" if not failed else "FAIL"
    print("[%s] checks=%d failedError=%d failedWarn=%d info=%d strict=%s"
          % (verdict, counts["total"], counts["failedError"], counts["failedWarn"],
             counts["info"], args.strict))
    for item in failed:
        print("  - %s: %s" % (item["id"], item["message"]), file=sys.stderr)
    return summary["exitCode"]


# --------------------------------------------------------------------------- #
# 阶段：extract / translate / sync
# --------------------------------------------------------------------------- #

def stage_extract(args, paths, sink, out_pool=None) -> int:
    """导出 pool。out_pool 非空时写到该路径（--plan-only 用临时文件，保证不改动 UniversalI18n 仓库）。"""
    target = Path(out_pool) if out_pool is not None else paths.pool
    print("\n=== [extract] CKAN 元数据 → pool ===", file=sink)
    if out_pool is not None:
        print("目标（临时，plan-only 不写仓库）: %s" % target, file=sink)
    cmd = [sys.executable, str(paths.extract_script), "--export-pool", str(target)]
    if paths.meta_root:
        cmd.extend(["--meta-root", str(paths.meta_root)])
    elif paths.repo_dir:
        cmd.extend(["--repo-dir", str(paths.repo_dir)])
    else:
        print(
            "[FAIL] 未找到 CKAN 数据源：--meta-root 未指定，且默认的 %%LOCALAPPDATA%%/CKAN/repos 不存在。\n"
            "修复：传 --repo-dir <CKAN 客户端缓存目录> 或 --meta-root <CKAN-meta 克隆>。",
            file=sys.stderr,
        )
        return EXIT_EXTRACT
    if args.dry_run:
        print("[dry-run] 不执行：%s" % " ".join(cmd), file=sink)
        return EXIT_OK
    target.parent.mkdir(parents=True, exist_ok=True)
    code = run_command(cmd, cwd=paths.ckan_ui_root, sink=sink)
    if code != 0:
        print("[FAIL] extract 阶段失败（退出码 %d）；pool 未更新。" % code, file=sys.stderr)
        return EXIT_EXTRACT
    return EXIT_OK


def stage_translate(args, paths, sink) -> int:
    print("\n=== [translate] pool → UniversalI18n 源包 ===", file=sink)
    if not paths.pool.is_file():
        print("[FAIL] pool 不存在：%s\n修复：先跑 --stage extract（或 --stage all）。" % paths.pool,
              file=sys.stderr)
        return EXIT_TRANSLATE
    tools_dir = paths.ui18n / "tools"
    if not (tools_dir / "i18n_tools").is_dir():
        print("[FAIL] UniversalI18n 缺少 tools/i18n_tools：%s" % tools_dir, file=sys.stderr)
        return EXIT_TRANSLATE
    cmd = [sys.executable, "-m", "i18n_tools.translate",
           "--pool", str(paths.pool),
           "--jobs", str(args.jobs)]
    if args.limit is not None:
        cmd.extend(["--limit", str(args.limit)])
    if args.dry_run:
        cmd.append("--dry-run")
    code = run_command(cmd, cwd=tools_dir, sink=sink)
    if code != 0:
        print("[FAIL] translate 阶段失败（退出码 %d）。" % code, file=sys.stderr)
        return EXIT_TRANSLATE
    return EXIT_OK


def _merge_overrides(source: Path, dest: Path) -> dict:
    """用户覆盖层只增不减：dest 已有的 (h, ctx) 键一律保留原位。"""
    source_rows, source_errors = read_jsonl(source)
    if dest.is_file():
        dest_text = dest.read_text(encoding="utf-8-sig")
        dest_rows, dest_errors = read_jsonl(dest)
    else:
        dest_text = ""
        dest_rows, dest_errors = [], []

    def key_of(row) -> str:
        return "%s:%s" % (row.get("h"), row.get("ctx") or "")

    existing = {key_of(row) for row in dest_rows}
    added = []
    for row in source_rows:
        key = key_of(row)
        if key in existing:
            continue
        existing.add(key)
        added.append(json.dumps(row, ensure_ascii=False, sort_keys=True))

    result = {
        "action": "merged" if added else "up-to-date",
        "added": len(added),
        "kept": len(dest_rows),
        "sourceRows": len(source_rows),
        "sourceErrors": source_errors,
        "destErrors": dest_errors,
    }
    if not added:
        return result

    body = dest_text
    if body and not body.endswith("\n"):
        body += "\n"
    body += "".join(line + "\n" for line in added)
    payload = body.encode("utf-8")
    tmp = Path(str(dest) + ".tmp")
    tmp.write_bytes(payload)
    if sha256_file(tmp) != sha256_bytes(payload):
        tmp.unlink(missing_ok=True)
        raise OSError("合并写入后校验失败：%s" % tmp)
    os.replace(tmp, dest)
    result["sha256"] = sha256_bytes(payload)
    return result


def _atomic_copy(source: Path, dest: Path) -> dict:
    """<dest>.tmp → 校验 sha256 → os.replace，事后复验。"""
    src_sha = sha256_file(source)
    before = sha256_file(dest) if dest.is_file() else None
    if before == src_sha:
        return {"action": "up-to-date", "srcSha256": src_sha, "beforeSha256": before,
                "afterSha256": before, "bytes": dest.stat().st_size}
    tmp = Path(str(dest) + ".tmp")
    dest.parent.mkdir(parents=True, exist_ok=True)
    with source.open("rb") as handle_in, tmp.open("wb") as handle_out:
        shutil.copyfileobj(handle_in, handle_out, 1 << 20)
        handle_out.flush()
        os.fsync(handle_out.fileno())
    tmp_sha = sha256_file(tmp)
    if tmp_sha != src_sha:
        tmp.unlink(missing_ok=True)
        raise OSError("临时文件 sha256 与源不一致：%s" % tmp)
    os.replace(tmp, dest)
    after = sha256_file(dest)
    if after != src_sha:
        raise OSError("替换后 sha256 与源不一致：%s" % dest)
    return {"action": "copied", "srcSha256": src_sha, "beforeSha256": before,
            "afterSha256": after, "bytes": dest.stat().st_size}


def discover_lang_packs(packs_dir: Path, langs: str) -> "dict[str, list[Path]]":
    """列出要同步的语言包：{语言目录名: [*.jsonl …]}。langs 为 all 或逗号分隔目录名。"""
    wanted = None
    if langs and langs.strip().lower() != "all":
        wanted = {item.strip().lower() for item in langs.split(",") if item.strip()}
    found: "dict[str, list[Path]]" = {}
    if not packs_dir.is_dir():
        return found
    for sub in sorted(packs_dir.iterdir()):
        if not sub.is_dir() or sub.name.lower() in SYNC_SKIP_LANG_DIRS:
            continue
        if wanted is not None and sub.name.lower() not in wanted:
            continue
        files = sorted(item for item in sub.iterdir()
                       if item.is_file() and item.name.lower().endswith(SYNC_LANG_PACK_SUFFIX))
        if files:
            found[sub.name] = files
    return found


def stage_sync(args, paths, sink) -> int:
    print("\n=== [sync] 源包 → ckan-ui 运行时（原子替换 + sha256 核对） ===", file=sink)
    if not check_vectors(Report(strict=False), paths.extract_script):
        print("[FAIL] 哈希向量不符，拒绝同步。", file=sys.stderr)
        return EXIT_SYNC
    if not paths.pack.is_file():
        print("[FAIL] 源包不存在：%s\n修复：先跑 --stage all 产出源包。" % paths.pack, file=sys.stderr)
        return EXIT_SYNC
    if paths.dest is None:
        print("[FAIL] 无法定位运行时目录（未设置 LOCALAPPDATA/USERPROFILE）；请传 --dest。", file=sys.stderr)
        return EXIT_SYNC

    core = contract()
    rows, errors = read_jsonl(paths.pack)
    _, groups = validate_pack(rows, {}, {}, core)
    blocking = [(item_id, violations) for item_id, violations in groups
                if violations and item_id in ("pack.hash", "pack.text", "pack.duplicate-h")]
    if errors or blocking:
        for item_id, violations in blocking:
            print("[FAIL] 源包 %s 违约：%s" % (item_id, _sample(violations)), file=sys.stderr)
        if errors:
            print("[FAIL] 源包有 %d 行无法解析：%s" % (len(errors), _sample(errors)), file=sys.stderr)
        print("[FAIL] 源包不满足契约，拒绝同步（先修包，别把坏包推进运行时）。", file=sys.stderr)
        return EXIT_SYNC

    ui_packs = paths.ui18n / "packs"
    lang_packs = discover_lang_packs(ui_packs, getattr(args, "langs", "all"))
    if not lang_packs:
        print("[FAIL] 在 %s 下没有找到任何语言包目录（--langs=%s）。"
              % (ui_packs, getattr(args, "langs", "all")), file=sys.stderr)
        return EXIT_SYNC

    plan = []
    for lang_dir, files in lang_packs.items():
        for source in files:
            plan.append(("copy", source, paths.dest / lang_dir / source.name))
    for name in SYNC_ROOT_COPY:
        plan.append(("copy", ui_packs / name, paths.dest / name))
    for name in SYNC_ROOT_MERGE:
        plan.append(("merge", ui_packs / name, paths.dest / name))

    total_packs = sum(len(files) for files in lang_packs.values())
    print("[INFO] 待同步语言包 %d 个文件 / %d 个语言目录（%s）"
          % (total_packs, len(lang_packs),
             "、".join("%s:%d" % (name, len(files)) for name, files in lang_packs.items())), file=sink)

    results = []
    failures = []
    for action, source, dest in plan:
        label = "%s -> %s" % (source.name, dest)
        if not source.is_file():
            print("[SKIP] 源不存在，跳过：%s" % source, file=sink)
            results.append({"action": "skipped", "source": p(source), "dest": p(dest)})
            continue
        if args.dry_run:
            print("[dry-run] %s %s" % (action, label), file=sink)
            results.append({"action": "dry-run", "source": p(source), "dest": p(dest)})
            continue
        try:
            if action == "merge":
                outcome = _merge_overrides(source, dest)
            else:
                outcome = _atomic_copy(source, dest)
        except OSError as exc:
            failures.append("%s：%s" % (label, exc))
            print("[FAIL] %s：%s" % (label, exc), file=sys.stderr)
            continue
        outcome.update({"source": p(source), "dest": p(dest)})
        results.append(outcome)
        print("[ OK ] %s（%s）" % (label, outcome["action"]), file=sink)

    leftover = list(paths.dest.rglob("*.tmp"))
    if leftover:
        failures.append("残留临时文件：%s" % ", ".join(p(item) for item in leftover))
        print("[FAIL] 残留临时文件：%s" % ", ".join(p(item) for item in leftover), file=sys.stderr)

    if args.json:
        print(json.dumps({"tool": "ckan_i18n_pipeline", "action": "sync",
                          "ok": not failures, "results": results, "failures": failures},
                         ensure_ascii=False))
    if failures:
        print("[FAIL] sync 阶段失败 %d 项。" % len(failures), file=sys.stderr)
        return EXIT_SYNC

    if not args.dry_run:
        dest_pack = paths.dest / paths.lang / PACK_NAME
        if dest_pack.is_file():
            src_sha = sha256_file(paths.pack)
            dst_sha = sha256_file(dest_pack)
            if src_sha != dst_sha:
                print("[FAIL] 同步后 sha256 不一致：dest=%s src=%s" % (dst_sha, src_sha), file=sys.stderr)
                return EXIT_SYNC
            print("[ OK ] 运行时包 sha256 与源包一致：%s" % dst_sha, file=sink)
    print("[ OK ] sync 完成（用户覆盖层只增不减，未被覆盖）。", file=sink)
    return EXIT_OK


def stage_dotnet(args, paths, sink) -> int:
    project = paths.ckan_ui_root / "tests" / "CkanUI.ModText.Tests" / "CkanUI.ModText.Tests.csproj"
    if not project.is_file():
        print("[FAIL] 找不到测试工程：%s" % project, file=sys.stderr)
        return EXIT_DOTNET
    print("\n=== [dotnet] ModText 测试门禁 ===", file=sink)
    code = run_command(["dotnet", "test", str(project)], cwd=paths.ckan_ui_root, sink=sink)
    if code != 0:
        print("[FAIL] dotnet test 失败（退出码 %d）。" % code, file=sys.stderr)
        return EXIT_DOTNET
    return EXIT_OK


# --------------------------------------------------------------------------- #
# --plan-only
# --------------------------------------------------------------------------- #

def report_pool_drift(repo_pool: Path, fresh_pool: Path, sink) -> None:
    """plan-only 只读仓库内 pool，同时报告"刚导出的临时 pool"与它的差异。"""
    if not fresh_pool.is_file():
        print("[plan] 临时 pool 未生成，跳过漂移对比。", file=sink)
        return
    fresh_rows, fresh_err = read_jsonl(fresh_pool)
    fresh_sha = sha256_file(fresh_pool)
    if not repo_pool.is_file():
        print("[plan] 仓库内还没有 pool；本次导出 %d 行（sha256=%s…）。该跑 --stage extract。"
              % (len(fresh_rows) + len(fresh_err), fresh_sha[:16]), file=sink)
        return
    repo_sha = sha256_file(repo_pool)
    repo_rows, repo_err = read_jsonl(repo_pool)
    repo_h = {row.get("h") for row in repo_rows if isinstance(row.get("h"), str)}
    fresh_h = {row.get("h") for row in fresh_rows if isinstance(row.get("h"), str)}
    added = len(fresh_h - repo_h)
    removed = len(repo_h - fresh_h)
    if repo_sha == fresh_sha:
        print("[plan] 仓库内 pool 与本次重导出一致（%d 行，sha256=%s…），无需更新。"
              % (len(fresh_rows) + len(fresh_err), fresh_sha[:16]), file=sink)
    else:
        print("[plan] 仓库内 pool 与本次重导出不一致：新增 %d / 消失 %d 个 h"
              "（仓库 %d 行 / 重导出 %d 行）。该跑 --stage extract。"
              % (added, removed, len(repo_rows) + len(repo_err), len(fresh_rows) + len(fresh_err)),
              file=sink)
    print("[plan] 本次没有改写仓库内 pool（导出到临时目录后删除）。", file=sink)


def print_plan(args, paths, sink) -> int:
    print("\n=== [plan] 执行计划（零网络；不调用 translate） ===", file=sink)
    pool_rows = 0
    distinct = 0
    pool_hashes = set()
    if paths.pool.is_file():
        rows, errors = read_jsonl(paths.pool)
        pool_rows = len(rows) + len(errors)
        distinct = len({row.get("h") for row in rows if isinstance(row.get("h"), str)})
        pool_hashes = {row.get("h") for row in rows}
        print("pool        : %s（%d 行 / distinct h %d）" % (paths.pool, pool_rows, distinct), file=sink)
    else:
        print("pool        : %s（不存在）" % paths.pool, file=sink)
    translated = 0
    if paths.pack.is_file():
        pack_rows, _ = read_jsonl(paths.pack)
        translated = sum(1 for row in pack_rows if row.get("h") in pool_hashes) if pool_hashes else len(pack_rows)
        print("源包        : %s（%d 行，命中 pool %d）" % (paths.pack, len(pack_rows), translated), file=sink)
    else:
        print("源包        : %s（尚未产出）" % paths.pack, file=sink)
    missing = max(0, distinct - translated)
    print("缺口        : %d 条（distinct h %d − 已译 %d）" % (missing, distinct, translated), file=sink)
    print("", file=sink)
    print("下一步：", file=sink)
    print("  1. 产出   : python i18n/tools/ckan_i18n_pipeline.py --stage all --jobs %d%s"
          % (args.jobs, "" if args.limit is None else " --limit %d" % args.limit), file=sink)
    print("  2. 验收   : python i18n/tools/ckan_i18n_pipeline.py --check", file=sink)
    print("  3. 同步   : python i18n/tools/ckan_i18n_pipeline.py --stage sync", file=sink)
    print("说明：这里没有调用 i18n_tools.translate --plan——它对 pool 只做分批统计，"
          "仍需真实 pool 与 .env.local；本模式用 pool 统计替代，全程零网络；"
          "pool 导出到临时目录并在统计后删除，仓库内 pool 不会被改写。",
          file=sink)
    return EXIT_OK


# --------------------------------------------------------------------------- #
# 主流程
# --------------------------------------------------------------------------- #

def run_stages(args) -> int:
    sink = sys.stderr if args.json else sys.stdout
    try:
        paths = Paths(args)
    except PipelineError as exc:
        print(exc, file=sys.stderr)
        return exc.code

    stage = args.stage
    print("=== [pipeline] stage=%s lang=%s%s ==="
          % (stage, paths.lang, " plan-only" if args.plan_only else ""), file=sink)
    print("ckan-ui      : %s" % paths.ckan_ui_root, file=sink)
    print("UniversalI18n: %s（来源：%s）" % (paths.ui18n, paths.ui18n_source), file=sink)
    print("pool         : %s" % paths.pool, file=sink)
    print("源包         : %s" % paths.pack, file=sink)
    print("运行时       : %s" % (paths.dest or "未定位"), file=sink)

    if stage in ("extract", "all") or args.plan_only:
        tmp_pool = None
        if args.plan_only and not args.dry_run:
            tmp_pool = Path(tempfile.mkdtemp(prefix="ckan-i18n-plan-")) / POOL_NAME
        try:
            code = stage_extract(args, paths, sink, out_pool=tmp_pool)
        finally:
            if tmp_pool is not None:
                report_pool_drift(paths.pool, tmp_pool, sink)
                shutil.rmtree(tmp_pool.parent, ignore_errors=True)
        if code != EXIT_OK:
            return code
    if args.plan_only:
        return print_plan(args, paths, sink)
    if stage in ("translate", "all"):
        code = stage_translate(args, paths, sink)
        if code != EXIT_OK:
            return code
    if stage in ("sync", "all"):
        code = stage_sync(args, paths, sink)
        if code != EXIT_OK:
            return code
    if args.dotnet_test:
        code = stage_dotnet(args, paths, sink)
        if code != EXIT_OK:
            return code
    print("\n[ OK ] 流水线完成（stage=%s）。" % stage, file=sink)
    return EXIT_OK


def build_parser():
    parser = argparse.ArgumentParser(
        prog="ckan_i18n_pipeline.py",
        description="CKAN 元数据 → UniversalI18n → ckan-ui 全链路编排与验收",
        epilog="退出码：0 成功 / 10 环境路径 / 11 extract / 12 translate / 13 sync / "
               "14 dotnet / 20 --check 不合格 / 21 --selftest 断言失败 / 2 参数错误",
    )
    parser.add_argument("--stage", choices=("extract", "translate", "sync", "all"), default="all",
                        help="只跑某个阶段（默认 all = extract → translate → sync）")
    parser.add_argument("--check", action="store_true", help="离线验收：零网络、零写盘，逐条核对链路健康")
    parser.add_argument("--strict", action="store_true", help="把 warn 也判红（用于 CI 硬门禁）")
    parser.add_argument("--json", action="store_true", help="机器可读：stdout 只输出一行 JSON")
    parser.add_argument("--selftest", action="store_true", help="临时目录造最小 fixtures，跑 --check/sync 正反路径")
    parser.add_argument("--plan-only", action="store_true", help="只导出 pool 并打印执行计划，不调 translate")
    parser.add_argument("--dry-run", action="store_true", help="不落盘：打印将要执行的命令与同步动作")
    parser.add_argument("--limit", type=int, default=None, help="仅翻译前 N 条（调试用）")
    parser.add_argument("--jobs", type=int, default=4, help="翻译并发批数（默认 4）")
    parser.add_argument("--lang", default=LANG, help="语言目录名（默认 zh-cn）")
    parser.add_argument("--langs", default="all",
                        help="sync 要搬运的语言目录：all 或逗号分隔（默认 all，覆盖所有存在语言包的语言）")
    parser.add_argument("--repo-dir", default=None, help="CKAN repos 缓存目录（默认 %%LOCALAPPDATA%%/CKAN/repos）")
    parser.add_argument("--meta-root", default=None, help="CKAN-meta 克隆根目录（优先于 --repo-dir）")
    parser.add_argument("--ckan-ui-root", default=None, help="ckan-ui 仓库根（默认从脚本位置上两级推导）")
    parser.add_argument("--ui18n-root", default=None, help="UniversalI18n 仓库根（默认按 env/external/相邻解析）")
    parser.add_argument("--extract-script", default=None,
                        help="抽取器路径（默认 <root>/i18n/tools/extract_ckan_meta.py）")
    parser.add_argument("--pool", default=None,
                        help="pool 路径（默认 <UI18n>/packs/ckan_pool.jsonl；extract 写此处）")
    parser.add_argument("--pack", default=None, help="源包路径（默认 <UI18n>/packs/<lang>/CKANMeta.jsonl）")
    parser.add_argument("--index", default=None, help="清单路径（默认 <UI18n>/packs/index.json）")
    parser.add_argument("--dest", default=None,
                        help="运行时 packs 根（默认 %%LOCALAPPDATA%%/CKAN/ckan-ui/i18n/packs）")
    parser.add_argument("--dotnet-test", action="store_true",
                        help="同步后跑 dotnet test（默认不跑，C# 侧另有独立门禁）")
    return parser


# --------------------------------------------------------------------------- #
# --selftest
# --------------------------------------------------------------------------- #

class SelfTest:
    def __init__(self) -> None:
        self.failures = []
        self.passed = 0

    def expect(self, name: str, condition: bool, detail: str = "") -> None:
        if condition:
            self.passed += 1
            print("[PASS] %s" % name)
        else:
            self.failures.append("%s%s" % (name, (": " + detail) if detail else ""))
            print("[FAIL] %s%s" % (name, (": " + detail) if detail else ""))

    def run(self, argv):
        env = dict(os.environ)
        env["PYTHONDONTWRITEBYTECODE"] = "1"
        env["PYTHONIOENCODING"] = "utf-8"
        proc = subprocess.run([sys.executable, str(THIS_FILE)] + list(argv), capture_output=True,
                              text=True, encoding="utf-8", errors="replace", env=env)
        return proc.returncode, proc.stdout or "", proc.stderr or ""


def _write_jsonl(path: Path, rows) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    body = "".join(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n" for row in rows)
    path.write_text(body, encoding="utf-8", newline="\n")


def _make_fixture(base: Path, core) -> dict:
    """最小 fixtures：假 pool + 假包 + 假清单 + 假运行时目录。"""
    pack_rows = [
        {"h": core.hash_hex("Alpha description"), "n": core.normalize("Alpha description"),
         "s": "Alpha description", "t": "阿尔法说明", "mod": MOD_NAME,
         "k": "ckan:SomeMod:description"},
        {"h": core.hash_hex("Beta   abstract"), "n": core.normalize("Beta   abstract"),
         "s": "Beta   abstract", "t": "贝塔摘要", "mod": MOD_NAME},
    ]
    pool_rows = [
        # 同一个 key 的历史文本（真实 pool 里 4833 行只有 3785 个 key，key 不是主键）
        {"id": 0, "h": core.hash_hex("Old alpha description"), "text": "Old alpha description",
         "mod": MOD_NAME, "src": "ckan", "key": "ckan:SomeMod:description", "ctx": "SomeMod",
         "field": "description", "len": len("Old alpha description")},
        {"id": 1, "h": pack_rows[0]["h"], "text": pack_rows[0]["s"], "mod": MOD_NAME,
         "src": "ckan", "key": "ckan:SomeMod:description", "ctx": "SomeMod",
         "field": "description", "len": len(pack_rows[0]["s"])},
        {"id": 2, "h": core.hash_hex("Gamma untranslated"), "text": "Gamma untranslated",
         "mod": MOD_NAME, "src": "ckan", "key": "ckan:Other:abstract", "ctx": "Other",
         "field": "abstract", "len": len("Gamma untranslated")},
    ]
    ui = base / "ui18n"
    pack = ui / "packs" / LANG / PACK_NAME
    pool = ui / "packs" / POOL_NAME
    _write_jsonl(pack, pack_rows)
    _write_jsonl(pool, pool_rows)
    _write_jsonl(ui / "packs" / "overrides.jsonl",
                 [{"h": pack_rows[0]["h"], "n": "Alpha description", "s": "Alpha description",
                   "t": "源包里的旧覆盖", "mod": MOD_NAME}])
    _write_jsonl(ui / "packs" / "glossary.jsonl",
                 [{"h": core.hash_hex("g"), "n": "g", "s": "g", "t": "术语", "mod": MOD_NAME}])
    index = ui / "packs" / INDEX_NAME
    index.write_text(json.dumps({
        "schema": 1, "lang": LANG, "generatedAt": "2026-01-01T00:00:00Z",
        "totalRows": len(pack_rows), "totalBytes": pack.stat().st_size, "packCount": 1,
        "packs": [{"mod": MOD_NAME, "file": "%s/%s" % (LANG, PACK_NAME),
                   "rows": len(pack_rows), "bytes": pack.stat().st_size,
                   "sha256": sha256_file(pack), "keepRows": 0}],
    }, indent=2) + "\n", encoding="utf-8")
    runtime = base / "runtime"
    (runtime / LANG).mkdir(parents=True, exist_ok=True)
    shutil.copyfile(pack, runtime / LANG / PACK_NAME)
    fake_ok = base / "fake-vectors-ok.py"
    fake_ok.write_text("import sys\nsys.exit(0)\n", encoding="utf-8")
    fake_fail = base / "fake-vectors-fail.py"
    fake_fail.write_text(
        "import sys\n"
        "print('向量不符：foobar 期望 85944171f73967e8，实得 0000000000000000', file=sys.stderr)\n"
        "sys.exit(1)\n",
        encoding="utf-8",
    )
    fake_ui = base / "fake-ckan-ui"
    (fake_ui / "i18n" / "tools").mkdir(parents=True, exist_ok=True)
    return {"ui": ui, "pack": pack, "pool": pool, "index": index, "runtime": runtime,
            "fakeOk": fake_ok, "fakeFail": fake_fail, "fakeUi": fake_ui,
            "packRows": pack_rows, "poolRows": pool_rows}


def run_selftest() -> int:
    core = contract()
    suite = SelfTest()
    base = Path(tempfile.mkdtemp(prefix="ckan-i18n-selftest-"))
    print("=== [selftest] fixtures: %s ===" % base)
    try:
        fixture = _make_fixture(base, core)
        ui = fixture["ui"]
        pack = fixture["pack"]
        runtime = fixture["runtime"]

        def refresh_index():
            rows, _ = read_jsonl(pack)
            data = json.loads(fixture["index"].read_text(encoding="utf-8"))
            data["totalRows"] = len(rows)
            data["totalBytes"] = pack.stat().st_size
            data["packCount"] = 1
            data["packs"] = [{"mod": MOD_NAME, "file": "%s/%s" % (LANG, PACK_NAME),
                              "rows": len(rows), "bytes": pack.stat().st_size,
                              "sha256": sha256_file(pack), "keepRows": 0}]
            fixture["index"].write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")

        def check_args(dest=None, extract=None, extra=None, root=None):
            argv = ["--check", "--json", "--ui18n-root", str(ui),
                    "--ckan-ui-root", str(root or REPO_ROOT),
                    "--extract-script", str(extract or fixture["fakeOk"]),
                    "--dest", str(dest or runtime)]
            return argv + list(extra or [])

        def as_json(out):
            for line in reversed([line for line in out.splitlines() if line.strip()]):
                try:
                    return json.loads(line)
                except json.JSONDecodeError:
                    continue
            return {}

        def flagged(payload, item_id):
            return any(item["id"] == item_id and not item["ok"] for item in payload.get("items", []))

        # 1) 正常路径
        code, out, err = suite.run(check_args())
        payload = as_json(out)
        suite.expect("happy: exit 0", code == 0, "code=%d stderr=%s" % (code, err.strip()[:300]))
        suite.expect("happy: json ok", payload.get("ok") is True, str(payload.get("counts")))
        suite.expect("happy: 无 error 级失败", (payload.get("counts") or {}).get("failedError") == 0,
                     str(payload.get("counts")))
        suite.expect("happy: runtime match", (payload.get("runtime") or {}).get("match") is True,
                     str(payload.get("runtime")))
        suite.expect("happy: k 行被识别", (payload.get("pack") or {}).get("kRows") == 1,
                     str(payload.get("pack")))
        suite.expect("happy: 已译/缺口统计", (payload.get("pool") or {}).get("distinctH") == 3
                     and (payload.get("pool") or {}).get("translated") == 1
                     and (payload.get("pool") or {}).get("missing") == 2, str(payload.get("pool")))
        suite.expect("happy: 同 key 多条历史文本不误判 k-hash",
                     not flagged(payload, "pack.k-hash"), str(payload.get("pack")))

        # 2) 向量失败
        code, out, _ = suite.run(check_args(extract=fixture["fakeFail"]))
        payload = as_json(out)
        suite.expect("vectors-fail: exit 20", code == EXIT_CHECK, "code=%d" % code)
        suite.expect("vectors-fail: vectors 标红", flagged(payload, "vectors"))

        # 3) 行哈希被篡改
        bad = [dict(row) for row in fixture["packRows"]]
        bad[0]["h"] = "0" * 16
        _write_jsonl(pack, bad)
        code, out, _ = suite.run(check_args())
        payload = as_json(out)
        suite.expect("bad-hash: exit 20", code == EXIT_CHECK, "code=%d" % code)
        suite.expect("bad-hash: pack.hash 标红", flagged(payload, "pack.hash"))
        suite.expect("bad-hash: k 与 pool 不对齐同时报", flagged(payload, "pack.k-hash"))

        # 4) n 与 normalize(s) 不一致
        bad = [dict(row) for row in fixture["packRows"]]
        bad[0]["s"] = "Alpha    description"
        bad[0]["n"] = "Alpha    description"
        _write_jsonl(pack, bad)
        code, out, _ = suite.run(check_args())
        payload = as_json(out)
        suite.expect("bad-normalize: exit 20", code == EXIT_CHECK, "code=%d" % code)
        suite.expect("bad-normalize: pack.normalized 标红", flagged(payload, "pack.normalized"))

        # 5) t 为空
        bad = [dict(row) for row in fixture["packRows"]]
        bad[0]["t"] = "  "
        _write_jsonl(pack, bad)
        suite.expect("empty-t: exit 20", suite.run(check_args())[0] == EXIT_CHECK)

        # 6) 同包 h 重复
        bad = [dict(row) for row in fixture["packRows"]]
        bad[1] = dict(bad[0])
        bad[1]["t"] = "重复行"
        _write_jsonl(pack, bad)
        code, out, _ = suite.run(check_args())
        payload = as_json(out)
        suite.expect("dup-h: exit 20", code == EXIT_CHECK, "code=%d" % code)
        suite.expect("dup-h: duplicate 标红", flagged(payload, "pack.duplicate-h"))

        # 7) k 形状非法
        bad = [dict(row) for row in fixture["packRows"]]
        bad[0]["k"] = "not-a-key"
        _write_jsonl(pack, bad)
        code, out, _ = suite.run(check_args())
        payload = as_json(out)
        suite.expect("bad-k-shape: exit 20", code == EXIT_CHECK, "code=%d" % code)
        suite.expect("bad-k-shape: k-shape 标红", flagged(payload, "pack.k-shape"))

        # 8) k 在 pool 里不存在
        bad = [dict(row) for row in fixture["packRows"]]
        bad[0]["k"] = "ckan:Ghost:abstract"
        _write_jsonl(pack, bad)
        code, out, _ = suite.run(check_args())
        payload = as_json(out)
        suite.expect("orphan-k: exit 20", code == EXIT_CHECK, "code=%d" % code)
        suite.expect("orphan-k: k-orphan 标红", flagged(payload, "pack.k-orphan"))

        # 9) 旧格式（无 k）不判红，只记 notice
        _write_jsonl(pack, [{k: v for k, v in row.items() if k != "k"}
                            for row in fixture["packRows"]])
        refresh_index()
        code, out, _ = suite.run(check_args())
        payload = as_json(out)
        suite.expect("legacy-k: exit 0（k 缺失不判红）", code == 0, "code=%d" % code)
        suite.expect("legacy-k: kRows 计数为 0", (payload.get("pack") or {}).get("kRows") == 0,
                     str(payload.get("pack")))

        # 10) 清单未登记 → warn（非 strict 通过 / strict 判红）
        _write_jsonl(pack, fixture["packRows"])
        data = json.loads(fixture["index"].read_text(encoding="utf-8"))
        data["packs"] = []
        data["packCount"] = 0
        fixture["index"].write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
        code, out, _ = suite.run(check_args())
        payload = as_json(out)
        suite.expect("index-unregistered: exit 0（warn 分级）", code == 0, "code=%d" % code)
        suite.expect("index-unregistered: 有 warn",
                     (payload.get("counts") or {}).get("failedWarn", 0) >= 1,
                     str(payload.get("counts")))
        suite.expect("index-unregistered: --strict 判红",
                     suite.run(check_args(extra=["--strict"]))[0] == EXIT_CHECK)

        # 11) 清单登记了但行数不一致 → 红
        refresh_index()
        data = json.loads(fixture["index"].read_text(encoding="utf-8"))
        data["packs"][0]["rows"] = 999
        fixture["index"].write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
        code, out, _ = suite.run(check_args())
        payload = as_json(out)
        suite.expect("index-drift: exit 20", code == EXIT_CHECK, "code=%d" % code)
        suite.expect("index-drift: index.rows 标红", flagged(payload, "index.rows"))
        refresh_index()

        # 12) 运行时漂移 → warn；--strict → 红
        drifted = [dict(row) for row in fixture["packRows"]]
        drifted[1]["t"] = "手工改过的译文"
        _write_jsonl(runtime / LANG / PACK_NAME, drifted)
        code, out, _ = suite.run(check_args())
        payload = as_json(out)
        suite.expect("runtime-drift: 非 strict 通过", code == 0, "code=%d" % code)
        suite.expect("runtime-drift: match=false", (payload.get("runtime") or {}).get("match") is False)
        suite.expect("runtime-drift: 计入 failedWarn",
                     (payload.get("counts") or {}).get("failedWarn", 0) >= 1,
                     str(payload.get("counts")))
        suite.expect("runtime-drift: --strict 判红",
                     suite.run(check_args(extra=["--strict"]))[0] == EXIT_CHECK)
        shutil.copyfile(pack, runtime / LANG / PACK_NAME)

        # 13) sync：原子替换 + sha 核对 + 覆盖层只增不减
        _write_jsonl(ui / "packs" / "overrides.jsonl", [
            {"h": fixture["packRows"][0]["h"], "n": "Alpha description",
             "s": "Alpha description", "t": "源包里的旧覆盖", "mod": MOD_NAME},
            {"h": core.hash_hex("New override"), "n": "New override", "s": "New override",
             "t": "新的覆盖", "mod": MOD_NAME},
        ])
        sync_dest = base / "sync-dest"
        (sync_dest / LANG).mkdir(parents=True, exist_ok=True)
        (sync_dest / "overrides.jsonl").write_text(
            json.dumps({"h": fixture["packRows"][0]["h"], "n": "Alpha description",
                        "s": "Alpha description", "t": "本机保留的覆盖", "mod": MOD_NAME},
                       ensure_ascii=False) + "\n", encoding="utf-8")
        sync_argv = ["--stage", "sync", "--ui18n-root", str(ui), "--ckan-ui-root", str(REPO_ROOT),
                     "--extract-script", str(fixture["fakeOk"]), "--dest", str(sync_dest)]
        code, out, err = suite.run(sync_argv)
        synced = sync_dest / LANG / PACK_NAME
        suite.expect("sync: exit 0", code == 0, "code=%d err=%s" % (code, err.strip()[:300]))
        suite.expect("sync: 目标包存在", synced.is_file())
        suite.expect("sync: sha256 与源一致",
                     synced.is_file() and sha256_file(synced) == sha256_file(pack))
        suite.expect("sync: 无 .tmp 残留", not list(sync_dest.rglob("*.tmp")))
        suite.expect("sync: glossary 已搬运", (sync_dest / "glossary.jsonl").is_file())
        overrides_text = (sync_dest / "overrides.jsonl").read_text(encoding="utf-8")
        suite.expect("sync: 用户覆盖未被覆盖", "本机保留的覆盖" in overrides_text, overrides_text[:200])
        suite.expect("sync: 覆盖层补入新行", len(overrides_text.strip().splitlines()) == 2,
                     overrides_text)
        before = synced.stat().st_mtime_ns
        suite.run(sync_argv)
        suite.expect("sync: 幂等（up-to-date 不重写）", synced.stat().st_mtime_ns == before)

        # 13b) 语言包全集：CKANMeta 之外的游戏内语言包同样要进运行时
        extra_pack = ui / "packs" / LANG / "OtherMod.jsonl"
        _write_jsonl(extra_pack, [dict(fixture["packRows"][0], mod="OtherMod")])
        code, out, err = suite.run(sync_argv)
        suite.expect("sync: 其余语言包一并搬运",
                     (sync_dest / LANG / "OtherMod.jsonl").is_file(), err.strip()[:200])
        suite.expect("sync: 全集同步后 exit 0", code == 0, "code=%d" % code)

        # 13c) --check 报出运行时缺包（快照是运行时点，缺文件必须可见）
        payload = as_json(suite.run(check_args())[1])
        suite.expect("check: 报出运行时缺包 runtime.packs",
                     flagged(payload, "runtime.packs"),
                     str([i.get("id") for i in payload.get("items", [])])[:300])

        # 13d) --langs 指定不存在的语言目录时直接判红
        code, _, _ = suite.run(sync_argv + ["--langs", "ja"])
        suite.expect("sync: --langs 无匹配目录 exit 13", code == EXIT_SYNC, "code=%d" % code)
        extra_pack.unlink()

        # 14) sync 拒绝坏包
        bad = [dict(row) for row in fixture["packRows"]]
        bad[0]["h"] = "f" * 16
        _write_jsonl(pack, bad)
        code, out, err = suite.run(sync_argv)
        suite.expect("sync: 坏包拒绝同步（exit 13）", code == EXIT_SYNC, "code=%d" % code)
        _write_jsonl(pack, fixture["packRows"])

        # 15) 零硬编码：UI18n 根解析不到时给可操作错误
        env_backup = os.environ.pop(ENV_UI18N, None)
        try:
            code, out, err = suite.run([
                "--check", "--json", "--ui18n-root", str(base / "nope"),
                "--ckan-ui-root", str(fixture["fakeUi"]),
                "--extract-script", str(fixture["fakeOk"]), "--dest", str(runtime)])
        finally:
            if env_backup is not None:
                os.environ[ENV_UI18N] = env_backup
        suite.expect("resolve: 缺失 UI18n 根 exit 10", code == EXIT_ENV, "code=%d" % code)
        suite.expect("resolve: 错误信息可操作", ENV_UI18N in (err + out), (err + out)[:300])

        # 16) --check 零写盘：fixtures 全树 sha256 前后一致
        def tree_snapshot(root):
            snap = {}
            for entry in sorted(root.rglob("*")):
                if entry.is_file():
                    snap[str(entry.relative_to(root))] = sha256_file(entry)
            return snap

        before_tree = tree_snapshot(base)
        code, _, _ = suite.run(check_args())
        after_tree = tree_snapshot(base)
        suite.expect("check: 零写盘（全树 sha256 未变）", code == 0 and before_tree == after_tree,
                     "changed=%s" % sorted(set(before_tree) ^ set(after_tree)))

        # 17) --check 幂等输出（可粘贴的机器可读摘要稳定）
        first, second = suite.run(check_args()), suite.run(check_args())
        suite.expect("check: JSON 输出稳定", first[0] == second[0] and first[1] == second[1])

        # 16) 真实仓库 --check（只读）：退出码只允许 0/20，stdout 仍是一行 JSON
        code, out, _ = suite.run(["--check", "--json"])
        suite.expect("real-check: 退出码为 0 或 20", code in (0, EXIT_CHECK), "code=%d" % code)
        suite.expect("real-check: 输出是一行 JSON", len([l for l in out.splitlines() if l.strip()]) == 1,
                     out[:200])
    finally:
        if suite.failures:
            print("\n[KEEP] fixtures 保留供排查：%s" % base)
        else:
            shutil.rmtree(base, ignore_errors=True)

    print("")
    if suite.failures:
        print("[FAIL] selftest %d 项失败 / %d 项通过" % (len(suite.failures), suite.passed))
        for item in suite.failures:
            print("  - %s" % item)
        return EXIT_SELFTEST
    print("[ OK ] selftest 全部通过（%d 项）" % suite.passed)
    return EXIT_OK


def main(argv) -> int:
    _configure_stdout()
    args = build_parser().parse_args(argv)
    try:
        if args.selftest:
            return run_selftest()
        if args.check:
            return run_check(args)
        return run_stages(args)
    except PipelineError as exc:
        print("[FAIL] %s" % exc, file=sys.stderr)
        return exc.code


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
