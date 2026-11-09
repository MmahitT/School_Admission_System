#!/usr/bin/env bash
# Repository and runtime secret-hygiene checks. Run from anywhere inside the repo:
#
#   ./scripts/security-check.sh
#
# 1. Repository: no secret-shaped files tracked, ignore rules correct, no secret patterns in the current tree
#    or anywhere in the full git history, and (if a local .env exists) its real password appears nowhere in git.
# 2. Runtime (only if the stack is running): the frontend container and the files nginx serves contain no
#    database credentials, backend/db are not published to the host, and security headers are present.
#
# Secret VALUES are never printed; findings show file/commit and the matching key name only.
set -u

cd "$(git rev-parse --show-toplevel)" || exit 2
PASS=0; FAIL=0; SKIP=0
ok()   { PASS=$((PASS + 1)); printf '  PASS  %s\n' "$1"; }
bad()  { FAIL=$((FAIL + 1)); printf '  FAIL  %s\n' "$1"; }
skip() { SKIP=$((SKIP + 1)); printf '  SKIP  %s\n' "$1"; }

# Generic secret-assignment pattern: key name, = or :, then a literal value of 8+ plain characters.
GENERIC='(password|passwd|pwd|secret|api[_-]?key|access[_-]?key|token)[A-Za-z_]*[[:space:]]*[=:][[:space:]]*["'"'"']?[A-Za-z0-9/+_.-]{8,}'
# High-confidence provider/format patterns.
SPECIFIC='-----BEGIN [A-Z ]*PRIVATE KEY-----|AKIA[0-9A-Z]{16}|ghp_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{20,}|xox[baprs]-[A-Za-z0-9-]{10,}|sk-[A-Za-z0-9]{32,}|eyJ[A-Za-z0-9_-]{15,}\.eyJ[A-Za-z0-9_-]{15,}'
# Lines that are known, documented placeholders (the example file once shipped a placeholder password).
ALLOW='change_me|CHANGE_ME|<[a-z ]+>|\$\{[A-Z_]+'
# Test code uses throwaway literals on purpose.
EXCLUDE=(':!backend/SchoolAdmission.Tests' ':!*package-lock.json')

TMP_TREE="$(mktemp)"; TMP_HIST="$(mktemp)"; TMP_ERR="$(mktemp)"
trap 'rm -f "$TMP_TREE" "$TMP_HIST" "$TMP_ERR"' EXIT

echo "[0] Scanner self-test (the patterns must detect known fake secrets)"
canary_generic='db_password = "Abcd1234efgh"'
canary_key='-----BEGIN RSA PRIVATE KEY-----'
printf '%s\n' "$canary_generic" | grep -qE -e "$GENERIC" && ok "generic secret pattern detects a canary assignment" || bad "generic pattern failed its canary - results below are NOT trustworthy"
printf '%s\n' "$canary_key"     | grep -qE -e "$SPECIFIC" && ok "private-key pattern detects a canary header" || bad "private-key pattern failed its canary - results below are NOT trustworthy"
printf '%s\n' 'POSTGRES_PASSWORD=${POSTGRES_PASSWORD:?x}' | grep -qE -e "$GENERIC" && printf '%s\n' 'POSTGRES_PASSWORD=${POSTGRES_PASSWORD:?x}' | grep -qvE -e "$ALLOW" && bad "allow-list failed to exempt a variable reference" || ok "variable references are not reported as secrets"

echo "[1] Repository"

tracked_secret_files="$(git ls-files | grep -iE '(^|/)\.env($|\.)|\.pem$|\.key$|\.pfx$|\.p12$|secrets?\.json$|id_rsa' | grep -vE '(^|/)\.env\.example$' || true)"
[ -z "$tracked_secret_files" ] && ok "no secret-shaped files are tracked (only .env.example files)" || { bad "secret-shaped files are tracked:"; printf '%s\n' "$tracked_secret_files" | sed 's/^/        /'; }

for f in .env .env.local .env.production secrets/x.txt server.pem; do
  git check-ignore -q "$f" && ok "ignored: $f" || bad "NOT ignored: $f"
done
for f in .env.example frontend/.env.example; do
  [ -f "$f" ] || continue
  git check-ignore -q "$f" && bad "$f is ignored (it must stay tracked)" || ok "tracked (not ignored): $f"
done

if [ -f .env.example ]; then
  pw_line="$(grep -E '^POSTGRES_PASSWORD=' .env.example || true)"
  [ "$pw_line" = "POSTGRES_PASSWORD=" ] && ok ".env.example carries no password value" || bad ".env.example POSTGRES_PASSWORD must be empty"
fi

git grep -InEi -e "$SPECIFIC|$GENERIC" -- . "${EXCLUDE[@]}" >"$TMP_TREE" 2>"$TMP_ERR"; rc=$?
[ "$rc" -le 1 ] || { bad "scanner error while searching the tree (exit $rc): $(head -c 200 "$TMP_ERR")"; }
tree_hits="$(grep -vE -e "$ALLOW" "$TMP_TREE" | cut -d: -f1,2 || true)"
[ -z "$tree_hits" ] && ok "no secret patterns in the current tracked tree" || { bad "possible secrets in tracked files (file:line):"; printf '%s\n' "$tree_hits" | sed 's/^/        /'; }

git log --all -p --format='commit %h' -- . "${EXCLUDE[@]}" 2>"$TMP_ERR" \
  | awk '/^commit /{c=$2} /^\+\+\+ /{f=$2} /^\+[^+]/{print c" "f" "$0}' >"$TMP_HIST"
[ -s "$TMP_ERR" ] && bad "scanner error while reading history: $(head -c 200 "$TMP_ERR")"
hist_hits="$(grep -Ei -e "$SPECIFIC|$GENERIC" "$TMP_HIST" | grep -viE -e "$ALLOW" | awk '{print $1" "$2}' | sort -u || true)"
n_commits="$(git rev-list --all --count)"
[ -z "$hist_hits" ] && ok "no secret patterns in $n_commits commits of history (all branches)" || { bad "possible secrets in history (commit file):"; printf '%s\n' "$hist_hits" | sed 's/^/        /'; }

if [ -f .env ]; then
  PW="$(sed -n 's/^POSTGRES_PASSWORD=//p' .env | head -n1)"
  if [ -n "$PW" ]; then
    [ "$(git log --all --oneline -S"$PW" | wc -l | tr -d ' ')" = "0" ] && ok "your local POSTGRES_PASSWORD never appears in git history" || bad "your local POSTGRES_PASSWORD appears in git history - rotate it"
    git grep -qF "$PW" && bad "your local POSTGRES_PASSWORD appears in a tracked file" || ok "your local POSTGRES_PASSWORD is not in any tracked file"
  else
    skip "local .env has an empty POSTGRES_PASSWORD (nothing to look for)"
  fi
else
  skip "no local .env (nothing to compare against git history)"
  PW=""
fi

echo "[2] Runtime"
if ! command -v docker >/dev/null 2>&1 || [ -z "$(docker compose ps -q frontend 2>/dev/null)" ]; then
  skip "stack is not running (start it, then re-run, to check the frontend container and served files)"
else
  fe_env="$(docker compose exec -T frontend env 2>/dev/null | grep -ciE 'POSTGRES|PASSWORD|ConnectionStrings|SECRET' || true)"
  [ "$fe_env" = "0" ] && ok "frontend container environment holds no database/secret variables" || bad "frontend container environment contains secret-like variables"

  if [ -n "$PW" ]; then
    leaked="$(docker compose exec -T frontend sh -c "grep -rlF -- '$PW' /usr/share/nginx/html /etc/nginx 2>/dev/null | wc -l" | tr -d ' \r')"
    [ "$leaked" = "0" ] && ok "your POSTGRES_PASSWORD is not in any file the frontend container serves" || bad "POSTGRES_PASSWORD found in files served by nginx"
  fi
  bundle_hits="$(docker compose exec -T frontend sh -c "grep -rlE 'ConnectionStrings|POSTGRES_(PASSWORD|USER|DB)|Host=[^;]{1,40};' /usr/share/nginx/html 2>/dev/null | wc -l" | tr -d ' \r')"
  [ "$bundle_hits" = "0" ] && ok "served JavaScript/HTML contains no connection-string or POSTGRES_* text" || bad "served files mention connection strings or POSTGRES_*"

  for svc in db:5432 backend:8080; do
    name="${svc%%:*}"; port="${svc##*:}"
    # A published port shows as "<host-ip>:<host-port>-><container-port>"; an unpublished one has no "->".
    out="$(docker compose ps --format '{{.Ports}}' "$name" 2>/dev/null || true)"
    case "$out" in *"->"*) bad "$name:$port is published on the host ($out)" ;; *) ok "$name:$port is not published to the host" ;; esac
  done

  url="http://localhost:${APP_PORT:-8080}/"
  hdr="$(curl -sI "$url" 2>/dev/null | tr -d '\r')"
  for h in 'x-content-type-options: nosniff' 'x-frame-options: DENY' 'content-security-policy:' 'referrer-policy:'; do
    printf '%s' "$hdr" | grep -qi "^$h" && ok "response header present: $h" || bad "response header missing: $h"
  done
  printf '%s' "$hdr" | grep -qi '^server: nginx/[0-9]' && bad "nginx version is advertised in the Server header" || ok "nginx version is not advertised"
fi

echo
echo "Result: $PASS passed, $FAIL failed, $SKIP skipped"
[ "$FAIL" -eq 0 ]
