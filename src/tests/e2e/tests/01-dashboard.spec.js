import { test, expect } from '@playwright/test';
import { goto, screenshot, trackErrors } from './helpers.js';

test.describe('Dashboard', () => {
    test('shows stats, recent models and most printed without console errors', async ({ page }) => {
        const errors = trackErrors(page);
        await goto(page, '/');

        await expect(page.getByRole('heading', { name: 'Your 3D print library' })).toBeVisible();
        await expect(page.getByText('Models', { exact: true })).toBeVisible();
        await expect(page.getByText('Print runs', { exact: true })).toBeVisible();
        await expect(page.getByText('Success rate', { exact: true })).toBeVisible();
        await expect(page.locator('a[href^="/models/"]').first()).toBeVisible();

        await screenshot(page, '01-dashboard-1280.png');
        expect(errors).toEqual([]);
    });
});
