# 2. Architecture

← [Index](README.md) · Prev: [Requirements](01-requirements.md) · Next: [Research & Reuse](03-research-and-reuse.md)

## 1. Solution layout

```
PrintShelf/
├── PrintShelf.sln
├── src/
│   ├── PrintShelf.Web/            # Blazor Server app (net10.0)
│   │   ├── Components/            # App, Routes, Layouts, Pages
│   │   ├── Services/              # IngestService, CatalogService, PrintRunService
│   │   ├── Data/                  # AppDbContext, Entities/, Migrations/
│   │   └── wwwroot/
│   │       ├── css/app.css        # Tailwind output
│   │       ├── js/stl-viewer.js   # three.js interop module
│   │       └── vendor/three/      # three.js + STLLoader/OrbitControls (local copy) [NFR-08]
│   ├── PrintShelf.Stl/            # Pure C# STL parsing + geometry stats (no deps)
│   └── tests/
│       ├── PrintShelf.Stl.Tests/      # xUnit + golden STL fixtures
│       ├── PrintShelf.Web.Tests/      # bUnit component tests
│       └── e2e/                       # Playwright (Node side or C# Playwright)
├── data/                          # runtime, git-ignored
│   ├── library/{sha256}.stl       # [A-01] content-addressed files
│   └── thumbs/{sha256}.png        # [A-04][U-02]
└── tools/tailwindcss.exe          # standalone CLI [U-03]
```

**Project dependency rule:** `Web → Stl` only. `Stl` has **zero NuGet
dependencies** so parser tests never touch EF or the web stack.
(U-01 default: internal parser, see [Research §2](03-research-and-reuse.md).)

## 2. Process model

Single Blazor Server process (D1) on Kestrel. SignalR circuits carry UI
state; file uploads use Blazor `<InputFile>`/`<FileUpload>` streamed to a
temp file, then moved into content-addressed storage. No background workers
in MVP; the orphan/drift scan is an admin page + optional scheduled script
(see [Deployment §5](06-deployment.md)).

## 3. PostgreSQL schema (EF Core 10 + Npgsql 10.0, D2)

```
models
  id            uuid pk
  name          text not null
  description   text
  category      text null                   -- loose ref to categories.name
  format        text not null default 'stl'   -- additive for later formats (D3)
  file_sha256   char(64) not null             -- links to disk file [A-01]
  file_bytes    bigint not null
  tri_count     int not null
  bbox_x/y/z    numeric(10,3) not null        -- mm, from mesh [A-05]
  volume_mm3    numeric(14,3) null            -- signed-volume method
  print_unit    text not null default 'mm'    -- STL has no unit; user-declared
  created_at / updated_at  timestamptz
  deleted_at    timestamptz null              -- soft delete (P1), purge in P6

tags       (id uuid pk, slug citext unique, label text)
model_tags (model_id fk, tag_id fk, pk(model_id, tag_id))

categories (id uuid pk, name varchar(80) unique, color char(7), created_at)
           -- user-managed; color is the preview material colour

print_runs
  id          uuid pk
  model_id    fk -> models
  printer     text not null
  material    text, color text
  nozzle_mm   numeric(4,2), layer_mm numeric(4,3)
  duration_min int
  result      text check in ('success','failed','partial')
  notes       text
  printed_at  timestamptz not null
```

Indexes: `models(name)`, `models(category)`, `models(file_sha256)` (unique
per non-deleted row — enforced in service layer, plain index in DB),
`categories(name)` unique. `models.category` is a loose reference by name
(the migration promotes existing strings into `categories`); renaming or
deleting a category updates/clears its models in the service layer, and a
category colour is applied to every model preview in that category.

**Unit caveat (documented, not solved):** STL has no unit metadata. The app
assumes millimetres by default and lets the user declare the unit per model
(`print_unit`) — misreading mm vs in is the #1 catalogue data-quality trap.

## 4. File storage & the DB↔disk contract [A-01]

- Upload → stream to temp → parse+validate (P2) → compute SHA-256 →
  move to `data/library/{sha256}.stl` (skip move if exists = dedupe, FR-03)
  → insert DB row in one transaction.
- **Order matters:** file first, DB row second. A crash between the two
  leaves an *orphan file* (harmless, swept by the scan), never a *dangling
  row* (broken UI). Delete = DB soft-delete first, file+thumb purged in P6
  hard-delete step.
- Path safety: filename is always the hex digest the server computed —
  **never** the user's original filename (that lives only in `name`).

## 5. Upload pipeline (FR-01/02)

```
Browser ── InputFile (≤200 MB [A-02][U-04]) ─→ temp file
  → magic sniff: "solid" ASCII header OR binary header+tri-count check
  → StlParser.Parse → StlMesh {triangles, bbox, volume}
  → SHA-256 → dedupe check → move to library → DB insert
```

Kestrel/Blazor limits to configure at P1: `MultipartBodyLengthLimit`,
`MaxRequestBodySize`, Blazor `SignalR` `MaximumReceiveBufferSize`/
`StreamBufferCapacity` (see [Phases P1](04-implementation-phases.md)).

## 6. 3D preview boundary (P3) [A-04]

- `stl-viewer.js` ES module: imports **local** three.js (r1xx, vendored),
  `STLLoader`, `OrbitControls`. Exposes `createViewer`, `disposeViewer`,
  `savePng`, `recaptureThumbnail` and the batch `generatePreviews`.
- Blazor component `StlViewer.razor` calls it via `IJSRuntime` (.NET 10
  `DotNetObjectReference` pattern for disposal), passing the model URL
  `/files/{id}` (a minimal API endpoint streaming from disk).
- Thumbnails [U-02]: on first preview, or from the **Previews** admin page,
  JS renders one frame in a studio-lit canvas with a ground shadow and the
  category's material colour, POSTs the PNG back; server stores
  `data/thumbs/{sha256}.png`. Grids use `<img>`; no server rasterizer anywhere
  [A-04]. `Save PNG` exports a 1600×1200 preview from the same scene.
- STL facet normals are advisory: many exporters write zero-length normals, so
  the viewer rebuilds normals from triangle positions before shading. Without
  this, valid files render as a black silhouette.
- No STL parsing in the browser except three.js's own STLLoader — the
  server parser (P2) is the authority for stats.

## 7. Deferred auth seam (P7, D5)

- All pages sit behind a placeholder `AuthState` service returning
  "anonymous owner". P7 swaps in ASP.NET Core Identity + per-user ownership
  columns (`owner_id`) without re-architecting. Until P7: bind LAN-only
  ([Deployment §3](06-deployment.md)).

## 8. Configuration

`appsettings.json`: connection string, `Storage:LibraryPath`,
`Storage:ThumbPath`, `Upload:MaxBytes` (200 MB default), `Server:BindUrl`
(default `http://localhost:5100`; LAN switch is a deployment decision
[U-05]). Secrets via `dotnet user-secrets` in dev.
