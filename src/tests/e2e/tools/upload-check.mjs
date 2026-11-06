import { chromium } from '@playwright/test';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '../../../..');
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
const errors = [];
page.on('console', (m) => { if (m.type() === 'error') errors.push('console: ' + m.text()); });
page.on('pageerror', (e) => errors.push('pageerror: ' + String(e)));
page.on('requestfailed', (r) => errors.push('requestfailed: ' + r.url() + ' ' + r.failure()?.errorText));

for (let i = 0; i < 30; i++) {
    try {
        const r = await page.request.get('http://localhost:5100/health');
        if (r.ok()) break;
    } catch {}
    await new Promise((r) => setTimeout(r, 1000));
}

await page.goto('http://localhost:5100/catalogue', { waitUntil: 'networkidle' });
const searchValue = await page.locator('input[name="search"]').getAttribute('placeholder');
const searchAttr = await page.locator('input[name="search"]').inputValue();
const sortValue = await page.locator('select[name="sort"]').inputValue();
console.log('placeholder:', searchValue, 'searchValue:', JSON.stringify(searchAttr), 'sort:', sortValue);

await page.goto('http://localhost:5100/upload', { waitUntil: 'networkidle' });
await page.locator('#upload-input').setInputFiles(path.join(root, 'tools/samples/pyramid.stl'));
await page.getByText('This session').waitFor({ timeout: 20000 });
console.log('upload rows:', await page.locator('section ul li').allTextContents());
await page.screenshot({ path: path.join(root, '.tmp/upload.png') });

const open = page.getByRole('link', { name: /^Open / }).first();
await open.click();
await page.waitForURL(/\/models\//, { timeout: 15000 });
await page.locator('canvas').waitFor({ timeout: 20000 });
await page.getByText('Thumbnail saved').waitFor({ timeout: 20000 });
await page.waitForTimeout(1000);
await page.screenshot({ path: path.join(root, '.tmp/uploaded-model.png') });
console.log('model url:', page.url());

// Duplicate flow
await page.goto('http://localhost:5100/upload', { waitUntil: 'networkidle' });
await page.locator('#upload-input').setInputFiles(path.join(root, 'tools/samples/pyramid.stl'));
await page.getByRole('heading', { name: 'Already in the library' }).waitFor({ timeout: 20000 });
console.log('duplicate prompt visible');
await page.screenshot({ path: path.join(root, '.tmp/duplicate.png') });
await page.getByRole('button', { name: 'Keep both' }).click();
await page.getByText(/Kept both/).waitFor({ timeout: 20000 });
console.log('kept both; rows:', await page.locator('section ul li').allTextContents());

console.log('errors:', errors);
await browser.close();
