# CLAUDE.md — School Admission System

Take-home assessment: seat-safe online school admission (React + ASP.NET Core + PostgreSQL, Dockerized). Read `PROJECT_SPECIFICATION.md` (design, API, schema, roadmap) and `ASSUMPTIONS.md` before changing behaviour.

## Workflow rules
- Work **phase by phase** (roadmap in PROJECT_SPECIFICATION §10). After each phase: summarize, list files, run tests, show real output, list open issues, **stop and wait for user approval**.
- Never claim tests passed unless they ran. Paste real results.
- Small, meaningful git commits throughout; no giant final commit. Commit only when the user's workflow calls for it; end commits with the attribution line given in the session.
- Don't silently change spec requirements; record any deviation in ASSUMPTIONS.md.
- Keep AI_USAGE.md truthful and updated each phase.

## Architecture
`backend/` = Domain ← Application ← Infrastructure, API on top; `frontend/` = Vite React TS; `docker-compose.yml` at root. Controllers stay thin; business rules live in Application services; DTOs only across the API boundary.

## Non-negotiables
- Seat allocation happens only in the backend, in one PostgreSQL transaction with `SELECT … FOR UPDATE` on the class row. Never read-then-write unlocked. Never trust frontend counts.
- Availability = `total − (HELD with expires_at > now) − CONFIRMED`, computed from rows; never negative.
- Expiry is enforced by time predicates in queries **and** by the `BackgroundService`; correctness must not depend on the sweeper.
- Confirm is idempotent. Student id = exactly 17 digits (`X-Student-Id`), validated in FE, API, and DB CHECK.
- UTC everywhere; inject `TimeProvider`.
- No hardcoded credentials/API URL; config via env (`ConnectionStrings__DefaultConnection`, `HOLD_DURATION_SECONDS`, `VITE_API_BASE_URL`, Postgres vars). `.env.example` committed, `.env` ignored.
- Never use `docker compose down -v` as normal procedure; data persists in the `pgdata` volume.
- Concurrency tests must use real PostgreSQL (Testcontainers), not EF InMemory.
- No mock data standing in for the DB; no localStorage as an application database (only `studentId`).
- No TODO stubs left behind.

## Commands (filled in as they come to exist)
```
docker compose up --build            # whole stack on http://localhost:8080
dotnet build backend                 # build
dotnet test backend                  # unit + integration + concurrency (needs Docker for Testcontainers)
cd frontend && npm ci && npm run dev # dev server :5173
cd frontend && npm test              # Vitest
```

## Conventions
- C#: nullable enabled, async everywhere with `CancellationToken`, file-scoped namespaces, `ILogger` structured logging, ProblemDetails errors with stable `code`.
- TS: strict mode, feature folders under `src/features/`, API calls only through `src/api/`.
- Error messages shown to users come from the spec §10 wording; never show raw server/DB errors.
