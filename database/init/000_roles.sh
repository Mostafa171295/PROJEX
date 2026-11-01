#!/bin/bash
# Runs once, as the PostgreSQL superuser, on first container start.
# Separates the schema OWNER (runs migrations) from the APPLICATION role (runs the API).
# The application role is NOT a superuser, does NOT own tables and has NO BYPASSRLS,
# so row-level security is actually enforced for the API.
set -euo pipefail
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v owner_pw="$PROJEX_OWNER_PASSWORD" -v app_pw="$PROJEX_APP_PASSWORD" -v dbname="$POSTGRES_DB" <<'SQL'
CREATE ROLE projex_owner LOGIN PASSWORD :'owner_pw' NOSUPERUSER NOBYPASSRLS NOCREATEROLE;
CREATE ROLE projex_app   LOGIN PASSWORD :'app_pw'   NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB;
GRANT CREATE, CONNECT ON DATABASE :"dbname" TO projex_owner;
GRANT CONNECT ON DATABASE :"dbname" TO projex_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CREATE ON SCHEMA public TO projex_owner;
SQL
