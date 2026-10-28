#!/usr/bin/env python3
"""CKAN-meta 元数据候选抽取（M11 MTI-01 / MTI-02 / MTI-09）。

把 CKAN-meta 仓库里的模组摘要/说明抽成运行时读取的 NDJSON 候选文件，
哈希与归一化规则必须与 C# 侧 UI18n.Core 完全一致，否则读取器永远命不中：

    h = fnv1a64(Normalize(sourceText)) 的 16 位小写十六进制
    Normalize = NFKC -> 换行折成空格 -> 空白折叠 -> trim

用法：
    python i18n/tools/extract_ckan_meta.py --verify-vectors
    python i18n/tools/extract_ckan_meta.py --meta-root <CKAN-meta> --out <CKANMeta.jsonl> [--state <state.json>]

只做读与写文件，不联网、不调用任何外部服务。
"""

from __future__ import annotations

import argparse
import datetime
import fnmatch
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


def load_candidates(meta_root: str):
    """产出去重后的候选：(hash, identifier, field, source_text)。

    同一个 identifier 可能有多个 .ckan（多版本），文本相同即同一条候选——按 h 去重。
    """
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


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="CKAN-meta 元数据候选抽取")
    parser.add_argument("--meta-root", help="CKAN-meta 仓库根目录")
    parser.add_argument("--out", help="输出的 NDJSON 文件路径")
    parser.add_argument("--state", help="增量状态文件（记录每条的首次出现日期，供孤儿清理）")
    parser.add_argument("--verify-vectors", action="store_true", help="只跑官方向量自检")
    args = parser.parse_args(argv)

    if args.verify_vectors:
        if verify_vectors():
            print("官方向量全部通过。")
            return 0
        return 1

    if not args.meta_root or not args.out:
        parser.error("需要 --meta-root 与 --out（或用 --verify-vectors 做自检）")

    candidates = load_candidates(args.meta_root)
    lines = render_lines(candidates)

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    tmp = args.out + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as handle:
        for line in lines:
            handle.write(line + "\n")
    os.replace(tmp, args.out)

    previous = load_state(args.state)
    previous_hashes = previous.get("hashes") if isinstance(previous.get("hashes"), dict) else {}
    today = datetime.date.today().isoformat()
    seen = {key: previous_hashes.get(key, today) for key, _i, _f, _t in candidates}
    added = sum(1 for key in seen if key not in previous_hashes)
    removed = sum(1 for key in previous_hashes if key not in seen)

    if args.state:
        write_state(args.state, seen, added, removed, len(candidates))

    print(f"候选 {len(candidates)} 条 -> {args.out}（新增 {added}，消失 {removed}）")
    if removed and args.state:
        orphans = [k for k in previous_hashes if k not in seen]
        oldest = min(previous_hashes[k] for k in orphans)
        print(f"首批消失条目的最早出现日期：{oldest}（30 天后可清理孤儿译文）")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
