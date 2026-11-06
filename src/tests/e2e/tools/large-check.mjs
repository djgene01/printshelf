import { chromium } from '@playwright/test';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '../../../..');
const shotDir = path.join(root, 'docs', 'screenshots');
const base = 'http://localhost:5100';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
const errors = [];
page.on('console', m => { if (m.type() === 'error') errors.push('console: ' + m.text()); });
page.on('pageerror', e => errors.push('pageerror: ' + e));

async function goto(url) {
  await page.goto(base + url, { waitUntil: 'networkidle' });
  await page.waitForFunction(() => window.Blazor !== undefined);
  await page.waitForTimeout(700);
}

async function purgeDeleted() {
  await goto('/admin/purge');
  const purgeAll = page.getByRole('button', { name: 'Purge all' });
  if (await purgeAll.isVisible().catch(() => false)) {
    await purgeAll.click();
    await page.getByRole('button', { name: /Permanently purge all/ }).click();
    await page.getByText(/Purged \d+ model/).waitFor();
  }
}

async function openModelLink(name) {
  await page.getByRole('link', { name: new RegExp(`Open ${name}`) }).click();
  await page.waitForURL(/\/models\/[0-9a-f-]{36}/);
  await page.getByRole('heading', { level: 1, name: new RegExp(`^${name}`) }).waitFor({ timeout: 60_000 });
  await page.waitForTimeout(500);
}

await purgeDeleted();
await goto('/upload');
const large = path.join(root, '.tmp', 'e2e', 'large-50mb.stl');
const started = Date.now();
await page.locator('#upload-input').setInputFiles(large);
await page.getByText(/Added “large-50mb”/).waitFor({ timeout: 240_000 });
console.log('large upload ms', Date.now() - started);
await page.screenshot({ path: path.join(shotDir, '10-large-upload.png'), fullPage: true });

await openModelLink('large-50mb');
const largeText = await page.locator('main').innerText();
console.log('tri count shown:', largeText.includes('1,003,520'), '| size:', largeText.match(/\d+(\.\d+)? MB/)?.[0]);
await page.screenshot({ path: path.join(shotDir, '10-large-model.png'), fullPage: true });

await page.getByRole('button', { name: 'Delete', exact: true }).click();
await page.getByRole('button', { name: 'Delete this model?' }).click();
await page.waitForURL(/\/catalogue/);

await goto('/upload');
await page.locator('#upload-input').setInputFiles(path.join(root, 'src', 'tests', 'PrintShelf.Stl.Tests', 'fixtures', 'cube_ascii.stl'));
await page.getByText(/Added “cube_ascii”/).waitFor({ timeout: 60_000 });
await openModelLink('cube_ascii');
const asciiText = await page.locator('main').innerText();
console.log('ascii 12 tris:', asciiText.includes('12'), '| 1000 volume:', asciiText.includes('1000'), '| 10 mm:', asciiText.includes('10 × 10 × 10'));
await page.screenshot({ path: path.join(shotDir, '10-ascii-model.png'), fullPage: true });

await page.getByRole('button', { name: 'Delete', exact: true }).click();
await page.getByRole('button', { name: 'Delete this model?' }).click();
await page.waitForURL(/\/catalogue/);

console.log('errors', JSON.stringify(errors));
await browser.close();
