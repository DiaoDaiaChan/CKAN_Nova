// 通用遍历探针：在自己的整合包上复现整页遍历与内存曲线。
//   CKAN_CONFIG_FILE=<你的 CKAN 配置 json> node tools/perf-probe/measure-instance.mjs
// 环境变量：CKANUI_NAV_LOG（trace 输出，默认 artifacts/nav-instance.log）、CKANUI_NAV_TIMEOUT_MS。
import {execFileSync, spawn} from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import {fileURLToPath} from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const exe = path.join(root, "src/CkanUI.App/bin/Release/net10.0/CkanUI.exe");
const log = process.env.CKANUI_NAV_LOG || path.join(root, "artifacts/nav-instance.log");
const timeout = Number(process.env.CKANUI_NAV_TIMEOUT_MS || 900000);

if (!process.env.CKAN_CONFIG_FILE) {
  console.error("缺少 CKAN_CONFIG_FILE：请先设置成你的 CKAN 配置路径。");
  process.exit(2);
}
if (!fs.existsSync(exe)) {
  console.error("找不到 " + exe + "，先执行 dotnet build ckan-ui.sln -c Release");
  process.exit(2);
}

fs.rmSync(log, {force: true});
const child = spawn(exe, [], {cwd: path.dirname(exe), stdio: "ignore", env: {...process.env,
  CKANUI_NAV_TRACE: log,
  CKANUI_NAV_MEM: "1",
  CKANUI_PROBE_REAL_CONFIG: "1",
  CKANUI_PROBE_INSTANCE: "1",
  CKANUI_APP_ID: "CkanUIUserInstance"}});

// 进程工作集采样：应用内的 [nav] 行只在自己走完一页时打印，静静涨到 4 GB 的那种现场抓不到，
// 所以这里从外面每 5 秒问一次 Windows（tasklist），谁涨的、涨多快都留在时间轴上。
const wsSamples = [];
let peakWsMb = 0;
const sampleWorkingSet = () => {
  try {
    const out = execFileSync("tasklist",
      ["/FI", "PID eq " + child.pid, "/FO", "CSV", "/NH"],
      {encoding: "utf8", windowsHide: true});
    const match = out.match(/"([\d.,]+) K"/);
    if (!match) return;
    const mb = Math.round(Number(match[1].replace(/[.,]/g, "")) / 1024);
    peakWsMb = Math.max(peakWsMb, mb);
    wsSamples.push({seconds: Math.round((Date.now() - started) / 1000), mb});
    console.log("[ws] t=" + wsSamples.at(-1).seconds + "s ws=" + mb + "MB");
  } catch { /* 进程已经退出就不用采了 */ }
};

const started = Date.now();
const wsTimer = setInterval(sampleWorkingSet, 5000);
for (;;) {
  await new Promise((r) => setTimeout(r, 1000));
  let trace = "";
  try { trace = fs.readFileSync(log, "utf8"); } catch { /* 文件还没建出来 */ }
  if (trace.includes("[nav] done") || Date.now() - started > timeout) {
    // 应用自己就在写这个 trace 文件（UTF-8），这里只补一句路径，不再由 shell 重定向落盘。
    console.log("trace 文件：" + log);
    console.log(trace || "<没有产生 trace：应用可能启动失败>");
    clearInterval(wsTimer);
    console.log("峰值工作集：" + peakWsMb + "MB（采样 " + wsSamples.length + " 次）");
    child.kill("SIGKILL");
    process.exit(trace.includes("[nav] done") ? 0 : 1);
  }
}
