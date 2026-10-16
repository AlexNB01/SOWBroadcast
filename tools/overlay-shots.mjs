// Takes 1920x1080 pictures of overlay pages with headless Edge (Chrome DevTools Protocol), for the visual regression check.
//
//   node tools/overlay-shots.mjs <outDir> <baseUrl> <name=path> [<name=path> ...] [--wait=5000] [--edge=<path>]
//
// e.g.  node tools/overlay-shots.mjs shots/after http://127.0.0.1:8324 scoreboard=/HTML/scoreboard.html team1=/HTML/team.html?side=1
//
// The page background is a fixed dark grey so transparent overlays look the same every time. Needs Node 22+ (global WebSocket and fetch).
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const args = process.argv.slice(2);
const options = Object.fromEntries(args.filter(a => a.startsWith('--')).map(a => a.slice(2).split('=')));
const positional = args.filter(a => !a.startsWith('--'));
const [outDir, baseUrl, ...jobSpecs] = positional;
if (!outDir || !baseUrl || jobSpecs.length === 0) {
    console.error('usage: node overlay-shots.mjs <outDir> <baseUrl> <name=path>... [--wait=5000] [--edge=path]');
    process.exit(2);
}
const wait = Number(options.wait ?? 5000);
const edge = options.edge ?? 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
mkdirSync(outDir, { recursive: true });

const profile = mkdtempSync(join(tmpdir(), 'sowb-shots-'));
const port = 9300 + Math.floor(Math.random() * 500);
const browser = spawn(edge, [
    '--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, '--no-first-run', '--disable-gpu',
    '--hide-scrollbars', '--window-size=1920,1080', '--autoplay-policy=no-user-gesture-required', 'about:blank',
], { stdio: 'ignore' });

const sleep = ms => new Promise(r => setTimeout(r, ms));

async function connect() {
    for (let i = 0; i < 100; i++) {
        try {
            const info = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
            const ws = new WebSocket(info.webSocketDebuggerUrl);
            await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
            return ws;
        } catch { await sleep(100); }
    }
    throw new Error('Edge did not start');
}

try {
    const ws = await connect();
    let nextId = 0;
    const pending = new Map();
    ws.onmessage = ev => {
        const msg = JSON.parse(ev.data);
        if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); }
    };
    const send = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
        const id = ++nextId;
        pending.set(id, msg => msg.error ? reject(new Error(`${method}: ${msg.error.message}`)) : resolve(msg.result));
        ws.send(JSON.stringify({ id, method, params, sessionId }));
    });

    for (const spec of jobSpecs) {
        const eq = spec.indexOf('=');
        const name = spec.slice(0, eq);
        const rawPath = spec.slice(eq + 1);
        const path = rawPath.startsWith('/') ? rawPath : '/' + rawPath; // (no leading slash needed: Git Bash rewrites those)
        const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
        const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true });
        await send('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false }, sessionId);
        await send('Emulation.setDefaultBackgroundColorOverride', { color: { r: 48, g: 48, b: 48, a: 1 } }, sessionId);
        await send('Page.navigate', { url: baseUrl + path }, sessionId);
        await sleep(wait);
        const shot = await send('Page.captureScreenshot', { format: 'png' }, sessionId);
        writeFileSync(join(outDir, `${name}.png`), Buffer.from(shot.data, 'base64'));
        await send('Target.closeTarget', { targetId });
        console.log(`${name}`);
    }
    ws.close();
} finally {
    browser.kill();
    await sleep(300);
    try { rmSync(profile, { recursive: true, force: true }); } catch { /* still in use; the OS temp cleanup gets it */ }
}
