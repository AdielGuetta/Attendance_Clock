# 🧪 Playground copy

This folder is a **practice copy** of the Attendance Clock project. Change or break anything here; the real project in
`..\tamir` isn't affected.

| | Real project (`tamir`) | This playground |
|---|---|---|
| App | http://localhost:5174 | **http://localhost:5175** |
| API | http://localhost:5080 | **http://localhost:5081** |
| Database (for tools) | localhost:1433 | **localhost:1434** |
| Containers | `attendance-db/-api/-web` | **`playground-db/-api/-web`** |
| Database data | its own volume | **its own, separate volume** (starts empty + demo users) |
| GitHub | `AdielGuetta/AttendanceClock` | **[`AdielGuetta/Attendance_Clock`](https://github.com/AdielGuetta/Attendance_Clock)** (public; the local `playground` branch is its `main`) |

## Commands (run inside this folder)
```bash
docker compose up -d                                   # start the playground
docker compose down                                    # stop it (data kept)
docker compose down -v                                 # stop it and wipe its database
docker compose run --rm tests                          # run the backend tests
git status / git diff                                  # see what you changed
git restore .                                          # undo all uncommitted changes
```

Demo logins are the same: `dana` / `Employee123!`, `yossi` / `Employee123!`, `manager` / `Manager123!`.

**To get rid of it completely:** `docker compose down -v`, then delete this folder.

> Note: the README and docs were updated for proj2; parts of CLAUDE.md still mention the real project's ports (5174/5080). In this copy, use the ports above.
