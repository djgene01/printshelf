import { chromium } from '@playwright/test';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '../../../..');
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
const errors = [];
page.on('console', (m) => { if (m.type() === 'error') errors.push('console: ' + m.text()); });
page.on('pageerror', (e) => errors.push('pageerror: ' + String(e)));
page.on('requestfailed', (r) => errors.push('requestfailed: ' + r.url() + ' ' + r.failure()?.errorText));

await page.goto('http://localhost:5100/catalogue', { waitUntil: 'networkidle' });
await page.waitForTimeout(1200);
await page.screenshot({ path: path.join(root, '.tmp/catalogue.png') });
console.log('catalogue title:', await page.title());
console.log('cards:', await page.locator('a[href^="/models/"]').count());

const first = page.locator('a[href^="/models/"]').first();
const href = await first.getAttribute('href');
await page.goto('http://localhost:5100' + href, { waitUntil: 'networkidle' });
await page.waitForTimeout(500);
const canvas = page.locator('canvas');
console.log('canvas count:', await canvas.count());
if (await canvas.count()) {
    const box = await canvas.first().boundingBox();
    console.log('canvas box:', box);
}
await page.waitForTimeout(2500);
await page.screenshot({ path: path.join(root, '.tmp/model.png') });
console.log('thumbnail saved visible:', await page.getByText('Thumbnail saved').count());
const id = href.split('/').pop();
const thumb = await page.request.get(`http://localhost:5100/thumbs/${id}`);
console.log('thumb status:', thumb.status(), 'bytes:', (await thumb.body()).length);
console.log('errors:', errors);

await browser.close();
