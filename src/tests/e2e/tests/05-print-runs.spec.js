import { test, expect } from '@playwright/test';
import { goto, screenshot, makeCubeStl, uniqueModel, uniqueScale, cubePath, deleteOpenModel } from './helpers.js';

test.describe('Print runs', () => {
    test('record, edit and delete a run updates the aggregate', async ({ page }) => {
        const model = uniqueModel();
        const stl = cubePath(model);
        makeCubeStl(stl, uniqueScale(model));

        await goto(page, '/upload');
        await page.locator('#upload-input').setInputFiles(stl);
        await expect(page.getByText(`Added “${model.displayName}”`)).toBeVisible({ timeout: 30_000 });
        await page.getByRole('link', { name: new RegExp(`Open ${model.displayName}`) }).click();
        await page.waitForURL(/\/models\/[0-9a-f-]{36}/);

        await expect(page.getByText('No print runs yet', { exact: false })).toBeVisible();
        await page.getByRole('button', { name: /Record print/ }).first().click();

        const form = page.locator('form').filter({ hasText: 'Add print run' });
        await form.locator('#printer').fill('E2E Printer');
        await form.locator('#duration').fill('45');
        await form.locator('#cost').fill('1.25');
        await form.getByRole('button', { name: 'Record print' }).click();

        const row = page.locator('tbody tr').filter({ hasText: 'E2E Printer' });
        await expect(row).toBeVisible();
        await expect(row).toContainText('success');
        await expect(row).toContainText('1.25');
        await screenshot(page, '05-print-runs.png');

        await row.getByRole('button', { name: 'Edit' }).click();
        const editForm = page.locator('form').filter({ hasText: 'Edit print run' });
        await editForm.locator('#printer').fill('E2E Printer 2');
        await editForm.locator('#duration').fill('60');
        await editForm.getByRole('button', { name: 'Save changes' }).click();

        const edited = page.locator('tbody tr').filter({ hasText: 'E2E Printer 2' });
        await expect(edited).toBeVisible();
        await expect(edited).toContainText('1h 0m');

        await edited.getByRole('button', { name: 'Delete', exact: true }).click();
        await page.getByRole('button', { name: 'Confirm', exact: true }).click();
        await expect(page.getByText('No print runs yet', { exact: false })).toBeVisible();

        await deleteOpenModel(page);
    });
});
