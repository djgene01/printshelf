# 3. Research & Reuse

← [Index](README.md) · Prev: [Architecture](02-architecture.md) · Next: [Implementation Phases](04-implementation-phases.md)

Research performed 2026-11-05. Versions checked against the installed SDK
(**.NET 10, SDK 10.0.401**). Reuse rule: prefer a small, MIT-licensed,
maintained package; build in-house only when the problem is genuinely small.

## 1. Verified stack (adopt as-is)

| Package / tool | Version | License | Role |
|---|---|---|---|
| .NET SDK | 10.0.401 (installed) | MIT | runtime/SDK (D1) |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | **10.0** (EF Core 10 provider; release notes confirm .NET 10 support) | ODbL | DB access (D2) |
| `Microsoft.EntityFrameworkCore.Design` | 10.x | MIT | migrations |
| Tailwind CSS | **v4** — standalone CLI `tailwindcss-windows-x64.exe` (official, no Node.js) or npm `@tailwindcss/cli` | MIT | styling (D6, [U-03]) |
| three.js + `STLLoader` + `OrbitControls` | r1xx, **vendored locally** into `wwwroot/vendor/three/` | MIT | browser 3D preview [NFR-08] |
| xUnit + FluentAssertions (optional) | current | MIT | unit tests |
| bUnit | current for net10 | MIT | component tests |
| Playwright (C# or Node) | current | Apache-2 | E2E tests |

## 2. STL parsing — the one real build-vs-reuse decision (U-01)

**Facts about the format.** Binary STL: 80-byte header + uint32 triangle
count + 50 bytes/triangle (normal + 3×3 float32 verts + uint16 attr). ASCII
STL: `solid NAME` … `vertex x y z` line grammar. Both are trivial to read;
the real work is *robustness* (truncated files, bogus tri-count, NaN verts,
`solid` inside binary headers).

| Candidate | State | Verdict |
|---|---|---|
| **Internal `PrintShelf.Stl`** (~200–300 LOC + tests) | to write | **Default (U-01)** — full control of validation/errors, zero deps, trivial format |
| [DomCR/MeshIO](https://github.com/DomCR/MeshIO) | MIT, pure C#, reads STL/OBJ/GLTF/FBX | Solid fallback if scope later grows past STL; adds surface we don't need yet |
| `Wpf3DModelFiles` (NuGet) | parses STL+3MF | **Rejected** — WPF/`MeshGeometry3D` output, wrong stack |
| `Aspose.3D` (NuGet) | huge format support | **Rejected** — commercial/licensed, overkill for STL-only (D3) |
| `PrintSharp3D` / WinRT `Printing3D3MFPackage` | Windows 3D print API | **Rejected** — 3MF-oriented, WinRT, not server-friendly |

**Volume/bbox math** (for [A-05]): bbox is a min/max pass; volume via the
signed tetrahedron sum `V = Σ dot(v0, cross(v1, v2)) / 6` — needs a
consistent winding order; document that the value is *estimated* and can be
negative for inverted normals (take absolute value, flag suspicious meshes).

## 3. 3D preview in the browser

| Candidate | Verdict |
|---|---|
| **three.js `STLLoader` + `OrbitControls`** | **Adopt.** Only mainstream browser-native STL path; MIT; vendored locally [NFR-08] |
| Google `<model-viewer>` web component | **Rejected** — needs glTF/GLB; STL→glTF conversion would add a pipeline step (revisit if formats grow) |
| Babylon.js `STLLoader` | Viable alternative; three.js chosen for the smaller bundle and wider example base |
| [stl-thumb](https://github.com/unlimitedbacon/stl-thumb) (CLI thumbnailer) | **Rejected as runtime dep** — external process on a web server; its *approach* (fixed camera, shaded render) informs our client-side thumb capture [A-04][U-02] |

## 4. Catalogue apps to *study*, not reuse

Meshory, Total STL + 3MF, and self-hosted NAS libraries (e.g. "My Model
Library"-style projects) exist and are popular — evidence the feature set in
[Requirements](01-requirements.md) is right. All are closed-source or
different-stack (Electron/Node); reuse = **feature benchmarking only**.

## 5. Tailwind with Blazor Server (no Node.js) [U-03]

Standalone CLI flow (verified against Tailwind v4 docs — v4 has **no
`init` step** and needs no `tailwind.config.js` for the standard setup):

```
input.css:  @import "tailwindcss" source("../../src/PrintShelf.Web");
build:      tailwindcss-windows-x64.exe -i input.css -o .../wwwroot/css/app.css --minify
dev:        same with --watch (run alongside dotnet watch)
```

Alternative: npm toolchain if you already use Node. Blazor's default
`app.css`/Bootstrap template styles are removed at P0 to avoid clashes.

## 6. Identity (P7, deferred D5)

ASP.NET Core Identity with the EF Core store (same PostgreSQL DB) is the
planned path — no third-party auth needed for a LAN tool. Watch: Blazor
Server auth flows in .NET 10 use the new `Identity` Blazor components
template; re-check at P7 time.

## 7. Things deliberately **not** used

- No ORM other than EF Core (Dapper unnecessary at this scale).
- No CDN at runtime (three.js, Tailwind, fonts all local) [NFR-08].
- No image-processing NuGet (thumbnails captured by the browser and POSTed
  back — [Architecture §6](02-architecture.md)).
- No g-code/slicer libraries — out of scope ([Requirements §5](01-requirements.md)).
