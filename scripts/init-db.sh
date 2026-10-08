#!/usr/bin/env bash
# Sets up the StudentSuccess database. Runs inside the db-init container
# (docker compose) and in the GitHub Actions workflow.
#
# First run (or RESET_DB=1): create tables, load the CSV dataset, create procedures and the app login.
# Later runs: keep the data (so edits made in the app survive restarts) and
# only refresh the stored procedures and the app login.
#
# Required env: SA_PASSWORD, APP_DB_PASSWORD
# Optional env: DB_HOST (sqlserver), SCRIPTS_DIR (/scripts), DATA_DIR (/data),
#               IMPORT_DIR (/import, must also be visible to SQL Server), RESET_DB (0)
set -euo pipefail

DB_HOST="${DB_HOST:-sqlserver}"
SCRIPTS_DIR="${SCRIPTS_DIR:-/scripts}"
DATA_DIR="${DATA_DIR:-/data}"
IMPORT_DIR="${IMPORT_DIR:-/import}"
RESET_DB="${RESET_DB:-0}"

SQLCMD=/opt/mssql-tools18/bin/sqlcmd
[ -x "$SQLCMD" ] || SQLCMD=/opt/mssql-tools/bin/sqlcmd

sql() {
  # -C trusts the local development certificate, -b stops on the first error
  "$SQLCMD" -C -S "$DB_HOST" -U sa -P "$SA_PASSWORD" -b "$@"
}

run_script() {
  local file="$1"; shift
  echo ">> Running $file"
  sql -i "$SCRIPTS_DIR/$file" "$@"
}

finish() {
  run_script 02_procedures.sql
  run_script 04_security.sql -v AppPassword="$APP_DB_PASSWORD"
  echo "Database is ready."
}

# --- Is the data already loaded? -------------------------------------------
loaded="$(sql -h -1 -W -Q "SET NOCOUNT ON;
  DECLARE @n INT = 0;
  IF OBJECT_ID(N'StudentSuccess.dbo.Enrollments') IS NOT NULL
      EXEC sp_executesql N'SELECT @n = COUNT(*) FROM StudentSuccess.dbo.Enrollments', N'@n INT OUTPUT', @n OUTPUT;
  SELECT @n;" | tr -dc '0-9')"
loaded="${loaded:-0}"

if [ "$loaded" -gt 0 ] && [ "$RESET_DB" != "1" ]; then
  echo "Found $loaded enrollments: keeping existing data (run with RESET_DB=1 to reload from the CSV files)."
  finish
  exit 0
fi

# --- Find and check the CSV files ------------------------------------------
# name:expected number of columns
FILES="courses.csv:3 assessments.csv:6 studentInfo.csv:12 studentRegistration.csv:5 studentAssessment.csv:5"

mkdir -p "$IMPORT_DIR"

for entry in $FILES; do
  name="${entry%%:*}"
  expected="${entry##*:}"

  # The zip may unpack into a subfolder, so search a few levels down
  path="$( (find "$DATA_DIR" -maxdepth 4 -type f -name "$name" 2>/dev/null || true) | head -n 1)"
  if [ -z "$path" ]; then
    echo "ERROR: $name not found in the data folder."
    echo "       Download the dataset first:  bash scripts/get-data.sh"
    exit 1
  fi

  # Count the columns in the header row
  header="$(head -n 1 "$path" | tr -d '\r"' | sed 's/^\xEF\xBB\xBF//')"
  IFS=',' read -r -a columns <<< "$header"
  if [ "${#columns[@]}" -ne "$expected" ]; then
    echo "ERROR: $name has ${#columns[@]} columns, expected $expected."
    echo "       Header found: $header"
    exit 1
  fi

  # Copy with Windows line endings and any byte-order mark removed, so
  # BULK INSERT can always use a plain newline as the row terminator
  sed '1s/^\xEF\xBB\xBF//' "$path" | tr -d '\r' > "$IMPORT_DIR/$name"
  chmod 644 "$IMPORT_DIR/$name"
  echo "   $name: $(($(wc -l < "$IMPORT_DIR/$name") - 1)) rows"
done

# --- Build the database ----------------------------------------------------
run_script 01_schema.sql
run_script 03_import.sql -v ImportDir="$IMPORT_DIR"
finish
