import { chromium } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const root = path.resolve(import.meta.dirname, '../../../..');
const shotDir = path.join(root, 'docs', 'screenshots');
const outDir = path.join(root, '.tmp', 'perf');
fs.mkdirSync(outDir, { recursive: true });
const base = 'http://localhost:5100';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
const cdp = await page.context().newCDPSession(page);
const problems = [];
page.on('console', (m) => {
    if (m.type() === 'error' || /webgl|context lost|too many active/i.test(m.text())) {
        problems.push(`${m.type()}: ${m.text()}`);
    }
});
page.on('pageerror', (e) => problems.push('pageerror: ' + e));

async function goto(url, waitBlazor = true) {
    await page.goto(base + url, { waitUntil: 'networkidle' });
    if (waitBlazor) {
        await page.waitForFunction(() => window.Blazor !== undefined);
        await page.waitForTimeout(600);
    }
}

async function heap() {
    await cdp.send('HeapProfiler.collectGarbage').catch(() => {});
    const { usedSize } = await cdp.send('Runtime.getHeapUsage');
    return usedSize;
}

async function fps() {
    return await page.evaluate(() => new Promise((resolve) => {
        let frames = 0;
        const start = performance.now();
        const tick = (t) => {
            frames++;
            if (t - start < 1500) requestAnimationFrame(tick);
            else resolve({ frames, ms: t - start, fps: frames / ((t - start) / 1000) });
        };
        requestAnimationFrame(tick);
    }));
}

// unique name, same known-good 100k geometry
const name = `perf-100k-${Date.now()}`;
const uploadPath = path.join(root, '.tmp', 'e2e', `${name}.stl`);
fs.mkdirSync(path.dirname(uploadPath), { recursive: true });
fs.copyFileSync(path.join(root, 'tools', 'samples', 'bench_100k.stl'), uploadPath);
await goto('/upload');
await page.locator('#upload-input').setInputFiles(uploadPath);
await page.getByText(new RegExp(`Added “${name}`)).waitFor({ timeout: 60_000 });

const opened = Date.now();
await page.getByRole('link', { name: /Open perf-100k-/ }).click();
await page.waitForURL(/\/models\/[0-9a-f-]{36}/);
await page.getByRole('heading', { level: 1, name: new RegExp(name) }).waitFor();
const canvas = page.locator('canvas');
await canvas.waitFor({ timeout: 60_000 });
await page.getByText('Loading mesh…').waitFor({ state: 'hidden', timeout: 120_000 });
const previewMs = Date.now() - opened;
await page.waitForTimeout(1500);

const before = await page.screenshot({ path: path.join(shotDir, '11-preview-100k.png'), fullPage: true });
const frameStats = await fps();

const box = await canvas.boundingBox();
await page.mouse.move(box.x + box.width * 0.65, box.y + box.height * 0.45);
await page.mouse.down();
await page.mouse.move(box.x + box.width * 0.3, box.y + box.height * 0.6, { steps: 15 });
await page.mouse.up();
await page.waitForTimeout(600);
const after = await page.screenshot();
const orbitChanged = crypto.createHash('md5').update(before).digest('hex') !== crypto.createHash('md5').update(after).digest('hex');
await page.screenshot({ path: path.join(shotDir, '11-preview-100k-orbited.png'), fullPage: true });

const heapStart = await heap();
const samples = [];
await goto('/catalogue?sort=newest');
for (let i = 0; i < 10; i++) {
    const links = page.locator('a[href^="/models/"]');
    const href = await links.nth(i).getAttribute('href');
    await goto(href);
    await page.locator('canvas').waitFor({ timeout: 60_000 });
    await page.waitForTimeout(700);
    samples.push({ i, heap: await heap(), canvases: await page.locator('canvas').count() });
    await goto('/catalogue?sort=newest');
}
const heapEnd = await heap();

const report = {
    previewMs,
    frameStats,
    orbitChanged,
    heapStart,
    heapEnd,
    heapDeltaMb: (heapEnd - heapStart) / 1048576,
    canvasesAfterNavigation: await page.locator('canvas').count(),
    samples,
    problems
};
fs.writeFileSync(path.join(outDir, 'preview.json'), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));

// cleanup: delete the 100k model
await goto(`/catalogue?q=${name}`);
await page.locator('a[href^="/models/"]').first().click();
await page.waitForURL(/\/models\/[0-9a-f-]{36}/);
await page.getByRole('heading', { level: 1, name: new RegExp(name) }).waitFor();
await page.getByRole('button', { name: 'Delete', exact: true }).click();
await page.getByRole('button', { name: 'Delete this model?' }).click();
await page.waitForURL(/\/catalogue/);
await browser.close();
