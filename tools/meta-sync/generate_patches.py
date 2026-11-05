#!/usr/bin/env python3
"""
CKAN Meta 增量同步与 GitHub Pages 静态站点生成器
"""
import os
import sys
import json
import gzip
import shutil
import subprocess
from datetime import datetime, timezone
from pathlib import Path

META_REPO = os.environ.get("META_REPO_URL", "https://github.com/KSP-CKAN/CKAN-meta.git")
META_BRANCH = os.environ.get("META_BRANCH", "master")
DIST_DIR = Path(os.environ.get("DIST_DIR", "dist"))
WORK_DIR = Path(".tmp_meta_repo")
MAX_COMMITS = int(os.environ.get("MAX_COMMITS", "15"))

def run_cmd(cmd, cwd=None):
    res = subprocess.run(cmd, cwd=cwd, shell=True, capture_output=True, text=True)
    if res.returncode != 0:
        raise RuntimeError(f"Command failed: {cmd}\nStderr: {res.stderr}")
    return res.stdout.strip()

def setup_repo():
    if WORK_DIR.exists():
        shutil.rmtree(WORK_DIR)
    print(f"[1/5] Cloning shallow meta repo (depth={MAX_COMMITS + 1})...")
    run_cmd(f"git clone --depth {MAX_COMMITS + 1} --branch {META_BRANCH} --single-branch {META_REPO} {WORK_DIR}")

def get_commits():
    log_out = run_cmd("git log --pretty=format:'%H|%h|%s|%cI' -n " + str(MAX_COMMITS), cwd=WORK_DIR)
    commits = []
    for line in log_out.splitlines():
        if not line:
            continue
        parts = line.split("|", 3)
        if len(parts) == 4:
            commits.append({
                "hash": parts[0],
                "short_hash": parts[1],
                "subject": parts[2],
                "date": parts[3]
            })
    return commits

def extract_patch(from_commit, to_commit):
    diff_stat = run_cmd(f"git diff --name-status {from_commit} {to_commit}", cwd=WORK_DIR)
    upsert = {}
    deleted = []

    for line in diff_stat.splitlines():
        if not line.strip():
            continue
        parts = line.split(maxsplit=1)
        if len(parts) != 2:
            continue
        status, rel_path = parts[0], parts[1]
        if not rel_path.endswith(".ckan"):
            continue

        if status.startswith("D"):
            deleted.append(rel_path)
        else:
            try:
                content = run_cmd(f"git show {to_commit}:{rel_path}", cwd=WORK_DIR)
                mod_json = json.loads(content)
                ident = mod_json.get("identifier", Path(rel_path).stem)
                if ident not in upsert:
                    upsert[ident] = []
                upsert[ident].append(mod_json)
            except Exception as e:
                print(f"Warning: failed reading {rel_path}: {e}")

    return {
        "from_commit": from_commit,
        "to_commit": to_commit,
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "total_upsert": sum(len(v) for v in upsert.values()),
        "total_deleted": len(deleted),
        "upsert": upsert,
        "delete": deleted
    }

def generate_index_html(manifest, commits):
    html = f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>CKAN 增量元数据服务原型 (CKAN_Nova)</title>
    <style>
        :root {{
            --bg: #0f172a; --card: #1e293b; --border: #334155;
            --text: #f8fafc; --muted: #94a3b8; --accent: #38bdf8;
            --success: #34d399; --warn: #fbbf24;
        }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "PingFang SC", sans-serif;
            background: var(--bg); color: var(--text); margin: 0; padding: 24px;
        }}
        .container {{ max-width: 1000px; margin: 0 auto; }}
        header {{ margin-bottom: 24px; padding-bottom: 16px; border-bottom: 1px solid var(--border); }}
        h1 {{ margin: 0 0 8px 0; font-size: 24px; color: var(--accent); }}
        .subtitle {{ color: var(--muted); font-size: 14px; margin: 0; }}
        .grid {{ display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; margin-bottom: 24px; }}
        .card {{ background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 16px; }}
        .card-label {{ font-size: 12px; color: var(--muted); text-transform: uppercase; letter-spacing: 0.5px; }}
        .card-value {{ font-size: 18px; font-weight: 600; margin-top: 6px; font-family: monospace; word-break: break-all; }}
        .card-value.highlight {{ color: var(--success); }}
        table {{ width: 100%; border-collapse: collapse; margin-top: 12px; background: var(--card); border-radius: 8px; overflow: hidden; border: 1px solid var(--border); }}
        th, td {{ padding: 12px 14px; text-align: left; border-bottom: 1px solid var(--border); font-size: 13px; }}
        th {{ background: #182234; color: var(--muted); font-weight: 600; }}
        tr:last-child td {{ border-bottom: none; }}
        a {{ color: var(--accent); text-decoration: none; }}
        a:hover {{ text-decoration: underline; }}
        .badge {{ display: inline-block; padding: 2px 8px; border-radius: 12px; font-size: 11px; font-weight: 600; background: #0369a1; color: #e0f2fe; }}
        .badge-warn {{ background: #854d0e; color: #fef08a; }}
        .tester {{ background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-top: 24px; }}
        input, button {{ padding: 8px 12px; border-radius: 6px; border: 1px solid var(--border); font-size: 14px; }}
        input {{ background: #0b1120; color: #fff; width: 340px; font-family: monospace; }}
        button {{ background: #0284c7; color: white; border: none; cursor: pointer; font-weight: 600; }}
        button:hover {{ background: #0369a1; }}
        #test-res {{ margin-top: 12px; padding: 12px; background: #090e17; border-radius: 6px; font-family: monospace; font-size: 13px; white-space: pre-wrap; display: none; }}
    </style>
</head>
<body>
    <div class="container">
        <header>
            <h1>CKAN 增量元数据服务原型 (CKAN_Nova)</h1>
            <p class="subtitle">针对 KSP-CKAN/CKAN-meta 提供的静态增量同步差量包服务，消除全量拉取卡顿。</p>
        </header>

        <div class="grid">
            <div class="card">
                <div class="card-label">最新同步 HEAD</div>
                <div class="card-value highlight">{manifest["head_commit"][:10]}...</div>
            </div>
            <div class="card">
                <div class="card-label">最近可用 Patch 数</div>
                <div class="card-value">{len(manifest["recent_patches"])} 个差量包</div>
            </div>
            <div class="card">
                <div class="card-label">服务状态 / 更新时间</div>
                <div class="card-value" style="font-size:14px; font-family:sans-serif;">{manifest["updated_at"][:19]} UTC</div>
            </div>
        </div>

        <div class="card">
            <div class="card-label" style="font-size:14px; color:var(--text); font-weight:600;">核心 API 接口</div>
            <p style="font-size:13px; color:var(--muted); margin: 8px 0 12px 0;">客户端请求流程：先获取 <a href="manifest.json"><code>manifest.json</code></a>，若本地 Commit 与 HEAD 不一致，查询对应的 Patch 文件进行增量合并。</p>
            <div style="font-family:monospace; font-size:13px; background:#0b1120; padding:12px; border-radius:6px;">
                • Manifest 索引：<a href="manifest.json">manifest.json</a><br>
                • 差量包目录：<code>patches/&lt;from_commit&gt;_&lt;to_commit&gt;.json</code>
            </div>
        </div>

        <h3 style="margin: 28px 0 12px 0; font-size: 16px;">最近生成的增量补丁包 (Recent Patches)</h3>
        <table>
            <thead>
                <tr>
                    <th>起始 Commit</th>
                    <th>目标 Commit (HEAD)</th>
                    <th>变动 Mod 数</th>
                    <th>删除数</th>
                    <th>文件体积</th>
                    <th>下载验证</th>
                </tr>
            </thead>
            <tbody>
"""
    for p in manifest["recent_patches"]:
        html += f"""                <tr>
                    <td><code>{p["from_commit"][:8]}</code></td>
                    <td><code>{p["to_commit"][:8]}</code></td>
                    <td><span class="badge">+{p["total_upsert"]} mod(s)</span></td>
                    <td><span class="badge badge-warn">-{p["total_deleted"]}</span></td>
                    <td>{p["size_kb"]:.1f} KB</td>
                    <td><a href="{p["path"]}" target="_blank">查看 JSON</a></td>
                </tr>\n"""

    html += """            </tbody>
        </table>

        <div class="tester">
            <div class="card-label" style="font-size:14px; color:var(--text); font-weight:600; margin-bottom:8px;">客户端增量命中模拟器</div>
            <div style="display:flex; gap:10px; align-items:center;">
                <input type="text" id="client-sha" placeholder="输入模拟的客户端本地 commit SHA (前8位或全长)..." />
                <button onclick="testSha()">查询增量方案</button>
            </div>
            <div id="test-res"></div>
        </div>
    </div>

    <script>
        const manifest = """ + json.dumps(manifest) + """;
        function testSha() {
            const input = document.getElementById('client-sha').value.trim().toLowerCase();
            const res = document.getElementById('test-res');
            res.style.display = 'block';
            if (!input) {
                res.innerHTML = '<span style="color:#f87171">请输入 SHA 哈希</span>';
                return;
            }
            if (manifest.head_commit.toLowerCase().startsWith(input)) {
                res.innerHTML = '<span style="color:var(--success)">✅ 客户端已是最新 (304 Not Modified) - 无需拉取任何数据，耗时 0ms</span>';
                return;
            }
            const match = manifest.recent_patches.find(p => p.from_commit.toLowerCase().startsWith(input));
            if (match) {
                res.innerHTML = '<span style="color:var(--success)">🎯 命中直接差量包！</span>\n' +
                                '• 需下载：' + match.path + ' (' + match.size_kb.toFixed(1) + ' KB)\n' +
                                '• 变更数量：+' + match.total_upsert + ' mods, -' + match.total_deleted + ' deleted\n' +
                                '• 优势：免去下载几十 MB 全量 tar.gz，本地直接字典 Patch 即可完成同步。';
            } else {
                res.innerHTML = '<span style="color:var(--warn)">⚠️ 未命中直接差量包 (落后过多或 SHA 无效)</span>\n' +
                                '• 策略：无缝回退至全量快照拉取，保证数据绝对自愈与一致性。';
            }
        }
    </script>
</body>
</html>
"""
    return html

def main():
    setup_repo()
    commits = get_commits()
    if len(commits) < 2:
        print("Not enough commits to generate diff.")
        sys.exit(1)

    print(f"[2/5] Fetched {len(commits)} commits. HEAD is {commits[0]['hash']}")

    if DIST_DIR.exists():
        shutil.rmtree(DIST_DIR)
    DIST_DIR.mkdir(parents=True, exist_ok=True)
    patches_dir = DIST_DIR / "patches"
    patches_dir.mkdir(parents=True, exist_ok=True)

    head_commit = commits[0]["hash"]
    manifest = {
        "service": "ckan-nova-meta-incremental",
        "head_commit": head_commit,
        "head_short": commits[0]["short_hash"],
        "head_subject": commits[0]["subject"],
        "updated_at": datetime.now(timezone.utc).isoformat(),
        "recent_patches": []
    }

    print("[3/5] Computing patches against HEAD and adjacent versions...")
    # 1. 生成每个较旧版本直接到最新 HEAD 的 patch
    for prev in commits[1:]:
        from_sha = prev["hash"]
        patch = extract_patch(from_sha, head_commit)
        patch_file = patches_dir / f"{prev['short_hash']}_{commits[0]['short_hash']}.json"
        with open(patch_file, "w", encoding="utf-8") as f:
            json.dump(patch, f, ensure_ascii=False, indent=2)

        file_size = os.path.getsize(patch_file)
        manifest["recent_patches"].append({
            "from_commit": from_sha,
            "to_commit": head_commit,
            "path": f"patches/{patch_file.name}",
            "size_kb": file_size / 1024,
            "total_upsert": patch["total_upsert"],
            "total_deleted": patch["total_deleted"]
        })

    print("[4/5] Writing manifest.json and .nojekyll...")
    with open(DIST_DIR / "manifest.json", "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)

    with open(DIST_DIR / ".nojekyll", "w") as f:
        pass

    print("[5/5] Generating index.html dashboard...")
    with open(DIST_DIR / "index.html", "w", encoding="utf-8") as f:
        f.write(generate_index_html(manifest, commits))

    # 清理临时工作目录
    if WORK_DIR.exists():
        shutil.rmtree(WORK_DIR)

    print(f"\nDone! Generated {len(manifest['recent_patches'])} patches in {DIST_DIR}/")

if __name__ == "__main__":
    main()
