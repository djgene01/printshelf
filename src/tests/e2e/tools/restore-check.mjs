import { chromium } from '@playwright/test';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '../../../..');
const base = 'http://localhost:5101';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
const problems = [];
page.on('console', m => { if (m.type() === 'error') problems.push(m.text()); });
page.on('pageerror', e => problems.push(String(e)));

await page.goto(base + '/catalogue', { waitUntil: 'networkidle' });
await page.waitForFunction(() => window.Blazor !== undefined);
await page.waitForTimeout(800);
const summary = await page.locator('main').innerText();
console.log('catalogue summary:', summary.split('\n').slice(0, 6).join(' | '));

const link = page.locator('a[href^="/models/"]').first();
const href = await link.getAttribute('href');
await link.click();
await page.waitForURL(/\/models\/[0-9a-f-]{36}/);
await page.getByRole('heading', { level: 1 }).waitFor();
const canvas = page.locator('canvas');
await canvas.waitFor({ timeout: 60_000 });
await page.getByText('Loading mesh…').waitFor({ state: 'hidden', timeout: 60_000 });
await page.waitForTimeout(800);
await page.screenshot({ path: path.join(root, 'docs', 'screenshots', '12-restore-drill.png'), fullPage: true });

const id = href.split('/').pop();
const file = await page.request.get(`${base}/files/${id}`);
console.log('file endpoint:', file.status(), file.headers()['content-type'], (await file.body()).length, 'bytes');

const detail = await page.locator('main').innerText();
console.log('detail has stats:', detail.includes('Triangles'), '| thumbnail:', detail.includes('Thumbnail saved'));
console.log('problems:', JSON.stringify(problems));
await browser.close();
