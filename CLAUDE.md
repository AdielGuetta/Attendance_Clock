# CLAUDE.md

Guidance for Claude Code when working in this repository.

## ⚠️ Read first: phase 2 and this folder
The project was built in two phases. **Phase 1** (plans 01–11) is the core the assignment asks for; it was finished,
verified from a fresh clone, and kept as a snapshot in a separate folder. **Phase 2** (plans 12–15) adds extensions on top,
in **this** folder (`…\שעון\tamir-playground`, which the user also calls "proj2"). This folder is **the project now**.

- **Never touch the phase-1 folder** `…\שעון\tamir` (and its repo, AdielGuetta/AttendanceClock), **including the user's
  private files there**. Don't read from or write to it at all. Everything, notes included, happens here.
- **Ports:** app **5175**, API **5081**, DB **1434** (on this computer; inside Docker the web and API still listen on 5174 /
  5080). Containers: `attendance-clock-db`, `attendance-clock-api`, `attendance-clock-web`.
- **GitHub:** remote `origin` = https://github.com/AdielGuetta/Attendance_Clock (**public**); local `main` = GitHub `main`.
  Push **only with the user's OK**, and never push other branches or tags. Work on a branch per feature (`feature/…`),
  commit with the usual style; merge into `main` only with the user's OK (see "Git workflow" below). The local branch
  `phase1` is the phase-1 history; it's already part of `main`'s history.
- **Never commit the private files** (`ADI-NOTE.md`, `INTERVIEW-NOTES.md`, `ADI-SYSTEM-MAP.*`, `SESSION-HANDOFF.md`): they're
  in `.git/info/exclude`. SESSION-HANDOFF.md was taken out of the history before the first push; the old history is only
  in the local tag `backup/playground-before-rewrite` (never push it).
- **Done in phase 2:** "Recent shifts" box on the Clock screen (`frontend/src/recent.ts`, `screens/RecentShifts.tsx`).
  **Work location** (Plan 12, branch `feature/work-location`): `Shifts.WorkLocation` (NULL for
  old shifts), Clock In body `{ location }` → 400 `INVALID_LOCATION`, `Punches/ClockInRequest.cs`, `frontend/src/location.ts`.
  **Correction requests** (Plan 13, branch `feature/correction-requests`, on top of work location): table
  `CorrectionRequests` (one pending per shift), `backend/…/Requests/`, approve = `AdminService.CorrectAsync` in a
  transaction; finished shifts only, reject without a note. UI: `src/requests.ts`, `RequestFixDialog`, `RequestsScreen`
  (manager tab + badge). Then: length/day-change warnings (13c), 24-hour `DateTimeField` with an hour/minute roller
  (13d). **Safety fixes** (Plan 14): Clock Out sends `{ shiftId }` (409 `SHIFT_CHANGED`), `Data/EmployeeLock.cs`
  (one change per employee at a time). "Still active on every request" was built and cancelled by the user: only
  Clock In / Out check it. **Manager reports** (Plan 15): `backend/…/Reports/` (report + CSV, no DB change),
  `src/reports.ts`, `screens/ReportsScreen.tsx` (manager tab). Feature 4 skipped for now (user agreed: low value).
  Tests: 95 backend, 35 frontend.
- **Feature ideas** (from the user's older app "Team Clock", a public demo at https://hybrid-clock.bountifulmallard2.chatgpt.site/),
  in the agreed order:
  1. **Work location** (Office / Remote, chosen before Clock In, stored per shift, shown everywhere). It goes through every
     layer: UI → request body → validation → new column + migration → DTOs → all screens → tests.
  2. **Employee correction requests** + manager approve/reject (approval goes through the existing audit trail).
  3. Manager reports: summary tiles, by month/date, **CSV export**.
  4. Employees tab (search, on/off-shift filter); greeting + design refresh.
  Skip: profile photos, "online in the last 2 minutes".

### The user's private files (in THIS folder, never in git)
They're kept out of git by `.git/info/exclude` (a local-only ignore list), so they never show in `git status` and could
never be uploaded. Don't move them into `.gitignore` or commit them.
- `ADI-NOTE.md` (root): after answering **any genuine question** from the user, add it under the right topic with a
  1–2 sentence answer. Keep numbers sequential (renumber the following questions and the glossary "More" refs if needed);
  add new key terms to the glossary. Edit it with the Edit tool only (never PowerShell Get-Content/Set-Content).
- `INTERVIEW-NOTES.md` (root): optional entries for notable lessons (What / Why / "Say").
- `ADI-SYSTEM-MAP.html` / `.pdf` / `.svg` (root): the user's system diagram (one uniform style: no "NEW" marks, by the user's choice); the `.svg` is
  page 1, shown at the top of ADI-NOTE. Re-print the PDF with headless Edge when the HTML changes.

### How to work with this user (same as always)
The user is new to programming and is preparing for an interview. One step at a time: explain *what* and *why* in a few
plain sentences (a kitchen or warehouse metaphor when it truly fits), **ask before acting**, show proof after each step
(tests + a browser check on http://localhost:5175), and say what's not done.

## Project
**Attendance Clock**: employees clock in and out of shifts; managers review and correct shifts.
Home assignment (deadline 2026-11-22 08:00). The full spec is in [docs/PRD.md](docs/PRD.md). Read it before
designing anything; decisions (`D-n`) and edge cases (`EC-n`) are referenced by ID in code and docs.

**Stack:** ASP.NET Core (.NET 10, controllers) · EF Core 10 · SQL Server 2022 · React 19 (Vite + TypeScript).

## The one hard rule
Every Clock In / Clock Out time **must come from an external time API, in Europe/Zurich**.
- Never use `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `TimeProvider.System` or browser time for punch records.
- The client never sends a timestamp for a punch. The server fetches it (PRD D-2).
- If every time provider fails, **reject the punch**. Never fall back to the local clock (PRD D-5).
- The server clock *is* fine for non-attendance things: JWT expiry, logs, `CreatedAtUtc` (PRD D-7).

## Running things: Docker only
Windows **Smart App Control** on the dev machine blocks every locally built .NET assembly, and it can't be
turned off. **Never run `dotnet build/run/test/ef` on Windows.** Run them in the Docker container.

```bash
docker compose up -d                 # start SQL Server (attendance-clock-db) + API (attendance-clock-api, dotnet watch) + web (attendance-clock-web, Vite)
docker compose down                  # stop (data kept); add -v to wipe the database
curl http://localhost:5081/health    # "Healthy" = API up and database reachable
docker logs attendance-clock-api --tail 50

# One-off .NET commands: stop the watcher first so two builds don't collide
docker compose stop api
docker compose run --rm --no-deps api sh -c "cd /src && dotnet tool restore && <command>"
docker compose start api
```
- Run backend tests: `docker compose run --rm tests` (a separate container with its own bin/obj volumes, profile `tests`),
  so the running API is **not** stopped. Use the stop/run/start pattern above only for `dotnet ef` and `dotnet add package`.
- Frontend: never run `npm`/`vite` on Windows either (esbuild is unsigned). Use the web container:
  `docker exec attendance-clock-web sh -c "npx tsc -b && npx vitest run"`. Port **5175** on this computer (5174 inside the container; 5173 is often taken).
- Add a migration: `dotnet ef migrations add <Name> --project AttendanceClock.Api --output-dir Data/Migrations`.
  Migrations are applied automatically when the API starts in Development.
- NuGet: pin **10.0.x** versions. Plain `dotnet add package` picks 11.x, which doesn't support net10.0.
- `bin/` and `obj/` live in Docker volumes, separate from Windows. Don't rely on Windows `obj` files.

### Gotchas
- **sqlcmd** needs `-I` (QUOTED_IDENTIFIER ON), or inserts fail because of the filtered unique index. Example:
  `docker exec -i attendance-clock-db sh -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -I -d AttendanceClock'`
- **Git Bash** rewrites `/opt/...` paths passed to `docker exec`. Prefix the command with `MSYS_NO_PATHCONV=1`.
- The repo path contains Hebrew characters (`שעון`). Always quote paths.
- **Windows PowerShell 5.1** reads UTF-8 files as ANSI by default and garbles `·`, `→`, `✅`. Don't edit files through
  `Get-Content`/`Set-Content`; use the Edit/Write tools (or `sed` in Git Bash).
- Integration tests use a separate database, `AttendanceClock_Tests`, deleted and recreated on every run (`ApiFactory`).
- If the API container can't reach nuget.org right after `up`, it's a transient DNS hiccup: `docker restart attendance-clock-api`.

## Code layout
```
backend/
  AttendanceClock.slnx
  dotnet-tools.json                  # local tools (dotnet-ef)
  AttendanceClock.Api/
    Program.cs                       # DI, health checks, dev auto-migrate
    Domain/                          # entities: Employee, Shift (+ enums)
    Data/AppDbContext.cs
    Data/Configurations/             # one IEntityTypeConfiguration per entity (lengths, indexes, constraints)
    Data/Migrations/
    Time/                            # trusted clock: ITrustedClock, FallbackTrustedClock, provider sources, validation
    Controllers/                     # TimeController (GET /api/time/now)
    Auth/                            # login: AuthController, TokenService (JWT), AuthOptions
    Punches/                         # Clock In/Out/status: ShiftService (rules) + PunchesController (thin)
    History/                         # GET /api/shifts/mine: HistoryService (Zurich day boundaries, nearest-minute totals) + ShiftsController
    Admin/                           # manager-only /api/admin/*: AdminService (on-shift, shifts, corrections + audit) + AdminController
    Domain/ShiftAudit.cs             # append-only audit rows for manager corrections
    Common/ApiProblem.cs             # ProblemDetails + stable error "code"
    Data/DevDataSeeder.cs            # demo accounts (Development only)
  AttendanceClock.Tests/             # xUnit. Time/: fake HTTP handler. Auth/, Punches/, History/, Admin/: real app in memory (ApiFactory) + test DB.
                                     # Punches/ swaps ITrustedClock for FakeTrustedClock (tests only!). DB test classes use
                                     # [Collection(DatabaseCollection.Name)] so they never run in parallel.
frontend/                            # React 19 + Vite 8 + TypeScript 5.9 (container attendance-clock-web, http://localhost:5175)
  vite.config.ts                     # dev server on 5174; proxies /api → http://api:5080 (no CORS)
  src/api.ts                         # all server calls + ApiError (ProblemDetails "code"); 401 → back to login
  src/session.ts                     # login badge in localStorage
  src/time.ts (+ time.test.ts)       # Zurich display helpers: read wall time from the server's ISO string, CET/CEST
  src/useZurichClock.ts              # cosmetic live clock: server time + performance.now()
  src/screens/                       # LoginScreen, ClockScreen (big button), WeekScreen, TeamScreen + EditShiftDialog (managers)
docs/PRD.md                          # requirements and decisions
docs/TECH-STACK.md                   # pros/cons + user impact per component (update when a component is added or changed)
docs/TECH-STACK.pdf                  # visual version, generated from docs/visual/tech-stack.html with headless Edge:
                                     #   msedge --headless=new --no-pdf-header-footer --print-to-pdf=<pdf> <file-uri of html>
docs/TESTS.md                        # edge case → handling → test mapping + list of all tests (update when tests change)
DECISIONS.md                         # reviewer-facing summary of decisions, edge cases, limitations (keep brief, update each step)
README.md                            # how to run + project status (update the status table each step)
docs/plans/                          # implementation plans, one per step (01–03 retrospective)
docs/WORKING-WITH-CLAUDE.md          # process + where Claude helped / was corrected (keep in sync with DECISIONS)
.claude/commands/                    # project slash commands: /test, /wrap-up-step (the end-of-step checklist)
docker-compose.yml
```

## Conventions
- **Data rules live in the database too**, not only in C#: one open shift per employee (filtered unique index),
  `ClockOut > ClockIn` (check constraint), `rowversion` for concurrency, no cascade delete of shifts.
- Times are `DateTimeOffset` (`datetimeoffset` in SQL): the instant plus the Zurich offset. Durations are computed on the instant.
- Errors are returned as ProblemDetails with stable error codes (`ALREADY_ON_SHIFT`, `NOT_ON_SHIFT`, `TIME_SOURCE_UNAVAILABLE`, …).
- Comments explain **why**, and reference PRD IDs where relevant.
- When implementation differs from the PRD, update the PRD in the same change and record the reason.

## Git workflow
- One branch per step (`feature/…`, `docs/…`, `setup/…`), merged into `main` with a fast-forward once it works.
- **Merge without leaving the branch:** `git branch -f main HEAD && git switch main` (fast-forward only; check first with
  `git merge-base --is-ancestor main HEAD`). Don't `git switch main` *before* merging: that briefly deletes files that
  only exist on the branch, and the running watchers (Vite, dotnet watch) restart without them. This happened once:
  Vite came back on its default port 5173 with no config, and `docker compose restart web` was needed.
  Since then the web container is fail-fast (`--config` + port flags, `restart: unless-stopped`, health check; DECISIONS #35),
  so a missing config logs an error and keeps the last good server. Don't remove those flags.
- Running `dotnet ef` / `dotnet add package` stops the `api` container for a minute (stop/run/start pattern above), so the
  app is unreachable meanwhile; tell the user. (Tests no longer need this: use the `tests` container.)
- Commit messages: conventional style (`feat:`, `docs:`, `fix:`), a short body explaining why, and the Co-Authored-By trailer.
- Never push to GitHub, or merge, without the user's approval.

## Working with the user
The user is new to programming and will present and explain this project in an interview, so:
- Go **one step at a time**. Before each step, explain what and why in a few plain sentences, then **ask before acting**,
  especially for installs, Windows or system changes, merges and pushes.
- Keep explanations short and jargon-free. Define terms when they first appear. A warehouse or kitchen metaphor helps when it truly fits.
- After each step, show proof that it works (a test, a health check, a query), and say what's not done yet.
- Record notable moments for DECISIONS.md: problems hit, options weighed, where Claude was wrong and had to be corrected.
