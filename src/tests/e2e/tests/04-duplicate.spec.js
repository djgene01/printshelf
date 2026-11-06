import { test, expect } from '@playwright/test';
import { goto, screenshot, makeCubeStl, uniqueModel, uniqueScale, cubePath, deleteOpenModel } from './helpers.js';

test.describe('Duplicate handling', () => {
    test('same SHA-256 prompts, skip records nothing, keep both creates a second row', async ({ page }) => {
        const model = uniqueModel();
        const stl = cubePath(model);
        makeCubeStl(stl, uniqueScale(model));

        await goto(page, '/upload');
        await page.locator('#upload-input').setInputFiles(stl);
        await expect(page.getByText(`Added “${model.displayName}”`)).toBeVisible({ timeout: 30_000 });

        await page.locator('#upload-input').setInputFiles(stl);
        await expect(page.getByRole('heading', { name: 'Already in the library' })).toBeVisible({ timeout: 30_000 });
        await screenshot(page, '04-duplicate-prompt.png');

        await page.getByRole('button', { name: 'Skip this file' }).click();
        await expect(page.getByText(`Skipped “${model.displayName}” (already in library).`)).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Already in the library' })).toBeHidden();

        // Upload again and keep both: a second catalogue entry shares the file.
        await page.locator('#upload-input').setInputFiles(stl);
        await expect(page.getByRole('heading', { name: 'Already in the library' })).toBeVisible();
        await page.getByRole('button', { name: 'Keep both' }).click();
        await expect(page.getByText(/Kept both — added/)).toBeVisible();

        await goto(page, `/catalogue?q=${model.displayName}`);
        const cards = page.locator('a[href^="/models/"]');
        await expect(cards.first()).toBeVisible();
        await expect(cards).toHaveCount(2);

        // Clean up both soft-deleted entries through the UI.
        for (let i = 0; i < 2; i++) {
            await page.locator('a[href^="/models/"]').first().click();
            await page.waitForURL(/\/models\/[0-9a-f-]{36}/);
            await expect(page.getByRole('heading', { level: 1 })).toContainText(model.displayName);
            await deleteOpenModel(page);
            await goto(page, `/catalogue?q=${model.displayName}`);
        }
    });
});
