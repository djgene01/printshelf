import { test, expect } from '@playwright/test';
import { goto, screenshot, trackErrors } from './helpers.js';

test.describe('Responsive layout', () => {
    test.use({ viewport: { width: 390, height: 844 } });

    test('mobile shell has no horizontal overflow and pages remain usable', async ({ page }) => {
        const errors = trackErrors(page);
        await goto(page, '/');

        await expect(page.locator('header').getByRole('link', { name: 'PrintShelf' })).toBeVisible();
        await expect(page.locator('aside').filter({ hasText: '3D print library' })).toBeHidden();
        await expect(page.getByRole('heading', { name: 'Your 3D print library' })).toBeVisible();

        let overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
        expect(overflow).toBeLessThanOrEqual(1);
        await screenshot(page, '08-mobile-dashboard.png');

        await goto(page, '/catalogue');
        await expect(page.locator('input[name="search"]')).toBeVisible();
        overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
        expect(overflow).toBeLessThanOrEqual(1);
        await screenshot(page, '08-mobile-catalogue.png');

        await goto(page, '/upload');
        await expect(page.locator('#drop-zone')).toBeVisible();
        overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
        expect(overflow).toBeLessThanOrEqual(1);
        await screenshot(page, '08-mobile-upload.png');

        expect(errors).toEqual([]);
    });
});
