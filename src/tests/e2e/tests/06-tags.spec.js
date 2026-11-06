import { test, expect } from '@playwright/test';
import { goto, screenshot } from './helpers.js';

test.describe('Tags', () => {
    test('create, rename and delete a tag', async ({ page }) => {
        const label = `e2e-tag-${Date.now()}`;
        await goto(page, '/tags');

        await page.locator('input[name="new-tag"]').fill(label);
        await page.getByRole('button', { name: 'Add tag' }).click();

        let row = page.getByTestId('tag-row').filter({ hasText: label });
        await expect(row).toBeVisible();
        await screenshot(page, '06-tags.png');

        await row.getByRole('button', { name: 'Rename' }).click();
        const editing = page.getByTestId('tag-row').filter({ has: page.locator('input[aria-label="Tag name"]') });
        await editing.locator('input[aria-label="Tag name"]').fill(`${label}-renamed`);
        await editing.getByRole('button', { name: 'Save' }).click();

        const renamed = `${label}-renamed`;
        row = page.getByTestId('tag-row').filter({ hasText: renamed });
        await expect(row).toBeVisible();

        await row.getByRole('button', { name: 'Delete', exact: true }).click();
        await page.getByRole('button', { name: 'Delete tag?' }).click();
        await expect(page.getByTestId('tag-row').filter({ hasText: renamed })).toHaveCount(0);
    });
});
