import { test, expect } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { goto, screenshot, trackErrors, makeCubeStl, uniqueModel, uniqueScale, cubePath, repoRoot, deleteOpenModel } from './helpers.js';

test.describe('Upload and 3D preview', () => {
    test('rejects non-STL, ingests a cube, renders it and creates a thumbnail', async ({ page }) => {
        const errors = trackErrors(page);
        const model = uniqueModel();
        const stl = cubePath(model);
        makeCubeStl(stl, uniqueScale(model));

        await goto(page, '/upload');

        const notStl = path.join(repoRoot, '.tmp', 'e2e', 'not-a-model.txt');
        fs.mkdirSync(path.dirname(notStl), { recursive: true });
        fs.writeFileSync(notStl, 'hello');
        await page.locator('#upload-input').setInputFiles(notStl);
        await expect(page.getByText('Only .stl files are accepted')).toBeVisible();

        await page.locator('#upload-input').setInputFiles(stl);
        await expect(page.getByText(`Added “${model.displayName}”`)).toBeVisible({ timeout: 30_000 });
        await screenshot(page, '03-upload-result.png');

        await page.getByRole('link', { name: new RegExp(`Open ${model.displayName}`) }).click();
        await page.waitForURL(/\/models\/[0-9a-f-]{36}/);

        const canvas = page.locator('canvas');
        await expect(canvas).toBeVisible();
        const box = await canvas.boundingBox();
        expect(box.width).toBeGreaterThan(200);
        expect(box.height).toBeGreaterThan(200);
        await expect(page.getByText('Thumbnail saved')).toBeVisible({ timeout: 30_000 });
        await page.waitForTimeout(500);

        // PNG export is generated from the same fitted scene.
        const downloadPromise = page.waitForEvent('download');
        await page.getByRole('button', { name: 'Save PNG' }).click();
        const download = await downloadPromise;
        expect(download.suggestedFilename()).toContain(model.displayName);

        await screenshot(page, '03-e2e-upload-preview.png');

        await goto(page, `/catalogue?q=${model.displayName}`);
        const thumb = page.locator('img[src*="/thumbs/"]').first();
        await expect(thumb).toBeVisible();
        await page.waitForFunction(() => {
            const img = document.querySelector('img[src*="/thumbs/"]');
            return img !== null && img.complete && img.naturalWidth > 0;
        });
        await screenshot(page, '03-e2e-catalogue-thumbnail.png');

        await page.locator(`a[href^="/models/"]`).first().click();
        await deleteOpenModel(page);

        expect(errors).toEqual([]);
    });

    test('renders an STL whose stored facet normals are all zero', async ({ page }) => {
        const errors = trackErrors(page);
        const model = uniqueModel();
        const stl = cubePath(model);
        makeCubeStl(stl, uniqueScale(model), true);

        await goto(page, '/upload');
        await page.locator('#upload-input').setInputFiles(stl);
        await expect(page.getByText(`Added “${model.displayName}”`)).toBeVisible({ timeout: 30_000 });
        await page.getByRole('link', { name: new RegExp(`Open ${model.displayName}`) }).click();
        await page.waitForURL(/\/models\/[0-9a-f-]{36}/);

        await expect(page.locator('canvas')).toBeVisible();
        await expect(page.getByText('Thumbnail saved')).toBeVisible({ timeout: 30_000 });
        await page.waitForTimeout(700);

        const luma = await page.evaluate(() => {
            const canvas = document.querySelector('canvas');
            const gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
            const pixels = new Uint8Array(canvas.width * canvas.height * 4);
            gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
            let max = 0;
            for (let i = 0; i < pixels.length; i += 4) {
                const value = 0.2126 * pixels[i] + 0.7152 * pixels[i + 1] + 0.0722 * pixels[i + 2];
                max = Math.max(max, value);
            }
            return max;
        });
        // A zero-normal mesh used to shade pure black; the rebuilt normals
        // produce a lit surface with near-white highlights.
        expect(luma).toBeGreaterThan(180);

        await deleteOpenModel(page);
        expect(errors).toEqual([]);
    });
});
