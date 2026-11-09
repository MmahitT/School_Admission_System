# School Admission System

An online admission portal for a private school. Each class/shift has a fixed number of seats, allocated
**first come, first served**. A parent applies, a seat is **held** for a limited time, the parent pays (simulated),
and the hold becomes a **confirmed** admission. If the hold expires, the seat returns to the pool.

The hard requirement is that seats are **never oversold**, even when thousands of parents apply at the same
moment. The database is the source of truth: every allocation is a PostgreSQL transaction that locks the class
row, and database constraints back it up.

> **Security first:** the application will not start until you provide a database password. This is deliberate.
> Setup takes one extra command; see [Quick start](#quick-start).

---

## Contents

1. [Technology stack](#technology-stack)
2. [Quick start](#quick-start)
3. [Configuration](#configuration)
4. [Using the application](#using-the-application)
5. [API documentation](#api-documentation)
6. [How seat safety works](#how-seat-safety-works)
7. [Project structure](#project-structure)
8. [Testing](#testing)
9. [Running without Docker (development)](#running-without-docker-development)
10. [Data and operations](#data-and-operations)
11. [Troubleshooting](#troubleshooting)
12. [Security](#security)
13. [Other documents](#other-documents)

---

## Technology stack

| Layer | Technology |
|---|---|
| Backend | C#, ASP.NET Core Web API on **.NET 8**, Entity Framework Core 8 (Npgsql), dependency injection, `BackgroundService` |
| Database | **PostgreSQL 16**: EF Core migrations, foreign keys, CHECK constraints, partial unique indexes, row locking, a capacity trigger |
| Frontend | React 19, TypeScript, Vite, React Router, Axios, Tailwind CSS 4, Lucide icons, React Hook Form |
| Tests | xUnit + FluentAssertions + Testcontainers (real PostgreSQL), Vitest + React Testing Library |
| Deployment | Docker, Docker Compose, Nginx (serves the app and reverse-proxies the API), named volume for PostgreSQL |

## Quick start

**Prerequisite:** [Docker](https://docs.docker.com/get-docker/) with Docker Compose v2. Nothing else is needed:
no .NET, Node.js or PostgreSQL on your machine.

```bash
# 1. Create your private configuration file (it is git-ignored and never committed)
cp .env.example .env

# 2. Set a database password in .env (REQUIRED, deliberately left empty in the example).
#    Letters and digits only. This one-liner generates a strong one and writes it for you:
sed -i.bak "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 24)/" .env && rm .env.bak
#    (or open .env in an editor and fill in POSTGRES_PASSWORD yourself)

# 3. Build and start everything
docker compose up --build
```

Then open **http://localhost:8080**.

The first build takes a few minutes (it downloads base images and compiles the backend and frontend). Startup
order is automatic: PostgreSQL becomes healthy, the backend applies the database migrations and becomes
healthy, then the web server starts.

If you run `docker compose up --build` **without** step 2 you will see:

```
required variable POSTGRES_PASSWORD is missing a value: POSTGRES_PASSWORD is required. Run 'cp .env.example .env' and set it ...
```

That is the intended fail-fast behaviour, not a bug: the password is never hard-coded or defaulted.

To stop the stack: `docker compose down`. **Your data is kept** (see [Data and operations](#data-and-operations)).

### Where things are

| What | URL |
|---|---|
| The application (parent portal and admin) | http://localhost:8080 |
| API through the proxy | http://localhost:8080/api/classes |
| API on the exact paths of the assignment | http://localhost:8080/classes, `/holds/{id}`, `/me/holds` |
| Health check | http://localhost:8080/health |

Only port 8080 is published to your machine. The backend and PostgreSQL are reachable only inside the Docker
network.

## Configuration

All configuration is through environment variables, read by Docker Compose from `.env`.

| Variable | Required | Default | Purpose |
|---|---|---|---|
| `POSTGRES_DB` | **yes** | `school_admission` (in `.env.example`) | Database name |
| `POSTGRES_USER` | **yes** | `school_admin` (in `.env.example`) | Database user |
| `POSTGRES_PASSWORD` | **yes** | *none: empty in `.env.example`* | Database password. Choose your own; letters and digits only (it is placed in a connection string). Generate with `openssl rand -hex 24` |
| `HOLD_DURATION_SECONDS` | no | `600` | How long a seat stays reserved before the hold expires |
| `EXPIRATION_SWEEP_INTERVAL_SECONDS` | no | `5` | How often the background service marks expired holds as EXPIRED |
| `CORS_ALLOWED_ORIGINS` | no | `http://localhost:5173` | Comma-separated browser origins allowed by CORS (only matters for the Vite dev server; the Docker setup is same-origin) |
| `VITE_API_BASE_URL` | no | `/api` | API base URL compiled into the frontend. **Public by design**; only `VITE_*` values can reach the browser |
| `APP_PORT` | no | `8080` | Host port for the web server |
| `ConnectionStrings__DefaultConnection` | n/a in Docker | built by Compose from `POSTGRES_*` | Backend connection string. Set it yourself only when running the backend outside Docker |

The backend **validates its configuration before it starts** and exits with a clear message that names what is
wrong (it never prints a value, so a password cannot leak through an error).

Compose and the backend both refuse to start when a required value is missing or empty.

## Using the application

### Sample student ID

Any **17-digit** number is a valid student birth registration number. For example:

```
20152691234567890
```

Use a different number for each student you want to simulate.

### Admin (create the classes first)

1. Open http://localhost:8080 and choose **Admin** in the top navigation (there is no login; see [Security](#security)).
2. In **Create a class**, enter a name such as `Class 6 - Morning Shift` and the total number of seats, then press **Create class**.
3. The table below shows, per class, total / held / confirmed / available seats and a utilization bar. It refreshes
   automatically every 5 seconds (or press **Refresh**).

Each class/shift combination is its own class with its own seat count, for example `Class 6 - Morning Shift`
and `Class 6 - Day Shift`.

### Parent / student

1. On the home page enter the 17-digit student number and press **Continue**. It is remembered in your browser,
   so a refresh or a later visit goes straight back to your dashboard. **Change** (top right) forgets it.
2. The dashboard shows each class with its seats and one of three states: **Available**, **Limited seats**, or
   **Fully booked** (Apply is disabled).
3. Press **Apply** and confirm. A seat is reserved for you and a **countdown** starts. The countdown is based on
   the server's expiry time, not just a browser timer.
4. In **My applications** press **Pay Admission Fee**. A dialog explains that this is a **simulated payment**;
   confirm it and your admission becomes **CONFIRMED**.
5. If the countdown reaches zero first, the application shows **EXPIRED**, the payment button is disabled, and
   the seat becomes available again.
6. You can **Cancel reservation** while it is held. A confirmed admission cannot be cancelled.

Application statuses:

| Status | Meaning |
|---|---|
| `HELD` | A seat is reserved for you until the expiry time |
| `CONFIRMED` | Admission confirmed (payment simulated) |
| `EXPIRED` | The reservation time ran out; the seat was released |
| `CANCELLED` | You cancelled the reservation |

Rules a student is held to: only **one active reservation at a time** across all classes, and only **one
application per class**, except that an expired application may be repeated (see
[ASSUMPTIONS.md](ASSUMPTIONS.md), A6).

The page updates by itself: seat numbers and applications are refreshed every 5 seconds while the tab is
visible, and countdowns tick every second.

### How the simulated payment works

There is **no payment gateway**, no card form and no money involved. **Pay Admission Fee** opens a dialog stating
that the payment is simulated; confirming it simply calls `POST /holds/{holdId}/confirm`, which always
"succeeds" as long as the reservation is still valid (held, not expired, owned by the student). Confirmation is
idempotent: pressing it twice, or sending the request repeatedly, confirms once and never double-counts a seat.

## API documentation

Base URL: `http://localhost:8080` (also available under `/api`, e.g. `http://localhost:8080/api/classes`).
All bodies are JSON; all timestamps are ISO-8601 **UTC** (`…Z`). IDs are UUIDs. Student-specific requests need the
header `X-Student-Id: <17 digits>`.

| # | Method and path | Header | Success | Typical errors |
|---|---|---|---|---|
| 1 | `POST /classes` | | `201` created class | `400` invalid input, `409` duplicate name |
| 2 | `GET /classes` | | `200` list with seat counts | |
| 3 | `GET /classes/{classId}` | | `200` one class with seat counts | `400` bad id, `404` |
| 4 | `POST /classes/{classId}/holds` | `X-Student-Id` | `201` the new hold | `400` bad/missing student id, `404` class, `409` no seats / active hold exists / already applied |
| 5 | `GET /holds/{holdId}` | | `200` hold (reports `EXPIRED` once past its expiry) | `404` |
| 6 | `POST /holds/{holdId}/confirm` | `X-Student-Id` | `200` `{holdId, status: "CONFIRMED"}` (idempotent) | `400`, `403` not owner, `404`, `409` expired or cancelled |
| 7 | `DELETE /holds/{holdId}` | `X-Student-Id` | `204` | `400`, `403`, `404`, `409` confirmed or expired |
| 8 | `GET /me/holds` | `X-Student-Id` | `200` your applications (`[]` if none) | `400` |

Also: `GET /health` returns `{"status":"ok"}`.

### Examples

```bash
# 1. Create a class
curl -s -X POST http://localhost:8080/classes -H 'Content-Type: application/json' \
  -d '{"name":"Class 6 - Morning Shift","totalSeats":120}'
# 201 {"id":"8f1c…","name":"Class 6 - Morning Shift","totalSeats":120}

# 2 / 3. List classes, or get one (live seat information)
curl -s http://localhost:8080/classes
# 200 [{"id":"8f1c…","name":"Class 6 - Morning Shift","totalSeats":120,
#       "heldSeats":15,"confirmedSeats":80,"availableSeats":25}]

# 4. Reserve a seat (use the id from step 1)
curl -s -X POST http://localhost:8080/classes/8f1c…/holds -H 'X-Student-Id: 20152691234567890'
# 201 {"holdId":"a94f…","classId":"8f1c…","studentId":"20152691234567890",
#      "status":"HELD","expiresAt":"2026-11-01T04:10:00Z"}

# 5. Read a hold
curl -s http://localhost:8080/holds/a94f…
# 200 {"holdId":"a94f…","classId":"8f1c…","status":"HELD","expiresAt":"2026-11-01T04:10:00Z"}

# 6. Confirm (simulated payment). Repeating it is safe.
curl -s -X POST http://localhost:8080/holds/a94f…/confirm -H 'X-Student-Id: 20152691234567890'
# 200 {"holdId":"a94f…","status":"CONFIRMED"}

# 7. Cancel a held reservation
curl -s -X DELETE http://localhost:8080/holds/a94f… -H 'X-Student-Id: 20152691234567890'
# 204 (no body)

# 8. My applications
curl -s http://localhost:8080/me/holds -H 'X-Student-Id: 20152691234567890'
# 200 [{"holdId":"a94f…","classId":"8f1c…","className":"Class 6 - Morning Shift",
#       "status":"HELD","expiresAt":"2026-11-01T04:10:00Z"}]
```

Seat figures: `heldSeats` counts holds that are `HELD` and not past their expiry, `confirmedSeats` counts
`CONFIRMED`, and `availableSeats = totalSeats − heldSeats − confirmedSeats` (never negative).

### Errors

Every error uses one JSON shape (RFC 7807 problem details plus a stable `code`):

```json
{ "title": "Conflict", "status": 409,
  "detail": "No seats are currently available for this class.",
  "code": "NO_SEATS_AVAILABLE", "traceId": "0HN…" }
```

`detail` is safe to show to end users. Stack traces, SQL and internal messages are never returned; an unexpected
failure produces a generic `500` ("Something went wrong. Please try again.") with only a `traceId`.

| `code` | HTTP | Meaning |
|---|---|---|
| `INVALID_STUDENT_ID` | 400 | Missing header, or not exactly 17 digits |
| `VALIDATION_FAILED` | 400 | Invalid body, malformed JSON, or a malformed id in the path |
| `CLASS_NOT_FOUND` / `HOLD_NOT_FOUND` | 404 | No such class or reservation |
| `NOT_HOLD_OWNER` | 403 | The reservation belongs to a different student |
| `NO_SEATS_AVAILABLE` | 409 | The class is full |
| `ACTIVE_HOLD_EXISTS` | 409 | The student already has an active reservation |
| `ALREADY_APPLIED` | 409 | The student already applied to this class |
| `HOLD_EXPIRED` | 409 | The reservation expired |
| `HOLD_CANCELLED` | 409 | The reservation was cancelled |
| `HOLD_CONFIRMED` | 409 | A confirmed admission cannot be cancelled |
| `DUPLICATE_CLASS_NAME` | 409 | A class with that name already exists (case-insensitive) |
| `INTERNAL_ERROR` | 500 | Unexpected failure |

## How seat safety works

When a student applies, the backend runs **one PostgreSQL transaction**:

1. `SELECT … FROM admission_classes WHERE id = … FOR UPDATE` locks the class row. Applicants for the same class
   queue here; different classes never block each other.
2. Time-expired holds for that class (and that student) are marked EXPIRED first.
3. It checks the student's rules (one active hold; one application per class).
4. It counts `CONFIRMED` + unexpired `HELD` holds. If `totalSeats − count ≤ 0` the request gets `409`.
5. Otherwise it inserts the `HELD` row with `expiresAt = now + HOLD_DURATION_SECONDS` and commits.

Because the lock serializes allocators per class, the count in step 4 is always exact. Independent backstops in
the database itself reject a rule violation even if application code were wrong: a **partial unique index**
(one `HELD` hold per student), a **partial unique index** (one application per student per class), `CHECK`
constraints (seats > 0, 17-digit student id, valid status, timestamp consistency), a foreign key, and a
**trigger** that refuses an insert beyond a class's capacity.

**Expiry never depends on a timer:** every read and write treats a `HELD` hold past its `expiresAt` as expired.
A background service (`HoldExpirationService`) additionally persists `EXPIRED` every few seconds, and once at
startup to catch up after downtime. **Confirm** locks the hold row, so confirming and expiring the same hold can
only ever end one way.

Details: [PROJECT_SPECIFICATION.md](PROJECT_SPECIFICATION.md) §4–§7.

## Project structure

```
school-admission-system/
├── backend/
│   ├── SchoolAdmission.Domain/          Entities (AdmissionClass, AdmissionHold), HoldStatus, student-id rule
│   ├── SchoolAdmission.Application/     DTOs, services (ClassService, HoldService), abstractions, error types
│   ├── SchoolAdmission.Infrastructure/  EF Core DbContext + migrations, repository (FOR UPDATE), unit of work,
│   │                                    expiration BackgroundService, startup configuration validator
│   ├── SchoolAdmission.API/             Thin controllers, global exception handler, CORS, Program.cs
│   ├── SchoolAdmission.Tests/           Unit, database, API-integration, concurrency and expiration tests
│   └── Dockerfile
├── frontend/
│   ├── src/
│   │   ├── api/                         Axios client + one function per endpoint, error-message mapping
│   │   ├── components/                  Toast, ConfirmDialog, Skeleton, StatusBadge, SeatMeter
│   │   ├── features/{admission,classes,holds,admin}/
│   │   ├── hooks/                       Polling, server-time clock, student identity
│   │   ├── layouts/  pages/  types/  utils/
│   │   └── App.tsx, main.tsx
│   ├── Dockerfile, nginx.conf, security-headers.inc
├── scripts/
│   ├── acceptance.sh                    Black-box checks of every endpoint, 120-way overbooking test, optional expiry
│   └── security-check.sh                Secret-hygiene checks of the repo, its history and the running containers
├── docker-compose.yml
├── .env.example                         Placeholders only (the password is intentionally empty)
├── README.md  ASSUMPTIONS.md  AI_USAGE.md  PROJECT_SPECIFICATION.md  CLAUDE.md
```

The layers depend inward: API → Application → Domain, and Infrastructure implements the Application
abstractions. DTOs are used at the API boundary; database entities are never returned.

## Testing

All of these need **only Docker** (nothing installed locally); native commands are shown too.

### Backend (136 tests)

| Category | Count | What it proves |
|---|---|---|
| Unit | 59 | Service rules on a fake repository (create/validate class, student id, reserve, no seats, duplicates, confirm, idempotency, expiry, cancel, my applications, seat counts) and the startup configuration validator |
| Database | 38 | The PostgreSQL schema itself enforces the rules; constraint errors map to the right business errors; transactions roll back; the class row lock really blocks |
| API integration | 26 | Full HTTP lifecycle against real PostgreSQL: responses, persistence, ownership, error shapes, CORS, injection, no leaked internals |
| Concurrency | 7 | 120 simultaneous students for 10 seats → exactly 10; 1,000 for 120 → exactly 120; counts sampled *during* a burst; one student many requests/classes; concurrent confirms |
| Expiration | 6 | Sweeper correctness; 80 simultaneous confirm-versus-expire contests; applications racing the sweeper; the real background loop and its recovery from errors |

The tests use a **real PostgreSQL** (Testcontainers), not an in-memory fake.

```bash
# With Docker only (the SDK runs in a container and talks to your Docker daemon):
docker run --rm -v "$PWD/backend":/src -v /var/run/docker.sock:/var/run/docker.sock \
  --add-host=host.docker.internal:host-gateway \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e TESTCONTAINERS_RYUK_DISABLED=true \
  -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test --nologo

# Or natively (.NET 8 SDK + a running Docker daemon):
cd backend && dotnet test
```

### Frontend (92 tests)

Student-id validation, class listing and seat states, applying (dialog, API call, errors), countdown, expiry
(including live with fake timers), simulated payment (no double submit), cancel, admin class creation and
validation, polling and cleanup, API layer (URLs and `X-Student-Id` header), toasts, dialogs.

```bash
# With Docker only:
docker run --rm -v "$PWD/frontend":/app -v /app/node_modules -w /app node:22-alpine sh -c "npm ci && npm test"

# Or natively (Node 20+):
cd frontend && npm ci && npm test            # add: npx vitest run --coverage
cd frontend && npm run lint && npx tsc -b    # lint and type-check
```

### End-to-end checks against the running stack

```bash
./scripts/acceptance.sh        # needs bash + curl; start the stack first (docker compose up --build -d)
```

It checks all 8 endpoints and their error codes (55 checks) and fires **120 simultaneous requests at a 10-seat
class**, expecting exactly 10 `201` and 110 `409`. To also check real expiry, start the backend with a short hold:

```bash
HOLD_DURATION_SECONDS=15 EXPIRATION_SWEEP_INTERVAL_SECONDS=2 docker compose up -d backend
EXPIRY_TEST=1 ./scripts/acceptance.sh          # 62 checks
docker compose up -d backend                   # back to the values in .env
```

```bash
./scripts/security-check.sh    # secrets: tracked files, ignore rules, full git history, running containers
```

## Running without Docker (development)

Requires the .NET 8 SDK, Node 20+, and a PostgreSQL you can reach. For example, a throwaway database:

```bash
DEVPW=$(openssl rand -hex 12)        # a throwaway password, used only for this local container
docker run -d --name dev-pg -e POSTGRES_DB=devdb -e POSTGRES_USER=dev \
  -e POSTGRES_PASSWORD="$DEVPW" -p 55432:5432 postgres:16-alpine

# Backend on http://localhost:5080 (not 5000: macOS uses it for AirPlay)
ConnectionStrings__DefaultConnection="Host=localhost;Port=55432;Database=devdb;Username=dev;Password=$DEVPW" \
  dotnet run --project backend/SchoolAdmission.API --urls http://localhost:5080 --no-launch-profile

# Frontend (separate terminal): http://localhost:5173, proxying /api to the backend
cd frontend && npm ci && npm run dev
```

Migrations are applied automatically when the backend starts. To add one:
`cd backend && dotnet tool restore && dotnet ef migrations add <Name> -p SchoolAdmission.Infrastructure -s SchoolAdmission.API -o Persistence/Migrations`.

## Data and operations

- PostgreSQL data lives in the Docker **named volume** `school-admission_pgdata`.
- `docker compose down` then `docker compose up --build` **keeps all data** (verified: classes, holds and confirmed
  admissions survive; holds that expired while the stack was down are marked EXPIRED on startup).
- **Never use `docker compose down -v` as a normal shutdown.** `-v` deletes the volume and all admission data.
  Use it only when you deliberately want a clean database.
- Docker Compose needs your `.env` for every command (including `down`, `ps`, `logs`) because it checks the
  required variables when reading the file.
- Logs: `docker compose logs -f backend`.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `required variable POSTGRES_PASSWORD is missing a value` | You have no `.env`, or `POSTGRES_PASSWORD` is empty. Run `cp .env.example .env` and set a password ([Quick start](#quick-start)) |
| Backend exits with "Invalid configuration … has no Password" | Same cause when running the backend directly: set the connection string / password |
| `password authentication failed` after you changed `POSTGRES_PASSWORD` | PostgreSQL stored the *old* password when the volume was first created. Put the old password back in `.env`, or, if you are fine losing the data, `docker compose down -v` and start again |
| `port is already allocated` (8080) | Set `APP_PORT=8081` (or any free port) in `.env` and use that port in the URLs |
| Password with special characters breaks startup | Use letters and digits only (`openssl rand -hex 24`); the value is embedded in a connection string |
| The page shows "Unable to reach the server" | The backend is still starting (`docker compose ps` shows it as `healthy` when ready) or is down (`docker compose logs backend`) |
| First `docker compose up --build` is slow | It downloads images and compiles both apps; later builds are cached |

## Security

- **No secrets in the repository.** `.env` and all `.env.*`, key and certificate files are git-ignored;
  `.env.example` contains placeholders only and an **empty** password. `./scripts/security-check.sh` verifies the
  tracked files, the entire git history, and that your real password appears nowhere in git or in anything the
  web server serves.
- **Fail-fast configuration**: Compose and the backend both refuse to start with a missing or empty required value,
  and error messages never contain a value.
- **The browser never receives backend secrets**: the frontend container is given only `VITE_API_BASE_URL`
  (public), and only `VITE_*` values can enter the JavaScript bundle.
- Only port 8080 is published. The backend runs as a non-root user. nginx sends `Content-Security-Policy`,
  `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy` and `Permissions-Policy`, and hides its version.
- All input is validated at three levels (frontend, API, database constraints); SQL is parameterized; errors
  never expose internals.

**Known limitations (by assignment design or scope):**

- **No authentication.** The assignment excludes login, so anyone who can reach the app can open the admin page
  and create classes, and any student number can be used. Do not expose this deployment to the public internet.
- A student can act as any other student whose number they know (the number *is* the identity).
- No rate limiting; a determined client could fill a class with fake student numbers.
- The database password is visible to anyone who can run `docker inspect` on the backend container
  (use Docker/Kubernetes secrets in a real deployment). Traffic is plain HTTP on localhost; terminate TLS in front of
  it for any real use.
- One backend instance is assumed (the sweeper and clock handling are not designed for several replicas).

## Other documents

| Document | Contents |
|---|---|
| [PROJECT_SPECIFICATION.md](PROJECT_SPECIFICATION.md) | Requirements, architecture, schema, API contract, concurrency design, roadmap, acceptance evidence |
| [ASSUMPTIONS.md](ASSUMPTIONS.md) | Every decision the assignment left open: what, why and how |
| [AI_USAGE.md](AI_USAGE.md) | How AI was used, what it got wrong, and how that was found and fixed |
| [CLAUDE.md](CLAUDE.md) | Working rules and commands for AI-assisted development of this repository |
