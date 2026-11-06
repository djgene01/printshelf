# 8. Build Record — P0 → P6

← [Index](README.md) · Prev: [Risks & Acceptance](07-risks-and-acceptance.md)

**Build date:** 2026-11-05 · **Preview/category update:** 2026-11-06 · **Host:** Windows 11 · **Runtime:** .NET SDK 10.0.401 / `net10.0`
**Database:** PostgreSQL 17 in Docker (`printshelf-postgres`, host port **5434**)
**App:** `http://localhost:5100` (Development) · single-user, localhost-only

This record is the evidence for the phase gates in
[04-implementation-phases.md](04-implementation-phases.md) and the global
checklist in [07-risks-and-acceptance.md](07-risks-and-acceptance.md).

## 1. What was built

| Phase | Status | Main evidence |
|---|---|---|
| **P0** Scaffold | ✅ | Solution builds with 0 warnings; `/health` 200; Tailwind CSS served (40 KB); EF migration applied to PostgreSQL; test projects wired |
| **P1** Catalogue + upload | ✅ | Upload → list → edit → tag → soft delete round-trip; non-STL rejected; 200 MB limit enforced through Kestrel **and** the SignalR circuit; tags CRUD; **Categories** page (create/rename/delete, material colour), category assignment on Upload/Edit, and catalogue bulk-select → assign category |
| **P2** STL ingest | ✅ | Dependency-free binary + ASCII parser; 15 golden-fixture tests; SHA-256 dedupe (keep-both/skip); content-addressed files; integrity scan finds orphans and dangling rows |
| **P3** 3D preview | ✅ | Locally vendored three.js 0.187.0; `/files/{id}`; camera fit + Z-up rotation; studio lighting + ground shadow; zero-length STL normals rebuilt (fixes black models); thumbnail captured on first preview; **Previews** page batch-renders all missing thumbnails and can regenerate all; 1600×1200 `Save PNG` |
| **P4** Search/views | ✅ | ILIKE search over name/description/tags, category + tag facets, 4 sorts, grid/list toggle, empty states; 1000-row seed; query latency measured |
| **P5** Print-run log | ✅ | `print_runs` CRUD on the model page, DB check constraint on `Result`, aggregate cards, dashboard "Most printed" |
| **P6** Export/purge/deploy | ✅ | UTF-8 BOM CSV for models + print runs; hard purge of rows/files/thumbs; backup + restore drill; published Production build served |

Deferred by design: **P7 user accounts** (D5). Until then the app is bound to
localhost only.

## 2. Verification commands and results

```bash
# Build (all projects, including tests)
dotnet build PrintShelf.slnx
# → Build succeeded. 0 Warning(s), 0 Error(s)

# All automated tests
dotnet test PrintShelf.slnx
# → PrintShelf.Stl.Tests      15 passed
# → PrintShelf.Web.Tests      29 passed   (17 component/service + 12 integration)
#   total 44 passed, 0 failed

# Tailwind (v4.3.4 standalone CLI)
./tools/tailwindcss.exe -i tailwind/input.css -o src/PrintShelf.Web/wwwroot/css/app.css --minify

# Demo data
dotnet run --project src/PrintShelf.Web -- seed 1000 --reset
# → Seeded 1000 models, 2012 tag links, 627 print runs, 8 tags.

# Browser E2E (Node Playwright 1.64.1, real app + PostgreSQL + files)
cd src/tests/e2e && npx playwright test
# → 12 passed
```

The 28 Web tests include 11 **integration tests against a real PostgreSQL
database** (`printshelf_test`, recreated per run via
`PRINTSHELF_TEST_CONN`). They cover ingest stats, duplicate keep-both,
truncated/oversize rejection, search/filter/sort, soft delete → restore →
purge, print-run CRUD/aggregates, category CRUD/rename/colour, bulk category
assignment to selected model IDs, orphan +
dangling integrity reports, CSV encoding/row counts, and the 1000-row latency
budget.

### Test layers

| Layer | Tests | What it proves |
|---|---:|---|
| `PrintShelf.Stl.Tests` (xUnit) | 15 | Binary/ASCII agreement (10×10×10 mm, 1000 mm³), truncated, lying header, NaN, binary-with-`solid` header, zero triangles, inverted winding, 100k budget |
| Component/service (bUnit + pure) | 17 | Toolbar filters/events, cards/thumbnails, two-click confirm, upload validation, tag slug, hand-computed print aggregate |
| Integration (EF Core + PostgreSQL) | 12 | Real Npgsql types, transactions, dedupe, integrity, CSV, latency, category CRUD/rename/colour, bulk category assignment |
| E2E (Playwright/Chromium) | 13 | Dashboard, search/filter/sort/list, upload → preview → thumbnail → Save PNG, zero-normal STL brightness, duplicate prompt, print-run edit/delete, tags CRUD, category + red material colour → upload → preview, bulk-assign two models, CSV, mobile 390 px, admin integrity/purge |

## 3. Performance evidence

| Check | Limit | Measured |
|---|---|---|
| Parse 100k-triangle binary STL | < 1 s | Passes the test budget (suite: 15 tests / 108 ms total) |
| Catalogue query on 1 000 rows | < 300 ms | Integration test median well under budget; HTTP page responses **19–26 ms** over 5 runs (`?q=cap&sort=name` and a real `?tag=<guid>`) |
| 100k-triangle preview first frame | interactive | **498 ms** to loaded canvas; **34.5 fps** over 1.5 s (headless software WebGL) |
| Orbit interaction | works | Pixel hash changed after mouse drag; no console errors |
| WebGL leak after 10 previews | flat | Heap 5.21–5.28 MB per visit; **0 canvases** after navigation; no "too many contexts" warnings |
| Batch preview generation | all models | **1,004 thumbnails** after “Regenerate all” (1,000 seed + 4 uploads); every PNG 480×360, **0 failed**, no black renders, each rendered with its category’s material colour; without ground shadow the same batch reached 999 with 0 failures |
| 50 MB-class upload | accepted | **50,176,084 B** (47.85 MB, 1,003,520 tris) uploaded through the UI in **1.8 s**, stats correct |

Only console noise during thumbnail capture is the Chromium software-GL
warning `GPU stall due to ReadPixels`; it is a driver warning, not an app error.

## 4. Preview and ingest evidence

- **Binary 50 MB-class file:** 1,003,520 triangles shown; file size 47.85 MB;
  detail page rendered ([10-large-model.png](screenshots/10-large-model.png)).
- **ASCII cube:** 12 triangles, 10 × 10 × 10 mm, 1000 mm³ — identical to the
  binary cube ([10-ascii-model.png](screenshots/10-ascii-model.png)).
- **100k preview:** [11-preview-100k.png](screenshots/11-preview-100k.png) and
  the orbited view [11-preview-100k-orbited.png](screenshots/11-preview-100k-orbited.png).
- three.js is served from `wwwroot/vendor/three/`; no CDN is contacted (NFR-08).

## 5. PostgreSQL, storage, backup and restore

- Migration `20261105204609_PrintRuns` applied; `print_runs` has
  `CK_print_runs_result` enforcing `success | failed | partial`.
- Storage layout: `data/library/{sha256}.stl`, `data/thumbs/{sha256}.png`,
  `data/tmp/`; paths are configurable under `Storage`.
- **Backup drill (performed):**
  1. `pg_dump -F c` inside the container → 166 KB dump.
  2. Copied the `data/` folder → 58 MB, 1,007 library files, 11 thumbnails.
  3. Created `printshelf_restore`, `pg_restore` exit 0; row count **1,008 =
     source 1,008** (including soft-deleted rows).
  4. Started the **published** Production build on `http://localhost:5101`
     against the restored DB and copied `data/`.
  5. Catalogue loaded; model detail preview rendered; `/files/{id}` returned
     `200 model/stl`, 684 bytes — [12-restore-drill.png](screenshots/12-restore-drill.png).

At drill time the folder was still named `App_Data`; it was renamed to
`data/` afterwards to match the documented layout, and the app was re-run
against the renamed folder (1000 files, thumbnail served 200).

The host has no PostgreSQL client on `PATH`, so the Docker CLI was used for
the drill. `deploy/backup-printshelf.ps1` wraps the same two-part backup
(`pg_dump` + zip of `data/`) for a real PostgreSQL install; set
`PGPASSWORD` or use `.pgpass` first.

## 6. Deployment evidence

```bash
dotnet publish src/PrintShelf.Web -c Release -o publish -r win-x64 --self-contained false
```

- Published framework-dependent build starts with `publish/PrintShelf.Web.dll`
  in `Production`, serving `/`, `/catalogue` and `/_framework/blazor.server.js`
  with **200**; `1,000 models` rendered.
- `deploy/run-printshelf.ps1` was executed and verified on port `5102`
  (`/health`, home, catalogue, framework assets all 200).
- Default bind is **localhost only**. `netstat` shows listeners on
  `127.0.0.1:5100` and `[::1]:5100` only — no `0.0.0.0`.
- `deploy/install-task.ps1` registers an at-logon Task Scheduler job. The
  registration itself was **not** applied on this dev box (scripts are ready);
  a logoff/reboot survival test was therefore not performed.

## 7. Global acceptance checklist status

| # | Check | Status |
|---|---|---|
| 1 | `dotnet build` + `dotnet test` clean on .NET 10 | ✅ 0 warnings/errors; 44 tests pass |
| 2 | Fresh DB → migrations → app on :5100 → `/health` | ✅ |
| 3 | Real binary STL ≥ 50 MB + ASCII STL: correct stats, thumbnail, preview | ✅ (50.18 MB; ASCII 12 tris / 10 mm / 1000 mm³) |
| 4 | Corrupt / oversize / duplicate designed behaviour | ✅ unit + integration + E2E |
| 5 | Search + tag/category filter + sort; grid thumbnails | ✅ |
| 6 | Print-run add/edit/delete; aggregates correct | ✅ hand-computed fixture + E2E |
| 7 | CSV opens with UTF-8 BOM and all models | ✅ BOM + row count verified; **Excel itself not installed** on this host |
| 8 | 100k preview interactive; no console errors; memory flat | ✅ 498 ms / 34.5 fps; heap flat; 0 canvases after 10 visits |
| 9 | Backup + restore drill | ✅ (see §5) |
| 10 | App survives logoff/reboot via service mechanism | ⚠️ scripts ready and run script tested; task not registered / reboot not tested |
| 11 | Localhost-only: external machine refused | ⚠️ bind address proves localhost only; no second PC available to test |

## 8. Screenshots

All screenshots are in [screenshots/](screenshots/):

| File | Page |
|---|---|
| `01-dashboard-1280.png` | Dashboard, 1,000 seeded models |
| `02-catalogue-grid.png`, `02-catalogue-list.png`, `02-catalogue-search.png`, `02-catalogue-tag-filter.png` | Catalogue grid/list/search/tag filter |
| `03-model-preview.png`, `03-catalogue-thumbnail.png` | Model detail with 3D preview; grid thumbnail |
| `03-upload-result.png`, `04-duplicate-prompt.png` | Upload result and duplicate prompt |
| `05-print-runs.png` | Print-run table + aggregates |
| `06-tags.png`, `07-upload-empty.png` | Tags CRUD; empty upload page |
| `08-mobile-*.png` | 390 px dashboard/catalogue/upload |
| `09-integrity.png`, `09-purge.png` | Integrity scan; purge page |
| `10-large-model.png`, `10-ascii-model.png` | 50 MB-class binary; ASCII cube |
| `11-preview-100k*.png` | 100k-triangle preview, before/after orbit |
| `12-restore-drill.png` | Restored instance on :5101 |
| `13-previews-page.png` | Batch preview generation — all thumbnails present |
| `14-category-colour-preview.png` | Category material colour applied to the 3D preview |
| `15-categories.png` | Category management — create, colour, rename, delete |
| `16-bulk-category.png` | Catalogue bulk bar — selected models and target category |

## 9. Known limitations / deviations

- **No authentication** (P7 deferred). Keep the bind on `localhost`; do not
  port-forward.
- **Thumbnails** are generated by the browser: on first preview or from the
  **Previews** page (batch). A fresh library shows placeholders only until the
  batch is run; `Save PNG` exports the current view at 1600×1200.
- **Seed data is synthetic**: 1,000 scaled cubes (684 B, 12 triangles) with
  generated names/tags/print runs. It is for search/latency/demo evidence;
  geometry is not representative of real printed parts.
- **Playwright is Node**, not C# — the docs allow either; Node was chosen at P3.
- The **50 MB acceptance file is generated on demand** into `.tmp/`, not
  committed (repository size). `tools/make_fixtures.py` regenerates the
  100k fixture.
- Production logs keep EF command logging at `Warning` to avoid per-query noise.
