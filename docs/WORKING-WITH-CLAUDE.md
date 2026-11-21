# Working with Claude Code

How this project was built with Claude Code: the process, where the tool helped, and where it had to be corrected.
The artefacts are all in the repo: [CLAUDE.md](../CLAUDE.md), [.claude/](../.claude/), the step plans in [docs/plans/](plans/),
and the commit history.

---

## The process
Every step followed the same loop:

```
PRD (what & why) → plan file for the step → I review/ask questions → approve
      → Claude builds on a branch → automated tests → live check (API calls / browser walkthrough)
      → docs updated (plan results, DECISIONS, TESTS, README, CLAUDE.md) → I approve → merge → push
```

- **One step at a time, nothing without approval.** CLAUDE.md tells Claude to explain each step in plain words, ask before
  installs, system changes, merges and pushes, and show proof after every step.
- **Plans before code:** `docs/plans/04…11` were written *before* each step and filled in with results *after*
  (01–03 are a retrospective). Several plans changed after my questions; for example, Plan 07 dropped the Idempotency-Key.
- **CLAUDE.md as the project's memory:** the one hard rule (external time only), "Docker only", the code map, and every
  gotcha we hit, so later sessions don't repeat mistakes.
- **Project commands:** `.claude/commands/test.md` (`/test`) and `wrap-up-step.md` (`/wrap-up-step`) capture the routine.
- **Branch per step, fast-forward merges, small commits** with messages that explain *why*.

## Where Claude Code helped most
- **Turning a vague brief into a spec:** the first PRD draft had 16 edge cases, a data model and an API (a 17th edge case, the shift-start rush, came from one of my questions).
- **Measuring instead of guessing:** it tested 6 time APIs (10 calls each) and found the famous one (worldtimeapi.org) down.
- **Depth on the hard parts:** DST handling (23/25-hour days, the ambiguous 02:30), database-level race protection,
  fail-closed time logic, and tests that simulate outages and DST nights with fakes.
- **Diagnosing environment problems:** Smart App Control, a port clash with another local project, and the merge incident
  (read from container logs).
- **Verification:** besides the tests, it drove the real app in a browser (stale tab, second device, phone size, manager flow).
- **Phase 2 (the extensions):** once the core was finished and verified, the same loop for four more features, built
  in a separate copy so the finished version stayed safe: a plan for my approval (plans 12–15, which I changed where I
  disagreed), a branch per feature, tests + a browser walkthrough, and a merge only on my OK. It also reviewed edge
  cases I listed against the actual code, and I chose which to fix (DECISIONS, "Not done, by choice").

## Where it had to be corrected (by me, or by evidence)
| What happened | How it was caught | Fix |
|---|---|---|
| After switching to Docker, the PRD still said "no Docker" | I noticed the contradiction | PRD updated with the full story (D-12) |
| `dotnet add package` pulled EF Core 11, which doesn't support .NET 10 | Build error | Versions pinned to 10.0.x; rule in CLAUDE.md |
| sqlcmd inserts failed on the filtered index | Error message | `-I` flag; rule in CLAUDE.md |
| A PowerShell 5.1 edit garbled `·` and `→` in a plan file | Claude noticed in the file diff | Fixed; "don't edit via Get-Content/Set-Content" in CLAUDE.md |
| History totals rounded each shift down, so minutes were lost | Live check of real data | First fix (exact total) was correct but **the rows no longer added up**. I pushed back from the user's side; we chose nearest-minute rounding (DECISIONS #7) |
| A stale-tab message said "Refresh to see your status" although the screen refreshes itself | Browser walkthrough | Reworded |
| **Merge incident:** switching to `main` before merging briefly deleted `frontend/`, so Vite restarted with default settings and the app went down | App unreachable; container log | Restarted; new merge procedure in CLAUDE.md. Then **I asked for a root fix**: fail fast instead of silent defaults, re-tested by re-creating the incident |
| "Do we auto-stop shifts after 14 h?": the system only flagged them, deep in the manager screen | My question | Kept "never invent a time", but added an employee banner + a manager badge (Plan 11) |
| The README test command could collide with the running server's build | Final review | A separate on-demand `tests` container |
| **Phase 2** · A phone-only CSS rule hid "column 5"; the new location column took that place, so phones hid the location instead of the flags | Claude's own browser check at phone width | The column gets a class instead of a position |
| **Phase 2** · The last work location was remembered per device: on a shared computer, the next person got someone else's choice | Browser check (the manager saw dana's "Remote") | Remembered per user |
| **Phase 2** · Claude kept writing my private notes into the **phase-1** folder, because its hand-over said so | I clarified: that folder stays completely untouched | Notes copied into the new copy (kept out of git), the old file restored, CLAUDE.md changed |
| **Phase 2** · The date-time box showed "05:00 PM" while the app shows "17:00", and later the focus ring overlapped the clock button | I noticed both | 24-hour time box with a roller; box and button made one unit |
| **Phase 2** · A fix request could move a shift to another day without the manager noticing | My question | New length, moved day and "Over 14 h" shown to both sides (DECISIONS #12) |
| **Phase 2** · I asked "can anyone with the link see it?" | Claude checked the Docker ports | They were open to the whole Wi-Fi (database included). Limiting them to this computer was tried and worked; I chose to leave the setup as it is (a local development setup) |
| **Phase 2** · "Check *active* on every request" was built as planned, then I cancelled it | My decision after seeing it | Undone as its own commit, so the history shows both |
| **Phase 2** · Is the new race test meaningful, or would it pass anyway? | Claude switched the lock off and ran it | It failed (both corrections went through), so it really detects the race (DECISIONS #18) |

## What I'd do differently
- Write plan files from step 1 (01–03 are retrospective).
- Add the fail-fast container settings and the separate tests container from the start; both came from incidents.
- Add end-to-end UI tests (e.g. Playwright); the UI was verified by hand plus unit tests.
