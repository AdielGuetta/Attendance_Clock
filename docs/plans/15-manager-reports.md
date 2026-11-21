# Plan 15 (phase 2): Manager reports + CSV export

**Status:** done ✅ (approved 2026-11-20) · **Branch:** `feature/manager-reports`

## Why
Managers need answers like "how many hours did the team work in November?", "who worked remotely?", "which shifts
need a look?", and a file they can open in Excel (payroll, archive). Today they can only page through the Team tab week
by week. Feature 3 of the agreed list (from Team Clock), and a good layer-by-layer exercise: a query that **adds up**
many rows, a new endpoint, and a **file download**.

## What the manager gets: a new **Reports** tab
1. **Period:** quick buttons **This month** / **Last month** / **This week**, or a custom From–To (max 93 days, same limit
   as history). Optional **employee** filter. "Today" comes from the server's trusted time, not the device.
2. **Summary tiles:** total hours · shifts · employees who worked · **Office vs Remote** hours (and "—" for old shifts
   without a location) · shifts **over 14 h** · **corrected** shifts · shifts **still open** now · fix requests **waiting**.
3. **Per employee table:** name, hours, shifts, Office / Remote, over 14 h, corrected. Clicking a row filters the report
   to that employee.
4. **Per day list:** each date with hours and shifts, with a simple bar so busy days stand out (plain HTML, no chart
   library).
5. **Download CSV** button: one row per shift for the same period and filter.

## Rules (same as the rest of the app, so every number matches My week and the Team tab)
- A shift counts on the **Zurich date of its Clock In** (D-10); day boundaries from Zurich's rules (DST days are 23/25 h).
- **Closed shifts only** in the hours; each shift rounded to the nearest minute and totals are the **sum of those**
  (DECISIONS #21). Shifts still open are counted separately ("still open"), never guessed.
- Over 14 h = strictly more than 14 h (same as the `TooLong` flag).

## Layer by layer
| # | Layer | Change |
|---|---|---|
| 1 | **Server: report** | `GET /api/admin/reports?from=&to=&employeeId=` → tiles + per-employee + per-day, computed in one place (`Reports/ReportService.cs`). Manager only (403 for employees); bad range → 400 `INVALID_RANGE`. |
| 2 | **Server: CSV** | `GET /api/admin/reports/export?from=&to=&employeeId=` → `text/csv` file, named e.g. `attendance-2026-11-01_2026-11-30.csv`. Built by the server, so the file uses exactly the same numbers as the screen. |
| 3 | **Database** | **No change**: everything is computed from existing tables (Shifts, Employees, CorrectionRequests). No migration, no app pause. |
| 4 | **Screens** | New `ReportsScreen.tsx` + manager tab "Reports"; period helpers in `src/reports.ts`. |
| 5 | **Download** | The login badge travels in a request header, which a plain link can't send. So the screen fetches the file with the badge and hands it to the browser as a download (a "blob"). |
| 6 | **Tests** | Backend + frontend (below). |

## The CSV file
Columns: `Date, Employee, Clock In, Clock Out, Zone, Length (h:mm), Minutes, Location, Flags, Clock In source,
Clock Out source`. Times are Zurich wall time ("2026-11-18 08:00:12") with the zone (CET/CEST) beside them.
- **Opens correctly in Excel:** UTF-8 with a "BOM" marker (so names with ä/é and the "—" show right), commas, values with
  commas or quotes wrapped in quotes.
- **Safe:** a cell starting with `=`, `+`, `-` or `@` gets a leading `'`, so a name like `=HYPERLINK(...)` can't run as a
  formula in Excel ("CSV injection").
- Open shifts are included with an empty Clock Out and "in progress", so the file is complete.

## Decisions (please confirm)
- **CSV = one row per shift** (the raw data). Excel can sum or pivot it; per-employee totals are on the screen.
- **Comma** as the separator: your computer uses US settings, where Excel expects commas. (Some European Excel setups
  expect `;`; easy to change later.)
- **No new database objects** and no stored reports: always computed live, so they're never out of date.
- **Not in this step:** charts library, PDF export, scheduled e-mail reports, payroll rules (overtime rates, breaks).

## Proof
- **Backend tests:** totals = sum of the rounded shift lengths · overnight shift counted on its start date · range edges at
  Zurich midnight (incl. a DST day) · open shifts excluded from hours but counted · Office / Remote / unknown split ·
  employee filter · waiting requests count · over 14 h and corrected counts · employee gets 403 · bad range 400 · CSV:
  header, one row per shift, BOM, quotes around commas/quotes, formula guard, file name.
- **Frontend tests:** period helpers (this / last month, including January → December of the previous year; this week
  Monday–Sunday), CSV file name.
- **Browser:** manager → Reports → this month: tiles and tables match the Team tab; click dana → filtered; Download CSV →
  open the file and check a few rows against the screen. Employee account: no Reports tab.

## Results (2026-11-20)
- **Server:** `Reports/ReportService.cs` (one query for the range: tiles, per employee, per day; reuses `ShiftDto`'s
  rounding and the `TooLongThreshold`), `Reports/ReportCsv.cs` (escaping + formula guard), `Reports/ReportsController.cs`
  (`GET /api/admin/reports`, `GET /api/admin/reports/export`, manager only, `INVALID_RANGE`, `from`/`to` required).
  No database change.
- **Screens:** `src/reports.ts` (+ tests: presets, January → December, leap year, all dates of a range, file name, bar
  width), `screens/ReportsScreen.tsx`, a manager **Reports** tab. `api.ts`: `send` / `request` / `download` (the CSV is
  fetched with the login badge and saved as a blob). The Team tab now reuses `mondayOf` from `reports.ts`.
- **Tests:** backend **95/95** (+7 in `Reports/ReportTests.cs`: rounded sums + overnight on its start date · DST-day
  edges · tiles · per employee + filter · waiting requests · 403 / bad range / missing `from` · CSV header, BOM, quoting,
  formula guard, file name, open shift row). Frontend **35/35** (+4), type-check clean.
- **Browser (manager):** Reports → This month: 17:57 h, 8 shifts, 1 employee, Office 0:01, location "—" 17:56,
  1 corrected, 2 fix requests waiting; per day Tue 17 9:00, Wed 18 8:00, Fri 20 0:57 · 6. **Same total as dana's My week
  (17:57).** The CSV for dana (fetched like the button does): BOM, `attendance-2026-11-01_2026-11-30.csv`, header + 8 rows
  whose minutes add up to 1077 = 17:57.
- **Not checked live:** pressing the Download button itself (it saves a file on the user's computer: left for the user),
  and the employee view without the Reports tab (gated by the same manager check as Team / Requests).
