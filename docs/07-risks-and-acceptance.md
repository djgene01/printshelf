# 7. Risks & Acceptance

← [Index](README.md) · Prev: [Deployment](06-deployment.md) · Next: [Build Record](08-build-record.md)

> **Build status (2026-11-05):** the global checklist below is mapped to
> evidence in [Build Record §7](08-build-record.md#7-global-acceptance-checklist-status).
> Items 1–9 and 12 pass; item 10 (reboot survival) and item 11 (external-PC refusal)
> are partially evidenced — see the build record's known limitations.

## 1. Risk register

| ID | Risk | Likelihood | Impact | Mitigation |
|----|------|-----------|--------|------------|
| R-01 | Huge/corrupt STLs crash or stall ingest (truncated files, lying headers) | High | Med | Strict parser + golden corrupt fixtures ([Testing §1](05-testing.md)); tri-count vs byte-size check; upload cap [A-02] |
| R-02 | Blazor upload limits silently reject big files (SignalR/Kestrel defaults) | High | Med | Configure all three limits at P1 with the exact checklist in [Phases P1](04-implementation-phases.md); test with ~50 MB file |
| R-03 | three.js perf on dense meshes (>500k tris) or WebGL context leaks from un-disposed viewers | Med | Med | Dispose on navigate-away; E2E memory check ([Testing §5](05-testing.md)); optional tri-count warning badge |
| R-04 | **No auth before P7** and someone exposes the app to the internet | Med | **High** | Default localhost-only bind; LAN firewall rule scoped to subnet; router port-forward explicitly forbidden; revisit at P7 |
| R-05 | DB↔disk drift (file deleted manually, row left; or crash mid-upload) | Med | Med | File-first/row-second order ([Architecture §4](02-architecture.md)); integrity scan page (P2); backups ([Deployment §5](06-deployment.md)) |
| R-06 | Unit ambiguity (mm vs inch STLs) makes dimensions/volume wrong | High | Low | `print_unit` per model + UI hint ([Architecture §3](02-architecture.md)) |
| R-07 | Volume estimate wrong for non-watertight meshes (common with downloaded STLs) | High | Low | Label as *estimated*; flag negative/near-zero volumes |
| R-08 | Tailwind v4 standalone CLI workflow surprises (no `init`, no config file) | Low | Low | Exact commands in [Research §5](03-research-and-reuse.md) |
| R-09 | Version drift: Npgsql/EF/three.js move during the build | Med | Low | Pin versions at P0; re-check at each gate; docs cite versions not "latest" |
| R-10 | Scope creep (3MF/OBJ, slicer integration) | Med | Med | Deferred list in [Requirements §2](01-requirements.md); new formats go through a gate |
| R-11 | Thumbnail capture depends on browser (no thumbs for models never previewed) | Low | Low | Batch **Previews** page renders all missing thumbnails from STL; placeholder remains until then |

## 2. Global acceptance checklist (the MVP is "done" when all pass)

Evidence style: paste command output / screenshot into the gate record.

1. `dotnet build` and `dotnet test` clean on .NET 10 (10.0.x SDK).
2. Fresh machine drill: create DB → apply migrations → app starts at
   `http://localhost:5100` → `/health` OK.
3. Upload a real binary STL (≥ 50 MB) and an ASCII STL; both show correct
   tri-count, bbox, volume (spot-checked against a slicer), thumbnail,
   preview.
4. Corrupt + oversize + duplicate uploads each produce the designed
   behaviour (reject message / reject / dedupe prompt).
5. Search + tag/category filter + sort all work from the grid; grid uses
   thumbnails.
6. Print-run add/edit/delete; aggregates correct on a fixture.
7. CSV export opens cleanly (UTF-8 BOM) with all models.
8. Preview of a 100k-triangle model is interactive [NFR-04]; no console
   errors; memory flat after 10 previews.
9. Backup + restore drill completed once ([Deployment §5](06-deployment.md)).
10. App survives logoff/reboot via the chosen service mechanism.
11. With U-05 = localhost: an external machine **cannot** reach the site
    (proof: connection refused from another PC).
12. On a machine with a supported slicer installed, the model page lists it
    and **Launch** passes the model file to the chosen OrcaSlicer,
    PrusaSlicer, or Cura executable.

**Status after P6 build:** 1–9 and 12 ✅, 10 ⚠️ (scripts ready, task not registered),
11 ⚠️ (listener proven localhost-only; no second PC available). Full mapping:
[Build Record §7](08-build-record.md#7-global-acceptance-checklist-status).

## 3. Gate protocol (how validation works each phase)

1. Builder runs the phase's `acceptance_checks` from
   [phase-plan.json](../phase-plan.json) and shows evidence.
2. Builder asks the phase's `user_validation_gate` question (choices are in
   the JSON).
3. **Approved** → next phase starts. **Changes requested** → fix, re-run
   checks, ask again. **Blocked** → record the blocker in the phase's
   `notes` and stop; do not start dependent phases.
