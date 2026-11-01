#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""CKAN 元数据国际化翻译流水线编排器 (ckan_i18n_pipeline.py)"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

DEFAULT_CKAN_REPO_DIR = os.path.expandvars("%LOCALAPPDATA%\\CKAN\\repos")
DEFAULT_UI18N_REPO = r"C:\Users\43701\Documents\github\UniversalI18n"
DEFAULT_CKAN_UI_ROOT = r"C:\Users\43701\Documents\github\ckan-ui"

def run_command(cmd: list[str], cwd: str | None = None) -> tuple[int, str, str]:
    print(f"[RUN] {' '.join(cmd)} (cwd={cwd or '.'})")
    env = dict(os.environ)
    env["PYTHONIOENCODING"] = "utf-8"
    proc = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace", env=env)
    if proc.stdout:
        sys.stdout.buffer.write(proc.stdout.encode("utf-8", errors="replace"))
        sys.stdout.buffer.flush()
    if proc.stderr:
        sys.stderr.buffer.write(proc.stderr.encode("utf-8", errors="replace"))
        sys.stderr.buffer.flush()
    return proc.returncode, proc.stdout, proc.stderr

def stage_extract(ckan_ui_root: str, repo_dir: str, pool_out: str) -> bool:
    print("\n=== [Stage 1: Extractor Agent] 抽取 CKAN 元数据候选并生成翻译池 ===")
    extract_script = os.path.join(ckan_ui_root, "i18n", "tools", "extract_ckan_meta.py")
    cmd = [
        sys.executable,
        extract_script,
        "--repo-dir", repo_dir,
        "--export-pool", pool_out,
    ]
    code, out, _ = run_command(cmd, cwd=ckan_ui_root)
    return code == 0

def stage_translate(ui18n_root: str, pool_path: str, limit: int | None = None, jobs: int = 4, dry_run: bool = False) -> bool:
    print("\n=== [Stage 2: Translation Worker Agent] 调用 UniversalI18n 翻译引擎 ===")
    tools_dir = os.path.join(ui18n_root, "tools")
    cmd = [
        sys.executable,
        "-m", "i18n_tools.translate",
        "--pool", pool_path,
        "--jobs", str(jobs),
    ]
    if limit is not None:
        cmd.extend(["--limit", str(limit)])
    if dry_run:
        cmd.append("--dry-run")

    code, out, _ = run_command(cmd, cwd=tools_dir)
    return code == 0

def stage_sync_and_verify(ckan_ui_root: str, ui18n_root: str) -> bool:
    print("\n=== [Stage 3: QA & Sync Agent] 校验契约并同步到 ckan-ui 运行时 ===")
    extract_script = os.path.join(ckan_ui_root, "i18n", "tools", "extract_ckan_meta.py")
    code, _, _ = run_command([sys.executable, extract_script, "--verify-vectors"], cwd=ckan_ui_root)
    if code != 0:
        print("[-] 向量自检失败！", file=sys.stderr)
        return False

    source_pack = os.path.join(ui18n_root, "packs", "zh-cn", "CKANMeta.jsonl")
    dest_dir = os.path.expandvars("%LOCALAPPDATA%\\CKAN\\ckan-ui\\i18n\\packs\\zh-cn")
    dest_pack = os.path.join(dest_dir, "CKANMeta.jsonl")
    if os.path.isfile(source_pack):
        os.makedirs(dest_dir, exist_ok=True)
        shutil.copy2(source_pack, dest_pack)
        print(f"[+] 已同步 {source_pack} -> {dest_pack}")
    else:
        print(f"[*] 提示：源语言包尚未生成: {source_pack}")

    test_proj = os.path.join(ckan_ui_root, "tests", "CkanUI.ModText.Tests", "CkanUI.ModText.Tests.csproj")
    code, _, _ = run_command(["dotnet", "test", test_proj], cwd=ckan_ui_root)
    return code == 0

def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="CKAN 元数据国际化翻译流水线编排器")
    parser.add_argument("--repo-dir", default=DEFAULT_CKAN_REPO_DIR, help="CKAN repos 缓存目录")
    parser.add_argument("--ui18n-root", default=DEFAULT_UI18N_REPO, help="UniversalI18n 仓库根目录")
    parser.add_argument("--ckan-ui-root", default=DEFAULT_CKAN_UI_ROOT, help="ckan-ui 仓库根目录")
    parser.add_argument("--limit", type=int, default=None, help="仅翻译前 N 条（调试用）")
    parser.add_argument("--jobs", type=int, default=4, help="并发批数")
    parser.add_argument("--dry-run", action="store_true", help="调用 LLM 但不写盘")
    parser.add_argument("--plan-only", action="store_true", help="只生成 pool 和执行计划，不发起翻译")
    args = parser.parse_args(argv)

    pool_file = os.path.join(args.ui18n_root, "packs", "ckan_pool.jsonl")

    if not stage_extract(args.ckan_ui_root, args.repo_dir, pool_file):
        print("[-] 阶段 1 失败", file=sys.stderr)
        return 1

    if args.plan_only:
        tools_dir = os.path.join(args.ui18n_root, "tools")
        code, _, _ = run_command([sys.executable, "-m", "i18n_tools.translate", "--pool", pool_file, "--plan"], cwd=tools_dir)
        return code

    if not stage_translate(args.ui18n_root, pool_file, limit=args.limit, jobs=args.jobs, dry_run=args.dry_run):
        print("[-] 阶段 2 失败", file=sys.stderr)
        return 1

    if not args.dry_run:
        if not stage_sync_and_verify(args.ckan_ui_root, args.ui18n_root):
            print("[-] 阶段 3 失败", file=sys.stderr)
            return 1

    print("\n[+] 流水线执行成功完成！")
    return 0

if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))