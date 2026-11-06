# PrintShelf — 3D Print File Manager & Catalogue

Documentation set for a **.NET 10 Blazor Server** web site that manages and
catalogues **STL 3D print files**, backed by **PostgreSQL**, styled with
**Tailwind CSS**, deployed on a **local Windows machine**.

> **Status: MVP implemented and verified (P0 → P6), updated 2026-11-06.**
> 44 automated tests (15 STL + 29 web/integration) and 13 Playwright E2E
> tests pass; the published build runs on `http://localhost:5100`.
> Evidence, measurements and known gaps: [Build Record](08-build-record.md).
> User accounts (P7) remain deferred; the app is localhost-only until then.

## Manuals

| # | Manual | Covers |
|---|--------|--------|
| 1 | [Requirements](01-requirements.md) | Scope, MVP vs deferred, functional & non-functional requirements |
| 2 | [Architecture](02-architecture.md) | Solution layout, PostgreSQL schema, file storage, upload pipeline, JS-interop boundary |
| 3 | [Research & Reuse](03-research-and-reuse.md) | Verified NuGet/GitHub candidates, what to reuse vs. build, rejected options |
| 4 | [Implementation Phases](04-implementation-phases.md) | Phase-by-phase build guidance with per-phase acceptance checks |
| 5 | [Testing](05-testing.md) | xUnit + golden STL fixtures, bUnit, Playwright E2E, manual visual checks |
| 6 | [Deployment](06-deployment.md) | PostgreSQL on Windows, Kestrel, LAN access, backups |
| 7 | [Risks & Acceptance](07-risks-and-acceptance.md) | Risk register + global acceptance checklist |
| 8 | [Build Record](08-build-record.md) | What was built, test/perf/backup/deploy evidence, known gaps |

Machine-readable phase plan: [phase-plan.json](../phase-plan.json)
(each phase carries `acceptance_checks` and a `user_validation_gate`).

## Decision log (confirmed by the user, 2026-11-05)

| # | Topic | Decision |
|---|-------|----------|
| D1 | Hosting model | Blazor Server, **.NET 10** (SDK 10.0.401 installed on this machine) |
| D2 | Database | **PostgreSQL** via EF Core 10 + Npgsql provider 10.0 |
| D3 | File formats | **STL only** (binary + ASCII) in MVP |
| D4 | Deployment | Local Windows machine, Kestrel / `dotnet run` |
| D5 | Authentication | None in MVP; **full user accounts deferred** to a later phase (P7) |
| D6 | UI styling | **Tailwind CSS** (v4, standalone CLI recommended — no Node.js needed) |

## Assumption register

Every assumption is labeled **[A-nn]** where it is used. These were **not**
explicitly confirmed by the user — challenge any of them at its phase gate.

| ID | Assumption | Where |
|----|-----------|-------|
| A-01 | Model files stored on local disk under `data/library/{sha256}.stl`; DB holds metadata only | [Architecture §4](02-architecture.md) |
| A-02 | Max upload size ~200 MB (configurable) | [Requirements §3](01-requirements.md) |
| A-03 | Single application instance; LAN-only access | [Deployment](06-deployment.md) |
| A-04 | Thumbnails rendered client-side via three.js (no server-side rasterizer) | [Architecture §6](02-architecture.md) |
| A-05 | Geometry stats (dimensions, volume, triangle count) computed server-side at upload | [Phases P2](04-implementation-phases.md) |
| A-06 | English-only UI | [Requirements §3](01-requirements.md) |

## Resolved decisions (2026-11-05)

| ID | Question | Outcome |
|----|----------|---------|
| U-01 | STL parsing: MeshIO NuGet vs. small internal parser | **Internal `PrintShelf.Stl`** — strict binary + ASCII parser, 15 golden tests |
| U-02 | Client-side thumbnail capture vs. on-demand preview only | **Capture on first preview + batch Previews page**, PNG in `data/thumbs/` |
| U-03 | Tailwind via standalone CLI vs. npm toolchain | **Standalone CLI** (v4.3.4, no Node needed to style) |
| U-04 | Final max upload size | **200 MB**, enforced by Kestrel + form + SignalR circuit |
| U-05 | LAN access for other people, or localhost only | **Localhost only** (secure default; no auth until P7) |

## Quick start (this machine)

```bash
# 1. PostgreSQL (dev container on host port 5434; see Deployment §1)
docker start printshelf-postgres

# 2. Build + CSS
dotnet build PrintShelf.slnx
./tools/tailwindcss.exe -i tailwind/input.css -o src/PrintShelf.Web/wwwroot/css/app.css --minify

# 3. Run (migrations apply on startup)
dotnet run --project src/PrintShelf.Web --launch-profile http
# → http://localhost:5100/health

# 4. Optional demo data
dotnet run --project src/PrintShelf.Web -- seed 1000 --reset

# 5. Verify
dotnet test PrintShelf.slnx
cd src/tests/e2e && npx playwright test
```

The connection string lives in `src/PrintShelf.Web/appsettings.json`; override
it with `PRINTSHELF_CONN` (or `ConnectionStrings__PrintShelf`) on another
machine. Publishing, the service scripts (`deploy/`) and the backup/restore
procedure are in [Deployment](06-deployment.md).

## Project name

"PrintShelf" is a working title used across these manuals — rename freely at
the P0 gate; it only affects solution/solution-folder names.
