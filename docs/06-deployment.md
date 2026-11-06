# 6. Deployment

← [Index](README.md) · Prev: [Testing](05-testing.md) · Next: [Risks & Acceptance](07-risks-and-acceptance.md)

Target: a **local Windows machine** (D4) — the same or a sibling PC to the
dev box. No cloud, no Docker requirement (Docker is a *possible* future
option, not MVP). Single instance [A-03].

## 1. PostgreSQL on Windows

1. Install the official EDB Windows installer (or `winget install
   PostgreSQL.PostgreSQL`). Create DB `printshelf` and a dedicated role
   `printshelf` (password stored in `appsettings.Production.json` protected
   per-machine, or an environment variable `PRINTSHELF_CONN`).
2. The PostgreSQL installer default port is **5432**. Verify it once at P0
   rather than assuming:
   `"C:\Program Files\PostgreSQL\<version>\bin\psql.exe" -U postgres -c "SHOW port;"`
   and record the actual value in the connection string.

   **Verified on this build host:** PostgreSQL runs in Docker as
   `printshelf-postgres` and is published on host port **5434**
   (`Host=127.0.0.1;Port=5434;…`). A normal Windows install will use **5432** —
   change the connection string, not the app.

3. Enable Windows service autostart (installer default) and schedule
   `pg_dump` (§5).

## 2. Publish & run the app

```
dotnet publish src/PrintShelf.Web -c Release -o publish -r win-x64 --self-contained false
```

- Framework-dependent is fine (SDK/runtime already installed); use
  `--self-contained true` only if the target PC lacks the .NET 10 runtime.
- Run: `dotnet PrintShelf.Web.dll` from the publish folder (the `data/`
  folder is created next to it; move it via `Storage:*Path` config).
- Kestrel URL: `http://localhost:5100` by default config
  ([Architecture §8](02-architecture.md)).
- Helper: `deploy/run-printshelf.ps1 -Url http://localhost:5100` sets
  `ASPNETCORE_ENVIRONMENT=Production` and starts the published DLL. This was
  executed successfully on port 5102 during the build (see
  [Build Record §6](08-build-record.md#6-deployment-evidence)).
- **Do not run `dotnet run` from the project in Production** — static web
  assets are only materialised correctly from a published output.

## 3. LAN exposure decision (U-05) — security-critical

MVP has **no authentication** (D5). Therefore:

- **Decision: bind `http://localhost:5100` only** — nobody else can reach it.
  Verified on this build: `netstat` shows `127.0.0.1:5100` and `[::1]:5100`
  only, no `0.0.0.0`.
- If U-05 later becomes "family can browse": bind `http://0.0.0.0:5100`
  **and** add a Windows Firewall rule restricted to the LAN subnet, e.g.
  `netsh advfirewall add rule name=PrintShelf dir=in localport=5100
  protocol=tcp remoteip=LocalSubnet`. Never port-forward on the router.
- HTTPS on LAN: out of MVP scope; acceptable only while the listener stays
  inside the LAN (risk R-04). Re-evaluate at P7 when real accounts exist.

## 4. Run as a service (survive logoff/reboot)

Pick one at the P6 gate:

| Option | How | Notes |
|---|---|---|
| Task Scheduler | `deploy/install-task.ps1` (at logon) runs `deploy/run-printshelf.ps1` | simplest, no extra software |
| NSSM / WinSW service wrapper | wraps `dotnet` as a real service | heavier; only if you want service semantics |

`install-task.ps1` registers task **PrintShelf** with restart-on-failure
(3 attempts) and no execution timeout; `run-printshelf.ps1` starts the
published DLL in Production. The scripts are tested individually, but the
task was **not registered** on the build host, so a real logoff/reboot was
not exercised. Verify after installing:
`Start-ScheduledTask -TaskName PrintShelf` then
`curl http://localhost:5100/health`, log off/on and repeat.

## 5. Backups (NFR-07)

Two moving parts — **back up both or you have nothing**:

1. `pg_dump -U printshelf -d printshelf -F c -f printshelf.dump` (nightly
   scheduled task, keep 14 daily copies).
2. `data/` folder (files + thumbs). Content-addressed, so a restore can
   also *rebuild* thumbs and re-scan stats, but the STLs themselves are the
   irreplaceable originals — keep them in the normal PC backup too.

**Script:** `deploy/backup-printshelf.ps1` writes
`printshelf-<timestamp>.dump` plus `printshelf-<timestamp>-data.zip` and keeps
14 pairs. It needs `pg_dump` on `PATH` and `PGPASSWORD` (or `.pgpass`). On
this build host PostgreSQL is in Docker, so the drill used
`docker exec printshelf-postgres pg_dump …` instead.

Restore drill (acceptance-checked at P6): restore dump into a scratch DB,
point a test instance at the copied `data/`, confirm catalogue + previews.
**This drill was performed:** 1,008 rows restored, 1,007 files copied, the
published build served `/catalogue` and `/files/{id}` on port 5101 — see
[Build Record §5](08-build-record.md#5-postgresql-storage-backup-and-restore).

## 6. Updates

`git pull` → `dotnet publish` → run
`dotnet ef database update -s src/PrintShelf.Web` (migrations) → restart task.
Because migrations touch the DB, run the backup command once before updating.
The app also applies pending migrations on startup, so restarting the
published build after a publish is enough in the normal case.

## 7. What this manual does NOT cover

Docker/Azure/reverse-proxy (rejected or deferred choices), HTTPS certs,
multi-instance scaling (single instance [A-03]).
