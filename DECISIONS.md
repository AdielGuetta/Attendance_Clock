# DECISIONS

The main decisions, in short. Built in two phases: **phase 1** = the core the assignment asks for, **phase 2** = extensions.
Details: edge cases and their tests in [docs/TESTS.md](docs/TESTS.md), design IDs (`D-n`, `EC-n`) in [docs/PRD.md](docs/PRD.md),
each step in [docs/plans/](docs/plans/).

## Product and scope
| # | Decision | Why |
|---|---|---|
| 1 | Internal tool: no sign-up; demo accounts created automatically | Employees are added by the company; reviewers can log in at once |
| 2 | Everything runs in Docker | Windows security on the dev machine blocks locally built programs; one command, any OS |
| 3 | Time from timeapi.io, backup time.now (chosen by testing 6 services) | The most reliable ones; no key or sign-up needed |
| 4 | Phase 2: work location, correction requests, reports + CSV; Employees tab and redesign skipped | Real value first; the skipped parts were cosmetic or duplicated the Team tab |

## Rules the system follows
| # | Decision | Why |
|---|---|---|
| 5 | Punch times come only from the external time service, fetched by the server | The core rule: a device clock can be changed by anyone |
| 6 | Time service down → the punch is refused | A wrong time is worse than a missing punch |
| 7 | A shift counts on the Zurich day it started; each shift rounds to the nearest minute; totals = sum of rows | Overnight shifts counted once; fair pay, and the numbers always add up |
| 8 | Long or forgotten shifts are flagged, never blocked or closed automatically | Never invent a time |
| 9 | Manager corrections need a reason and leave a permanent audit trail; managers may fix their own shifts | The only non-API times, so each is explained; with one manager, nobody else could fix theirs |
| 10 | Employee requests (finished shifts only) are suggestions; approving = an audited manager correction; reject needs no note | One set of rules and one audit trail |
| 11 | Work location (Office / Remote) is required; older shifts show "—" | No silent defaults, no invented data |
| 12 | Unusual requests (another day, over 14 h) are flagged to both sides, not blocked | Night shifts are real; typos shouldn't slip by |

## Key technical decisions
| # | Decision | Why |
|---|---|---|
| 13 | Safety rules live in the database too (one open shift, Clock Out after Clock In) | Holds even when requests arrive at the same instant |
| 14 | Times stored with their Zurich offset | Correct lengths when clocks change (7 h, not 8) |
| 15 | The server never trusts the screen; permissions are checked on the server | Stale tabs and direct calls can't break the rules |
| 16 | Passwords stored only as salted hashes (PBKDF2); one generic login error; 5 tries per minute | A stolen database doesn't reveal passwords; no username leaks or guessing |
| 17 | Login badge (JWT) expires after 60 minutes | It can't be cancelled early, so its life is short |
| 18 | One change per employee at a time (a lock) | Two managers fixing the same person at once can't create overlapping shifts |

## Edge cases
Every edge case (two clicks at once, overnight shifts, clock-change nights, time service down, stale tabs, corrections that
overlap…), how it's handled, and the test that proves it: **[docs/TESTS.md](docs/TESTS.md)**.

## Not done, by choice
- No fallback to the computer's clock; no automatic closing of shifts; no e-mail notifications.
- Considered and left as is: a time service returning an old but valid time; a rare clock-change-night bug in corrections;
  "account still active" checked on every request (built, then cancelled); limiting Docker's ports to this computer.

## Known limitations
- Free time services have no uptime guarantee; hundreds of punches in one minute could hit their limits.
- No employee-management screen or password reset; employees can't cancel a request.
- Runs on the development server; production would serve built files over HTTPS.

How Claude Code was used, and where it was corrected: [docs/WORKING-WITH-CLAUDE.md](docs/WORKING-WITH-CLAUDE.md).
