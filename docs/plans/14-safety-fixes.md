# Plan 14 (proj2): Safety fixes from the edge-case review

**Status:** done ✅ (approved 2026-11-20) · **Branch:** `feature/safety-fixes`

The user listed edge cases; each was checked in the code (answers in ADI-NOTE Q42–Q48). Decisions:

| # | Case | Decision |
|---|---|---|
| 1 | An old tab clocks the employee out of a **newer** shift | **Fix** |
| 2 | Punch saved, connection dropped | Already handled (status re-read, a repeat answers 409 with the current state) |
| 3 | Two managers edit different shifts at once → overlap | **Fix** |
| 4 | Provider returns an old time that still passes validation | Not fixing (user's decision) |
| 5 | Fall-back night: one offset applied to two times | Not fixing (user's decision) |
| 6 | A manager corrects / approves their own shift | Keep: the user wants managers to fix their own shifts (audited, EC-16) |
| 7 | A deactivated employee's login badge still works (≤ 60 min) | **Fix** |

## The fixes
1. **Clock Out names the shift.** The screen sends `{ "shiftId": 42 }`: the shift it shows. If a different shift is
   open now, the server refuses with 409 `SHIFT_CHANGED` (and the current open shift), and the screen refreshes.
   Checked before the time API is called. Without a body it behaves as before.
2. **One change per employee at a time.** Clock In, manager corrections and request approvals take a lock on that
   employee's row (inside a transaction) right before their "does this overlap / is it after the last shift?" check,
   and keep it until saved. A second change for the same employee waits, then sees the first one's result. Different
   employees never wait for each other.
3. **"Still active?" on every request.** When the login badge is checked, the server also checks the account is still
   active (one small lookup by id). If not: 401 `ACCOUNT_INACTIVE` on **every** endpoint (history, requests, manager
   actions), and the screen goes back to login. Replaces the check that only Clock In / Out had.

## Proof
Backend tests for each fix (stale tab refused, right shift accepted · parallel corrections never both succeed ·
deactivated employee / manager refused everywhere), all tests green, a browser check of the stale-tab case.

## Results (2026-11-20)
- **Fix 7:** `Program.cs` JWT `OnTokenValidated` checks `IsActive` on every request; `OnChallenge` answers 401
  ProblemDetails `ACCOUNT_INACTIVE`. The punch-only check (`ShiftService.IsActiveEmployeeAsync`) was removed. The
  screen's login page now says "Your account is no longer active…" for that code.
- **Fix 1:** `ClockOutRequest(long? ShiftId)`; `ShiftService.ClockOutAsync(…, expectedShiftId)` → `PunchOutcome.ShiftChanged`
  → 409 `SHIFT_CHANGED` with the open shift; the Clock screen sends the shown shift's id and explains the refusal.
- **Fix 3:** `Data/EmployeeLock.cs` (`LockEmployeeAsync`: `SELECT 1 FROM Employees WITH (UPDLOCK, ROWLOCK)` inside a
  transaction). Used in `ShiftService.ClockInAsync` (after the time call, before the "after the last Clock Out?" check)
  and `AdminService.CorrectAsync` (before the overlap check; reuses the approval's transaction when there is one).
- **Tests:** backend **90/90** (+5 in `Safety/SafetyFixTests.cs`): old tab refused + newer shift untouched · naming the
  open shift works · 15 rounds of two managers' simultaneous corrections → always exactly one wins, the other gets
  `OVERLAPS_ANOTHER_SHIFT` · deactivated employee refused on 5 endpoints · deactivated manager can't correct / approve /
  list, shift unchanged. **The race test was run once with the lock switched off: it failed (both corrections
  succeeded: "Expected 1, Actual 2")**, so it really detects the problem. Frontend 31/31, type-check clean.
- **Browser:** tab 1 (dana) clocks in 19:52; tab 2 clocks out and in again (newer shift); Clock Out in the old tab 1 →
  "The shift it showed is already closed. You're on a newer shift since 19:52; nothing was changed." The newer shift
  stayed open; pressing Clock Out again on the refreshed screen closed it (19:53:06).
- **Not checked live:** the deactivated-account message (no way to deactivate from the UI yet; covered by tests).
