import { test, expect } from '@playwright/test';
import { goto, trackErrors, makeCubeStl, cubePath, uniqueScale, deleteOpenModel } from './helpers.js';

test.describe('Bulk category assignment', () => {
    test('select two models and assign them to a category at once', async ({ page }) => {
        const errors = trackErrors(page);
        const stamp = Date.now();
        const a = { stamp: `${stamp}1`, fileName: `e2e-bulk-${stamp}-a.stl`, displayName: `e2e-bulk-${stamp}-a` };
        const b = { stamp: `${stamp}2`, fileName: `e2e-bulk-${stamp}-b.stl`, displayName: `e2e-bulk-${stamp}-b` };
        makeCubeStl(cubePath(a), uniqueScale(a));
        makeCubeStl(cubePath(b), uniqueScale(b));

        await goto(page, '/upload');
        await page.locator('#upload-input').setInputFiles([cubePath(a), cubePath(b)]);
        await expect(page.getByText(`Added “${a.displayName}”`)).toBeVisible({ timeout: 60_000 });
        await expect(page.getByText(`Added “${b.displayName}”`)).toBeVisible({ timeout: 60_000 });

        await goto(page, `/catalogue?q=e2e-bulk-${stamp}`);
        await expect(page.getByText(/2 models/)).toBeVisible();
        await page.getByLabel(`Select ${a.displayName}`).check();
        await page.getByLabel(`Select ${b.displayName}`).check();
        await expect(page.getByText('2 selected')).toBeVisible();
        await expect(page.getByRole('button', { name: 'Apply to selected' })).toBeDisabled();

        await page.getByLabel('Category for selected models').selectOption({ label: 'Tools' });
        await expect(page.getByRole('button', { name: 'Apply to selected' })).toBeEnabled();
        await page.getByRole('button', { name: 'Apply to selected' }).click();
        await expect(page.getByText('Assigned 2 models to “Tools”.')).toBeVisible();

        // The assignment is persisted, not just shown in the bulk bar.
        await goto(page, `/catalogue?q=e2e-bulk-${stamp}`);
        const cards = page.locator('a[href^="/models/"]');
        await expect(cards).toHaveCount(2);
        await expect(cards.nth(0).locator('[data-testid="model-category"]')).toHaveText('Tools');
        await expect(cards.nth(1).locator('[data-testid="model-category"]')).toHaveText('Tools');

        // Clean up the two temporary models. Let the model page finish its
        // first preview/thumbnail pass before deleting, otherwise the late
        // thumbnail POST can hit a deleted row and log a 400.
        for (const name of [a.displayName, b.displayName]) {
            await goto(page, `/catalogue?q=${name}`);
            await page.locator('a[href^="/models/"]').first().click();
            await page.waitForURL(/\/models\/[0-9a-f-]{36}/);
            await page.locator('canvas').waitFor({ timeout: 60_000 });
            await page.getByText('Loading mesh…').waitFor({ state: 'hidden', timeout: 60_000 });
            await page.waitForTimeout(500);
            await deleteOpenModel(page);
        }

        expect(errors).toEqual([]);
    });
});
