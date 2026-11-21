# Plan 13 (phase 2): Employee correction requests + manager approve / reject

**Status:** done ✅ (approved 2026-11-20 with two changes: closed shifts only; reject without a note) · **Branch:** `feature/correction-requests` · **PRD refs:** N3, F6, rule 2, EC-13

## Why
Today, if a recorded time is wrong (e.g. dana clocked out late because she forgot), she has to ask her manager by phone
or chat, and the manager types the fix in. Now dana asks **inside the app**, on a **finished** shift: "I actually left at 17:00". The manager sees the
request, compares it with the recorded times, and approves or rejects it. Like Team Clock's "Pending approval".

## The key idea: approval *is* a manager correction
An employee's typed time is **never written to a shift directly**. It's only a *suggestion*. When the manager approves,
the server runs the **existing correction code** (Plan 10) with those times. So everything we already trust applies
unchanged: Zurich DST checks, no future times, no overlaps, the version stamp, the `ManualCorrection` label, the
`Corrected` flag and an **audit row** naming the approving manager. The PRD's rule 2 holds: a time that didn't come
from the time API exists only as an explicit, audited manager correction.

## What changes, layer by layer
| # | Layer | Change |
|---|---|---|
| 1 | **Database** | New table `CorrectionRequests`: shift, employee, requested Clock In and/or Clock Out, reason, status (`Pending` / `Approved` / `Rejected`), created at, decided by, decided at, rowversion. Rules in the DB: **at most one pending request per shift** (filtered unique index, like "one open shift"), at least one time requested (check constraint), status only the 3 values. Migration `AddCorrectionRequests`. |
| 2 | **Employee API** | `POST /api/shifts/{id}/correction-requests` `{ clockIn?, clockOut?, reason }` · `GET /api/correction-requests/mine`. |
| 3 | **Manager API** | `GET /api/admin/correction-requests?status=Pending` · `POST …/{id}/approve` · `POST …/{id}/reject` (no body). |
| 4 | **Validation (on request)** | Only **your own** shift (else 404) · **only a finished shift** (still open → 409 `SHIFT_STILL_OPEN`) · reason ≥ 5 characters · times read with Zurich rules (same parser as the manager's) · Clock Out after Clock In · not in the future (trusted clock) · a real change · no other pending request for that shift (409 `REQUEST_ALREADY_PENDING`). |
| 5 | **Approve** | Runs the Plan 10 correction with the requested times; the audit reason reads *"Request #7 from Dana Cohen: forgot to clock out, left at 17:00"*. If the correction is refused (e.g. it would now overlap a newer shift), the request **stays pending** and the manager sees why. The shift change and the "Approved" status are saved **together** (one transaction): never one without the other. |
| 6 | **Reject** | One tap, no explanation needed. Marks the request Rejected; the shift isn't touched. |
| 7 | **Screens** | **Employee:** a "Request a fix" button on each **finished** shift in My week → small form (new Clock In / Clock Out, reason); a "Pending approval" / "Approved" / "Rejected" label on the shift. **Manager:** a new **Requests** tab with a count badge: each request shows the recorded vs requested times, the reason, and Approve / Reject. |
| 8 | **Tests** | Backend + frontend (below). |

## Decisions (please confirm)
- **Approval reuses the manager correction.** One set of rules, one audit trail, no second way to change a shift.
- **Only finished shifts** (user's decision). A shift still open is refused; a forgotten open shift is still closed by
  the manager directly (Plan 10/11).
- **Reject needs no explanation** (user's decision).
- **One pending request per shift.** Avoids two contradicting requests; enforced by the database.
- **Requests are never deleted.** Approved and rejected ones stay as history (like the audit rows).
- **Not in this step:** the employee cancelling their own request, requests for a *missing* day (a shift that was
  never clocked: Team Clock's "Self-reported"), changing the work location by request, e-mail notifications.

## Build in two parts (each with proof, each its own commit)
- **13a: Backend:** table + migration, endpoints, validation, approve/reject, tests. *The migration pauses the app
  at http://localhost:5175 for about a minute (stop api → run → start api).*
- **13b: Screens:** employee form + statuses, manager Requests tab + badge, frontend tests, browser walkthrough.

## Proof
- **Backend tests**, new: request on own shift → 201 Pending · someone else's shift → 404 · open shift → 409 ·
  no reason / future time / Clock Out before Clock In / no change → 400 · second pending request → 409 · employee
  can't approve (403) · approve → shift corrected, labelled, flagged, audit row names the manager, request Approved ·
  approve that would overlap → refused, request still Pending, shift unchanged · reject → shift unchanged ·
  approve or reject twice → 409.
- **Frontend tests:** request-status label helper and the form's "what changed" logic.
- **Browser:** dana requests a Clock Out fix → sees "Pending approval" → manager sees the badge, approves → dana sees
  the corrected time and "Approved"; the Edit dialog's history shows the audit row. Second request → manager rejects →
  dana sees "Rejected". The open shift has no "Request a fix" button.

## Results
### 13a: Backend (2026-11-20) ✅
- **Database:** table `CorrectionRequests` (migration `20261120175133_AddCorrectionRequests`) with 3 check constraints
  (asks for a change · status values · decided ⇔ decider + time) and the filtered unique index
  `UX_CorrectionRequests_OnePendingPerShift`. Verified in the live DB.
- **Code:** `Domain/CorrectionRequest.cs`, `Data/Configurations/CorrectionRequestConfiguration.cs`, `Requests/`
  (`CorrectionRequestService`, DTOs, two controllers: employee + manager). To reuse rather than copy, the Plan 10 code
  was split: `AdminService.ParseTimes` (Zurich time reading) and `AdminService.OverlapsAnotherShiftAsync`; the
  error → HTTP mapping moved to `Admin/CorrectionProblems.cs` so both features answer with the same codes.
- **Approve** runs `AdminService.CorrectAsync` inside a transaction together with the status change; the audit reason
  reads "Request #N from Name: reason". **Reject** is one call with no body.
- **Tests: 85/85** (was 67; +18 in `Requests/CorrectionRequestTests.cs`, all passing on the first run): pending + shift
  untouched · stranger's shift 404 · open shift 409 · 6 bad inputs · reason too long · second pending 409 · 5 simultaneous
  requests → exactly 1 · employee 403 · approve = corrected + labelled + flagged + audit row · approve that now overlaps →
  refused, still pending, no audit · reject → shift untouched, new request allowed · decide twice 409 · list filter.

### 13b: Screens (2026-11-20) ✅
- **Code:** `src/requests.ts` (status labels, newest request per shift, "can ask for a fix?", send only changed times)
  + `requests.test.ts`; `api.ts` (types + 5 calls); `screens/RequestFixDialog.tsx`; My week (`WeekScreen.tsx`):
  "Request a fix" on finished shifts without a waiting request, status badge (blue pending / green approved / red
  rejected); manager `screens/RequestsScreen.tsx` (recorded vs requested with the changed time highlighted and the new
  length, Approve / Reject, "Recently decided"); `App.tsx`: **Requests** tab with a count badge, refreshed with the
  forgotten-shift badge (every 2 min, on focus, after a decision).
- **Tests:** frontend **23/23** (was 20; +3), type-check clean; backend still 85/85.
- **Browser (http://localhost:5175):** dana → My week → "Request a fix" on Tue 17 (16:30 → 17:00, "Stayed for a
  delivery") → "Fix pending approval", button gone, time unchanged · second request on Wed 18 (in 22:00 → 22:30) ·
  manager sees **"Requests ②"**, recorded vs requested side by side → Approve #1 → badge ① → Reject #2 → no badge,
  both under "Recently decided" · Team tab: Tue 17 = 08:00 → 17:00 ✎, 9:00, "Corrected"; Wed 18 unchanged · Edit dialog
  history: *"Clock Out 16:30 → 17:00 by Maya Manager · Request #1 from Dana Cohen: Stayed for a delivery, left at 17:00"*
  · dana sees "Corrected by manager" + "Fix approved" on Tue 17, "Fix rejected" (and can ask again) on Wed 18, week total
  17:26 → 17:56 · phone width: wraps, no sideways scroll.
- **Not shown live:** the "no button on an open shift" case (dana had no open shift at the time); covered by the
  `canRequestFix` unit test and backend test 3.

### 13c: Length and day-change warnings (2026-11-20, user's idea) ✅
**Why:** the date pickers let an employee move a time to another day. That's right for a night shift, but a typo
(16 Nov instead of 17 Nov) could quietly create a 33-hour shift, and the manager card showed only "08:00".
**What (warn, never block: the manager decides, like F7's "record honestly and flag"):**
- `time.ts`: `zurichWallToInstant` (typed Zurich time → exact instant, DST-aware), `formatDelta`, `TOO_LONG_MINUTES`.
  `requests.ts`: `summarizeChange` / `summarizeRequest` (new length, change vs recorded, moved day, over 14 h).
- **Employee form:** live "New shift: Tue 17 Nov 08:00 → Wed 18 Nov 08:00 · Length 24:00 (was 9:00, +15:00)", a moved
  day highlighted with a note, an amber "Over 14 hours" box, and "Clock Out must be after Clock In" (Send disabled).
- **Manager card:** full dates on both rows when a day moved (a "+1" would hide it), the changed date highlighted, new
  length with the change, "Day changed" and red "Over 14 h" badges, red card border.
- **Tests:** frontend **27/27** (+4: length + moved day, strictly-more-than-14 h, DST night, stored request).
- **Browser:** the user's own request #3 (Fri 20 Nov Clock In moved to *Thu 19 Nov*, "forgot") now shows
  "Thu 19 Nov 17:36" highlighted, 24:56 (+24:00), Day changed, Over 14 h. Test request #4 (Tue 17 Clock Out → Wed 18
  08:00, 24:00) the same. A test that moved a Clock In over dana's other Friday shifts was refused by the server
  ("would overlap another shift"), as designed. Both requests left pending for the user.

**Not done (as agreed):** cancelling your own request, requests for a missing day, changing the location by request,
notifications. docs/TESTS.md, DECISIONS.md and README were brought up to date later (21 Nov).

### 13d: 24-hour time entry with an hour/minute roller (2026-11-20, user's request)
**Why:** the browser's built-in date-and-time box follows the computer's region settings, so it showed "05:00 PM" while
the rest of the app shows "17:00". A website can't force it to 24-hour.
**What:** `screens/DateTimeField.tsx` replaces it in both dialogs (Request a fix, Correct shift): the date keeps the
browser's calendar picker; the time is a 24-hour box, typed ("17:00", also "1700" / "7:05") or picked from the 🕒
roller (hours 00–23, minutes 00–59; opens upward when there's no room below). Wrong times ("25:00", "5pm") show "Use
24-hour time, like 17:00" and keep Save / Send off. Clock In and Clock Out are now stacked (side by side didn't fit).
Same "2026-11-18T17:00" value as before, so the server is unchanged. Helpers in `src/timeInput.ts`.
**Proof:** frontend **31/31** (+4 in `timeInput.test.ts`), type-check clean. Browser (dana, Request a fix): times show
19:11; roller opens on 19 : 11, picking an hour then a minute fills the box and closes it; "25:00" → error + Send off;
"2030" → "20:30" on leaving the box, length updated. Dialog cancelled, nothing sent.
**Not checked live:** the manager's Correct shift dialog (same component; the browser was logged in as dana).
The date part still follows the computer's format (11/20/2026); the summaries below it read "Fri 20 Nov".
