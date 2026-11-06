import { test, expect } from '@playwright/test';

test.describe('CSV export', () => {
    test('models and print-runs CSV are UTF-8 BOM with headers and rows', async ({ page }) => {
        const models = await page.request.get('/export/models.csv');
        expect(models.status()).toBe(200);
        expect(models.headers()['content-type']).toContain('text/csv');
        const modelBytes = await models.body();
        expect([...modelBytes.subarray(0, 3)]).toEqual([0xEF, 0xBB, 0xBF]);
        const modelText = modelBytes.subarray(3).toString('utf8');
        const modelLines = modelText.split(/\r?\n/).filter(Boolean);
        expect(modelLines[0]).toContain('Name,Category,Tags');
        expect(modelLines.length).toBeGreaterThan(1000);

        const runs = await page.request.get('/export/print-runs.csv');
        expect(runs.status()).toBe(200);
        const runBytes = await runs.body();
        expect([...runBytes.subarray(0, 3)]).toEqual([0xEF, 0xBB, 0xBF]);
        expect(runBytes.subarray(3).toString('utf8').split(/\r?\n/)[0]).toContain('Model,Printer');
    });
});
