# Plan 10: Manager screens and shift corrections

**Status:** done ✅ · **Branch:** `feature/manager` · **PRD refs:** F5, F6, F7, D-11, EC-4, EC-13, EC-16

## Goal
A manager can see **who's on shift right now**, browse **everyone's shifts**, and **correct a shift**: fix a wrong time,
or close a forgotten shift. Every correction needs a **reason** and leaves a permanent **audit trail**.

## Endpoints (manager only: `[Authorize(Roles = "Manager")]`, so employees get `403`)
| Method | Route | Does |
|---|---|---|
| GET | `/api/admin/on-shift` | Everyone currently on shift: name, since when, and a **ForgotClockOut** flag if open > 14 h |
| GET | `/api/admin/shifts?from=&to=&employeeId=&flaggedOnly=` | All shifts in a Zurich date range (max 93 days), filterable, with employee names |
| PATCH | `/api/admin/shifts/{id}` | Correct Clock In and/or Clock Out. Body: new time(s), **reason**, and the shift's version stamp |
| GET | `/api/admin/shifts/{id}/audit` | The shift's change history: who, when, what changed, old → new, why |

## Correction rules
| Rule | Why |
|---|---|
| **Reason is required** (at least 5 characters) | Every non-API time must be explained (PRD F6). |
| Corrected times get source **`ManualCorrection`** and the shift is flagged **`Corrected`** | The "all times come from the API" promise stays honest: every exception is visible and attributable. |
| **Audit row per changed field**: who, when (from the **trusted clock**), old value, new value, reason | A permanent trail. Rows are only ever added, never edited (D-11). |
| Clock Out must be after Clock In; **not in the future** (checked against the trusted clock) | No impossible or invented shifts (EC-13). |
| **No overlap** with the employee's other shifts | One person can't work two shifts at once (EC-13). |
| **Version-stamp check** (`rowversion`): if the shift changed since the manager opened it → `409 SHIFT_MODIFIED` | E.g. the employee clocked out meanwhile; the manager must reload instead of overwriting (EC-2 for edits). |
| Manager enters **Zurich local time** (e.g. `2026-11-20T17:00`); the server applies Zurich's rules | Managers think in wall-clock time. On the DST "fall back" night, 02:30 happens twice → `400 AMBIGUOUS_TIME` unless an offset is given; 02:30 on "spring forward" night doesn't exist → `400 INVALID_LOCAL_TIME`. |
| A manager may correct **their own** shift, but it's audited and flagged | No four-eyes rule for now; recorded as a known limitation (EC-16). |
| `TooLong` is recalculated after a correction | Flags stay truthful. |

## Data model change
New table **`ShiftAudits`** (`Id, ShiftId, ChangedByEmployeeId, ChangedAt, Field, OldValue, NewValue, Reason`) via a new
EF migration. It references shifts and employees, with no cascade delete.

## UI (React): a "Team" tab, visible only to managers
- **On shift now**: a live list with "open for 3 h 12 min", with forgotten shifts highlighted.
- **Shifts**: week view (◀ ▶), filter by employee and "flagged only", each row with an **Edit** button.
- **Edit dialog**: Clock In / Clock Out (Zurich time), a reason (required), Save; below, the shift's **audit history**.
  Server errors (overlap, future time, changed meanwhile) are shown in plain words.

## Tests (integration: real app + test DB + fake clock)
| # | Scenario | Expected |
|---|---|---|
| 1 | Employee calls any `/api/admin` endpoint | 403 |
| 2 | On-shift list | Shows open shifts with names; a shift open > 14 h is flagged `ForgotClockOut` |
| 3 | Shifts list filters (employee, flagged only, range) | Correct subsets |
| 4 | Correct a Clock Out | Saved, source `ManualCorrection`, flag `Corrected`, 1 audit row with old/new/reason/manager |
| 5 | Close a forgotten open shift | Shift closed, audited; the employee can clock in again |
| 6 | Missing or short reason | 400 |
| 7 | Clock Out ≤ Clock In | 400 |
| 8 | Time in the future | 400 |
| 9 | Overlaps another shift of the same employee | 409 |
| 10 | Stale version stamp | 409 `SHIFT_MODIFIED` |
| 11 | Ambiguous local time (DST fall-back 02:30) | 400 `AMBIGUOUS_TIME`; with an explicit offset, accepted |
| 12 | Audit endpoint | Returns the history in order |

Plus a browser walkthrough: manager logs in → Team tab → closes Dana's forgotten shift with a reason → history shows it →
Dana's My week shows the shift marked "Corrected by manager".

## Out of scope (possible step 11 if time allows)
Creating and deactivating employees from the UI (deactivation while on shift, EC-12), CSV export, a four-eyes approval rule.

## Results (2026-11-20)
- Built: `backend/AttendanceClock.Api/Admin/` (AdminService, AdminController, DTOs), `Domain/ShiftAudit.cs` + migration
  `AddShiftAudits`, `ZurichTime.ParseManagerTime` (DST-aware), plus `GET /api/admin/employees` for the filter list.
  UI: `frontend/src/screens/TeamScreen.tsx` and `EditShiftDialog.tsx` (with buttons for "which 02:30?" on DST nights).
- **15 manager test cases pass, 58/58 backend overall**; 10/10 frontend; type-check clean.
- **Browser walkthrough (real timeapi.io):** manager logs in → Team tab → Dana is "on shift since Thu 08:00 · 31 h 56 min"
  with **"Forgot to clock out?"** → Edit → Clock Out 17:00 + reason → saved: "08:00 → 17:00✎, 9:00, Corrected", nobody on
  shift → history: "Clock Out (open) → 17:00 CET, by Maya Manager, Fri 20 Nov 15:57:31 CET" → a future time is refused
  ("Times can't be in the future.") → Dana logs in: no Team tab; My week shows the shift with **"Corrected by manager"**.
- **Incident found while testing:** after the step 9 merge the screens were unreachable. Cause: Claude merged by first
  switching to `main`, which briefly deleted `frontend/`; Vite restarted without its config (port 5173, container-only).
  Fixed with `docker compose restart web`, and CLAUDE.md now says to fast-forward `main` without switching first.
- Also fixed in passing: PRD EC-15 still mentioned the dropped idempotency key.
- **Follow-up (suggested by the user): fail fast.** The web container now starts Vite with `--config vite.config.ts`
  plus explicit host/port flags, `restart: unless-stopped`, and a health check. Re-created the incident
  on purpose (config file removed for 3 s): Vite logged "failed to load config … server restart failed", kept serving
  with its last good settings, restarted correctly when the file returned; the app answered 200 throughout, health "healthy".
