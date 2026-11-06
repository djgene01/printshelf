import { chromium } from '@playwright/test';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '../../../..');
const shot = path.join(root, 'docs', 'screenshots');
const base = 'http://localhost:5100';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
const problems = [];
page.on('console', m => { if (m.type() === 'error') problems.push(m.text()); });
page.on('pageerror', e => problems.push(String(e)));

async function goto(url) {
  await page.goto(base + url, { waitUntil: 'networkidle' });
  await page.waitForFunction(() => window.Blazor !== undefined);
  await page.waitForTimeout(700);
}

async function snap(name) {
  await page.screenshot({ path: path.join(shot, name), fullPage: true });
  console.log('shot', name);
}

await goto('/');
await snap('01-dashboard-1280.png');

await goto('/catalogue');
await snap('02-catalogue-grid.png');

await goto('/catalogue?q=Snap-fit');
await page.locator('a[href^="/models/"]').first().click();
await page.waitForURL(/\/models\/[0-9a-f-]{36}/);
await page.getByRole('heading', { level: 1 }).waitFor();
await page.locator('canvas').waitFor({ timeout: 60_000 });
await page.getByText('Loading mesh…').waitFor({ state: 'hidden', timeout: 60_000 });
await page.getByText('Thumbnail saved').waitFor({ timeout: 60_000 }).catch(() => {});
await page.waitForTimeout(800);
await snap('03-model-preview.png');
await snap('05-print-runs.png');

await goto('/catalogue?sort=name');
await page.getByRole('button', { name: /List/ }).click();
await page.waitForTimeout(500);
await snap('02-catalogue-list.png');

await goto('/catalogue');
await page.locator('img[src*="/thumbs/"]').first().waitFor({ timeout: 30_000 }).catch(() => {});
await page.waitForTimeout(500);
await snap('03-catalogue-thumbnail.png');

await goto('/upload');
await snap('07-upload-empty.png');

await goto('/tags');
await snap('06-tags.png');

await goto('/categories');
await snap('15-categories.png');

await goto('/admin/integrity');
await snap('09-integrity.png');

await page.setViewportSize({ width: 390, height: 844 });
await goto('/');
await snap('08-mobile-dashboard.png');
await goto('/catalogue');
await snap('08-mobile-catalogue.png');
await goto('/upload');
await snap('08-mobile-upload.png');

console.log('problems', JSON.stringify(problems));
await browser.close();
