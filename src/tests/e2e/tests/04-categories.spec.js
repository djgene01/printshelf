import { test, expect } from '@playwright/test';
import { goto, screenshot, trackErrors, uniqueModel, cubePath, uniqueScale, makeCubeStl, deleteOpenModel } from './helpers.js';

test.describe('Categories and per-category material colour', () => {
    test('create a category, colour it, upload into it and preview that colour', async ({ page }) => {
        const errors = trackErrors(page);
        const stamp = Date.now();
        const category = `e2e-cat-${stamp}`;

        await goto(page, '/categories');
        await page.locator('#new-category-name').fill(category);
        await page.getByRole('button', { name: 'Create category' }).click();
        await expect(page.getByLabel(`Name for ${category}`)).toBeVisible();

        // Change the category colour through the row's colour picker.
        const row = page.locator('li').filter({ has: page.getByLabel(`Colour for ${category}`) });
        await row.getByLabel(`Colour for ${category}`).evaluate((el) => {
            el.value = '#ff0000';
            el.dispatchEvent(new Event('change', { bubbles: true }));
        });
        await expect(page.getByText(`${category} is now #ff0000`)).toBeVisible();

        // Reload: the colour must be persisted.
        await goto(page, '/categories');
        await expect(page.getByLabel(`Colour for ${category}`)).toHaveValue('#ff0000');

        // Upload a cube and place it in the new category.
        const model = uniqueModel();
        makeCubeStl(cubePath(model), uniqueScale(model));
        await goto(page, '/upload');
        await page.locator('#upload-category').selectOption({ label: category });
        await page.locator('#upload-input').setInputFiles(cubePath(model));
        await expect(page.getByText(`Added “${model.displayName}”`)).toBeVisible({ timeout: 60_000 });
        await page.getByRole('link', { name: `Open ${model.displayName} →` }).click();
        await page.waitForURL(/\/models\/[0-9a-f-]{36}/);

        await expect(page.getByText(category.toUpperCase())).toBeVisible();
        await page.locator('canvas').waitFor({ timeout: 60_000 });
        await page.getByText('Loading mesh…').waitFor({ state: 'hidden', timeout: 60_000 });

        // Read the rendered centre pixel: the material must be red, not the
        // default off-white (#e7edf5). WebGL keeps the buffer for screenshots.
        let pixel = null;
        for (let attempt = 0; attempt < 40; attempt++) {
            pixel = await page.evaluate(() => {
                const canvas = document.querySelector('canvas');
                const gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
                const p = new Uint8Array(4);
                gl.readPixels(canvas.width >> 1, canvas.height >> 1, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, p);
                return [p[0], p[1], p[2]];
            });
            if (pixel[0] > 100 && pixel[1] < 130 && pixel[2] < 130) break;
            await page.waitForTimeout(250);
        }
        expect(pixel[0], `pixel ${pixel}`).toBeGreaterThan(100);
        expect(pixel[1], `pixel ${pixel}`).toBeLessThan(130);
        expect(pixel[2], `pixel ${pixel}`).toBeLessThan(130);

        await screenshot(page, '14-category-colour-preview.png');
        await deleteOpenModel(page);

        // Remove the category; the uploaded model is already soft-deleted.
        await goto(page, '/categories');
        const deleteRow = page.locator('li').filter({ has: page.getByLabel(`Name for ${category}`) });
        await deleteRow.getByRole('button', { name: 'Delete' }).click();
        await deleteRow.getByRole('button', { name: /Delete this category/ }).click();
        await expect(page.getByLabel(`Name for ${category}`)).toHaveCount(0);

        expect(errors).toEqual([]);
    });
});
