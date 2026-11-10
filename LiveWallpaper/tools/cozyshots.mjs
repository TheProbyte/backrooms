// Screenshots of cozy-beach surprises in headless Edge.
//   node cozyshots.mjs <jobs.json> <outdir>
// jobs: [{ scene, hour, name, at: [0.2, 0.5], w, h, extra }]
import puppeteer from 'puppeteer-core';
import fs from 'node:fs';
import path from 'node:path';

const [jobsFile, outdir] = process.argv.slice(2);
const jobs = JSON.parse(fs.readFileSync(jobsFile, 'utf8'));
fs.mkdirSync(outdir, { recursive: true });
const browser = await puppeteer.launch({
  executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', headless: true,
  args: ['--ignore-gpu-blocklist', '--enable-gpu', '--use-angle=d3d11'],
});
const page = await browser.newPage();
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
page.on('console', (m) => { if (m.type() === 'warn' || m.type() === 'warning' || m.type() === 'error') errors.push(m.text()); });
let lastUrl = '';
for (const j of jobs) {
  const w = j.w || 1280, h = j.h || 720;
  await page.setViewport({ width: w, height: h, isMobile: h > w, hasTouch: h > w });
  const url = `http://localhost:8741/index.html?scene=${j.scene}&hour=${j.hour}&set=SURPRISE_MINUTES:30${j.extra || ''}`;
  if (url + w + h !== lastUrl) { await page.goto(url, { waitUntil: 'load' }); await page.waitForFunction('window.__surprise', { timeout: 20000 }); await new Promise((r) => setTimeout(r, 2500)); lastUrl = url + w + h; }
  for (const at of j.at) {
    const res = await page.evaluate((n, a) => window.__surprise.run(n, a), j.name, at);
    await new Promise((r) => setTimeout(r, j.wait ?? 600));
    const f = path.join(outdir, `${j.name}_${j.scene}_${String(at).replace('.', '')}${h > w ? '_tall' : ''}.jpg`);
    await page.screenshot({ path: f, type: 'jpeg', quality: 80 });
    console.log(res, f);
  }
}
if (errors.length) console.log('ERRORS:\n' + [...new Set(errors)].join('\n'));
await browser.close();
