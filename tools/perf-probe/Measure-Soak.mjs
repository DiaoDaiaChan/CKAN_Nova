// 驻留与空转的长时采样：起 Release exe（不抢前台、不动光标），按固定间隔读
// WorkingSet / PrivateMemory / TotalProcessorTime / 线程数，用来判断「内存是稳定
// 收敛还是缓慢泄漏」以及空转 CPU 的稳态水平。
//
// 用法：node tools/perf-probe/Measure-Soak.mjs --exe <绝对路径> --minutes 4 [--sample-ms 10000] [--out soak.json]
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

const arg = (name, fallback) => {
  const i = process.argv.indexOf(`--${name}`);
  return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
};

const exe = path.resolve(arg("exe", "src/CkanUI.App/bin/Release/net10.0/CkanUI.exe"));
const minutes = Number(arg("minutes", "4"));
const sampleMs = Number(arg("sample-ms", "10000"));
const out = path.resolve(arg("out", "artifacts/perf/soak.json"));
const warmupMs = Number(arg("warmup-ms", "40000"));

if (!fs.existsSync(exe)) {
  console.error(`找不到可执行文件：${exe}`);
  process.exit(2);
}

const ps = (command) =>
  spawnSync("powershell", ["-NoProfile", "-Command", command], { encoding: "utf8" }).stdout.trim();

spawnSync("taskkill", ["/F", "/IM", "CkanUI.exe"], { stdio: "ignore" });
const child = spawn(exe, [], { cwd: path.dirname(exe), stdio: "ignore", detached: true });
const pid = child.pid;

const sample = () => {
  const line = ps(
    `$p = Get-Process -Id ${pid} -ErrorAction SilentlyContinue; if ($p) { '{0}|{1}|{2}|{3}' -f [int64]$p.WorkingSet64, [int64]$p.PrivateMemorySize64, $p.TotalProcessorTime.TotalSeconds, $p.Threads.Count }`,
  );
  if (!line) return null;
  const [ws, priv, cpu, threads] = line.split("|");
  return { ws: Number(ws), priv: Number(priv), cpu: Number(cpu), threads: Number(threads) };
};

const rows = [];
const startedAt = Date.now();
const deadline = startedAt + minutes * 60000;
let previous = null;
let previousAt = startedAt;

while (Date.now() < deadline) {
  await new Promise((resolve) => setTimeout(resolve, sampleMs));
  const current = sample();
  if (!current) break;
  const at = Date.now();
  const elapsedSeconds = (at - startedAt) / 1000;
  const cpuPercent =
    previous === null ? null : ((current.cpu - previous.cpu) * 1000) / (at - previousAt);
  previous = current;
  previousAt = at;
  rows.push({
    seconds: Math.round(elapsedSeconds),
    warm: at - startedAt >= warmupMs,
    workingSetMb: +(current.ws / 1048576).toFixed(1),
    privateMb: +(current.priv / 1048576).toFixed(1),
    threads: current.threads,
    cpuPercentOneCore: cpuPercent === null ? null : +cpuPercent.toFixed(2),
  });
}

spawnSync("taskkill", ["/F", "/PID", String(pid)], { stdio: "ignore" });

fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, JSON.stringify({ exe, minutes, sampleMs, rows }, null, 1), "utf8");

const warm = rows.filter((row) => row.warm);
const summarise = (key) => {
  const values = warm.map((row) => row[key]).filter((value) => typeof value === "number");
  if (values.length === 0) return "n/a";
  return `${Math.min(...values).toFixed(1)} / ${Math.max(...values).toFixed(1)}`;
};
console.log(`[Soak] 样本=${rows.length} 预热后样本=${warm.length}`);
console.log(`[Soak] workingSet min/max = ${summarise("workingSetMb")} MB`);
console.log(`[Soak] private   min/max = ${summarise("privateMb")} MB`);
console.log(`[Soak] 线程      min/max = ${summarise("threads")}`);
console.log(`[Soak] 单核 CPU  min/max = ${summarise("cpuPercentOneCore")} %`);
if (warm.length >= 3) {
  const head = warm[0];
  const tail = warm[warm.length - 1];
  console.log(`[Soak] 收敛检查：WS ${head.workingSetMb} → ${tail.workingSetMb} MB（${(tail.workingSetMb - head.workingSetMb).toFixed(1)}）`);
}
console.log(`[Soak] 明细写入 ${out}`);
