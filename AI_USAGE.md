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
