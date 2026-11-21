# Attendance Clock (proj2)

> **This is proj2, the practice copy.** It started as an exact copy of the submitted project and adds features on top:
> **work location**, **employee correction requests**, **manager reports with CSV export**, and a few safety fixes.
> It runs next to the original with its own containers (`playground-*`), its own database and its own ports
> (**app 5175 · API 5081 · DB 1434**). The submitted version is a separate repo,
> [AdielGuetta/AttendanceClock](https://github.com/AdielGuetta/AttendanceClock). See [PLAYGROUND.md](PLAYGROUND.md).

An attendance system where employees **clock in and out** of shifts, and managers review and correct them.

**The core rule:** every Clock In / Clock Out time comes from an **external time API, in Europe/Zurich time**, never from
the browser's or the server's clock. If the time service is unavailable, the punch is refused rather than recorded with a
time we can't trust.

**Quick start:** `docker compose up -d`, then open **http://localhost:5175** and log in as `dana` / `Employee123!`
(employee) or `manager` / `Manager123!` (manager). Details below.

**Highlights**
- Punch times come only from **timeapi.io** (backup: **time.now**), validated against Zurich's DST rules. If both fail,
  the punch is refused, never guessed.
- The **database itself** guarantees one open shift per employee: 10 simultaneous clicks → exactly 1 shift (tested).
- DST-safe everywhere: a night shift when clocks jump forward counts 7 h, not 8.
- Manager corrections need a **reason** and leave an **append-only audit trail**; forgotten shifts are alerted, never auto-closed.
- **proj2:** employees pick **Office / Remote** before Clock In; they can **ask for a fix** on a finished shift, which a
  manager approves (as an audited correction) or rejects; managers get **Reports** (tiles, per employee, per day) and a
  **CSV** file. Changes for one employee are handled **one at a time** (a lock), and an old browser tab can't close a newer shift.
- **95 backend + 35 frontend automated tests** (the submitted version had 58 + 13); one command starts everything.

---

## Tech stack
| Part | Technology |
|---|---|
| Backend | ASP.NET Core (.NET 10), C# |
| Database | Microsoft SQL Server 2022, Entity Framework Core 10 |
| Frontend | React 19 (Vite + TypeScript) |
| Time source | [timeapi.io](https://timeapi.io) (primary), [time.now](https://time.now) (backup) |
| Local environment | Docker Compose |

proj2 added **no new libraries or services**: every feature is built with the same stack.

## Run it locally

**You need:** Docker, running. This works the same on **Windows, macOS and Linux**:

| OS | Install |
|---|---|
| Windows 10/11 | [Docker Desktop](https://www.docker.com/products/docker-desktop/) (uses WSL 2 under the hood) |
| macOS (Intel or Apple Silicon) | [Docker Desktop](https://www.docker.com/products/docker-desktop/). On Apple Silicon, SQL Server runs under emulation; see [notes](#macos-and-linux-notes) |
| Linux | [Docker Engine](https://docs.docker.com/engine/install/) + the [Compose plugin](https://docs.docker.com/compose/install/linux/) (or Docker Desktop) |

```bash
git clone https://github.com/AdielGuetta/Attendance_Clock.git
cd Attendance_Clock
docker compose up -d
```

The first start takes a few minutes (it downloads SQL Server, the .NET SDK and Node). Then open:

| URL | What you'll see |
|---|---|
| **http://localhost:5175** | **The app**: log in, Clock In / Clock Out (Office / Remote), My week (+ "Request a fix"); managers also get **Team**, **Requests** and **Reports** |
| http://localhost:5081/health | `Healthy`: the server is up and can reach the database |
| http://localhost:5081/api/time/now | The current Zurich time from the external time API |

### Demo accounts (created automatically in Development)
| Username | Password | Role |
|---|---|---|
| `manager` | `Manager123!` | Manager |
| `dana` | `Employee123!` | Employee |
| `yossi` | `Employee123!` | Employee |

**Using the API directly** (optional; the app does all of this for you):

Log in: `POST http://localhost:5081/api/auth/login` with `{"userName":"dana","password":"Employee123!"}`. The answer
contains a token; send it as `Authorization: Bearer <token>`, e.g. to `GET /api/auth/me`.

Punch (with the token): `POST /api/punches/clock-in` with `{"location":"Office"}` or `{"location":"Remote"}` (required;
anything else → 400 `INVALID_LOCATION`), `POST /api/punches/clock-out` (optional `{"shiftId":42}`: the shift the screen
shows; if a different one is open → 409 `SHIFT_CHANGED`), `GET /api/punches/status`. **No time is ever sent**: the server
takes it from the external time API, and a time added to the body is ignored.

History (with the token): `GET /api/shifts/mine?from=2026-11-16&to=2026-11-22` (Zurich dates; no dates = this week),
with daily and range totals.

Correction requests (with the token): `POST /api/shifts/{id}/correction-requests` with `clockIn` and/or `clockOut`
(Zurich time, e.g. `2026-11-18T17:00`) and a `reason`; only for your own **finished** shifts. `GET /api/correction-requests/mine`.

Manager only (log in as `manager`): `GET /api/admin/on-shift`, `GET /api/admin/shifts?from=&to=&employeeId=&flaggedOnly=`,
`PATCH /api/admin/shifts/{id}` (body: `clockIn`/`clockOut` as Zurich time, `reason`, `rowVersion`), `GET /api/admin/shifts/{id}/audit`,
`GET /api/admin/correction-requests?status=Pending`, `POST /api/admin/correction-requests/{id}/approve` and `/reject`,
`GET /api/admin/reports?from=&to=&employeeId=` and `GET /api/admin/reports/export?from=&to=&employeeId=` (CSV file).

**Database setup is automatic:** SQL Server runs in a container, and the server creates and upgrades the tables on
startup (EF Core migrations, including proj2's `AddWorkLocation` and `AddCorrectionRequests`). There are no manual steps.

Stop with `docker compose down` (data is kept), or `docker compose down -v` to also delete the database.

### Run the tests
```bash
docker compose run --rm tests                                         # backend: 95 tests
docker exec playground-web sh -c "npx tsc -b && npx vitest run"       # frontend: 35 tests (+ type-check)
```

### Without Docker *(alternative, any OS)*
With the [.NET 10 SDK](https://dotnet.microsoft.com/download) and any reachable SQL Server: set the connection string in
`backend/AttendanceClock.Api/appsettings.Development.json`, then run `dotnet run --project backend/AttendanceClock.Api`.
*(On the original dev machine this wasn't possible: Windows Smart App Control blocks locally built programs. That's why
Docker is the main path. See [DECISIONS.md](DECISIONS.md), #9.)*

## How the Docker setup works

`docker-compose.yml` describes three **containers**. A container is an isolated mini-computer (Linux inside) that holds
one program plus everything it needs. They're started from **images**, ready-made templates downloaded once.

```
Your computer (Windows / macOS / Linux)
│
│  browser → http://localhost:5175                      localhost:1434 (optional: SSMS, Azure Data Studio…)
│                │                                            │
│  ┌─ Docker ────┼────────────────────────────────────────────┼─────────────────────┐
│  │  playground-web            playground-api                ▼                     │
│  │  image: Node 24   "api:5080"  image: .NET 10 SDK  "db:1433"  playground-db     │
│  │  runs: Vite  ─── /api/* ──►  runs: dotnet watch  ────────►  image: SQL Server   │
│  │  code: ./frontend (live)     code: ./backend (live)          data: volume db-data│
│  └─────────────────────────────────────────────────────────────────────────────────┘
```

**What `docker compose up -d` does:**
1. Downloads the three images, the first time only.
2. Creates a private network, so the containers can reach each other **by name**: the API connects to `db`, the web app to `api`.
3. Starts **SQL Server** and waits until its health check passes (it really answers a query).
4. Starts the **API** with `dotnet watch`, which builds the code, applies database migrations, adds the demo accounts,
   and listens on port 5080 inside Docker (**5081** on your computer).
5. Starts the **web app** (Vite dev server) on port 5174 inside Docker (**5175** on your computer). The browser talks only
   to it; it forwards `/api/*` calls to the API.

**Good to know**
- **Edit code → it reloads by itself.** `backend/` and `frontend/` are shared live into their containers.
- **Self-healing:** the API and web containers restart automatically if they stop (`restart: unless-stopped`), and the web
  container has a health check, so Docker Desktop shows "unhealthy" if the app stops answering.
- **Data survives restarts.** It lives in the `db-data` *volume*, a storage area managed by Docker. Only
  `docker compose down -v` deletes it.
- **Ports** connect your computer to the containers: `5175` → the app, `5081` → the API, `1434` → SQL Server. They differ
  from the original project's (5174 / 5080 / 1433), so both can run at the same time.
- **Build output** (`bin/`, `obj/`) stays inside Docker volumes, so it never mixes with your own OS's files.
- Useful commands: `docker compose ps` (status) · `docker logs playground-api --tail 50` (API log) ·
  `docker compose restart api`. Docker Desktop shows the same in its **Containers** tab.

### macOS and Linux notes
- **Same commands** on every OS. Use the terminal instead of PowerShell.
- **Apple Silicon (M1–M4):** Microsoft publishes SQL Server images only for Intel/AMD, so `docker-compose.yml` asks for
  `platform: linux/amd64` and Docker Desktop emulates it. In Docker Desktop → Settings → General, keep
  **"Use Rosetta for x86_64/amd64 emulation on Apple Silicon"** on. The first start is slower; after that it works normally.
- **Linux:** if `docker` needs `sudo`, either use `sudo docker compose up -d` or
  [add your user to the `docker` group](https://docs.docker.com/engine/install/linux-postinstall/).
- **Port already in use:** change the left-hand number of the port in `docker-compose.yml` (e.g. `"1434:1433"` →
  `"1435:1433"`). The API isn't affected, since it talks to the database inside Docker.
- **Line endings** are normalised by `.gitattributes`, so a copy works the same on every OS.

## Project status
| Step | Status |
|---|---|
| Requirements (PRD) | ✅ |
| Server + database in Docker | ✅ |
| Database tables and safety rules | ✅ |
| Choosing the time providers (measured) | ✅ |
| Trusted clock: external time, backup, validation, 10 tests | ✅ |
| Login and roles: JWT, hashed passwords, brute-force limit, 9 tests | ✅ |
| Clock In / Clock Out / status: race-safe, DST-safe, 12 tests | ✅ |
| Shift history and totals: DST-safe days, nearest-minute totals that add up, 10 tests | ✅ |
| React UI (employee): login, Clock In/Out, My week, phone-friendly | ✅ |
| Manager: who's on shift, all shifts, corrections with reason + audit trail, 15 tests | ✅ |
| Forgotten-shift alerts (> 14 h): employee banner + manager badge | ✅ |
| Final polish: fresh-clone test (clean DB, all tests green), docs review | ✅ (submitted version) |
| **proj2** · Recent shifts on the Clock screen (warm-up, reuses history) | ✅ |
| **proj2** · Work location: Office / Remote per shift, new column + rule, shown everywhere, 9 tests ([plan 12](docs/plans/12-work-location.md)) | ✅ |
| **proj2** · Correction requests: ask on a finished shift, manager approves (audited) / rejects, 18 tests ([plan 13](docs/plans/13-correction-requests.md)) | ✅ |
| **proj2** · Length / moved-day warnings, 24-hour time box with an hour/minute roller (screens only) | ✅ |
| **proj2** · Safety fixes: old tab can't close a newer shift, one change per employee at a time, 3 tests ([plan 14](docs/plans/14-safety-fixes.md)) | ✅ |
| **proj2** · Manager reports: tiles, per employee, per day, CSV export, 7 + 4 tests ([plan 15](docs/plans/15-manager-reports.md)) | ✅ |
| **proj2** · Employees tab with search, greeting, design refresh | ⏭️ skipped (low value) |

## Documentation
- [PLAYGROUND.md](PLAYGROUND.md): what proj2 is and how it differs from the original
- [docs/PRD.md](docs/PRD.md): requirements, edge cases and design (proj2 changes are marked "Plan 12–15 (proj2)")
- [DECISIONS.md](DECISIONS.md): key decisions, edge cases, what was left out, known limitations
- [docs/TECH-STACK.md](docs/TECH-STACK.md): pros and cons of each component, and how each affects the user experience.
  **Visual version:** [docs/TECH-STACK.pdf](docs/TECH-STACK.pdf) (diagrams: architecture, Clock In flow, waiting time, concurrency, Docker)
- [docs/TESTS.md](docs/TESTS.md): every edge case (מקרי קצה), how it's handled, and which automated test proves it
- [docs/plans/](docs/plans/): the plan for each step, with its results (12–15 are proj2)
- [docs/WORKING-WITH-CLAUDE.md](docs/WORKING-WITH-CLAUDE.md): the process, where Claude Code helped, and where it had to be corrected
- [CLAUDE.md](CLAUDE.md) and [.claude/](.claude/): the instructions and project commands Claude Code worked with
