#!/usr/bin/env bash
# End-to-end check of the running app and database: signs in, then exercises
# every page, the API, filters, sorting, CSV export, add, edit, conflict
# detection, scores, delete, the audit log and sign-out.
#
#   bash scripts/smoke-test.sh [base-url] [username] [password]
# Defaults: http://localhost:5080, and ADMIN_USERNAME / ADMIN_PASSWORD from .env
#
# Leaves the data as it found it: the test student it adds is deleted at the end.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
env_value() { [ -f "$ROOT/.env" ] && grep "^$1=" "$ROOT/.env" | head -n 1 | cut -d= -f2-; }

BASE="${1:-http://localhost:5080}"
USERNAME="${2:-$(env_value ADMIN_USERNAME)}"
USERNAME="${USERNAME:-admin}"
PASSWORD="${3:-$(env_value ADMIN_PASSWORD)}"

if [ -z "$PASSWORD" ]; then
  echo "No password given and ADMIN_PASSWORD not found in .env"
  exit 1
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
JAR="$WORK/cookies"
BODY="$WORK/body"
HEADERS="$WORK/headers"
PASS=0
FAIL=0

# request <curl args...>: sets STATUS and LOCATION, body in $BODY
request() {
  STATUS="$(curl -s -b "$JAR" -c "$JAR" -D "$HEADERS" -o "$BODY" -w '%{http_code}' "$@")"
  LOCATION="$(grep -i '^location:' "$HEADERS" | tail -n 1 | tr -d '\r' | sed 's/^[Ll]ocation: *//')"
}

ok()   { echo "PASS  $1"; PASS=$((PASS + 1)); }
bad()  { echo "FAIL  $1"; FAIL=$((FAIL + 1)); head -c 300 "$BODY" | tr '\n' ' '; echo; }

# expect <status> <description> [text that the body must contain]
expect() {
  local status="$1" description="$2" text="${3:-}"
  if [ "$STATUS" != "$status" ]; then
    bad "$description (got $STATUS, expected $status)"
  elif [ -n "$text" ] && ! grep -q -- "$text" "$BODY"; then
    bad "$description (body did not contain: $text)"
  else
    ok "$description"
  fi
}

token() { sed -n 's/.*name="__RequestVerificationToken" type="hidden" value="\([^"]*\)".*/\1/p' "$BODY" | head -n 1; }
json()  { grep -o "\"$1\":\"[^\"]*\"" "$BODY" | head -n 1 | cut -d'"' -f4; }
jsonn() { grep -o "\"$1\":[0-9-]*" "$BODY" | head -n 1 | cut -d: -f2; }

echo "Testing $BASE as $USERNAME"
echo

# ----- Without signing in -----------------------------------------------
request "$BASE/health";                 expect 200 "health check reaches SQL Server" "Healthy"
request "$BASE/students";               expect 302 "pages require sign-in"
request "$BASE/api/enrollments";        expect 401 "API requires sign-in"

# ----- Sign in ------------------------------------------------------------
request "$BASE/account/login";          expect 200 "login page"
TOKEN="$(token)"
request -X POST "$BASE/account/login" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Username=$USERNAME" --data-urlencode "Password=wrong-password"
expect 200 "wrong password is rejected" "incorrect"
TOKEN="$(token)"
request -X POST "$BASE/account/login" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "Username=$USERNAME" --data-urlencode "Password=$PASSWORD"
expect 302 "correct password signs in"

# ----- Reading ------------------------------------------------------------
request "$BASE/";                                             expect 200 "dashboard" "Outcomes by course run"
request "$BASE/students";                                     expect 200 "students list" "Showing"
request "$BASE/students?module=AAA&sort=AvgScore&dir=desc";   expect 200 "filter by course, sort by score" 'aria-sort="descending"'
request "$BASE/students?result=Withdrawn";                    expect 200 "filter by final result" "Withdrawn"
request "$BASE/students?page=2&pageSize=10";                  expect 200 "second page" "Showing 11"
request "$BASE/students?page=999999&pageSize=10";             expect 302 "page past the end goes to the last page"
request "$BASE/students?studentId=abc";                       expect 200 "invalid student ID is explained" "whole number"
request "$BASE/students?sort=Password;DROP%20TABLE";          expect 200 "unknown sort column is ignored safely" "Showing"
request "$BASE/students/export?module=AAA";                   expect 200 "CSV export" "EnrollmentId,StudentId"
request "$BASE/reports/atrisk";                               expect 200 "at-risk report" "How did flagged students finish"
request "$BASE/reports/atrisk?cutoffDay=0";                   expect 200 "at-risk settings are validated" "between 1 and 400"
request "$BASE/api/enrollments?pageSize=5";                   expect 200 "API search" "totalCount"
request "$BASE/api/reports/dashboard";                        expect 200 "API dashboard" "courses"
request "$BASE/api/enrollments/999999999";                    expect 404 "API unknown enrollment is 404"
request "$BASE/students/details/999999999";                   expect 404 "unknown student page is 404"

# ----- Add a test student, copying valid values from an existing record --
request "$BASE/api/enrollments?ageBand=0-35&pageSize=1"
TEMPLATE_ID="$(jsonn enrollmentId)"
request "$BASE/api/enrollments/$TEMPLATE_ID"
GENDER="$(json gender)"; REGION="$(json region)"; EDUCATION="$(json highestEducation)"; COURSE="$(jsonn presentationId)"

request "$BASE/students/create";        expect 200 "add student form"
TOKEN="$(token)"
FIELDS=(--data-urlencode "Gender=$GENDER" --data-urlencode "Region=$REGION"
        --data-urlencode "HighestEducation=$EDUCATION" --data-urlencode "AgeBand=0-35"
        --data-urlencode "PresentationId=$COURSE" --data-urlencode "PreviousAttempts=0"
        --data-urlencode "RegistrationDay=-10")
CREDITS=(--data-urlencode "StudiedCredits=60")

request -X POST "$BASE/students/create" "${FIELDS[@]}" "${CREDITS[@]}"
expect 400 "form without anti-forgery token is refused"

request -X POST "$BASE/students/create" --data-urlencode "__RequestVerificationToken=$TOKEN" "${FIELDS[@]}" \
  --data-urlencode "StudiedCredits=5000"
expect 200 "invalid credits are rejected with a message" "between 1 and 1000"

request -X POST "$BASE/students/create" --data-urlencode "__RequestVerificationToken=$TOKEN" "${FIELDS[@]}" "${CREDITS[@]}"
expect 302 "add student"
NEW_ID="$(echo "$LOCATION" | sed -n 's#.*/details/\([0-9]*\).*#\1#p')"

request "$BASE/students/details/$NEW_ID"; expect 200 "new student's page" "was added"

# ----- Edit, then try to save a stale copy --------------------------------
request "$BASE/students/edit/$NEW_ID";  expect 200 "edit form"
TOKEN="$(token)"
ROWVER="$(sed -n 's/.*name="RowVersion" value="\([^"]*\)".*/\1/p' "$BODY" | head -n 1)"
request -X POST "$BASE/students/edit/$NEW_ID" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "RowVersion=$ROWVER" "${FIELDS[@]}" "${CREDITS[@]}" --data-urlencode "FinalResult=Pass"
expect 302 "save changes"
request -X POST "$BASE/students/edit/$NEW_ID" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "RowVersion=$ROWVER" "${FIELDS[@]}" "${CREDITS[@]}" --data-urlencode "FinalResult=Fail"
expect 200 "conflicting edit is detected" "Someone else changed"

# ----- Scores -------------------------------------------------------------
request "$BASE/api/enrollments/$NEW_ID"
ASSESSMENT="$(jsonn assessmentId)"
request "$BASE/students/details/$NEW_ID"; TOKEN="$(token)"
request -X POST "$BASE/students/savescore/$NEW_ID" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "AssessmentId=$ASSESSMENT" --data-urlencode "Score=77" --data-urlencode "SubmittedDay=12"
expect 302 "save a score"
request "$BASE/api/enrollments/$NEW_ID"; expect 200 "score is stored" '"score":77'
request -X POST "$BASE/students/deletescore/$NEW_ID" --data-urlencode "__RequestVerificationToken=$TOKEN" \
  --data-urlencode "assessmentId=$ASSESSMENT"
expect 302 "remove the score"

# ----- Delete -------------------------------------------------------------
request "$BASE/students/delete/$NEW_ID"; expect 200 "delete confirmation page" "Yes, delete"
TOKEN="$(token)"
request -X POST "$BASE/students/delete/$NEW_ID" --data-urlencode "__RequestVerificationToken=$TOKEN"
expect 302 "delete the test student"
request "$BASE/students/details/$NEW_ID"; expect 404 "deleted student is gone"
request "$BASE/reports/audit";           expect 200 "audit log records the changes" "Delete"

# ----- Sign out -----------------------------------------------------------
request "$BASE/"; TOKEN="$(token)"
request -X POST "$BASE/account/logout" --data-urlencode "__RequestVerificationToken=$TOKEN"
expect 302 "sign out"
request "$BASE/students";               expect 302 "signed out: pages require sign-in again"

echo
echo "$PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
