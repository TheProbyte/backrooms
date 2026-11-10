// Renders the wallpaper deterministically in headless Edge.
//   node render.mjs --w 1080 --h 2400 --loop 15 --video ../out/phone.mp4
//   node render.mjs --w 1920 --h 1080 --stills 0,96,200 --outdir ../debug
import puppeteer from 'puppeteer-core';
import { spawn, execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, all) =>
  a.startsWith('--') ? [...acc, [a.slice(2), all[i + 1] && !all[i + 1].startsWith('--') ? all[i + 1] : true]] : acc, []));

const W = +args.w || 1080, H = +args.h || 2400, LOOP = +args.loop || 15, FPS = 24;
const EDGE = args.browser || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const page_url = pathToFileURL(path.join(here, '..', 'wallpaper', 'index.html')).href + `?render=1&w=${W}&h=${H}&loop=${LOOP}`;

const browser = await puppeteer.launch({
  executablePath: EDGE, headless: true,
  args: ['--ignore-gpu-blocklist', '--enable-gpu', '--use-angle=d3d11', '--allow-file-access-from-files'],
});
const page = await browser.newPage();
page.on('console', m => console.log('[page]', m.text()));
page.on('pageerror', e => console.error('[page error]', e.message));
await page.setViewport({ width: Math.min(W, 1200), height: Math.min(H, 1200) });
await page.goto(page_url);
await page.waitForFunction('window.wallpaperReady === true', { timeout: 30000 });
console.log('renderer:', await page.evaluate(() => {
  const gl = document.getElementById('c').getContext('webgl2');
  const ext = gl.getExtension('WEBGL_debug_renderer_info');
  return ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : 'unknown';
}));

const grab = async (i) => Buffer.from((await page.evaluate(n => window.renderFrame(n), i)).split(',')[1], 'base64');

if (args.stills){
  const outdir = path.resolve(args.outdir || path.join(here, '..', 'debug'));
  fs.mkdirSync(outdir, { recursive: true });
  for (const s of String(args.stills).split(',')){
    const f = path.join(outdir, `still_${W}x${H}_${s}.png`);
    fs.writeFileSync(f, await grab(+s));
    console.log('wrote', f);
  }
}

if (args.video){
  const out = path.resolve(args.video);
  fs.mkdirSync(path.dirname(out), { recursive: true });
  const ffmpeg = execFileSync('python', ['-c', 'import imageio_ffmpeg;print(imageio_ffmpeg.get_ffmpeg_exe())']).toString().trim();
  const total = Math.round(LOOP * FPS);
  const enc = spawn(ffmpeg, ['-y', '-loglevel', 'error', '-f', 'image2pipe', '-framerate', String(FPS), '-i', '-',
    '-c:v', 'libx264', '-preset', 'slow', '-tune', 'grain', '-crf', '21', '-maxrate', '14M', '-bufsize', '28M',
    '-pix_fmt', 'yuv420p', '-profile:v', 'high', '-movflags', '+faststart', out], { stdio: ['pipe', 'inherit', 'inherit'] });
  const t0 = Date.now();
  for (let i = 0; i < total; i++){
    const png = await grab(i);
    if (!enc.stdin.write(png)) await new Promise(r => enc.stdin.once('drain', r));
    if (i % 24 === 0) process.stdout.write(`\rframe ${i}/${total}  ${((Date.now() - t0) / 1000).toFixed(0)}s`);
  }
  enc.stdin.end();
  await new Promise(r => enc.on('close', r));
  console.log(`\nwrote ${out} (${(fs.statSync(out).size / 1e6).toFixed(1)} MB)`);
}

await browser.close();
