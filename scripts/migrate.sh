#!/bin/bash
# Applies database/migrations/*.sql in order, once each, as the schema-owner role.
# Usage: PGPASSWORD=<owner password> scripts/migrate.sh [host] [port] [db]
set -euo pipefail
HOST=${1:-localhost}; PORT=${2:-5432}; DB=${3:-projex_enterprise}
PSQL=(psql -v ON_ERROR_STOP=1 -h "$HOST" -p "$PORT" -U projex_owner -d "$DB")
"${PSQL[@]}" -c "CREATE TABLE IF NOT EXISTS public.schema_migrations (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());"
for f in "$(dirname "$0")"/../database/migrations/*.sql; do
  n=$(basename "$f")
  if [ "$("${PSQL[@]}" -tAc "SELECT 1 FROM public.schema_migrations WHERE name='$n'")" = "1" ]; then echo "skip  $n"; continue; fi
  echo "apply $n"
  "${PSQL[@]}" --single-transaction -f "$f" -c "INSERT INTO public.schema_migrations(name) VALUES ('$n');"
done
