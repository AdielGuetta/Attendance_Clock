# PRD — Attendance Clock (שעון נוכחות)

| | |
|---|---|
| **Status** | v1.0, implemented. Changes made during implementation are marked *"Revised…"* in place and summarised in [DECISIONS.md](../DECISIONS.md) |
| **Date** | 2026-11-19 (draft) · 2026-11-20 (final) |
| **Deadline** | Sunday 2026-11-22, 08:00 |
| **Stack** | ASP.NET Core (.NET 10) · Microsoft SQL Server · React (Vite + TypeScript) |
| **Hard constraint** | Every Clock In / Clock Out timestamp comes from an **external time API**, in **Europe/Zurich**. Never the browser clock, never the server's local clock. |

> Decision and edge-case IDs (`D-n`, `EC-n`) are referenced from `DECISIONS.md` later, so the reasoning stays traceable from requirement → code → write-up.

---

## 1. Problem & goal

Employees need to record when they start and end a shift. The business needs records it can **trust**: they can't be faked by changing a laptop clock, don't depend on a misconfigured server, and are consistent across daylight-saving changes.

**Goal:** a small, robust attendance system where the punch itself is airtight, plus the minimum around it (auth, history, manager review) that makes it usable.

**Guiding principle from the brief:** do a little, deeply and with reasons, rather than a lot of shallow features.

### Success criteria
1. An employee can clock in and out, and always sees a correct current state ("on shift since 08:02" / "off shift").
2. No record can exist whose time came from anything except the external API, apart from **explicit, audited** manager corrections that are labelled as such.
3. Concurrency, double-clicks, API outages and DST transitions never produce a corrupt state (two open shifts, out < in, a silently wrong time).
4. A reviewer can clone the repo, run one command for the DB, and have it working locally in under 10 minutes.

---

## 2. Users & roles

| Role | Can do |
|---|---|
| **Employee** | Log in · clock in / out · see current status · see own shift history and weekly/monthly totals |
| **Manager (Admin)** | Everything an employee can · see all employees' status ("who's on shift now") · view and filter all shifts · correct or close a shift (reason required, audited) · create and deactivate employees |

Self-registration: **no**. Employees are created by a manager (or seeded). *(D-1: an attendance system is an internal tool; open sign-up makes no sense and adds attack surface.)*

---

## 3. Scope

### In scope (MVP — must ship)
- **F1** Authentication (username + password, JWT), two roles.
- **F2** Clock In / Clock Out using trusted external time.
- **F3** Current status for the logged-in employee.
- **F4** Employee's own history: list of shifts, durations, totals per day/week.
- **F5** Manager dashboard: who's on shift now, all shifts with filters (employee, date range, flagged).
- **F6** Manager correction of a shift (edit times / close a forgotten shift) with mandatory reason and audit trail.
- **F7** Flagging of anomalous shifts (too long, forgotten clock-out, manually corrected).
- **F8** Seed data + local setup (SQL Server in Docker, EF Core migrations).

### Nice to have (only if MVP is solid)
- **N1** CSV export of shifts for a date range (payroll hand-off).
- **N2** Employee "undo" of an accidental punch within 2 minutes.
- **N3** Employee can request a correction (manager approves).
- ~~**N4** Hebrew / RTL UI.~~ Decided: the UI is in English.

### Out of scope (stated in DECISIONS.md as "chose not to do")
Breaks within a shift · payroll / overtime rules · geofencing / IP restriction · multiple sites or time zones · SSO / external identity providers · offline punching · mobile app · notifications.

---

## 4. The core: trusted time (F2)

This is the heart of the assignment, so it gets the most design.

### 4.1 Architecture
```
React ──POST /api/punches/clock-in──► API ──► ITrustedClock ──► Provider A (primary)
                                               │                 └─► Provider B (fallback)
                                               ▼
                                         TrustedTimestamp { UtcInstant, ZurichLocal, Offset, Source, LatencyMs }
                                               ▼
                                         ShiftService (rules) ──► SQL Server
```

- **D-2: Time is fetched on the server, never the client.** The client sends *intent* ("clock me in"), never a timestamp. Anything the browser sends is ignored for recording. This removes the whole category of "user changed their PC clock".
- **D-3: `ITrustedClock` abstraction.** One interface, provider implementations behind it. Business logic depends only on the interface. Tests inject a fake clock, so DST, midnight and outages can be tested deterministically.
- **D-4: Providers** (decided after the T0 spike; measurements in [plans/04-time-providers.md](plans/04-time-providers.md)). Two independent providers, because free time APIs do go down and one bad provider shouldn't stop the business.
  - **Primary: timeapi.io** `GET /api/timezone/zone?timeZone=Europe/Zurich`. Returns Zurich local time **plus the current UTC offset**. 10/10 successes, ~0.27 s typical, occasionally ~1.3 s.
  - **Backup: time.now** `GET /developer/api/timezone/Europe/Zurich`. Returns local time with offset, UTC time and unix time. **Known risk:** ~20–30% connection failures in the spike. Acceptable for a backup that only runs when the primary fails; documented in DECISIONS.md.
  - Rejected: worldtimeapi.org (down for the whole test), timeapi.world (dead), aisenseapi.com (UTC only), keyed providers (reviewers would have to sign up).
- **D-5: Fail closed.** If all providers fail (timeout ~2.5 s each, total budget ~5 s), the punch is **rejected** with a clear message ("Time service unavailable, punch not recorded, try again"). We **never** fall back to the server clock: that would break the hard constraint at exactly the moment nobody's watching.
  - *Considered and rejected:* "anchor + monotonic offset" (sync once from the API, then add elapsed `Stopwatch` time). It's resilient, but arguably derives time from the local machine, and the constraint is explicit. Documented as an alternative in DECISIONS.md.
  - *Considered and rejected:* queueing the punch and timestamping it later. The recorded time would be the time of processing, not of the event. That's worse than rejecting.

### 4.2 What we store per punch
For each of clock-in and clock-out:
- **The time as a `datetimeoffset`**, e.g. `2026-11-19 08:02:00 +01:00`. One value holds both the exact instant and the Zurich UTC offset at that moment (+01:00 winter / +02:00 summer). SQL Server compares and sorts `datetimeoffset` by the underlying UTC instant, so ordering, the "out > in" check and durations are DST-safe.
- **Source** (`varchar(32)`): the provider's name, or `ManualCorrection`.
- Provider latency is written to the **logs**, not the database (not needed for any business query).

**D-6: Store the instant with its Zurich offset (`datetimeoffset`), display Zurich.** *(Revised during implementation: the first draft used two columns, `datetime2` UTC + offset minutes. `datetimeoffset` stores the same information in one column, so it can't get out of sync.)* Durations are computed on the instant, so DST can't produce negative or off-by-an-hour shifts. Display is always Europe/Zurich, labelled ("08:02 CET" / "CEST"), whatever the viewer's browser zone.

### 4.3 Response validation (don't trust the external API blindly)
- The response must parse, and must report the Europe/Zurich zone with an offset of +60 or +120.
- **Cross-check:** when a provider returns both a UTC/unix value and a local value, they must agree with the stated offset.
- **Monotonicity per employee:** a clock-out must be strictly after its clock-in. If not (e.g. primary and fallback disagree by a few seconds), the punch is rejected and logged rather than saved as a negative shift. *(EC-9)*

### 4.4 What the server clock *is* allowed for
JWT expiry, log timestamps, HTTP caching: infrastructure, not attendance records. Stated explicitly so it's clear the constraint was read precisely and not over-applied. *(D-7)*

### 4.5 The live clock on screen
The UI shows a ticking Zurich clock for convenience. It's seeded from `GET /api/time/now` (external time via the server), then ticks locally. It's **cosmetic**. After every punch the UI shows the **recorded** time returned by the server, so what the employee sees as confirmation is always the real stored value.

---

## 5. Functional requirements

### F1 Authentication
- Login with username + password. Passwords are hashed with ASP.NET Core Identity's `PasswordHasher` (PBKDF2 + salt); the full Identity table set isn't used.
- JWT access token (short-lived, ~60 min); role claim `Employee` | `Manager`.
- Deactivated users can't log in; their existing records are kept.
- One generic error for unknown user / wrong password / deactivated (`INVALID_CREDENTIALS`), with equal timing; 5 login attempts per minute per IP → `429 TOO_MANY_ATTEMPTS`.
- ✅ Implemented in [Plan 06](plans/06-login.md); demo accounts are seeded in Development.
- *(D-8: JWT over cookies: simpler for a separate React dev server, and the API stays stateless. Trade-off: no server-side revocation; acceptable with a short lifetime.)*

### F2 Clock In / Clock Out
- `POST /api/punches/clock-in`, `POST /api/punches/clock-out`. No body.
  *Revised in Plan 12 (proj2): Clock In now takes `{ "location": "Office" | "Remote" }` (required, else 400
  `INVALID_LOCATION`). Still no time in the body: any time a client adds is ignored (D-2). Clock Out has no body.*
  *(Revised in Plan 07: the optional `Idempotency-Key` header was dropped. The DB unique index already guarantees correctness,
  and a repeated punch gets a `409` that includes the current state, so the UI can refresh. ✅ Implemented in [Plan 07](plans/07-clock-in-out.md).)*
- Clock in: allowed only if the employee has **no open shift**. Creates a shift with `ClockIn*` set.
- Clock out: allowed only if there **is** an open shift. Sets `ClockOut*` and computes `DurationMinutes`.
- The response returns the full shift, including the recorded Zurich time and source.
- **D-9: One open shift per employee is enforced in the database**, by a filtered unique index `UNIQUE (EmployeeId) WHERE ClockOutUtc IS NULL`, not only in code. Two concurrent requests can't both succeed; the loser gets `409 Conflict`. *(EC-1, EC-2)*

### F3 Current status
- `GET /api/punches/status` → `{ state: "OnShift" | "OffShift", openShift? }`.
  *(Revised in Plan 07: status deliberately doesn't call the time API, because the UI polls it and EC-17 applies. The today/week totals move to F4 history.)*
- The main screen is one big context-aware button: **Clock In** or **Clock Out**, plus "on shift for 3h 12m".

### F4 My history
- `GET /api/shifts/mine?from=&to=`: shifts in a date range (default: current week), with daily totals and a weekly total.
- A shift belongs to the **Zurich calendar date of its clock-in** (D-10; see EC-5).
- Flagged shifts are visibly marked, with the reason.
- ✅ Implemented in [Plan 08](plans/08-history.md): day boundaries use Zurich's official rules (DST days are 23/25 h);
  each shift rounds to the nearest minute and totals are the sum of the rows (always add up, no systematic loss);
  the open shift is returned separately and not counted; max range 93 days.

### F5 Manager dashboard
- "On shift now": employees with an open shift and how long it's been open.
- All shifts: filter by employee, date range, flagged only. *(No pagination: a week at a time, max 93 days per request.)*
- ✅ Implemented in [Plan 10](plans/10-manager.md) (`/api/admin/on-shift`, `/api/admin/shifts`, manager-only Team tab).

### F6 Manager corrections
- A manager can edit clock-in / clock-out times of a shift or close an open shift.
- **Reason is mandatory.** Every change writes a `ShiftAudit` row: who, when, field, old value, new value, reason.
- Corrected punches get `TimeSource = ManualCorrection`. They're a business override, explicitly labelled, so the "all times come from the API" guarantee stays honest: every non-API time is visible and attributable.
- The correction's *own* timestamp (when the edit happened) does come from the trusted clock.
- Validation: out > in; no overlap with the employee's other shifts; not in the future.
- ✅ Implemented in [Plan 10](plans/10-manager.md), plus: the version stamp (`rowversion`) refuses edits to a shift that
  changed meanwhile (`409 SHIFT_MODIFIED`), and managers type Zurich wall time. On DST nights an ambiguous time (fall back)
  needs an explicit offset, and a non-existent one (spring forward) is refused.

### F7 Flags
A shift is flagged automatically when:
- it's still open more than **14 h** after clock-in → `ForgotClockOut` (computed on read against trusted "now", or by a periodic check);
- it closes with a duration of more than **14 h** → `TooLong`;
- it's been manually corrected → `Corrected`.

Flags never block the employee; they surface in the manager's view.

---

## 6. Edge cases & complex scenarios

| ID | Scenario | Handling |
|---|---|---|
| **EC-1** | Double-click on Clock In | UI disables the button while in flight; DB unique index is the final guard → 409 `ALREADY_ON_SHIFT` with the open shift. ✅ Tested with 10 simultaneous requests. |
| **EC-2** | Two tabs / devices punch at the same time | DB filtered unique index + optimistic concurrency (`rowversion`) on the shift. One wins, the other gets 409 and the UI refetches status. |
| **EC-3** | Clock Out with no open shift (stale tab) | 409 with a "you're not on shift" message; UI refetches status. |
| **EC-4** | Forgot to clock out yesterday, tries to Clock In today | Clock in is blocked (there's still an open shift). UI explains: "You have an open shift since Mon 08:02. Clock out now (it will be flagged for your manager) or ask your manager to correct it." |
| **EC-5** | Overnight shift (22:00 → 06:00) | Fully supported; duration from UTC. Attributed to the clock-in date. |
| **EC-6** | DST start (last Sun of March, 02:00 → 03:00) during a night shift | Wall clock shows 8 h, real duration 7 h. UTC math gives 7 h, which is correct. The UI shows both times with the CET/CEST label so it isn't confusing. Covered by a unit test. |
| **EC-7** | DST end (last Sun of October, 03:00 → 02:00): the hour 02:00–03:00 happens twice | The local time alone is ambiguous; the stored UTC + offset is not. Covered by a unit test. |
| **EC-8** | External time API down / slow / returns garbage | Timeout → fallback provider → fail closed with a clear message. Never the local clock. Health endpoint reports provider status. |
| **EC-9** | Primary and fallback providers disagree by a few seconds, making out ≤ in | Reject and log. Never save a zero or negative shift. |
| **EC-10** | Employee's browser is in a different time zone, or their PC clock is wrong | Irrelevant to recording (server-side time). Display is always Zurich, labelled. |
| **EC-11** | Very short accidental shift (seconds) | Recorded honestly. (N2 "undo within 2 min" if time allows.) |
| **EC-12** | Employee deactivated while on shift | Deactivation is blocked until the open shift is closed, or the manager closes it as part of deactivating (audited). |
| **EC-13** | Manager correction creates an overlap or a future time | Rejected by validation. ✅ Tested (Plan 10). |
| **EC-14** | Expired JWT mid-session | 401 → UI redirects to login, keeps nothing stale. |
| **EC-15** | Punch request arrives, API call succeeds, DB write fails | Transaction; nothing is saved; the user gets an error and retries. The retry is safe: a repeated punch gets a `409` with the current state (the idempotency key was dropped in Plan 07). |
| **EC-16** | A manager edits their own shift | Allowed but audited (and flagged `Corrected`). Noted as a known limitation: no four-eyes rule. |
| **EC-17** | **Shift-start rush:** many employees punch in the same minute (e.g. 200 at 08:00) | The database copes easily (different rows, no blocking). The weak point is the **free time API**: one call per punch may hit its rate limits → slow answers or blocks → the flaky backup takes over → in the worst case, refused punches. **Known limitation for now.** Mitigation options: (a) share one fetched time among punches arriving within the same ~1 s (requests wait for one in-flight call instead of each calling); (b) a paid provider with an SLA; (c) a self-hosted NTP-synced time service. Not "cache for minutes", because each punch needs a fresh time. |

---

## 7. Data model (initial)

```
Employees                         ✅ implemented (migration InitialCreate)
  Id, UserName (unique), DisplayName, PasswordHash, Role ("Employee"|"Manager"),
  IsActive, CreatedAtUtc (DB default SYSUTCDATETIME())

Shifts                            ✅ implemented (migration InitialCreate)
  Id                  bigint PK
  EmployeeId          FK → Employees (ON DELETE RESTRICT)
  ClockIn             datetimeoffset NOT NULL
  ClockInSource       varchar(32)    NOT NULL
  ClockOut            datetimeoffset NULL
  ClockOutSource      varchar(32)    NULL
  Flags               int (bitmask)  NOT NULL
  RowVersion          rowversion
  CHECK CK_Shifts_ClockOutAfterClockIn (ClockOut IS NULL OR ClockOut > ClockIn)
  UNIQUE INDEX UX_Shifts_OneOpenShiftPerEmployee ON (EmployeeId) WHERE ClockOut IS NULL
  INDEX IX_Shifts_EmployeeId_ClockIn ON (EmployeeId, ClockIn)

ShiftAudits
  Id, ShiftId, ChangedByEmployeeId, ChangedAtUtc (trusted clock),
  Field, OldValue, NewValue, Reason

IdempotencyKeys: dropped in Plan 07 (see F2)
```

**D-11: a Shift row (in/out pair), not a raw "punch events" log.** Pairing is enforced by structure and constraints, and history queries are trivial. The trade-off (losing an append-only event log) is covered by `ShiftAudits` for every mutation after creation.

---

## 8. API (initial)

| Method | Route | Role | Purpose |
|---|---|---|---|
| POST | `/api/auth/login` | — | Get JWT |
| GET | `/api/time/now` | any | Current trusted Zurich time (for display) |
| GET | `/api/punches/status` | Employee | Current state + totals |
| POST | `/api/punches/clock-in` | Employee | Clock in |
| POST | `/api/punches/clock-out` | Employee | Clock out |
| GET | `/api/shifts/mine` | Employee | Own history |
| GET | `/api/admin/shifts` | Manager | All shifts, filters |
| GET | `/api/admin/on-shift` | Manager | Who's on shift now |
| PATCH | `/api/admin/shifts/{id}` | Manager | Correct a shift (reason required) |
| GET | `/api/admin/shifts/{id}/audit` | Manager | Audit trail |
| POST/PATCH | `/api/admin/employees` | Manager | Create / deactivate |
| GET | `/health` | — | App + DB + time-provider health |

Errors use **ProblemDetails** (RFC 9457) with stable error codes (`ALREADY_ON_SHIFT`, `NOT_ON_SHIFT`, `TIME_SOURCE_UNAVAILABLE`, …) so the UI can show precise messages.

---

## 9. Non-functional requirements

- **Correctness over availability** for punches (fail closed, D-5).
- **Testing:**
  - unit tests for `ShiftService` with a fake `ITrustedClock` (DST, overnight, out ≤ in, flags);
  - provider tests with mocked HTTP (timeouts, garbage, fallback);
  - integration tests against a real SQL Server (the Docker container) for the unique-index / concurrency guarantees.
- **Security:** hashed passwords (Identity), role-based authorization on every endpoint, employees can only read their own data, no secrets in the repo (user-secrets / env vars; dev defaults documented).
- **Observability:** structured logs for every punch attempt (employee, provider used, latency, outcome); provider failures logged as warnings.
- **Local setup (Docker, D-12):** `docker compose up -d` starts SQL Server 2022 and the API (`dotnet watch`, auto-applies migrations + seed in Development) and the React app (Vite, http://localhost:5174), all three in containers. The README also documents running without Docker (`dotnet run` against any SQL Server, via a configurable connection string).
- **UX:** responsive (punching from a phone is realistic); one-tap primary action; clear error messages.

---

## 10. Delivery plan (≈ 2.5 days)

| When | Milestone |
|---|---|
| Thu evening | **T0** time-provider spike (pick primary/fallback) · repo skeleton · CLAUDE.md · docker-compose (SQL Server + API) ✅ · EF model + migrations |
| Fri | **F2 + F3** trusted clock, ShiftService, punch endpoints, tests (DST, concurrency) · **F1** auth |
| Sat | React: login, punch screen, my history · manager dashboard + corrections (F5–F7) |
| Sat night | README, DECISIONS.md, cleanup, end-to-end run from a clean clone |
| Sun 08:00 | Submit (repo link + access) |

Commit small and often. Plans live in `docs/plans/`; the `.claude/` folder and CLAUDE.md are part of the deliverable, along with notes on where Claude Code helped and where it had to be corrected.

---

## 11. Decisions from review (2026-11-19)

1. **UI language:** English. ✅
2. **Way of working:** build from scratch, step by step, each step explained and approved before it's done. Manager features (F5–F7) stay in scope; we'll trim later if time runs short. ✅
3. **Forgotten-shift threshold:** 14 h. ✅
4. **Fallback time provider:** yes, we'll have a primary and a backup provider. If the T0 spike finds no reliable free backup, we go single-provider and document it as a known risk in DECISIONS.md. ✅
5. **Local environment: Docker** (D-12, revised the same day). ✅
   - *Original plan:* no Docker; run the API on Windows against the SQL Server Express already installed.
   - *What happened:* Windows **Smart App Control** on the dev machine blocks every locally built, unsigned .NET assembly (`0x800711C7`), even through the signed `dotnet` host. Turning it off would require reinstalling Windows.
   - *Options weighed:* turn off Smart App Control (rejected: irreversible); WSL/Ubuntu (works, but needs extra installs plus opening the Windows SQL Server to the network: TCP, a SQL login, a firewall rule); Docker (already installed, no Windows changes).
   - *Decision:* **Docker Compose** runs both SQL Server 2022 and the API. Extra benefit: reviewers start everything with one command. The Windows SQL Server Express isn't used.
