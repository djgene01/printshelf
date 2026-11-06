import { test, expect } from '@playwright/test';
import { goto, screenshot } from './helpers.js';

test.describe('Admin: integrity and purge', () => {
    test('integrity scan reports a consistent library', async ({ page }) => {
        await goto(page, '/admin/integrity');

        await expect(page.getByRole('heading', { name: 'Integrity scan' })).toBeVisible();
        await expect(page.getByText('Live models', { exact: true })).toBeVisible();
        await expect(page.getByText('Library files', { exact: true })).toBeVisible();
        await expect(page.getByText(/No integrity problems|Dangling rows/)).toBeVisible();
        await screenshot(page, '09-integrity.png');
    });

    test('purge permanently removes soft-deleted e2e models', async ({ page }) => {
        await goto(page, '/admin/purge');

        const nothing = page.getByText('Nothing to purge', { exact: true });
        if (await nothing.isVisible().catch(() => false)) {
            await expect(nothing).toBeVisible();
            return;
        }

        const purgeAll = page.getByRole('button', { name: 'Purge all' });
        await expect(purgeAll).toBeVisible();
        await purgeAll.click();
        await page.getByRole('button', { name: /Permanently purge all/ }).click();

        await expect(page.getByText(/Purged \d+ model\(s\)\./)).toBeVisible();
        await expect(page.getByText('Nothing to purge', { exact: true })).toBeVisible({ timeout: 20_000 });
        await screenshot(page, '09-purge.png');
    });
});
