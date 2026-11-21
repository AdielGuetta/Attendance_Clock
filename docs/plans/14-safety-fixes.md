# Plan 14 (phase 2): Safety fixes from the edge-case review

**Status:** done ✅ (approved 2026-11-20; fix 7 built, then cancelled by the user) · **Branch:** `feature/safety-fixes`

The user listed edge cases; each was checked against the actual code. Decisions:

| # | Case | Decision |
|---|---|---|
| 1 | An old tab clocks the employee out of a **newer** shift | **Fixed** |
| 2 | Punch saved, connection dropped | Already handled (status re-read, a repeat answers 409 with the current state) |
| 3 | Two managers edit different shifts at once → overlap | **Fixed** |
| 4 | Provider returns an old time that still passes validation | Not fixing (user's decision) |
| 5 | Fall-back night: one offset applied to two times | Not fixing (user's decision) |
| 6 | A manager corrects / approves their own shift | Keep: the user wants managers to fix their own shifts (audited, EC-16) |
| 7 | A deactivated employee's login badge still works (≤ 60 min) | Built, then **cancelled** by the user: as before, only Clock In / Out check "still active" |

## The fixes
1. **Clock Out names the shift.** The screen sends `{ "shiftId": 42 }`: the shift it shows. If a different shift is
   open now, the server refuses with 409 `SHIFT_CHANGED` (and the current open shift), and the screen refreshes.
   Checked before the time API is called. Without a body it behaves as before.
2. **One change per employee at a time.** Clock In, manager corrections and request approvals take a lock on that
   employee's row (inside a transaction) right before their "does this overlap / is it after the last shift?" check,
   and keep it until saved. A second change for the same employee waits, then sees the first one's result. Different
   employees never wait for each other.

## Results (2026-11-20)
- **Fix 1:** `ClockOutRequest(long? ShiftId)`; `ShiftService.ClockOutAsync(…, expectedShiftId)` → `PunchOutcome.ShiftChanged`
  → 409 `SHIFT_CHANGED` with the open shift; the Clock screen sends the shown shift's id and explains the refusal.
- **Fix 3:** `Data/EmployeeLock.cs` (`LockEmployeeAsync`: `SELECT 1 FROM Employees WITH (UPDLOCK, ROWLOCK)` inside a
  transaction). Used in `ShiftService.ClockInAsync` (after the time call, before the "after the last Clock Out?" check)
  and `AdminService.CorrectAsync` (before the overlap check; reuses the approval's transaction when there is one).
- **Tests:** backend **88/88** (+3 in `Safety/SafetyFixTests.cs`): old tab refused + newer shift untouched · naming the
  open shift works · 15 rounds of two managers' simultaneous corrections → always exactly one wins, the other gets
  `OVERLAPS_ANOTHER_SHIFT`. **The race test was run once with the lock switched off: it failed (both corrections
  succeeded: "Expected 1, Actual 2")**, so it really detects the problem. Frontend 31/31, type-check clean.
- **Browser:** tab 1 (dana) clocks in 19:52; tab 2 clocks out and in again (newer shift); Clock Out in the old tab 1 →
  "The shift it showed is already closed. You're on a newer shift since 19:52; nothing was changed." The newer shift
  stayed open; pressing Clock Out again on the refreshed screen closed it (19:53:06).
- **Fix 7, cancelled:** it checked "still active" on every request (JWT `OnTokenValidated`, 401 `ACCOUNT_INACTIVE`
  everywhere, 2 tests). The user cancelled it, so it was removed again: the punch-only check is back, unchanged.
