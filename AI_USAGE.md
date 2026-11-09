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
