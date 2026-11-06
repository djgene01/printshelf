import { defineConfig, devices } from '@playwright/test';

/**
 * E2E against a running PrintShelf instance (docs/05-testing.md §4).
 * Start the app first:  dotnet run --project src/PrintShelf.Web
 * Then:                 cd src/tests/e2e && npx playwright test
 */
export default defineConfig({
    testDir: './tests',
    fullyParallel: false,
    workers: 1,
    timeout: 90_000,
    expect: { timeout: 20_000 },
    reporter: [['list']],
    use: {
        baseURL: process.env.PRINTSHELF_URL || 'http://localhost:5100',
        viewport: { width: 1280, height: 900 },
        actionTimeout: 20_000,
        navigationTimeout: 30_000,
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure'
    },
    projects: [
        { name: 'chromium', use: { ...devices['Desktop Chrome'] } }
    ]
});
