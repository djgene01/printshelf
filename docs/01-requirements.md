# 1. Requirements

← [Index](README.md) · Next: [Architecture](02-architecture.md)

## 1. Vision

A private web site where the owner uploads, organises, previews and tracks
their **STL 3D print files** — a personal "library of things to print" with
search, tags, a 3D preview in the browser, and print-run history per model.

## 2. Scope

### In scope (MVP, phases P0–P6)

- Upload of **STL files** (binary and ASCII), stored on local disk [A-01]
- Catalogue CRUD: name, description, category, tags, notes
- Managed **categories** with a material colour per category; models render
  in their category's colour in previews and thumbnails
- Automatic metadata at upload: file size, SHA-256 (dedupe), triangle count,
  bounding box (dimensions), estimated volume [A-05]
- In-browser **3D preview** of STL models (three.js) [A-04]
- Thumbnail images for grid views [U-02]
- Search, filter (tag/category), sort (name, date, size, dimensions)
- **Print-run log** per model: printer, filament/material, nozzle, layer
  height, print time, result (success/failed), cost estimate (P5)
- CSV export of the catalogue + print runs; backup guidance (P6)

### Deferred (explicitly out of MVP)

- **User accounts / login / ownership** — deferred by user decision (D5) to
  phase **P7**. Until then the site is **LAN-only** and must never be exposed
  to the internet (see [Risks R-04](07-risks-and-acceptance.md)).
- Other file formats (3MF, OBJ, GLB, STEP…) — user chose **STL only** (D3).
  Architecture keeps a `format` column so later formats are additive.
- Multi-printer fleet management, slicer integration, g-code storage.
- Mobile app (responsive Tailwind layout is in scope; native app is not).

## 3. Functional requirements

| ID | Requirement | Phase |
|----|-------------|-------|
| FR-01 | User can upload one or many `.stl` files (drag-drop + file picker) | P1 |
| FR-02 | Upload rejects non-STL, corrupt or oversize (>200 MB [A-02], [U-04]) files with a clear message | P1/P2 |
| FR-03 | Duplicate upload (same SHA-256) is detected; user chooses keep-both or skip | P2 |
| FR-04 | Every model shows: name, tags, category, size, date added, dimensions, triangle count, volume | P2 |
| FR-05 | User can edit metadata and delete a model (file + thumbnail + DB row together) | P1/P4 |
| FR-06 | Model detail page shows interactive 3D preview: orbit, zoom, pan, reset | P3 |
| FR-07 | Grid/list catalogue with thumbnails; grid is default | P3/P4 |
| FR-08 | Free-text search across name/description/tags; filter by tag/category; sort | P4 |
| FR-09 | Print-run log CRUD per model; aggregate stats (times printed, success rate, est. cost) | P5 |
| FR-10 | CSV export of catalogue and print runs | P6 |
| FR-11 | UI usable on desktop and phone widths (Tailwind responsive) | all |
| FR-12 | User can create/rename/delete categories and set a material colour per category; models render in that colour | P1/P3 |
| FR-13 | Select multiple models in the catalogue and assign them to a category in one action | P1 |
| FR-14 | Launch a model in an installed desktop slicer — OrcaSlicer, PrusaSlicer, or Cura — choosing the app | P3 |

## 4. Non-functional requirements

| ID | Requirement | Target |
|----|-------------|--------|
| NFR-01 | Platform | .NET 10 (SDK 10.0.401 on the dev machine), Blazor Server (D1) |
| NFR-02 | Database | PostgreSQL, EF Core 10 + Npgsql 10.0 (D2), migrations only |
| NFR-03 | Styling | Tailwind CSS v4, standalone CLI default [U-03] (D6) |
| NFR-04 | Preview performance | 100k-triangle STL renders interactively (>15 fps orbit) on a typical laptop |
| NFR-05 | Catalogue scale | Smooth browsing up to ~5 000 models / ~50 GB of files |
| NFR-06 | Upload throughput | 100 MB STL uploads in under ~30 s on localhost |
| NFR-07 | Data safety | DB↔disk consistency rule + orphan scan documented; nightly `pg_dump` [A-03] |
| NFR-08 | No internet dependency at runtime | three.js + Tailwind served locally (no CDN at run time) |
| NFR-09 | Language | English-only UI [A-06] |
| NFR-10 | No auth in MVP (D5) — **LAN-only binding required** (see [Deployment](06-deployment.md)) |

## 5. Out-of-scope acceptance traps (what "done" does NOT mean)

- Not a slicer, not a print server — files are only *catalogued and previewed*.
- No cloud sync, no Printables/Thingiverse import in MVP.
- No role-based permissions until P7.

## 6. Requirements traceability

Each FR maps to phase acceptance checks in
[phase-plan.json](../phase-plan.json); the global checklist is in
[7. Risks & Acceptance](07-risks-and-acceptance.md).
