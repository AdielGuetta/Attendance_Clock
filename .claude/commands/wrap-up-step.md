---
description: Finish a build step the way this project does it (proof, docs, notes, commit), without merging
---

We finish every step the same way. Go through this checklist for the step just built, and tell me what you changed:

1. **Proof:** run `/test`; if the step is visible in the UI, walk through it in the browser at http://localhost:5174
   (demo accounts in the README) and note what you saw.
2. **Plan file** (`docs/plans/NN-*.md`): status → done ✅; fill in "Results" with what was built, test counts, the live check,
   and anything that went wrong or was corrected.
3. **PRD:** if the implementation differs from the PRD, mark the change in place ("Revised in Plan NN: …") with the reason.
4. **DECISIONS.md:** only important decisions, one short line each (edge cases go to docs/TESTS.md). **Where Claude needed
   correcting** goes to docs/WORKING-WITH-CLAUDE.md (be honest: this is what reviewers look for).
5. **docs/TESTS.md:** new edge cases → test mapping, the test list, the totals.
6. **README.md:** the project status table, any new URLs/commands. **CLAUDE.md:** the code map, new gotchas.
7. Check for garbled characters (`Ã`, `Â·`, `â€`) in the files you edited.
8. Commit on the step's branch (conventional message + Co-Authored-By). **Don't merge or push**: ask me first.
   When merging, use `git branch -f main HEAD && git switch main` (see CLAUDE.md).
