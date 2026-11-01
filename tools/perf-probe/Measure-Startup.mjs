// 真机启动分段采样：起 Release exe → 等 first-paint 落盘 → 杀进程；可重复 N 轮。
// 用法：node tools/perf-probe/Measure-Startup.mjs [--exe <path>] [--runs 3] [--out json路径]
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

const argv = process.argv.slice(2);
const arg = (name, dflt) => {
  const i = argv.indexOf("--" + name);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : dflt;
};
const exe = path.resolve(arg("exe", "src/CkanUI.App/bin/Release/net10.0/CkanUI.exe"));
const runs = Number(arg("runs", "3"));
const out = path.resolve(arg("out", "artifacts/perf/startup-trace.json"));
const dir = path.dirname(exe);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const killAll = () => spawnSync("taskkill", ["/F", "/IM", "CkanUI.exe"], { stdio: "ignore" });

const results = [];
killAll();
await sleep(2000);
for (let i = 0; i < runs; i++) {
  const trace = path.join(dir, `startup-trace-${process.pid}-${i}.log`);
  try { fs.unlinkSync(trace); } catch {}
  const child = spawn(exe, [], { cwd: dir, stdio: "ignore", env: { ...process.env, CKANUI_STARTUP_TRACE: trace } });
  let text = "";
  const deadline = Date.now() + 60000;
  while (Date.now() < deadline) {
    await sleep(200);
    try { text = fs.readFileSync(trace, "utf8"); } catch { continue; }
    if (text.includes("first-paint")) break;
  }
  await sleep(300);
  killAll();
  await sleep(1500);
  const phases = {};
  for (const m of text.matchAll(/\[启动计时\] ([a-z0-9+_-]+)=(\d+)ms(?: \(\+(\d+)ms\))?/g)) {
    phases[m[1]] = { at: Number(m[2]), delta: m[3] === undefined ? null : Number(m[3]) };
  }
  results.push({ run: i + 1, captured: Object.keys(phases).length > 0, phases });
  const paint = phases["first-paint"]?.at ?? null;
  console.log(`run ${i + 1}: first-paint=${paint}ms phases=${Object.keys(phases).length}`);
}
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, JSON.stringify(results, null, 2), "utf8");
console.log("written " + out);
