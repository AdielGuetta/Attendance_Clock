# Plans 01–03: Setup (retrospective)

**Status:** done ✅ · *Written afterwards:* the first three steps were planned and approved in conversation before the
"one plan file per step" habit started (from Plan 04). This file records what was planned, decided and verified.

## 01 · Requirements and repository
- **Plan:** define the product before coding (the brief has no spec on purpose): roles, scope, the trusted-time design,
  edge cases, data model, API, delivery schedule → [docs/PRD.md](../PRD.md).
- **Review decisions:** English UI; build step by step with each step explained and approved; 14 h "forgotten shift"
  threshold; primary + backup time provider (single provider documented as a risk if needed).
- **Result:** git repository, PRD as the first commit.

## 02 · Backend skeleton and local environment
- **Plan:** an ASP.NET Core (.NET 10) Web API with a `/health` endpoint, running on localhost.
- **What happened:** Windows **Smart App Control** on the dev machine blocked every locally built .NET assembly
  (`0x800711C7`), even through the signed `dotnet` host and outside OneDrive. Turning it off would require reinstalling Windows.
- **Options weighed:** turn it off (rejected: irreversible) · WSL/Ubuntu (works, more setup) · **Docker** (already
  installed, no system changes) → **chosen** (PRD D-12, DECISIONS #2).
- **Result:** `docker-compose.yml` with SQL Server 2022 + the API (`dotnet watch`); `/health` → `Healthy`.
- **Correction:** the PRD still said "no Docker" after the switch. Caught by the user and fixed in a separate commit.

## 03 · Database and safety rules
- **Plan:** EF Core 10, `Employees` and `Shifts` tables; the key rules enforced **in the database**, not only in code.
- **Decisions:** `datetimeoffset` (instant + Zurich offset) instead of two columns (D-6, revised during implementation);
  a filtered unique index (one open shift per employee, D-9); a check constraint (Clock Out > Clock In); `rowversion`;
  no cascade delete.
- **Verified:** 5 direct SQL tests in a rolled-back transaction: first open shift accepted, second refused, out-before-in
  refused, normal clock-out accepted, next-day shift accepted.
- **Gotchas recorded in CLAUDE.md:** EF Core 11 needs .NET 11 (pin 10.0.x); sqlcmd needs `-I` for filtered indexes.
