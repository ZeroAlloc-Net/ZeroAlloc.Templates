#!/usr/bin/env bash
# Exercises the authenticated request path of a booted template app.
#
# Usage: request-smoke.sh <base-url> <valid-order-json> <invalid-order-json> <server-log>
#
# Asserts:
#   POST /orders without a token        -> 401
#   POST /orders with a valid body      -> 201 and a numeric id
#   POST /orders with an invalid body   -> 400, which proves the mediator
#                                          validation behavior ran
# Used by both the JIT and the NativeAOT smoke jobs so the AOT binary is checked
# beyond the anonymous /healthz endpoint.
set -euo pipefail

BASE_URL="$1"
VALID_BODY="$2"
INVALID_BODY="$3"
SERVER_LOG="$4"

# Hand-crafted HS256 JWT signed with the template's DEV key; base64url has no padding.
KEY="DEV-ONLY-KEY-DO-NOT-USE-IN-PRODUCTION-AT-LEAST-32-CHARS-LONG"
b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
HEADER='{"alg":"HS256","typ":"JWT"}'
EXP=$(($(date +%s) + 3600))
PAYLOAD="{\"scope\":\"orders.write\",\"sub\":\"smoke\",\"exp\":$EXP}"
H=$(printf '%s' "$HEADER" | b64url)
P=$(printf '%s' "$PAYLOAD" | b64url)
SIG=$(printf '%s' "$H.$P" | openssl dgst -sha256 -hmac "$KEY" -binary | b64url)
JWT="$H.$P.$SIG"

FAILED=0

# post <expected-status> <description> <body> [auth]
post() {
  local expected="$1" description="$2" body="$3" auth="${4:-yes}"
  local args=(-sS -X POST "$BASE_URL/orders" -H "Content-Type: application/json" -d "$body" -w "\n%{http_code}")
  if [ "$auth" = "yes" ]; then
    args+=(-H "Authorization: Bearer $JWT")
  fi
  local response status body_out
  response=$(curl "${args[@]}" 2>&1 || true)
  status=$(echo "$response" | tail -n1)
  body_out=$(echo "$response" | sed '$d')
  if [ "$status" != "$expected" ]; then
    echo "::error::$description returned HTTP $status, expected $expected. Body: $body_out"
    FAILED=1
    return
  fi
  echo "ok: $description -> $status"
  LAST_BODY="$body_out"
}

LAST_BODY=""
post 401 "POST /orders without a token" "$VALID_BODY" no
post 201 "POST /orders with a valid body" "$VALID_BODY"
if [ "$FAILED" = 0 ] && ! echo "$LAST_BODY" | grep -qE '^[0-9]+$|"[Ii]d"\s*:\s*[0-9]+'; then
  echo "::error::201 response is missing a numeric id: $LAST_BODY"
  FAILED=1
fi
post 400 "POST /orders with an invalid body" "$INVALID_BODY"

if [ "$FAILED" != 0 ]; then
  echo "--- server log ---"
  cat "$SERVER_LOG"
  exit 1
fi
