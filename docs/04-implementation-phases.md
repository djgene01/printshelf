# 4. Implementation Phases

← [Index](README.md) · Prev: [Research & Reuse](03-research-and-reuse.md) · Next: [Testing](05-testing.md)

Machine-readable mirror: [phase-plan.json](../phase-plan.json).
**Implemented 2026-11-05:** P0–P6 complete and verified; see
[Build Record](08-build-record.md) for the evidence. P7 (accounts) remains
deferred. **Every phase ends in a user validation gate** — the original
protocol is below; the build record lists what was checked per phase.

---

## P0 — Scaffold (gate decisions: project name, U-03 Tailwind tooling)

1. `dotnet new blazor -n PrintShelf.Web -int Web` (.NET 10 template,
   **Interactive (Server)** render mode) targeting `net10.0`.
2. Add `PrintShelf.Stl` class library + test projects; wire solution.
3. Tailwind standalone CLI into `tools/`, `input.css` with
   `@import "tailwindcss" source(...)`, output to `wwwroot/css/app.css`;
   delete template Bootstrap-ish styles; wire a `--watch` dev script.
4. PostgreSQL: local dev DB + `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0,
   `AppDbContext`, first migration (`models`, `tags`, `model_tags`),
   connection string in user-secrets.
5. `appsettings` keys per [Architecture §8](02-architecture.md); create
   `data/library`, `data/thumbs` on startup.
6. Layout: sidebar nav (Catalogue / Upload / Tags / Settings), Tailwind
   shell, health page.

**Acceptance:** `dotnet build` clean; site runs at `http://localhost:5100`;
Tailwind class (e.g. `bg-slate-900`) visibly applied; migration applies to
PostgreSQL; CI-less `dotnet test` green (empty suite OK).

---

## P1 — Catalogue CRUD + upload plumbing (gate: U-04 max upload size)

- Entities + EF config per [Architecture §3](02-architecture.md); soft delete.
- Upload page: drag-drop + picker (`InputFile`), multiple files, per-file
  progress; temp-file staging; **magic sniff only** (real parsing lands in
  P2 — until then accepted files get `tri_count=0` placeholder, flagged).
- Configure size limits (Kestrel `MaxRequestBodySize`,
  `MultipartBodyLengthLimit`, SignalR buffer settings) — the usual Blazor
  upload gotcha; test with a ~50 MB STL.
- Model list (table), detail, edit, delete (soft) pages — Tailwind, plain.
- Tags CRUD + assign; **Categories** page: create/rename/delete, pick a
  material colour per category, assign a category on Upload or Edit. The
  viewer and thumbnail renderer use that colour.
- Bulk assignment: tick models in the catalogue grid/list (or **Select page**),
  choose a category in the bulk bar, and apply it to all selected models at
  once. Selection survives grid/list toggles and pagination; it is cleared when
  the search/category/tag filter changes.

**Acceptance:** upload/list/edit/delete round-trip works for a real STL;
non-STL rejected with message; oversize rejected at configured limit;
tags filter works.

---

## P2 — STL ingest: validation + geometry stats (gate: U-01 parser choice)

- `PrintShelf.Stl`: binary + ASCII parser, strict validation (truncation,
  tri-count vs actual bytes, NaN/Inf verts), returns triangles, bbox,
  triangle count, estimated volume ([Research §2](03-research-and-reuse.md)).
- xUnit suite with **golden fixtures** (tiny hand-made STLs + real ones) —
  see [Testing §2](05-testing.md). This is the highest-value test work.
- Wire into upload: parse → stats stored → SHA-256 dedupe UX (FR-03:
  "already in library" prompt, keep-both/skip).
- Content-addressed move + DB insert transaction order
  ([Architecture §4](02-architecture.md)).
- Admin "integrity scan" page: orphan files / dangling rows report.

**Acceptance:** known-good files report correct tri-count/bbox/volume
(matched against a reference tool, e.g. MeshLab/PrusaSlicer values);
corrupt/truncated files rejected with specific reason; duplicate upload
detected; stats visible on detail page.

---

## P3 — 3D preview + thumbnails (gate: U-02 thumb capture yes/no)

- Vendor three.js (+STLLoader/OrbitControls) into `wwwroot/vendor/three/`.
- `StlViewer.razor` + `stl-viewer.js`: create/dispose lifecycle, auto-center
  and scale camera to bbox, light grey shaded material, orbit/zoom/pan,
  reset button, dispose on navigate-away (memory!).
- `/files/{id}` minimal API streaming the STL (`Content-Disposition: inline`,
  cache by sha).
- Thumbnail capture [U-02]: offscreen render (category material colour) → POST
  PNG → store → `<img>` used in grid; fallback placeholder when missing.
- Ground shadow (`ShadowMaterial` catcher + scaled key-light shadow camera)
  in live view, thumbnails and batch renders.
- Batch **Previews** page: renders every missing thumbnail from its STL,
  with progress; “Regenerate all” rebuilds existing ones; model page can
  re-capture or save a 1600×1200 PNG.
- **Open in slicer:** detect installed OrcaSlicer, PrusaSlicer, and Cura;
  show a picker on the model page; launch the stored STL in the selected app.
  Explicit paths are configurable via `Slicers:*Path`.

**Acceptance:** preview of a ~100k-triangle STL is interactive [NFR-04];
no WebGL context leak after visiting 10 models (browser task manager);
thumb appears in grid after first preview; the installed slicers are listed
and the selected one receives the model file.

---

## P4 — Search, filter, sort, grid views

- Search box (ILIKE over name/description/tags), tag/category facets
  (categories come from the managed category list), sort (added, name, size,
  dims). Server-side via EF queries.
- Grid view with thumbnails (default) + list view toggle.
- Empty/zero-result states.

**Acceptance:** FR-07/FR-08 checks in phase-plan.json; query stays < 300 ms
on a seeded 1 000-row dev dataset.

---

## P5 — Print-run log

- `print_runs` table + CRUD UI on the model page; result enum;
  aggregates (count, success rate, total time, est. cost from material).
- Simple stats card on detail page; "most printed" on dashboard.

**Acceptance:** FR-09; aggregates verified against hand-computed fixture.

---

## P6 — Export, hard-delete/purge, backup & deploy polish (gate: U-05 LAN?)

- CSV export (catalogue, print runs) streamed from server.
- Purge page for soft-deleted models (file + thumb + row).
- Backup script (`pg_dump` + `data/` copy) + restore notes
  ([Deployment §5](06-deployment.md)); optional scheduled task.
- Run-as-service decision: Task Scheduler or NSSM; LAN bind + firewall
  rule if U-05 = yes.

**Acceptance:** CSV opens in Excel with correct encoding (UTF-8 BOM);
restore drill from a backup actually restores; service survives logoff.

---

## P7 — (Deferred, D5) User accounts

- ASP.NET Core Identity (Blazor template) on same PostgreSQL; login page;
  `owner_id` columns + query filters; per-user upload quotas optional.
- Until this phase ships: **LAN-only binding is mandatory**
  ([Risks R-04](07-risks-and-acceptance.md)).

**Acceptance:** anonymous visitors see only login; users see only their
models; existing single-user data assigned to first account.

---

## Sequencing notes

- P2 before P3 (preview wants bbox for camera fit).
- P4 and P5 are independent — either order.
- P7 is intentionally last: auth touches every page; doing it after the
  feature set settles avoids rework.
