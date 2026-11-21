# DECISIONS

The main decisions, the edge cases, what was left out, and known limitations. Kept short. The full reasoning is in
[docs/PRD.md](docs/PRD.md) (IDs like `D-5`, `EC-7`) and the step plans in [docs/plans/](docs/plans/).

*Status at submission: everything in the "Key decisions" table is built and tested. What wasn't built is listed under
"What I chose not to do" and "Known limitations". The story of how Claude Code was used is in [docs/WORKING-WITH-CLAUDE.md](docs/WORKING-WITH-CLAUDE.md).*

*proj2 (the practice copy) adds decisions #37–#48 and the edge cases marked **proj2**: work location, employee correction
requests, manager reports with CSV export, and safety fixes ([plans 12–15](docs/plans/)).*

---

## Key decisions

| # | Decision | Why |
|---|---|---|
| 1 | **Time is fetched by the server, never sent by the client** (D-2) | A browser clock can be changed by anyone; the client only sends intent ("clock me in"). |
| 2 | **One `ITrustedClock`; providers behind it** (D-3) | Business code has exactly one place to get "now". Tests swap in fakes to simulate outages and DST. |
| 3 | **Primary timeapi.io, backup time.now** (D-4) | Chosen by measurement: 6 candidates, 10 calls each ([plan 04](docs/plans/04-time-providers.md)). timeapi.io was 10/10; worldtimeapi.org was down. Both are keyless, so reviewers don't need to sign up. |
| 4 | **Fail closed: both providers down → punch rejected (503)** (D-5) | A wrong time is worse than a missing punch, which a manager can add later with a reason. Never falls back to the local clock. |
| 5 | **2.5 s timeout per provider** | timeapi.io sometimes takes ~1.3 s; a short per-attempt budget lets a slow primary fall through to the backup quickly. |
| 6 | **Validate every answer** (§4.3) | Zone must be Europe/Zurich; offset must be +01:00/+02:00 **and** match Zurich's DST rules for that instant; time.now's local and UTC fields must agree; times without an offset are rejected. |
| 7 | **Store `datetimeoffset` (instant + Zurich offset)** (D-6) | One column keeps the true instant (DST-safe durations) and the wall time the employee saw. It replaced a two-column design during implementation. |
| 8 | **Safety rules in the database, not only in code** (D-9) | Filtered unique index = at most one open shift per employee, even under concurrent requests; check constraint = Clock Out after Clock In. |
| 9 | **Docker Compose for local dev** (D-12) | Windows Smart App Control on the dev machine blocks locally built .NET assemblies, and turning it off would require a Windows reinstall. Docker needed no system changes, and reviewers get a one-command start. |
| 10 | **.NET 10 (LTS) + EF Core 10** | Long-term support. EF Core 11 needs .NET 11, so packages are pinned to 10.0.x. |
| 11 | **JWT login, 60-minute tokens** (D-8) | Stateless and simple with a separate React app. Trade-off: a token can't be revoked early; the short lifetime limits the risk. |
| 12 | **Passwords hashed with Identity's `PasswordHasher` only** | Microsoft's tested PBKDF2 + salt, without the full Identity table set we don't need. |
| 13 | **One generic login error, equal timing, 5 attempts/min per IP** | Doesn't reveal which usernames exist; stops password guessing. |
| 14 | **No self-registration; demo accounts seeded in Development** (D-1) | Internal tool. Reviewers can log in immediately (accounts listed in the README). |
| 15 | **The server never trusts the UI** | The UI shows only the valid button, but stale tabs, double-clicks and direct API calls still reach the server. Rules are enforced in `ShiftService` and, finally, by the database. |
| 16 | **No `Idempotency-Key` for punches** (revised PRD F2) | The DB unique index already guarantees one open shift; a repeated punch gets a clear `409` with the current state. Keys would add a table for a nicer response only. |
| 17 | **`/status` doesn't call the time API** | The UI checks status often; external calls there would worsen the rush risk (EC-17). Elapsed time on screen is cosmetic; recorded times always come from the API. |
| 18 | **Clock In must be after the previous Clock Out; Clock Out after Clock In** | Two providers that disagree by a second can't create overlapping or negative shifts. The punch is refused with `503 TIME_SOURCE_INCONSISTENT` instead. |
| 19 | **Shifts over 14 h are recorded and flagged `TooLong`**, not blocked | The record stays honest; the manager reviews it (F7). |
| 20 | **History day boundaries use Zurich's official rules**; a shift belongs to its Clock In date (D-10) | DST days are 23 or 25 hours long. An overnight shift is counted once, on its start date. |
| 21 | **Each shift rounds to the nearest minute; totals = the sum of the rows** | First version rounded each shift *down* and added those, which loses time systematically (found in the live check). Adding exact durations fixed the loss, but then the rows didn't visibly add up to the total. Nearest-minute rounding is fair in both directions (≤ 30 s per shift) **and** what the employee sees always adds up. |
| 22 | **History: only your own shifts (id from the token), max 93 days, default = current Zurich week via the trusted clock** | No peeking at others' data via the URL; bounded queries; "today" comes from a trusted source. |
| 23 | **React UI runs in Docker too (Node 24, Vite), with a Vite proxy for `/api`** | Vite's unsigned native helper would face the same Smart App Control block; the proxy means one address for the browser and no CORS setup. |
| 24 | **One big context-aware button, disabled while recording** | The UI prevents ordinary double-clicks; the server still enforces the rules (#15). After any punch or error, the status is re-read from the server. |
| 25 | **Times shown straight from the server's ISO string (already Zurich + offset), labelled CET/CEST** | No conversion on the device, so a phone set to another zone still shows Zurich time. The live clock is cosmetic (server time + elapsed `performance.now()`). |
| 26 | **No router or UI library: three screens, a tab switch and plain CSS** | Less to install, explain and maintain for a small app. |
| 27 | **Login badge kept in `localStorage`** | Survives a refresh. Trade-off: readable by scripts if the page had an XSS hole. Mitigated: React escapes all output, no raw HTML, 60-minute expiry. |
| 28 | **App on port 5174, not Vite's default 5173** | During testing, another local Vite project was on 5173 and the browser showed the *wrong app*. Moved ours rather than stopping the user's other project. |
| 29 | **Manager corrections: reason required, labelled `ManualCorrection`, shift flagged `Corrected`** (F6) | The only non-API times in the system, so each one is explained and visible: the "external time" promise stays honest. |
| 30 | **Append-only `ShiftAudits` table**: who, when (trusted clock), field, old → new, reason (D-11) | A permanent, attributable history. Rows are never updated or deleted; no cascade delete. |
| 31 | **Version stamp on edits** (`rowversion`, sent back by the UI) | If the employee clocked out while the manager was editing, the save is refused (`409 SHIFT_MODIFIED`) instead of silently overwriting. |
| 32 | **Managers type Zurich wall time; DST is resolved by the server** | 02:30 on the fall-back night is ambiguous → the UI asks "the first or the second 02:30?"; 02:30 on the spring-forward night doesn't exist → refused. |
| 33 | **"Forgot to clock out" is computed on read** (open > 14 h, against the trusted clock), not stored | It changes with time; storing it would go stale. If the time service is down, lists still load, just without that flag. |
| 34 | **Manager-only endpoints enforced on the server** (`[Authorize(Roles = "Manager")]` → 403); the Team tab is just a convenience | The UI never decides permissions. |
| 35 | **Fail fast instead of silent defaults** for the dev containers: Vite gets `--config vite.config.ts` plus the port/host flags; `restart: unless-stopped`; a health check on the web container | After the merge incident (Vite silently came back with default settings), suggested by the user: a missing config should be an **error**, not a guess. Verified by re-creating the incident: Vite logged "failed to load config", kept its last good settings, and recovered when the file returned. No outage. |
| 36 | **Forgotten shifts (> 14 h) are alerted, never auto-closed**: a banner for the employee, a red count on the manager's Team tab | Auto-closing would invent a Clock Out time (not from the API, not from a person, likely wrong pay). Alerts make it hard to miss; the manager fixes it with the real time and a reason. E-mail/push not built (needs extra services). |

### proj2 decisions
| # | Decision | Why |
|---|---|---|
| 37 | **Work location is required at Clock In**: `{"location":"Office"\|"Remote"}`, checked first (400 `INVALID_LOCATION`); stored as text with a check constraint; old shifts stay `NULL` ("—") | A silent default would record wrong data without anyone noticing; inventing "Office" for old shifts would be false. Checked before the time call, so a bad request costs nothing. A time sent in the body is ignored (D-2 still holds). |
| 38 | **The last location is remembered per user** on the device | First version remembered it per device, so on a shared computer the next person got the previous person's choice pre-selected (found in the browser check). |
| 39 | **Correction requests: an employee's times are only a suggestion; approval runs the manager correction (#29–#31) in one transaction** | One set of rules and one audit trail: the approved change is labelled `ManualCorrection`, flagged and audited, and the "Approved" status is saved together with it or not at all. PRD rule 2 holds: a non-API time exists only as an audited manager correction. |
| 40 | **Requests only for finished shifts; reject needs no explanation** (user's decisions) | An open shift is ended by clocking out (or a manager correction), not by a request. Rejecting is one tap. |
| 41 | **One waiting request per shift, enforced by the database** (filtered unique index); requests are never deleted | Two contradicting requests can't exist, even if sent at the same instant (5 simultaneous → exactly 1, tested). Decided requests stay as history. |
| 42 | **Shared code instead of copies**: Zurich time parsing, the overlap check and the error → HTTP mapping are used by both corrections and requests | The same problem always gets the same error code, and a rule changes in one place. |
| 43 | **Requests show the new length, a moved day and "Over 14 h" to both sides; warn, never block** (user's idea) | A night shift can legitimately end the next day, but a typo (16 Nov for 17 Nov) could quietly create a 33-hour shift. The manager decides, like F7's "record honestly and flag". |
| 44 | **24-hour time box with an hour/minute roller instead of the browser's date-time box** | The browser's box follows the computer's region and showed "05:00 PM" while the app shows "17:00". The date keeps the browser's calendar. Same value format, so no server change. |
| 45 | **Clock Out names the shift the screen shows** (`{"shiftId"}` → 409 `SHIFT_CHANGED` if a different one is open) | An old tab still showing a closed shift could otherwise close a newer one by mistake. No body = old behaviour. |
| 46 | **One change per employee at a time**: Clock In, corrections and approvals lock the employee row (`UPDLOCK` in a transaction) before their overlap / "after the last Clock Out" check | Rules spanning several shifts can't be a constraint on one row. Without the lock, two managers' simultaneous corrections both passed (proved by switching the lock off in the test). Different employees never wait for each other. |
| 47 | **Reports are computed live with the same rules as My week** (Clock In date, nearest-minute sum); **the CSV is built by the server** (UTF-8 BOM, RFC 4180 quoting, formula guard) and downloaded via `fetch` + blob | A report can never disagree with the screens. The BOM makes Excel read ä/—; the guard stops "CSV injection". A plain link can't carry the login badge, so the screen fetches the file. |
| 48 | **Scope decisions by the user**: managers may fix their own shifts (EC-16 kept); "check *active* on every request" was built, then cancelled; stale-provider time and the fall-back-night offset not fixed (rare); feature 4 (Employees tab, design refresh) skipped | Recorded so a reviewer sees they were considered, not missed. |

## Edge cases

| Case | Handling | Status |
|---|---|---|
| Time API down / slow / garbage (EC-8) | Backup provider → otherwise 503 `TIME_SOURCE_UNAVAILABLE` | ✅ built + tested |
| Provider returns an impossible offset or wrong zone | Rejected, next provider tried | ✅ built + tested |
| DST "fall back": 02:30 happens twice (EC-7) | The offset distinguishes the two instants | ✅ tested |
| Two open shifts (double-click, two tabs) (EC-1/2) | Database unique filtered index → `409 ALREADY_ON_SHIFT` | ✅ tested: 10 simultaneous Clock Ins → exactly 1 shift |
| Clock Out with no open shift (stale tab) (EC-3) | `409 NOT_ON_SHIFT` | ✅ tested |
| Forgot to clock out, tries to clock in next day (EC-4) | `409` with "on shift since …" | ✅ tested |
| Overnight shift (EC-5) | Duration from the instants; counted on the start date | ✅ tested (480 min) |
| DST spring-forward during a night shift (EC-6) | 22:00 → 06:00 = 7 h, not 8 | ✅ tested |
| Clock Out before Clock In (EC-9) | Rejected in code (`503 TIME_SOURCE_INCONSISTENT`) + DB check constraint | ✅ tested |
| Time API down during a punch | `503`, nothing saved | ✅ tested |
| Shift over 14 h (F7) | Saved, flagged `TooLong` | ✅ tested |
| History: overnight shift / shift at 00:30 on a DST day | Counted on its start date; boundaries from Zurich rules | ✅ tested |
| History: another employee's shifts | Never returned (id from the token) | ✅ tested |
| UI: stale tab / second device presses an outdated button | Server `409` → clear message, screen refreshes itself | ✅ checked live in the browser |
| UI: device clock or time zone wrong | Recorded and displayed times come from the server; the live clock is server-seeded | ✅ by design + formatting tests |
| UI: login badge expires mid-session | Any `401` → back to the login screen with a message | ✅ built (not yet tested automatically) |
| Expired / tampered / missing login token (EC-14) | 401; the UI will send the user back to login | ✅ API side built + tested |
| Deactivated employee tries to log in | Same generic 401 as a wrong password | ✅ tested |
| Forgotten open shift (EC-4) | Flagged in "On shift now"; manager closes it with a reason → employee can clock in again | ✅ tested + browser walkthrough |
| Correction overlaps another shift / is in the future / out before in (EC-13) | `409` / `400` with a clear code | ✅ tested |
| Shift changed while a manager edits it | `409 SHIFT_MODIFIED`, reload first | ✅ tested |
| Manager types 02:30 on a DST night | Fall back: "which one?" (offset required). Spring forward: "doesn't exist" | ✅ tested |
| Manager corrects their own shift (EC-16) | Allowed, audited and flagged like any correction | ⚠️ no four-eyes rule (known limitation) |
| Employee calls manager endpoints | `403` | ✅ tested |
| Deactivating an employee mid-shift (EC-12) | Not built (no employee management UI) | ⏳ not done |
| **proj2** · Missing / unknown work location (incl. `"1"`) | `400 INVALID_LOCATION`, before the time API is called; nothing saved | ✅ tested |
| **proj2** · Client sends a time with Clock In | Ignored; the trusted clock's time is recorded | ✅ tested |
| **proj2** · Shifts from before work location existed | Shown as "—", counted as "location —" in reports | ✅ tested |
| **proj2** · Fix request for someone else's shift / an open shift | `404` / `409 SHIFT_STILL_OPEN` | ✅ tested |
| **proj2** · Two fix requests for the same shift at once | Database index → exactly one; the other `409 REQUEST_ALREADY_PENDING` | ✅ tested |
| **proj2** · Approval that would now overlap a newer shift | Refused (`409 OVERLAPS_ANOTHER_SHIFT`), request stays waiting, shift unchanged | ✅ tested |
| **proj2** · A request approved or rejected twice | `409 REQUEST_ALREADY_DECIDED`, shift corrected once | ✅ tested |
| **proj2** · Request moves a day / makes a shift over 14 h | Flagged for employee and manager, not blocked | ✅ unit tests + browser |
| **proj2** · Old tab clocks out a *newer* shift | `409 SHIFT_CHANGED`, the newer shift is untouched | ✅ tested + browser (two tabs) |
| **proj2** · Two managers correct two shifts of one employee at once | Employee lock: exactly one wins, the other gets `OVERLAPS_ANOTHER_SHIFT` | ✅ tested (fails with the lock off) |
| **proj2** · Report: overnight shift, DST-day edges, open shifts | Same rules as history; open shifts counted separately, not in the hours | ✅ tested |
| **proj2** · CSV: commas, quotes, a name like `=HYPERLINK(…)`, Excel encoding | Quoted, formula guard (`'`), UTF-8 BOM | ✅ tested |
| **proj2** · Provider returns an old time that still passes validation | Not detected (only offset + "after the last Clock Out" are checked) | ⚠️ not fixed (user's decision) |
| **proj2** · Fall-back night: one "first / second 02:30" answer applied to both corrected times; requests can't choose | Known bug, once a year, night shifts only | ⚠️ not fixed (user's decision) |
| **proj2** · Deactivated employee with a still-valid login badge | Clock In / Out refuse it; other actions work until the badge expires (≤ 60 min) | ⚠️ fix built, then cancelled (user's decision) |

## What I chose not to do
- **No fallback to the server clock**, even as a last resort (it would break the core rule silently).
- **No "anchor + stopwatch" time** (sync once, then count locally): resilient, but arguably local time.
- **No retry of the same provider**: within the ~5 s budget, the backup *is* the retry.
- **No keyed time APIs** (TimeZoneDB, API Ninjas…): reviewers would have to sign up.

## Known limitations and risks
- **The backup provider is flaky**: time.now failed ~20–30% of calls in our test. Both failing at once should be rare, but when it happens the punch is refused.
- **Free third-party APIs** have no uptime guarantee. A paid or self-hosted time source would be the production answer.
- **Shift-start rush (EC-17):** every punch makes one external time call, so hundreds of punches in the same minute could
  hit the free provider's rate limits. SQL Server isn't the bottleneck. Mitigation (not built): let punches arriving
  within the same second share one in-flight time request, or use a paid provider.
- `/health` checks the API and the database, but not the time providers (not done; the punch path reports provider problems itself).
- No password change/reset or refresh tokens yet; logging out = the client discards its token.
- The rate limit is per IP and in memory, so it resets on restart and isn't shared across multiple servers.
- The UI runs on the Vite **dev server**; a production setup would build static files and serve them (e.g. from the API or a CDN).
- No automated UI (end-to-end) tests yet: the screens were verified by a manual browser walkthrough, plus unit tests for the formatting.
- **No employee management screen**: employees are seeded; creating, deactivating or resetting passwords isn't built (EC-12 open).
- **No four-eyes rule**: a manager can correct their own shift (audited and flagged, but not approved by someone else).
- Forgotten-shift alerts are in-app only (banner + Team badge); no e-mail or push notifications.
- No payroll rules (overtime rates, breaks); the shift list has no paging (one week at a time, max 93 days). *(proj2 added
  a CSV export and reports.)*
- **proj2:** an employee can't cancel their own request or ask for a whole missing day; the manager can't change a shift's
  location; no automated UI tests for the new screens either (checked by browser walkthroughs + unit tests for the logic).

## Working with Claude Code
- Rules for the AI live in [CLAUDE.md](CLAUDE.md); each big step starts with a plan in [docs/plans/](docs/plans/).
- **Where it needed correcting:**
  - after switching to Docker, the PRD still said "no Docker". I caught it and had it fixed;
  - `dotnet add package` pulled EF Core 11 (incompatible with .NET 10), so versions are pinned;
  - sqlcmd needed `-I` to work with the filtered index;
  - a PowerShell 5.1 file edit garbled non-ASCII characters (wrong default encoding). Caught, fixed and added to CLAUDE.md;
  - its fix for the history rounding bug was mathematically right but made the rows not visibly add up to the total.
    I pushed back from the user's point of view, and we switched to nearest-minute rounding (DECISIONS #21);
  - in the UI walkthrough, a stale-tab message said "Refresh to see your status" although the screen refreshes itself. Reworded;
  - its merge procedure (`git switch main` before merging) briefly deleted the `frontend/` folder, so Vite restarted without
    its config and the app became unreachable. Diagnosed from the container log, fixed, and the procedure changed in CLAUDE.md.
    I then pushed for a root fix: make the server *fail* rather than fall back to defaults (#35), re-tested by simulating the incident.
- **In proj2:**
  - a phone-only CSS rule hid "column 5"; the new location column took that place, so on phones it hid the location
    instead of the flags. Caught in its own browser check; the column now has a class instead of a position;
  - it first remembered the work location per device; on a shared computer that pre-selects someone else's choice (#38);
  - it kept writing my private notes into the real project's folder (its hand-over said so). I clarified that the
    original must stay completely untouched: the notes moved into proj2 and the real file was restored;
  - I questioned two UI details it had missed: the AM/PM time box (#44) and the focus ring overlapping the clock button;
  - when I asked "can anyone with the link see it?", it checked and found the containers' ports were open on the whole
    Wi-Fi network (database included); a fix limiting proj2's ports to this computer is ready on its own branch
    (`fix/local-only-ports`, not merged yet);
  - for the race fix it proved the test is meaningful by switching the lock off: the test failed, as it should (#46).
