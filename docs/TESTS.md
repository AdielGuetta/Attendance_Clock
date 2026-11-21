# Automated tests and edge cases (מקרי קצה)

The assignment asks us to **identify edge cases and complex scenarios and handle them**. This document maps each edge case
(IDs from [PRD §6](PRD.md)) to **how it's handled** and **which automated test proves it**.

**Current result (phase 2): 95 / 95 backend test cases + 35 / 35 frontend tests passing.** *(After phase 1, the core: 58 + 13.
Phase 2 added 37 backend and 22 frontend tests; they're marked **Phase 2** below.)*

```bash
docker compose run --rm tests                                          # backend (own build folders; the app keeps running)
docker exec attendance-clock-web sh -c "npx tsc -b && npx vitest run"        # frontend (+ type-check)
```

---

## The three kinds of tests

| Kind | Where | How it works | Why |
|---|---|---|---|
| **Trusted clock unit tests** (10) | `Tests/Time/FallbackTrustedClockTests.cs` | A **fake HTTP handler** replaces the internet and returns any answer we want: correct, broken, slow, lying | Simulate outages and DST nights without waiting for them; no real network, so fast and repeatable |
| **Login integration tests** (9) | `Tests/Auth/AuthTests.cs` | The **real app** runs in memory (`ApiFactory`) against the real SQL Server, in a **separate database** (`AttendanceClock_Tests`) reset every run | Tests the real security behaviour end to end; development data is never touched |
| **Punch integration tests** (15 methods, 19 cases) | `Tests/Punches/PunchTests.cs` | Real app + test DB, with the time APIs swapped for a **fake clock** we control ("it's now 22:00", "the service is down") | Lets us "travel" to a DST night or a 15-hour shift; the fake exists **only in tests** |
| **History integration tests** (11 methods, 13 cases) | `Tests/History/HistoryTests.cs` | Real app + test DB + fake clock; shifts on many days inserted directly | Day boundaries, DST days, totals, privacy |
| **Manager integration tests** (14 methods, 16 cases) | `Tests/Admin/AdminTests.cs` | Real app + test DB + fake clock, logged in as a manager | Permissions, corrections, audit trail, version stamps, DST input |
| **Phase 2** · **Correction request tests** (13 methods, 18 cases) | `Tests/Requests/CorrectionRequestTests.cs` | Real app + test DB + fake clock, as employee and manager | Ask, approve (= audited correction), reject, races, permissions |
| **Phase 2** · **Safety fix tests** (3) | `Tests/Safety/SafetyFixTests.cs` | Real app + test DB + fake clock; parallel requests | Old tab, the per-employee lock (15 rounds of simultaneous corrections) |
| **Phase 2** · **Report tests** (7) | `Tests/Reports/ReportTests.cs` | Real app + test DB; each test uses its own month so totals don't mix | Totals, DST-day edges, CSV safety |
| **Frontend unit tests** (35) | `frontend/src/*.test.ts` (Vitest) | Pure functions, no browser needed | Zurich display, 14-h alert, *(phase 2:)* recent shifts, location, request warnings, 24-h time entry, report periods |

The database test classes share the test database, so they never run in parallel (`DatabaseCollection`).

---

## Edge cases: how they're handled and proven

Legend: ✅ automated test · 🔒 enforced by the database · 🟡 handled by design, no dedicated test yet · ⏳ planned (later step) · ⚠️ known limitation

### Time (the core requirement)
| ID | Edge case | Handling | Proof |
|---|---|---|---|
| EC-8 | Time API **down** | Try the backup; if both fail, refuse with `503 TIME_SOURCE_UNAVAILABLE`, nothing saved | ✅ Time #10 · ✅ Punch #7 |
| EC-8 | Time API **slow** | 2.5 s per provider, then the backup | ✅ Time #4 |
| EC-8 | Time API returns **HTTP error / garbage** | Rejected, backup used | ✅ Time #5, #6 |
| — | Provider returns the **wrong time zone** | Rejected, backup used | ✅ Time #7 |
| — | Provider's **offset breaks Zurich's rules** (e.g. summer offset in November) | Rejected, checked against the official IANA rules | ✅ Time #8 |
| EC-9 | Provider **contradicts itself** (local ≠ UTC) | Rejected | ✅ Time #9 |
| EC-9 | Providers disagree, so **Clock Out ≤ Clock In** | Refused (`503 TIME_SOURCE_INCONSISTENT`), shift stays open | ✅ Punch #11 · 🔒 check constraint |
| — | Time string **without an offset** | Rejected (otherwise .NET would silently assume the server's zone) | 🟡 strict parser, no dedicated test |
| EC-6 | **DST start** (clocks jump forward) during a night shift | Durations computed on real instants → 7 h, not 8 | ✅ Punch #6 · ✅ Time #2 (summer offset) |
| EC-7 | **DST end**: 02:30 happens twice | The stored offset tells the two apart | ✅ Time #3 |
| EC-10 | Employee's **browser clock / time zone is wrong** | Irrelevant: the client never sends a time; the server takes it from the API | ✅ Punch #1 (time = trusted clock's) |
| EC-17 | **Shift-start rush** (hundreds of punches per minute) | One external call per punch may hit the free API's limits | ⚠️ documented; mitigation not built |

### Clock In / Clock Out
| ID | Edge case | Handling | Proof |
|---|---|---|---|
| EC-1 | **Double-click** on Clock In | DB unique index → `409 ALREADY_ON_SHIFT` | ✅ Punch #8 (10 simultaneous → exactly 1 shift) · 🔒 |
| EC-2 | **Two tabs / two devices** at the same time | Same mechanism | ✅ Punch #8 · 🔒 |
| EC-3 | **Clock Out with no open shift** (stale tab) | `409 NOT_ON_SHIFT` | ✅ Punch #3 |
| EC-4 | **Forgot to clock out**, clocks in the next day | `409` with "on shift since …" | ✅ Punch #2 |
| EC-5 | **Overnight shift** (22:00 → 06:00) | Duration from instants; counted on the start date | ✅ Punch #5 |
| F7 | **Very long shift** (> 14 h) | Recorded honestly, flagged `TooLong` | ✅ Punch #10 |
| EC-11 | **Very short accidental shift** (seconds) | Recorded honestly (0 min) | 🟡 seen in the live run, no dedicated test |
| EC-15 | Time fetched, then the **DB write fails** | One `SaveChanges` call = one transaction, so nothing is half-saved; the user retries | 🟡 by design |
| — | **Punching without logging in** | `401` | ✅ Punch #9 |

### History and totals
| ID | Edge case | Handling | Proof |
|---|---|---|---|
| D-10 / EC-5 | **Overnight shift** in history | Counted once, on its start date | ✅ History #2 |
| EC-6 | Shift starting **00:30 on a DST day** (a 23-hour day) | Day boundaries computed from Zurich rules → right day | ✅ History #3 |
| — | Shift starting **just before midnight** of the range | Not included (belongs to the previous day) | ✅ History #4 |
| — | **Seconds that don't fill a minute** | Each shift rounds to the nearest minute; total = sum of rows (always adds up, no systematic loss) | ✅ History #1b |
| — | **Open shift** | Shown separately, not counted in totals | ✅ History #6 |
| — | **Someone else's shifts** | Never returned: employee id from the token, not the URL | ✅ History #5 |
| — | **Huge or reversed date range** | `400 INVALID_RANGE` (max 93 days) | ✅ History #8 |

### Login and security
| ID | Edge case | Handling | Proof |
|---|---|---|---|
| — | Wrong password | `401 INVALID_CREDENTIALS` | ✅ Auth #2 |
| — | **Unknown username** (user enumeration) | The *same* message and timing as a wrong password | ✅ Auth #3 |
| — | **Deactivated** employee | The same generic `401` | ✅ Auth #4 |
| EC-14 | **Missing / tampered** token | `401` | ✅ Auth #5, #7 |
| EC-14 | **Expired** token | `401` (standard JWT validation, 30 s clock skew) | 🟡 not tested (would need waiting or clock control) |
| — | **Password guessing** (brute force) | 5 attempts / min / IP → `429 TOO_MANY_ATTEMPTS` | ✅ Auth #8 |
| — | Passwords readable from the DB | Stored only as salted PBKDF2 hashes | ✅ Auth #9 |

### Manager corrections
| ID | Edge case | Handling | Proof |
|---|---|---|---|
| — | **Employee calls a manager endpoint** | `403` (server-side role check) | ✅ Admin #1 |
| EC-4 / F7 | **Forgotten open shift** | Flagged "ForgotClockOut" after 14 h (computed against the trusted clock); manager closes it → employee can clock in again | ✅ Admin #2, #5 |
| F6 | **A correction must be explained and visible** | Reason ≥ 5 chars; source `ManualCorrection`; flag `Corrected`; one audit row per changed field, stamped by the trusted clock | ✅ Admin #4, #6, #12 |
| EC-13 | Correction puts **Clock Out before Clock In** | `400` | ✅ Admin #7 |
| EC-13 | Correction **in the future** | `400 TIME_IN_FUTURE` (trusted "now") | ✅ Admin #8 |
| EC-13 | Correction **overlaps another shift** | `409 OVERLAPS_ANOTHER_SHIFT` | ✅ Admin #9 |
| EC-2 (edit) | **Shift changed while the manager was editing** | Version stamp → `409 SHIFT_MODIFIED` | ✅ Admin #10 · 🔒 rowversion |
| EC-7 | Manager types **02:30 on the fall-back night** (happens twice) | `400 AMBIGUOUS_TIME`; with an explicit offset it's accepted, and the duration is correct | ✅ Admin #11 |
| EC-6 | Manager types **02:30 on the spring-forward night** (doesn't exist) | `400 INVALID_LOCAL_TIME` | ✅ Admin #11b |
| EC-16 | Manager corrects **their own** shift | Allowed, audited and flagged like any other | ⚠️ no four-eyes rule (kept by the user's decision) |
| EC-12 | Employee **deactivated while on shift** | No employee-management screen yet | ⏳ not built |

### Phase 2 · Work location
| ID | Edge case | Handling | Proof |
|---|---|---|---|
| — | Location **missing**, empty, unknown (`"Moon"`) or a number (`"1"`) | `400 INVALID_LOCATION`, checked **before** the time API (the test runs with the time service "down" and still gets 400) | ✅ Punch #14 (5 cases) |
| D-2 | Client **sends a time** with Clock In | Ignored; the trusted clock's time is recorded | ✅ Punch #15 |
| — | Location kept through Clock Out, shown in history and to the manager | Stored per shift; one field in `ShiftDto` | ✅ Punch #13 · History #10 · Admin #13 |
| — | **Old shifts** without a location | `NULL` → "—"; a correction never changes the location | ✅ History #10 · Admin #13 · 🔒 check constraint |
| — | Shared computer: the next person gets the previous person's choice | Remembered **per user** | ✅ frontend location #2 |

### Phase 2 · Correction requests
| ID | Edge case | Handling | Proof |
|---|---|---|---|
| — | Request for **someone else's** shift | `404` (ids don't reveal other people's data) | ✅ Requests #2 |
| — | Request for an **open** shift | `409 SHIFT_STILL_OPEN` (user's decision: finished shifts only) | ✅ Requests #3 |
| EC-13 | No reason / future time / out before in / no change / bad format / reason too long | `400` with a clear code | ✅ Requests #4 (6 cases), #5 |
| EC-1/2 | **Two requests** for the same shift (also simultaneously) | One waiting request per shift: `409 REQUEST_ALREADY_PENDING` | ✅ Requests #6, #7 (5 at once → 1) · 🔒 filtered unique index |
| — | Employee tries to **list all or decide** | `403` | ✅ Requests #8 |
| F6 | **Approve** must be a real, visible correction | Runs the manager correction in a transaction: `ManualCorrection`, `Corrected`, audit row "Request #N from …", decided by + trusted time | ✅ Requests #9 |
| EC-13 | Approval would **now overlap** a newer shift | Refused, request stays waiting, shift and audit unchanged | ✅ Requests #10 |
| — | **Reject**, then ask again | Shift untouched; a new request is allowed | ✅ Requests #11 |
| — | Approve / reject **twice** | `409 REQUEST_ALREADY_DECIDED`; corrected once | ✅ Requests #12 · 🔒 rowversion |
| — | Request **moves a day** or makes a shift **over 14 h** | Flagged to employee and manager, not blocked (user's idea) | ✅ frontend requests #3–#6 |
| EC-6 | Typed time on the night the **clocks change** (length shown) | Zurich offset of that very moment | ✅ frontend requests #5 |
| EC-7 | Fall-back night: one "first / second 02:30" answer applied to **both** corrected times; requests can't choose | Known bug | ⚠️ not fixed (user's decision) |

### Phase 2 · Safety fixes
| ID | Edge case | Handling | Proof |
|---|---|---|---|
| EC-3 | **Old tab** clocks out a *newer* shift | Clock Out names the shown shift → `409 SHIFT_CHANGED`, newer shift untouched | ✅ Safety #1, #2 · browser (two tabs) |
| EC-13 | **Two managers** correct two shifts of one employee at the same moment | Employee lock (`UPDLOCK`): exactly one wins | ✅ Safety #3 (15 rounds; **fails with the lock switched off**) |
| EC-15 | Punch saved, **connection dropped** before the confirmation | Status re-read; a repeat answers `409` with the current state | 🟡 by design (EC-15) |
| — | Provider returns an **old time** that still passes validation | Not detected | ⚠️ not fixed (user's decision) |
| EC-14 | **Deactivated** employee with a still-valid badge | Clock In / Out refuse it; other actions until the badge expires | ⚠️ fix built, then cancelled (user's decision) |

### Phase 2 · Reports and CSV
| ID | Edge case | Handling | Proof |
|---|---|---|---|
| D-10 | **Overnight shift** in a report | Counted on its start date | ✅ Reports #1 |
| #7 | **Seconds** that don't fill a minute | Sum of each shift's nearest-minute length (matches My week) | ✅ Reports #1 |
| EC-6 | **DST day** (23 h) at the range edge | Zurich midnight from the official rules | ✅ Reports #2 |
| — | **Open shifts**, unknown location, long and corrected shifts | Open: counted, not in hours; each tile counted | ✅ Reports #3 |
| — | **Employee filter**, waiting requests | Only that employee; requests for shifts in the range | ✅ Reports #4, #5 |
| — | Employee asks for a report / bad or missing range | `403` / `400 INVALID_RANGE` / `400` | ✅ Reports #6 |
| — | CSV: **commas, quotes**, a name like `=HYPERLINK(…)`, Excel encoding, open shift | RFC 4180 quoting, formula guard (`'`), UTF-8 BOM, "in progress" row | ✅ Reports #7 |
| — | Period presets: January → last December, leap year | Computed from the Zurich "today" | ✅ frontend reports #1–#3 |

---

## All tests

### Trusted clock: `FallbackTrustedClockTests` (10)
| # | Test | Proves |
|---|---|---|
| 1 | Primary answers in winter time | Correct value, +01:00, backup not called |
| 2 | Primary answers in summer time | +02:00 handled |
| 3 | Clocks-go-back night: the two 02:30s are different instants | EC-7 |
| 4 | Primary times out → backup used | 2.5 s budget works |
| 5 | Primary returns HTTP 500 → backup used | |
| 6 | Primary returns garbage → backup used | |
| 7 | Primary reports the wrong time zone → rejected | |
| 8 | Primary offset breaks Zurich rules → rejected | Validation against IANA rules |
| 9 | Backup's local and UTC times disagree → rejected | |
| 10 | Both fail → refuses instead of guessing | Fail closed (D-5) |

### Login: `AuthTests` (9)
| # | Test | Proves |
|---|---|---|
| 1 | Correct password → token + role | |
| 2 | Wrong password → `INVALID_CREDENTIALS` | |
| 3 | Unknown user gets exactly the same answer | No user enumeration |
| 4 | Deactivated employee can't log in, even with the right password | |
| 5 | `/me` without a token → 401 | |
| 6 | `/me` with a valid token → the right employee | |
| 7 | `/me` with a tampered token → 401 | Signature check |
| 8 | 6th login attempt within a minute → 429 | Brute-force limit |
| 9 | Passwords stored as hashes | |

### Clock In / Clock Out: `PunchTests` (15 methods, 19 cases)
| # | Test | Proves |
|---|---|---|
| 1 | Clock In records the time from the trusted clock | Time + source + Zurich offset |
| 2 | Clocking in twice is refused and says since when | EC-4 |
| 3 | Clocking out while off shift is refused | EC-3 |
| 4 | 08:00 → 16:30 = 510 min, status back to OffShift | |
| 5 | Overnight 22:00 → 06:00 = 480 min | EC-5 |
| 6 | Clocks jump forward night = 7 h, not 8 | EC-6 |
| 7 | Time service down → nothing saved | D-5, EC-8 |
| 8 | 10 simultaneous Clock Ins → exactly 1 shift | EC-1/2, D-9 |
| 9 | Punching without logging in → 401 | |
| 10 | 15-hour shift → recorded and flagged `TooLong` | F7 |
| 11 | Clock Out time before Clock In → rejected, shift stays open | EC-9 |
| 12 | Status while on shift shows since when | F3 |
| 13 | **Phase 2** · Work location is saved with the shift and kept after Clock Out (any letter case) | Plan 12 |
| 14 | **Phase 2** · Missing or unknown location is refused before calling the time API (5 cases: no body, `{}`, `""`, `"Moon"`, `"1"`) | `400 INVALID_LOCATION`, nothing saved |
| 15 | **Phase 2** · A time sent by the client is ignored | D-2 |

### History: `HistoryTests` (11 methods, 13 cases)
| # | Test | Proves |
|---|---|---|
| 1 | Daily and range totals add up | 480 + 510 = 990 min |
| 1b | Each shift rounds to the nearest minute and totals add up | 1:40 → 2, 1:20 → 1, 0:30 → 1; total 4 = sum of rows |
| 2 | Overnight shift counts only on its start date | D-10 |
| 3 | Shift at 00:30 on the DST day lands on that day | EC-6, Zurich day boundaries |
| 4 | Shifts outside the range are not included | Boundary just before midnight |
| 5 | Another employee's shifts are never included | Privacy |
| 6 | Open shift returned separately, not counted | |
| 7 | No dates → this week, from the trusted clock | Mon–Sun in Zurich |
| 8 | Invalid range refused (3 cases: reversed, > 93 days, one date only) | `400 INVALID_RANGE` |
| 9 | History requires login | 401 |
| 10 | **Phase 2** · My week shows each shift's work location (Office, Remote, "—" for old shifts) | Plan 12 |

---

### Manager: `AdminTests` (14 methods, 16 cases)
| # | Test | Proves |
|---|---|---|
| 1 | Employees can't use manager endpoints | 403 |
| 2 | On-shift list shows open shifts and flags forgotten ones | F5, F7 |
| 3 | Shift list filters by employee and flagged-only | F5 |
| 4 | Correcting a Clock Out → labelled, flagged, audited (old/new/reason/manager/trusted time) | F6 |
| 5 | Closing a forgotten shift lets the employee clock in again; audit shows "(open)" | EC-4 |
| 6 | Reason required (3 cases: missing, empty, too short) | F6 |
| 7 | Clock Out must stay after Clock In | |
| 8 | Times in the future are refused | EC-13 |
| 9 | A correction can't overlap another shift | EC-13 |
| 10 | A stale version stamp is refused | Optimistic concurrency |
| 11 | Ambiguous fall-back 02:30 needs an offset; with +01:00 → 330 min | EC-7 |
| 11b | Non-existent spring-forward 02:30 is refused | EC-6 |
| 12 | Audit history lists every change in order | D-11 |
| 13 | **Phase 2** · Manager sees each shift's location and none for old shifts; a correction keeps the location | Plan 12 |

### Phase 2 · Correction requests: `CorrectionRequestTests` (13 methods, 18 cases)
| # | Test | Proves |
|---|---|---|
| 1 | Request on my finished shift is pending and changes nothing yet | Only the changed time is stored |
| 2 | Someone else's shift is not found | 404, privacy |
| 3 | An open shift can't be requested | 409 `SHIFT_STILL_OPEN` |
| 4 | Bad requests are refused with a clear code (6 cases: no reason, future, out before in, same as recorded, nothing, bad format) | 400 |
| 5 | A too-long reason is refused | `REASON_TOO_LONG` |
| 6 | A second pending request for the same shift is refused | `REQUEST_ALREADY_PENDING` |
| 7 | Five simultaneous requests create exactly one | 🔒 filtered unique index |
| 8 | Employees can't see all requests or decide | 403 |
| 9 | Approving corrects the shift, labels it and writes an audit row | PRD rule 2, F6 |
| 10 | Approval that would now overlap is refused and the request stays pending | EC-13 |
| 11 | Rejecting leaves the shift alone and allows a new request | |
| 12 | A decided request can't be decided again | `REQUEST_ALREADY_DECIDED` |
| 13 | Manager list filters by status and rejects an unknown status | `INVALID_STATUS` |

### Phase 2 · Safety fixes: `SafetyFixTests` (3)
| # | Test | Proves |
|---|---|---|
| 1 | Clock Out from an old tab doesn't close the newer shift | `SHIFT_CHANGED` |
| 2 | Clock Out naming the open shift works | |
| 3 | Two managers correcting two shifts at once never create an overlap (15 rounds) | Employee lock; fails with the lock off |

### Phase 2 · Reports: `ReportTests` (7)
| # | Test | Proves |
|---|---|---|
| 1 | Hours are the sum of rounded shifts; an overnight shift counts on its start date | 480 + 181 + 480 = 1141 |
| 2 | Range edges follow Zurich midnight on the night the clocks jump | DST day |
| 3 | Tiles split Office / Remote / unknown and count long, corrected and open shifts | |
| 4 | One row per employee, and the filter shows only that employee | |
| 5 | Waiting fix requests for shifts in the range are counted | |
| 6 | Employees can't see reports, and bad ranges are refused | 403, `INVALID_RANGE`, missing `from` |
| 7 | CSV has one row per shift, opens in Excel and can't run formulas | BOM, quoting, formula guard, file name |

### Frontend: `frontend/src/time.test.ts` (Vitest, 13)
| # | Test | Proves |
|---|---|---|
| 1 | Server time "…08:02:11+01:00" → "08:02:11 CET" | Display reads Zurich time straight from the server |
| 2 | "+02:00" → CEST | Summer label |
| 3 | 23:30 Zurich stays 23:30 / same date | Independent of the device's time zone (EC-10) |
| 4 | Live clock: UTC instant → Zurich winter time + date | |
| 5 | Live clock: UTC instant → Zurich summer time | |
| 6 | Label switches CET → CEST exactly at 01:00 UTC on 29 Mar 2026 (01:59:59 → 03:00:00) | DST on screen |
| 7 | 510 → "8:30", 5 → "0:05" | Duration format |
| 8 | 192 → "3 h 12 min" | "On shift for …" |
| 9 | Date steps across month end and the DST day | Week navigation |
| 10 | Seven dates of a week | |
| 11 | Forgotten-shift alert quiet at exactly 14 h | Plan 11 |
| 12 | Alert fires just after 14 h | Plan 11 |
| 13 | Open time measured on real instants, across a DST change | Plan 11 |

### Phase 2 · Frontend: 22 more Vitest tests
| File | Tests | What they prove |
|---|---|---|
| `recent.test.ts` | 4 | Last 14 days across a month boundary · newest first, at most 5 · a shift in progress on top · empty list |
| `location.test.ts` | 3 | Office / Remote / "—" labels · remembered **per user** on a shared device · junk or blocked storage ignored |
| `requests.test.ts` | 7 | Only changed times are sent · fix allowed only on a finished shift without a waiting request · new length + moved day · strictly more than 14 h · DST-aware typed times · summary of a stored request · newest request per shift |
| `timeInput.test.ts` | 4 | 24-hour shapes (`17:00`, `1700`, `7:05`) · refuses `25:00`, `5pm`, AM/PM · "empty" vs "not finished" · roller offers 00–23 / 00–59 |
| `reports.test.ts` | 4 | This week Mon–Sun · this / last month incl. leap year · January → last December · every day of the range, file name, bar width |

## Manual checks done along the way
- **Database rules (plan 03):** 5 direct SQL tests inside a rolled-back transaction: first shift accepted, second open shift
  rejected, Clock Out before Clock In rejected, normal Clock Out accepted, next-day shift accepted.
- **Live runs** against the real time API: `/api/time/now` (plan 05), real logins (plan 06), and a real clock-in/out cycle (plan 07).
- **Browser walkthrough of the UI (plan 09):** wrong password, login, Clock In, refresh, a **stale tab after a second
  device clocked out** (409 → message + self-refresh), My week, phone-size layout, log out.
- **Browser walkthrough of the manager screens (plan 10):** forgotten shift flagged → closed with a reason → history entry →
  future time refused → the employee sees "Corrected by manager" and has no Team tab.
- **Phase 2 browser walkthroughs** (details in each plan's "Results"): Office / Remote chosen and shown everywhere, phone
  layout (plan 12) · fix requests sent, approved and rejected, badge counts, audit entry, statuses for the employee
  (plan 13) · length / moved-day warnings, 24-hour time box and roller (13c/d) · old tab refused with two tabs (plan 14) ·
  report totals equal My week, CSV rows add up (plan 15) · Wi-Fi exposure: the ports answered through the computer's
  Wi-Fi address, and limiting them to this computer closed them (tried, then left as is by choice; DECISIONS, "Not done, by choice").
