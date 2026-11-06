import { chromium } from '@playwright/test';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '../../../..');
const shot = path.join(root, 'docs', 'screenshots');
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
for (const [url, name] of [['/', '01-dashboard-1280.png'], ['/catalogue', '02-catalogue-grid.png']]) {
    await page.goto('http://localhost:5100' + url, { waitUntil: 'networkidle' });
    await page.waitForFunction(() => window.Blazor !== undefined);
    await page.waitForTimeout(900);
    await page.screenshot({ path: path.join(shot, name), fullPage: true });
    console.log('shot', name);
}
await browser.close();
