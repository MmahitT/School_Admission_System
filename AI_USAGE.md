# AI_USAGE

Draft — kept truthful and updated at the end of every phase.

## Tools used
- Claude Code (model: Claude Sonnet 5.5) in VS Code, acting as pair-programmer/architect.

## Tasks performed with AI
| Phase | Task | Files |
|---|---|---|
| Planning | Requirement analysis, schema design, concurrency strategy, roadmap | `PROJECT_SPECIFICATION.md`, `ASSUMPTIONS.md`, `CLAUDE.md`, `AI_USAGE.md` |

(Rows are added as each phase is completed.)

## Incorrect or risky AI suggestions and how they were handled
None recorded yet. Planned-review items from the planning phase that the human reviewer should double-check:
- A single `UNIQUE (class_id, student_id)` constraint would contradict "apply again after expiry"; replaced with a partial unique index (A6).
- A plain partial unique index on `student_id WHERE status='HELD'` can be tripped by *expired-but-not-yet-swept* rows; the allocator expires the student's stale rows inside the transaction first.

## Human review and testing
To be filled in: what was manually read, run, and verified (with commands and real results) per phase.

## Phase 1 (setup)
- AI scaffolded the .NET solution, Vite app, Dockerfiles, compose and nginx config. Files: `backend/*`, `frontend/*`, `docker-compose.yml`, `.env.example`, `.gitignore`.
- Issues caught during the phase: (1) first nginx draft included a non-existent `proxy_params.inc`; replaced by inline proxy headers. (2) Vite's template produced React 19 and TypeScript 7 (spec requires React 18+, so accepted). (3) zsh glob expansion broke `--version 8.0.*` NuGet arguments; fixed by quoting.
- Verified by running: `dotnet build`, `npm run build`, `npm test`, `docker compose up --build`, and curl against `/`, `/dashboard`, `/health`, `/api/health`.

## Phase 2 (database and domain)
- AI wrote the entities, EF configurations, the `InitialCreate` migration (with hand-written SQL for the capacity trigger and the case-insensitive name index), the DB initializer and `SchemaConstraintTests`. Files: `backend/SchoolAdmission.Domain/*`, `backend/SchoolAdmission.Infrastructure/Persistence/*`, `backend/SchoolAdmission.Tests/Database/*`, `backend/SchoolAdmission.Tests/Support/*`.
- Incorrect AI output found and fixed:
  1. EF Core silently merged the plain `student_id` index with the filtered unique index on the same column, so the required `student_id` index was missing. Found by listing `pg_indexes` in the running database (not by the compiler); fixed with raw SQL in the migration plus a test (`Required_indexes_exist`).
  2. Floating package versions (`8.0.*`) produced an EF Core version-conflict warning; pinned all versions.
  3. A test expected SQLSTATE 23514 for an 18-digit student id, but `character(17)` rejects it earlier with 22001; test corrected (the database still rejects it).
- Verified by running: `dotnet test` (28 tests against real PostgreSQL via Testcontainers), `dotnet ef migrations has-pending-model-changes`, and a `psql` insert surviving `docker compose down` / `up`.

## Phase 3 (backend)
- AI wrote the Application services, repository/unit of work, controllers, exception handler and all tests: `backend/SchoolAdmission.Application/*`, `backend/SchoolAdmission.Infrastructure/Persistence/{AdmissionRepository,UnitOfWork}.cs`, `backend/SchoolAdmission.API/*`, `backend/SchoolAdmission.Tests/{Unit,Integration,Support}/*`.
- Incorrect AI output found and fixed:
  1. The seat query filtered on a constructor-built record projection; EF could not translate it and every `GET /classes/{id}` returned 500. Found by the integration tests (11 failed on the first run); fixed by filtering the entity query before projecting.
  2. Throwing inside the confirm transaction would have rolled back the "mark EXPIRED" write; the service now returns an outcome and throws after commit (A23).
  3. Default Npgsql pool settings left thin margin for 1,000 simultaneous requests; pool limits made explicit (A21).
- Verified by running: `dotnet test` (90 tests; unit tests use a fake repository, integration and concurrency tests use real PostgreSQL), the concurrency tests six times in a row, and a curl walk-through of all 8 endpoints through nginx in Docker.
- Observed but not explained: one of six repeated concurrency runs took 21 s instead of ~2 s (all passed). Not yet investigated.

## Phase 4 (frontend)
- AI wrote the whole React app and its tests: `frontend/src/{api,components,features,hooks,layouts,pages,utils,test}/*`, `App.tsx`.
- Incorrect AI output found and fixed:
  1. The test helper wrapped the element in providers, so `rerender()` dropped them and a countdown test crashed; changed to the `wrapper` option.
  2. The first countdown displayed `10:01` for a 600 s hold. Found only by driving the real UI in Chrome (not by the unit tests): the HTTP `Date` header is truncated to whole seconds, so the server-clock offset was up to 1 s off. Fixed by centring the offset and flooring the display (A24).
- Verified by running: `npm test` (65 tests), `tsc -b`, `npm run build`, and a Playwright script (system Chrome, kept outside the repo) that drove the Docker stack end to end: admin creates a class and sees validation, invalid/valid student id, apply, countdown, reload restore, simulated payment, fresh-browser restore from the database, cancel. No console errors.
- Not verified yet: real expiry in the browser (needs a short `HOLD_DURATION_SECONDS`, Phase 5), screenshots below ~500 px width (headless Chrome's minimum window), and screen-reader behaviour (only roles/labels checked via tests).

## Phase 5 (background expiration)
- AI wrote `HoldExpirationService`, its options/registration, and `ExpirationTests`.
- Incorrect AI output found and fixed: the first "confirmation races the sweeper" test was not a race. The sweeper loop started 50 ms before the confirmations, so it expired all 60 holds first and every run reported "0 confirmed, 60 lost". It would have passed forever while proving nothing. Found by printing the outcome distribution; rewritten as one simultaneous confirm-vs-sweep release per hold. Now both sides win in every run (confirm won 54–67 of 80 rounds, sweeper 13–26) and the invariant holds (200 ⇒ CONFIRMED, 409 ⇒ EXPIRED).
- Verified by running: `dotnet test` ×3 (96 passed each time), and on the real Docker stack with `HOLD_DURATION_SECONDS=20`: the browser UI flipped to EXPIRED by itself (no reload), seat freed and re-appliable; backend restart mid-hold; backend stopped while a hold expired then catch-up sweep on start (~2 s); `docker compose down`/`up` kept HELD→EXPIRED and CONFIRMED data. `.env` was restored to 600 s / 5 s afterwards.
- Not verified: behaviour with multiple backend instances (ASSUMPTIONS A11/A14 assume one).

## Phase 6 (consolidated testing)
- AI added `RepositoryAndTransactionTests`, `ApiHardeningTests`, a mid-burst observer concurrency test, and frontend `api/admission.test`, `components.test`, `Dashboard.test`; refactored `usePolledResource`, `Toast` and `useStudent` to clear lint warnings. Gaps were chosen from a coverage report, not guessed.
- Checks that the new tests can actually fail (done because they all passed first time, which proves nothing by itself):
  - Backend mutation: replaced the seat count with `0` and disabled the capacity trigger → 4 of 7 concurrency tests failed with 120 holds created for 10 seats and 1000 for 120. Source restored from git.
  - Frontend mutations: time-based expiry disabled → 3 tests failed; poll timer cleanup removed → 2 tests failed. Files restored.
- Incorrect AI output found: (1) my first mutation attempt had a syntax error and never ran (noticed from compiler errors; redone with a regex edit). (2) A header test silently skipped inputs via `catch { continue; }`, and its whitespace cases are untestable because HTTP strips leading/trailing spaces from header values; rewritten to assert all four inputs were actually sent.
- Final results (run as written): backend 113 passed (3 consecutive runs); frontend 92 passed (3 consecutive runs); `tsc -b`, `oxlint` and `vite build` clean. Coverage: frontend 94.4% statements / 96.8% lines; backend lines — Application 97.1%, API 94.2%, Domain 91.4%, Infrastructure 72.7%.
- Not covered / known limits: `DatabaseInitializer` retry loop and `DesignTimeDbContextFactory` (tooling) have no tests; the 499 client-abort path in `GlobalExceptionHandler` is untested. The first backend run in a series once took 43 s against ~22 s for the others (and an earlier run took 21 s vs ~2 s); all passed, cause not found.

## Phase 7 (Docker acceptance)
- AI wrote `scripts/acceptance.sh` and the throw-away Playwright scripts (kept outside the repo), ran the cold-clone verification, and fixed what it found.
- Findings and fixes: (1) `docker compose up --build` without a `.env` aborts (by design, to avoid a hard-coded DB password) — flagged as a decision, not silently changed. (2) The Dockerfile's `ASPNETCORE_URLS` duplicated the .NET 8 image's `HTTP_PORTS` and logged a warning at every start; removed. (3) EF logged every SQL command at Information, which together with the sweeper would flood production logs; EF set to Warning. (4) The design-time DbContext factory carried a placeholder user/password; removed so no credential-shaped string is in source.
- Verified by running (not by reasoning): see PROJECT_SPECIFICATION §12 for the table of commands and results.
- Not verified: Linux/Windows hosts (everything ran on macOS + Docker Desktop); `scripts/acceptance.sh` was only run on macOS bash 3.2/curl; build time on a slow connection; behaviour with multiple backend replicas.
