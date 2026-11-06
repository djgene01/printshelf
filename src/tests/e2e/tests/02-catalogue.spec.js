import { test, expect } from '@playwright/test';
import { goto, screenshot, trackErrors } from './helpers.js';

test.describe('Catalogue search, filter and views', () => {
    test('search narrows by name, then tag/category filters and sort/view work', async ({ page }) => {
        const errors = trackErrors(page);
        await goto(page, '/catalogue');

        const search = page.locator('input[name="search"]');
        await expect(search).toHaveAttribute('placeholder', /Search name/);
        await search.fill('bracket');
        await page.getByRole('button', { name: 'Search', exact: true }).click();

        const firstCard = page.locator('a[href^="/models/"]').first();
        await expect(firstCard).toBeVisible();
        await expect(firstCard).toContainText(/bracket/i);
        await expect(page.getByText(/matching “bracket”/)).toBeVisible();
        await screenshot(page, '02-catalogue-search.png');

        await page.getByRole('button', { name: 'Clear filters' }).click();
        await expect(page.getByText(/1,0\d\d models|1,0\d\d model\b/)).toBeVisible();

        // Tag filter: pick the first real tag (index 0 is "All tags").
        const tagSelect = page.locator('select[name="tag"]');
        const tagCount = await tagSelect.locator('option').count();
        expect(tagCount).toBeGreaterThan(1);
        await tagSelect.selectOption({ index: 1 });
        await expect(page.getByText(/tagged/)).toBeVisible();
        await screenshot(page, '02-catalogue-tag-filter.png');

        await page.getByRole('button', { name: 'Clear filters' }).click();

        // Sort by name and toggle list view.
        await page.locator('select[name="sort"]').selectOption('name');
        await page.getByRole('button', { name: /List/ }).click();
        await expect(page.getByRole('button', { name: /List/ })).toHaveAttribute('aria-pressed', 'true');
        await expect(page.locator('a[href^="/models/"]').first()).toBeVisible();
        await screenshot(page, '02-catalogue-list.png');

        expect(errors).toEqual([]);
    });
});
