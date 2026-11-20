# Plan 12 (proj2): Work location (Office / Remote)

**Status:** done ✅ (approved 2026-11-20) · **Branch:** `feature/work-location` (cut from `feature/recent-shifts`)

## Why
Managers want to know *where* a shift was worked. The employee picks **Office** or **Remote** before Clock In; the choice
is saved with that shift and shown everywhere shifts appear. It's also the first proj2 feature that goes through **every
layer** (screen → request → validation → database → DTOs → screens → tests), so it's the practice run for bigger features.

## What changes, layer by layer
| # | Layer | Change |
|---|---|---|
| 1 | **Clock screen (UI)** | Two buttons above Clock In: **🏢 Office** / **🏠 Remote**. The last choice is remembered in the browser (`localStorage`), so it's one tap less tomorrow. While on shift, the screen shows "📍 Office". |
| 2 | **Request** | `POST /api/punches/clock-in` gets a small JSON body: `{ "location": "Office" }`. **Only the location, never a time** (PRD D-2). Clock Out stays without a body. |
| 3 | **Server validation** | `location` is **required** and must be `Office` or `Remote` (case-insensitive). Missing or anything else → **400 `INVALID_LOCATION`**, checked *before* the time API is called, so a bad request costs no external call and saves nothing. |
| 4 | **Database** | New column `Shifts.WorkLocation` (`varchar(16)`, stored as the text "Office"/"Remote" so it's readable in SQL), plus a check constraint `IN ('Office','Remote')`: the data rule lives in the database too. EF migration `AddWorkLocation`. |
| 5 | **DTOs** | `ShiftDto` gets `workLocation`. `AdminShiftDto` builds on `ShiftDto`, so the manager screens get it too. |
| 6 | **Screens** | Clock screen + Recent shifts, My week, Team tab (new column), Edit dialog (shown, read-only). |
| 7 | **Tests** | Backend + frontend (below). |

## Decisions (please confirm)
- **Required, not defaulted.** If the server silently picked "Office", a bug in the UI would record wrong data without
  anyone noticing. A clear 400 is safer. (Our UI always sends it.)
- **Old shifts get no location (`NULL`), shown as "—".** Shifts recorded before this feature: we don't know where they were
  worked, and inventing "Office" would be false data. So the column allows NULL, but every *new* shift always has a value.
- **A time in the body is ignored.** If someone sends `{ "location": "Office", "clockIn": "2020-01-01…" }`, the extra field
  is dropped and the time still comes from the time API. A test proves it.
- **The manager can see the location but not change it** in this step. Correcting it would need a new audit field;
  that fits better with the next feature (correction requests). Easy to add later if you want.

## The migration (app pauses ~1 minute)
`dotnet ef` runs in Docker with the stop api → run → start api pattern, so http://localhost:5175 is briefly unreachable.
Next API start applies the migration automatically (Development).

## Proof
- **Backend tests** (`docker compose run --rm tests`), new ones:
  Clock In with `Remote` → 201 and saved as Remote · missing location → 400 `INVALID_LOCATION`, nothing saved ·
  unknown value (`"Moon"`) → 400 · a time sent in the body is ignored · My week and the manager list include the location.
- **Frontend tests** (`npx tsc -b && npx vitest run`): label helper (Office / Remote / old shift "—") and the remembered choice.
- **Browser** on http://localhost:5175: dana picks Remote → clocks in → sees "📍 Remote" → clocks out → Recent shifts and
  My week show it; the manager sees it in the Team tab and in the Edit dialog. Old demo shifts show "—".

## Results (2026-11-20)
**Built, layer by layer**
- **Database:** `Shifts.WorkLocation varchar(16) NULL` + check constraint `CK_Shifts_WorkLocation`, migration
  `20261120172846_AddWorkLocation` (created in Docker, applied automatically on API start). The 3 old demo shifts are NULL.
- **Backend:** `WorkLocation` enum + property on `Shift` (stored as text), `Punches/ClockInRequest.cs` (body + strict
  name check: `Enum.TryParse` alone would accept "1"), `PunchesController` validates **before** anything else
  (`EmptyBodyBehavior.Allow`, so even a missing body gets our `INVALID_LOCATION`), `ShiftService.ClockInAsync(…, location)`,
  `workLocation` on `ShiftDto` and `AdminShiftDto`.
- **Frontend:** `src/location.ts` (label, remembered choice), Office/Remote toggle on the Clock screen (Clock In disabled
  until chosen: "Pick a location"), location in the on-shift status line and the success banner, a **Where** column in
  Recent shifts and the Team table, a label in My week, the Team "On shift now" list and the Edit dialog.

**Tests**
- Backend **67/67** (was 58; +9): Punches 13–15 (Remote saved and kept after Clock Out · 5 bad bodies → 400 with the time
  service *down*, which proves the time API isn't called · a client-sent time is ignored), History 10, Admin 13 (manager
  sees the location; a correction keeps it).
- Frontend **20/20** (was 17; +3 in `location.test.ts`), type-check clean.

**Browser (http://localhost:5175)**
- dana: clocked out of the old open shift → nothing pre-selected, big button says "Pick a location" → picked Remote →
  Clock In → "On shift since 18:32 CET · 🏠 Remote", Recent shifts row "🏠 Remote" → Clock Out → My week shows "🏠 Remote",
  old shifts "—". The request body was only `{ "location": "Remote" }`; the time came from timeapi.io.
- manager: Team tab "Where" column (Remote / —), Edit dialog header "Dana Cohen · Fri 20 Nov · 🏠 Remote". No console errors.

**Found and fixed during the browser check**
- A phone-only CSS rule hid the Team table's flags by position (`nth-child(5)`). The new column took position 5, so on
  phones it hid **Where** instead. Now the flags cell has a class (`flags-col`).
- The remembered choice was per browser, so the manager saw dana's "Remote" pre-selected. On a shared computer that could
  record the wrong place, so it's now remembered **per user**.
- On phones the Team table was wider than the screen (Where and Edit cut off; Edit already was before). It now scrolls
  sideways inside its panel (`.table-scroll`).

**Not done**
- The manager can't change a shift's location (planned for the correction-requests feature).
- docs/TESTS.md, DECISIONS.md and README describe the submitted project and weren't updated in proj2.
