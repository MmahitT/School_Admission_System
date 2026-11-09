# AI_USAGE

This document states honestly how AI was used on this project.

## Summary

- **Tool:** Claude Code (Anthropic), model Claude Sonnet 5.5, used as a pair programmer inside VS Code.
- **Extent:** the AI wrote essentially all of the source code, tests, configuration, scripts and documentation in this repository.
  The project owner set the requirements (the assignment and master specification), controlled the process phase by phase,
  made the open design decisions (D1–D6 in `PROJECT_SPECIFICATION.md` §11, including the security decision on
  environment setup), approved each phase before the next began, and asked for each phase to be pushed to GitHub.
- **Process:** eight phases (setup, database, backend, frontend, background services, testing, Docker validation, delivery).
  After each phase the AI reported what it built, the real output of the tests it ran, and the problems it found.

## What the AI was used for

| Phase | Work done by the AI | Main files |
|---|---|---|
| Planning | Requirement analysis, schema and API design, concurrency strategy, roadmap | `PROJECT_SPECIFICATION.md`, `ASSUMPTIONS.md`, `CLAUDE.md` |
| 1 Setup | .NET solution and layered projects, Vite/React/TypeScript/Tailwind app, Dockerfiles, Compose, nginx | `backend/*`, `frontend/*`, `docker-compose.yml` |
| 2 Database | Entities, EF Core mappings, migration with hand-written SQL (capacity trigger, case-insensitive name index), schema tests | `SchoolAdmission.Domain`, `Infrastructure/Persistence/*`, `Tests/Database/SchemaConstraintTests.cs` |
| 3 Backend | Services, repository with `FOR UPDATE` locking, unit of work, controllers, exception handler, API and concurrency tests | `SchoolAdmission.Application/*`, `AdmissionRepository.cs`, `UnitOfWork.cs`, `SchoolAdmission.API/*`, `Tests/{Unit,Integration}/*` |
| 4 Frontend | Entire React app: pages, features, hooks, API layer, server-time countdown, polling; Vitest tests | `frontend/src/*` |
| 5 Background | `HoldExpirationService`, race tests against the sweeper | `Infrastructure/BackgroundServices/*`, `Tests/Integration/ExpirationTests.cs` |
| 6 Testing | Gap-filling tests chosen from coverage reports, mutation checks, lint fixes | `RepositoryAndTransactionTests.cs`, `ApiHardeningTests.cs`, frontend `*.test.*` |
| 7 Docker | Black-box acceptance script, clean-clone verification, log and hygiene fixes | `scripts/acceptance.sh` |
| 8 Delivery and security | Fail-fast configuration, startup validator, security headers, secret scanner, final documentation | `EnvironmentValidator.cs`, `scripts/security-check.sh`, `nginx.conf`, `README.md` |

## Incorrect or risky AI output, and how it was found and fixed

The AI made the following mistakes. Several were found only because the work was run for real, not because the code
looked right.

| # | Phase | Mistake | How it was found | Fix |
|---|---|---|---|---|
| 1 | Planning | A single `UNIQUE (class_id, student_id)` would contradict "apply again after expiry" | Review of the spec's own wording | Partial unique index that excludes EXPIRED rows |
| 2 | Planning | A plain partial unique index on `student_id WHERE status='HELD'` is tripped by expired-but-unswept rows | Reasoning about the race | The allocator expires the student's stale holds inside the transaction first |
| 3 | 1 | nginx config included a file (`proxy_params.inc`) that did not exist | Reading its own output before running | Inline proxy headers |
| 4 | 1 | zsh expanded `8.0.*` in NuGet commands | The commands failed | Quoted version strings, then pinned exact versions |
| 5 | 2 | EF Core silently merged the plain `student_id` index into the unique one, so a required index did not exist | Listing `pg_indexes` in the running database | Created in migration SQL; test `Required_indexes_exist` |
| 6 | 2 | Floating package versions caused an EF version conflict | Build warning | Pinned versions |
| 7 | 2 | A test expected SQLSTATE 23514 for an 18-digit id, but the `character(17)` column rejects it first (22001) | Test failure | Test accepts either rejection |
| 8 | 3 | A seat query filtered on a constructor-built projection; EF could not translate it and every `GET /classes/{id}` returned 500 | 11 integration tests failed on the first run | Filter before projecting |
| 9 | 3 | Throwing inside the confirm transaction would roll back the "mark EXPIRED" write | Reasoning during design review of the outcome handling | The service returns an outcome and throws after commit |
| 10 | 3 | Default connection-pool settings left thin margin for 1,000 simultaneous requests | Timing the 1,000-request test | Explicit pool limits |
| 11 | 4 | A test helper dropped providers on `rerender`, crashing a countdown test | Test failure | `wrapper` option |
| 12 | 4 | The countdown showed `10:01` for a 600 s hold (HTTP `Date` header is truncated to whole seconds) | Driving the real UI in Chrome; unit tests did not catch it | Midpoint correction and floored display |
| 13 | 5 | The "confirmation races the sweeper" test was not a race: the sweeper expired every hold first, so it always reported "0 confirmed, 60 lost" and could never fail | Printing the outcome distribution | One simultaneous confirm-versus-sweep release per hold; both outcomes now occur |
| 14 | 6 | The first mutation attempt had a syntax error and never ran | Compiler errors | Redone correctly |
| 15 | 6 | A header test silently skipped inputs (`catch { continue; }`); whitespace cases are untestable because HTTP strips them | Review after it passed on the first run | Asserts all inputs were sent |
| 16 | 7 | `docker compose up --build` aborts without a `.env` | Running it exactly as an evaluator would | Reported as a decision; resolved by the owner (fail fast) |
| 17 | 7 | `ASPNETCORE_URLS` duplicated the .NET 8 image's port setting and logged a warning on every start; EF logged every SQL command at Information | Reading container logs | Removed the variable; EF set to Warning |
| 18 | 7 | The design-time DB factory carried a placeholder username/password | Secret scan of tracked files | Removed |
| 19 | 8 | **The new secret scanner silently passed while scanning nothing**: a pattern starting with `-----BEGIN` was parsed as command-line options and the scans errored | A planted-secret test (the scanner was not trusted until it found a planted secret) | Patterns passed with `-e`; scanner errors now fail the run; a canary self-test runs first |
| 20 | 8 | The history scan was case-sensitive and missed `POSTGRES_PASSWORD=…` while the tree scan found it | The same planted-secret test (secret committed, then deleted, so it exists only in history) | Case-insensitive; all three planted secrets are now found |
| 21 | 8 | Two false failures in the new runtime checks: a loose pattern matched axios's `encodeBasicAuthUsername`, and `docker compose port` prints "invalid IP:0" for unpublished ports | Reading the failures | Tighter pattern; check `docker compose ps` for a host mapping |
| 22 | 8 | The documented dev setup used port 5000, which macOS reserves for AirPlay, so it would fail on any Mac | Following the README's own instructions | Moved to 5080 |

## What was verified, and how

Verification means commands that were run, with their output read, not reasoning about the code.

- **Automated:** 136 backend tests (real PostgreSQL via Testcontainers) and 92 frontend tests, repeated several times
  to detect flakiness; type-check, lint and production build clean. Test sensitivity was checked by **mutation**:
  with the seat-count query and the capacity trigger disabled, the concurrency tests fail (120 holds created for
  10 seats); with time-based expiry disabled or the poll timer left running, the matching frontend tests fail.
- **Black-box:** `scripts/acceptance.sh` against stacks built from fresh `git clone`s, including 120 simultaneous
  requests against 10 seats and a real expiry run.
- **Browser:** a Playwright script drove the real UI in Chrome: admin and parent flows, reload persistence, simulated
  payment, expiry with no reload, and six separate browsers racing for one seat.
- **Restart and persistence:** backend restarts, a backend that was down while a hold expired, and
  `docker compose down` / `up --build` without `-v`.
- **Security:** `scripts/security-check.sh` (after proving it detects planted secrets), plus failure-mode checks of
  Compose and the backend when configuration is missing, empty or malformed.
- Full commands and results: `PROJECT_SPECIFICATION.md` §13 and the README.

## Limits of this verification (what the AI could not establish)

- Everything was run on **macOS with Docker Desktop**. Linux and Windows hosts were not tested.
- The AI's browser tests exercise behaviour; they do not replace a human judging the visual design, copy, or usability.
- Screen-reader behaviour was checked only through roles and labels in tests.
- Multiple backend replicas, TLS, and production-scale load were not tested.
- Some timings (one test run that took 43 s against ~22 s for the others, and an earlier 21 s versus 2 s) were not
  explained; every run passed.

## Human review

This file cannot truthfully claim that a person read every line of code, because the AI did not observe that.
What is on record is the owner's direction and approvals: they wrote the specification, resolved decisions D1–D6,
approved each phase after reading its report, requested the security hardening, and requested the pushes.
The project owner should add here anything they personally reviewed, ran, or changed beyond that.
