import fs from 'node:fs';
import path from 'node:path';

export const repoRoot = path.resolve(import.meta.dirname, '../../../..');
export const shotDir = path.join(repoRoot, 'docs', 'screenshots');

export async function goto(page, url) {
    await page.goto(url, { waitUntil: 'networkidle' });
    // Let the Blazor Server circuit connect: DOM events before that are lost.
    await page.waitForFunction(() => window.Blazor !== undefined, null, { timeout: 20_000 });
    await page.waitForTimeout(700);
}

export async function screenshot(page, name, fullPage = true) {
    fs.mkdirSync(shotDir, { recursive: true });
    await page.screenshot({ path: path.join(shotDir, name), fullPage });
}

export function trackErrors(page) {
    const errors = [];
    page.on('console', (msg) => {
        if (msg.type() === 'error') errors.push('console: ' + msg.text());
    });
    page.on('pageerror', (err) => errors.push('pageerror: ' + String(err)));
    page.on('requestfailed', (req) => {
        const failure = req.failure()?.errorText || '';
        if (!failure.includes('net::ERR_ABORTED')) {
            errors.push(`requestfailed: ${req.url()} ${failure}`);
        }
    });
    return errors;
}

const CUBE = [
    [[0, 0, 0], [0, 10, 0], [10, 10, 0]],
    [[0, 0, 0], [10, 10, 0], [10, 0, 0]],
    [[0, 0, 10], [10, 0, 10], [10, 10, 10]],
    [[0, 0, 10], [10, 10, 10], [0, 10, 10]],
    [[0, 0, 0], [10, 0, 0], [10, 0, 10]],
    [[0, 0, 0], [10, 0, 10], [0, 0, 10]],
    [[0, 10, 0], [0, 10, 10], [10, 10, 10]],
    [[0, 10, 0], [10, 10, 10], [10, 10, 0]],
    [[0, 0, 0], [0, 0, 10], [0, 10, 10]],
    [[0, 0, 0], [0, 10, 10], [0, 10, 0]],
    [[10, 0, 0], [10, 10, 0], [10, 10, 10]],
    [[10, 0, 0], [10, 10, 10], [10, 0, 10]]
];

function normal(a, b, c) {
    const u = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
    const v = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];
    const n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
    const len = Math.hypot(...n) || 1;
    return n.map((x) => x / len);
}

/** Unique binary cube STL so every run has a fresh SHA-256 (no accidental dedupe). */
export function makeCubeStl(filePath, scale, zeroNormals = false) {
    const buffer = Buffer.alloc(84 + 50 * CUBE.length);
    buffer.write('PrintShelf E2E cube', 0, 'ascii');
    buffer.writeUInt32LE(CUBE.length, 80);
    let offset = 84;
    for (const [a, b, c] of CUBE) {
        const n = zeroNormals ? [0, 0, 0] : normal(a, b, c);
        for (const v of n) { buffer.writeFloatLE(v, offset); offset += 4; }
        for (const point of [a, b, c]) {
            for (const v of point) { buffer.writeFloatLE(v * scale, offset); offset += 4; }
        }
        buffer.writeUInt16LE(0, offset); offset += 2;
    }
    fs.writeFileSync(filePath, buffer);
    return buffer.length;
}

export function uniqueModel() {
    const stamp = `${Date.now()}`;
    return {
        stamp,
        fileName: `e2e-cube-${stamp}.stl`,
        displayName: `e2e-cube-${stamp}`
    };
}

export function cubePath(model) {
    const dir = path.join(repoRoot, '.tmp', 'e2e');
    fs.mkdirSync(dir, { recursive: true });
    return path.join(dir, model.fileName);
}

/** Content-derived scale so each run gets a fresh SHA-256, not just a fresh name. */
export function uniqueScale(model) {
    return 2 + (Number(model.stamp.slice(-7)) % 90000) / 1000;
}

export async function deleteOpenModel(page) {
    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await page.getByRole('button', { name: 'Delete this model?' }).click();
    await page.waitForURL(/\/catalogue/, { timeout: 15_000 });
}
