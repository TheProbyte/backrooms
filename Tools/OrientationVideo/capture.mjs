// Captures the orientation video frame by frame with headless Edge over the DevTools protocol (no packages needed).
// node capture.mjs <outDir> [first] [last] [step] [captions]
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, existsSync, appendFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const [outDir, firstArg, lastArg, stepArg, capArg] = process.argv.slice(2);
mkdirSync(outDir, { recursive: true });
// messages go to a log file as well: the console can lose its last lines when the process exits
const logFile = join(outDir, 'capture.log');
const log = (...a) => { const m = a.join(' '); console.log(m); appendFileSync(logFile, m + '\n'); };
process.on('uncaughtException', e => { log('crash', e.stack || e); process.exit(1); });
process.on('unhandledRejection', e => { log('crash', e && e.stack || e); process.exit(1); });
const EDGE = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const port = 9300 + Math.floor(Math.random() * 500);
const profile = join(outDir, '..', `edge-profile-${port}`);
const edge = spawn(EDGE, ['--headless=new', '--disable-gpu', '--hide-scrollbars', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
  '--window-size=960,720', '--force-device-scale-factor=1', 'about:blank'], { stdio: 'ignore' });

const sleep = ms => new Promise(r => setTimeout(r, ms));
let targets;
for (let i = 0; i < 60; i++) {
  try { targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json(); if (targets.find(t => t.type === 'page')) break; } catch { }
  await sleep(250);
}
const page = targets.find(t => t.type === 'page');
const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener('open', r, { once: true }));
let id = 0; const pending = new Map();
ws.addEventListener('message', ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } });
const send = (method, params = {}, timeoutMs = 0) => new Promise(r => {
  const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params }));
  if (timeoutMs) setTimeout(() => { if (pending.has(i)) { pending.delete(i); r({ timeout: true }); } }, timeoutMs);
});

await send('Emulation.setDeviceMetricsOverride', { width: 960, height: 720, deviceScaleFactor: 1, mobile: false });
const url = pathToFileURL(join(here, 'video.html')).href + (capArg === '1' ? '?captions=1' : '');
await send('Page.enable');
await send('Page.navigate', { url });
for (let i = 0; i < 80; i++) {
  const r = await send('Runtime.evaluate', { expression: 'typeof window.renderFrame === "function" && document.fonts.status === "loaded"', returnByValue: true });
  if (r.result?.result?.value) break;
  await sleep(250);
}
await send('Runtime.evaluate', { expression: 'document.fonts.ready.then(() => true)', awaitPromise: true });
const total = (await send('Runtime.evaluate', { expression: 'window.TOTAL_FRAMES', returnByValue: true })).result.result.value;
const first = firstArg ? Number(firstArg) : 0, last = lastArg ? Math.min(Number(lastArg), total - 1) : total - 1, step = stepArg ? Number(stepArg) : 1;
const times = await send('Runtime.evaluate', { expression: 'JSON.stringify(window.SCENE_TIMES)', returnByValue: true });
writeFileSync(join(outDir, 'scenes.json'), times.result.result.value);
const t0 = Date.now();
let count = 0;
for (let n = first; n <= last; n += step) {
  const file = join(outDir, `f${String(n).padStart(5, '0')}.png`);
  if (existsSync(file)) continue;   // resume where an earlier run stopped
  const ft = Date.now(); if (process.env.CAPDEBUG) log('start', n);
  // draw, then wait two animation frames so the picture is fully painted before the shot;
  // if the page does not answer in time (it happens now and then), draw again and wait a moment instead
  let r = await send('Runtime.evaluate', { expression: `new Promise(res => { const v = JSON.stringify(renderFrame(${n})); requestAnimationFrame(() => requestAnimationFrame(() => res(v))); })`, awaitPromise: true, returnByValue: true }, 4000);
  if (r.timeout) { r = await send('Runtime.evaluate', { expression: `JSON.stringify(renderFrame(${n}))`, returnByValue: true }, 8000); await sleep(120); }
  if (r.timeout) { log('frame', n, 'no answer from the page'); break; }
  if (r.result?.exceptionDetails) { log('frame', n, JSON.stringify(r.result.exceptionDetails).slice(0, 600)); break; }
  let shot = await send('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: 0, width: 960, height: 720, scale: 1 } }, 10000);
  if (shot.timeout) shot = await send('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: 0, width: 960, height: 720, scale: 1 } }, 10000);
  if (shot.timeout) { log('frame', n, 'screenshot timed out'); break; }
  writeFileSync(file, Buffer.from(shot.result.data, 'base64'));
  if (process.env.CAPDEBUG) log('frame', n, Date.now() - ft, 'ms');
  if (++count % 240 === 0) log(`${n}/${last} ${((Date.now() - t0) / count).toFixed(0)} ms/frame`);
}
log(`done ${count} frames, total ${total}, ${((Date.now() - t0) / 1000).toFixed(1)} s`);
ws.close();
// Edge starts child processes of its own: close the whole tree, not just the launcher
spawn('taskkill', ['/PID', String(edge.pid), '/T', '/F'], { stdio: 'ignore' }).on('exit', () => setTimeout(() => process.exit(0), 300));
