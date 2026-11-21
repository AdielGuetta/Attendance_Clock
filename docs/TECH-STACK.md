# Tech stack: pros, cons and user impact

For each component: **why it's here**, its **technical pros and cons**, and **what it means for the people using the
system**: employees punching the clock, managers reviewing shifts, and (for tooling) reviewers and developers.

📄 **A visual version with diagrams is in [TECH-STACK.pdf](TECH-STACK.pdf).**

Components marked **(required)** were fixed by the assignment. Their pros and cons are still listed, because they shape the design.

---

## At a glance

| Component | Role | Biggest pro | Biggest con | User impact in one line |
|---|---|---|---|---|
| ASP.NET Core (.NET 10) **(required)** | Backend API | Fast, mature, built-in security pieces | Verbose for tiny apps | Quick, reliable responses to every punch |
| SQL Server 2022 **(required)** | Database | Enforces data rules itself | Heavy; no ARM image | No duplicate or impossible shifts, ever |
| EF Core 10 | Database access | Typed queries + migrations | Hides the SQL; easy to misuse | Indirect: fewer bugs, safer schema changes |
| External time APIs (timeapi.io, time.now) **(required)** | Source of truth for punch times | Times can't be faked locally | Network dependency; free = no SLA | ~0.1–1.3 s extra per punch; rare "try again" |
| Trusted clock + fail-closed | Wraps the time APIs | One safe path for "now" | Refuses punches when both APIs are down | Correct times, occasionally a retry message |
| JWT login | Who's calling, which role | Stateless, simple with React | Can't revoke a token early | Log in once, then work for an hour |
| Password hashing + rate limit | Account security | Stolen DB ≠ stolen passwords; no guessing | Lockout can hit a user who mistypes | After 5 typos: wait a minute |
| React + Vite + TypeScript **(required)** | UI | Instant, app-like screens | Needs JavaScript; more moving parts | One big Clock In/Out button, fast feedback |
| Docker Compose | Local environment | One command, any OS | Extra layer; slower on Apple-chip Macs | None for end users; easy for reviewers |
| xUnit + in-memory API tests | Automated tests | Catches regressions in seconds | Takes time to write | Indirect: features keep working |

> **proj2 (practice copy): no new components.** Work location, correction requests, reports and the safety fixes are all
> built with the stack above, with no new libraries or services. What they newly *use* from it:
> **SQL Server** transactions and a row lock (`UPDLOCK`: one change per employee at a time) and a filtered unique index
> (one waiting request per shift); **EF Core** migrations for the new column and table; **C#** to build the CSV file
> (no CSV library: the quoting and the formula guard are a few lines, fully tested); and the **browser's** download of a
> fetched file ("blob"), so the login token can travel with it. The PDF version below still shows the submitted project.

---

## Backend: ASP.NET Core on .NET 10 (LTS) **(required)**
**Pros**
- High performance, mature, and well documented.
- Built-in pieces we use instead of writing our own: dependency injection, JWT authentication, the rate limiter, health checks, ProblemDetails errors.
- .NET 10 is **LTS** (supported for 3 years), so the project won't be on an unsupported version soon.
- Cross-platform: runs on Windows, macOS and Linux.

**Cons**
- More ceremony than minimal frameworks for a small app (controllers, options classes, DI registration).
- Built output is unsigned locally, which is why Windows Smart App Control blocked it on the dev machine (solved with Docker).
- The newest EF Core (11) needs .NET 11, so versions must be pinned to 10.0.x.

**User impact**
- Responses are fast, so the user's wait on a punch is dominated by the external time API, not by our server.
- Consistent error messages (ProblemDetails with a `code`) let the UI show precise, friendly explanations, e.g. "You're already on shift" instead of "Error 409".

## Database: SQL Server 2022 **(required)**
**Pros**
- **Enforces our rules itself:** a filtered unique index (one open shift per employee), a check constraint (Clock Out after Clock In), and `rowversion` for concurrent edits. These hold even if application code has a bug or two requests race.
- A native `datetimeoffset` type stores the instant plus the Zurich offset in one column, which keeps DST handling simple.
- Strong transactions: a punch is saved completely or not at all.

**Concurrency (many users at once)**
- Different employees punching at the same moment touch different rows, so they don't block each other. Hundreds of punches
  per second are well within SQL Server's capacity.
- Same employee twice at once (double-click, two tabs): the second insert waits briefly for the first, then hits the
  "one open shift" unique index and is refused. Exactly one shift is created.
- Two people editing the same shift: `rowversion` detects that the row changed since it was loaded, so the second save
  fails with "please refresh" instead of silently overwriting (optimistic concurrency).

**Cons**
- Heavy: the image is about 2 GB and needs about 2 GB of RAM.
- No ARM image, so Apple-chip Macs need emulation.
- The free Developer/Express editions have limits (Express: 10 GB per database). Fine here, but a real deployment needs licensing decisions.
- Some tools need special settings (sqlcmd needs `-I` for filtered indexes).

**User impact**
- **Employees never see a "ghost" double shift**, even after double-clicking or punching from two tabs: the second attempt is cleanly refused.
- Managers can trust the numbers: impossible shifts (out before in) can't exist.
- Data survives restarts and crashes.

## Database access: EF Core 10
**Pros**
- C# classes ↔ tables, with queries checked by the compiler.
- **Migrations** version the schema; the app upgrades the database on startup in development, so a reviewer's clone just works.
- Rules (lengths, indexes, constraints) are declared in one place, under `Data/Configurations`.

**Cons**
- It hides the SQL, so inefficient queries are easy to write by accident and need attention as data grows.
- Startup auto-migration is convenient in development but not appropriate for production. Production should run migrations as a deliberate deployment step.

**User impact**
- Indirect: fewer data bugs, and schema changes that don't break existing data.

## External time APIs: timeapi.io (primary) + time.now (backup) **(required)**
**Pros**
- **Punch times can't be manipulated** by changing a PC's clock or by a misconfigured server. That's the whole point of the assignment.
- Two independent providers: one going down doesn't stop the business.
- Keyless: reviewers don't need to sign up for anything.
- Chosen by measurement ([plan 04](plans/04-time-providers.md)), not by reputation.

**Cons**
- **Every punch depends on the internet** and on a third party.
- Free services have **no uptime guarantee (SLA)**. The backup failed ~20–30% of calls in our test.
- Adds latency: typically ~0.1–0.3 s, sometimes ~1.3 s.
- Providers could change their response format; our validation would then reject answers, safely but disruptively.
- **One call per punch.** At a shift-start rush (say 200 punches at 08:00), a free provider may rate-limit us. See PRD EC-17
  for mitigation options; the main one is sharing a single in-flight request among punches in the same second.

**User impact**
- A punch takes a moment longer than a purely local system. The UI will show a short "Recording…" state.
- At a busy shift start, punches may be slower, and in the worst case some employees get "please try again".
- The time shown as confirmation is the **official recorded time**, so employees and managers see the same trustworthy number.

## Trusted clock + fail-closed policy
**Pros**
- **One path** for "now" (`ITrustedClock`), so no code can accidentally use the local clock.
- Every answer is validated (zone, possible offset, Zurich DST rules, internal consistency).
- 2.5 s per provider, so a slow primary falls through quickly.
- Fully testable with fakes (outages, DST nights, lying providers).

**Cons**
- **When both providers are down, punches are refused.** Availability is traded for correctness.
- Worst case, a user waits ~5 s (two timeouts) before seeing the error.

**User impact**
- Usually invisible.
- In a rare double outage: a clear message, "Couldn't get the official time. Nothing was recorded. Please try again." It never silently records a wrong time. A missed punch can be fixed later by a manager, with an audited reason.

## Login: JWT tokens (60 minutes)
**Pros**
- Stateless: the server doesn't store sessions, which suits a separate React app.
- The role travels in the token, so manager-only endpoints are a single attribute (`[Authorize(Roles = "Manager")]`).
- Tampering is detected (the signature check fails).

**Cons**
- **A token can't be revoked early.** A deactivated user's token stays valid until it expires; the 60-minute lifetime limits this.
- No refresh tokens yet.
- The token must be stored in the browser, so the UI must protect it from XSS.

**User impact**
- Log in once and work uninterrupted for an hour.
- **After an hour, the user is asked to log in again**, mildly annoying for long sessions (refresh tokens would fix this later).
- Logging out is instant: the browser discards the token.

## Password hashing (PBKDF2) + brute-force limit
**Pros**
- Passwords are stored as salted hashes, so a leaked database doesn't reveal passwords.
- The same error and the same timing for "unknown user" and "wrong password", so nothing leaks about who has an account.
- 5 attempts per minute per IP stops password guessing.

**Cons**
- Hashing is intentionally slow (a few ms per login); negligible at this scale.
- The rate limit is **in memory and per IP**: it resets on restart, isn't shared across servers, and people behind one office IP share the limit.

**User impact**
- An employee who **mistypes 5 times must wait a minute.** The message says so clearly ("Too many login attempts. Please wait a minute").
- Error messages stay deliberately vague ("Invalid username or password"). That's slightly less helpful, but safer.

## Frontend: React + Vite + TypeScript **(required)**
**Pros**
- App-like: instant screen updates without page reloads.
- TypeScript catches mistakes before they reach users.
- Vite gives a fast dev server.
- A huge ecosystem.

**Cons**
- Requires JavaScript in the browser.
- A separate app to build and run.
- More moving parts (routing, state, API calls) than a server-rendered page.
- Vite's build tool uses an unsigned native binary, so it may also need to run in Docker on the dev machine.

**User impact**
- One large, context-aware **Clock In / Clock Out** button that works well on phones.
- Immediate feedback: "Recording…", then the official recorded time.
- All times shown in Zurich time with a CET/CEST label, whatever the device's own zone.

## Local environment: Docker Compose
**Pros**
- **One command** (`docker compose up -d`) starts the database and the API on Windows, macOS or Linux.
- No local SQL Server or .NET install needed.
- It got around Smart App Control without changing any security settings.

**Cons**
- One more concept to learn.
- About 3.5 GB of images on first download.
- Code reload is a bit slower than native (file polling).
- Slower on Apple-chip Macs (SQL Server emulation).

**User impact**
- **None for end users**: it's a development tool.
- **Big for reviewers and developers**: a clone-to-running experience in minutes, with no setup guesswork.

## Testing: xUnit, fake HTTP, in-memory API + test database
**Pros**
- Outages, DST nights and attacks are simulated in seconds, with no real network.
- Login tests run the **real app** against a real SQL Server, in a separate database that's reset every run.

**Cons**
- Integration tests need the database container running.
- Writing tests takes time, which on a tight deadline means fewer features.

**User impact**
- Indirect but real: core guarantees (correct time, no double shifts, secure login) are checked automatically on every change, so they don't silently break.
