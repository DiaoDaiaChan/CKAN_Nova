#!/usr/bin/env python3
"""CKAN-meta 元数据候选抽取（M11 MTI-01 / MTI-02 / MTI-09）。

把 CKAN-meta 仓库或 CKAN 客户端缓存的 repo.json 里的模组摘要/说明抽成运行时读取的
NDJSON 候选文件及 UniversalI18n 翻译池文件。
哈希与归一化规则必须与 C# 侧 UI18n.Core 完全一致，否则读取器永远命不中：

    h = fnv1a64(Normalize(sourceText)) 的 16 位小写十六进制
    Normalize = NFKC -> 换行折成空格 -> 空白折叠 -> trim

用法：
    python i18n/tools/extract_ckan_meta.py --verify-vectors
    python i18n/tools/extract_ckan_meta.py --repo-dir <AppData/Local/CKAN/repos> --out <CKANMeta.jsonl> [--export-pool <pool.jsonl>]
    python i18n/tools/extract_ckan_meta.py --repo-json <repo.json> --out <CKANMeta.jsonl>
    python i18n/tools/extract_ckan_meta.py --meta-root <CKAN-meta> --out <CKANMeta.jsonl>

只做读与写文件，不联网、不调用任何外部服务。
"""

from __future__ import annotations

import argparse
import datetime
import fnmatch
import glob
import json
import os
import re
import sys
import unicodedata

OFFSET_BASIS = 0xCBF29CE484222325
PRIME = 0x100000001B3
MASK64 = 0xFFFFFFFFFFFFFFFF

# 官方向量（17 篇 §2.2），任何实现漂移都会先在这里炸掉。
OFFICIAL_VECTORS = {
    "": "cbf29ce484222325",
    "a": "af63dc4c8601ec8c",
    "foobar": "85944171f73967e8",
}

FIELDS = ("abstract", "description")
MOD_NAME = "CKANMeta"


def normalize(text: str) -> str:
    """与 UI18n.Core.Text.TextNormalizer 对齐：NFKC + 空白折叠 + trim。"""
    if not text:
        return ""
    folded = unicodedata.normalize("NFKC", text)
    collapsed = re.sub(r"\s+", " ", folded)
    return collapsed.strip()


def fnv1a64(text: str) -> int:
    """UTF-8 逐字节 FNV-1a 64；与 UI18n.Core.Hashing.Fnv1a 一致（不做小写折叠）。"""
    value = OFFSET_BASIS
    for byte in normalize(text).encode("utf-8", errors="replace"):
        value ^= byte
        value = (value * PRIME) & MASK64
    return value


def hash_hex(text: str) -> str:
    return format(fnv1a64(text), "016x")


def verify_vectors() -> bool:
    ok = True
    for source, expected in OFFICIAL_VECTORS.items():
        actual = hash_hex(source)
        if actual != expected:
            ok = False
            print(f"向量不符：{source!r} 期望 {expected}，实得 {actual}", file=sys.stderr)
    return ok


def iter_meta_files(meta_root: str):
    """递归枚举 *.ckan；CKAN-meta 的目录布局是按 mod 分目录，文件序必须稳定。"""
    for directory, _dirs, files in os.walk(meta_root):
        for name in sorted(files):
            if fnmatch.fnmatch(name, "*.ckan"):
                yield os.path.join(directory, name)


def load_candidates_from_meta_root(meta_root: str):
    """从 CKAN-meta 仓库目录枚举 *.ckan 提取候选。"""
    seen: dict[str, tuple[str, str, str, str]] = {}
    for path in iter_meta_files(meta_root):
        try:
            with open(path, "r", encoding="utf-8-sig") as handle:
                payload = json.load(handle)
        except (OSError, ValueError) as error:
            print(f"跳过无法解析的文件 {path}: {error}", file=sys.stderr)
            continue

        identifier = str(payload.get("identifier") or "").strip()
        if not identifier:
            continue

        for field in FIELDS:
            text = payload.get(field)
            if not isinstance(text, str) or not text.strip():
                continue
            key = hash_hex(text)
            seen.setdefault(key, (key, identifier, field, text))
    return [seen[key] for key in sorted(seen)]


def load_candidates_from_repo_json(repo_json_path: str, seen: dict[str, tuple[str, str, str, str]] | None = None):
    """从 CKAN 缓存的 repo.json (如 4A85405A-KSP-默认数据库.json) 提取候选。"""
    if seen is None:
        seen = {}
    try:
        with open(repo_json_path, "r", encoding="utf-8-sig") as handle:
            data = json.load(handle)
    except (OSError, ValueError) as error:
        print(f"跳过无法解析的 repo 文件 {repo_json_path}: {error}", file=sys.stderr)
        return seen

    if not isinstance(data, dict):
        return seen

    modules = data.get("available_modules") or {}
    for identifier, mod_data in modules.items():
        if not isinstance(mod_data, dict):
            continue
        ident_str = str(identifier).strip()
        if not ident_str:
            continue

        versions = mod_data.get("module_version") or {}
        for _ver_name, ver_info in versions.items():
            if not isinstance(ver_info, dict):
                continue
            for field in FIELDS:
                text = ver_info.get(field)
                if not isinstance(text, str) or not text.strip():
                    continue
                key = hash_hex(text)
                seen.setdefault(key, (key, ident_str, field, text))
    return seen


def load_candidates_from_repo_dir(repo_dir: str):
    """从包含多个 *.json 的 CKAN repos 目录提取候选并去重。"""
    seen: dict[str, tuple[str, str, str, str]] = {}
    pattern = os.path.join(repo_dir, "*.json")
    for file_path in sorted(glob.glob(pattern)):
        basename = os.path.basename(file_path).lower()
        if basename in ("etags.json", "auth.json"):
            continue
        load_candidates_from_repo_json(file_path, seen)
    return [seen[key] for key in sorted(seen)]


def render_lines(candidates) -> list[str]:
    lines = []
    for key, identifier, field, text in candidates:
        record = {
            "h": key,
            "n": f"ckan:{identifier}:{field}",
            "s": text,
            "mod": MOD_NAME,
            "ctx": identifier,
        }
        lines.append(json.dumps(record, ensure_ascii=False, sort_keys=True))
    return lines


def render_pool_lines(candidates) -> list[str]:
    """导出为符合 UniversalI18n 规范的 pool.jsonl。"""
    lines = []
    for idx, (key, identifier, field, text) in enumerate(candidates, 1):
        record = {
            "id": idx,
            "h": key,
            "text": text,
            "mod": MOD_NAME,
            "src": "ckan",
            "key": f"ckan:{identifier}:{field}",
            "ctx": identifier,
            "field": field,
            "len": len(text),
        }
        lines.append(json.dumps(record, ensure_ascii=False))
    return lines


def load_state(path: str) -> dict:
    if not path or not os.path.exists(path):
        return {}
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            data = json.load(handle)
        return data if isinstance(data, dict) else {}
    except (OSError, ValueError):
        return {}


def write_state(path: str, seen: dict, added: int, removed: int, total: int) -> None:
    payload = {
        "generatedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "count": total,
        "added": added,
        "removed": removed,
        "hashes": seen,
    }
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=1, sort_keys=True)
    os.replace(tmp, path)


def get_default_ckan_repo_dir() -> str | None:
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        target = os.path.join(local_app_data, "CKAN", "repos")
        if os.path.isdir(target):
            return target
    return None


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="CKAN-meta 元数据候选抽取")
    parser.add_argument("--meta-root", help="CKAN-meta 仓库根目录")
    parser.add_argument("--repo-json", help="单个 CKAN repo json 路径")
    parser.add_argument("--repo-dir", help="CKAN repos 缓存目录（默认自动探测 LocalAppData/CKAN/repos）")
    parser.add_argument("--out", help="输出的 NDJSON 候选文件路径")
    parser.add_argument("--export-pool", help="输出 UniversalI18n 所需的 pool.jsonl 文件路径")
    parser.add_argument("--state", help="增量状态文件（记录每条首次出现日期，供孤儿清理）")
    parser.add_argument("--verify-vectors", action="store_true", help="只跑官方向量自检")
    args = parser.parse_args(argv)

    if args.verify_vectors:
        if verify_vectors():
            print("官方向量全部通过。")
            return 0
        return 1

    if args.meta_root:
        candidates = load_candidates_from_meta_root(args.meta_root)
    elif args.repo_json:
        dict_candidates = load_candidates_from_repo_json(args.repo_json)
        candidates = [dict_candidates[k] for k in sorted(dict_candidates)]
    else:
        repo_dir = args.repo_dir or get_default_ckan_repo_dir()
        if not repo_dir or not os.path.isdir(repo_dir):
            parser.error("未能找到 CKAN repos 目录，请指定 --repo-dir、--repo-json 或 --meta-root")
        candidates = load_candidates_from_repo_dir(repo_dir)

    if not args.out and not args.export_pool:
        parser.error("请指定至少一个输出路径：--out <CKANMeta.jsonl> 或 --export-pool <pool.jsonl>")

    if args.out:
        lines = render_lines(candidates)
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        tmp = args.out + ".tmp"
        with open(tmp, "w", encoding="utf-8", newline="\n") as handle:
            for line in lines:
                handle.write(line + "\n")
        os.replace(tmp, args.out)
        print(f"候选 {len(candidates)} 条 -> {args.out}")

    if args.export_pool:
        pool_lines = render_pool_lines(candidates)
        os.makedirs(os.path.dirname(os.path.abspath(args.export_pool)), exist_ok=True)
        tmp = args.export_pool + ".tmp"
        with open(tmp, "w", encoding="utf-8", newline="\n") as handle:
            for line in pool_lines:
                handle.write(line + "\n")
        os.replace(tmp, args.export_pool)
        print(f"UniversalI18n pool 导出 {len(candidates)} 条 -> {args.export_pool}")

    if args.state:
        previous = load_state(args.state)
        previous_hashes = previous.get("hashes") if isinstance(previous.get("hashes"), dict) else {}
        today = datetime.date.today().isoformat()
        seen = {key: previous_hashes.get(key, today) for key, _i, _f, _t in candidates}
        added = sum(1 for key in seen if key not in previous_hashes)
        removed = sum(1 for key in previous_hashes if key not in seen)
        write_state(args.state, seen, added, removed, len(candidates))
        print(f"状态更新：新增 {added}，消失 {removed}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
