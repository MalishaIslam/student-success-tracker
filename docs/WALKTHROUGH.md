# Walkthrough: how the app works

## 1. Getting the data in (`scripts/init-db.sh` and `database/03_import.sql`)

When the containers start, the `db-init` container runs `init-db.sh`:

1. It asks SQL Server whether `Enrollments` already has rows. If yes, it **keeps the data** and only
   refreshes the stored procedures and the app login. That's why edits survive restarts.
2. Otherwise it finds the five CSV files, checks each has the expected number of columns, strips
   Windows line endings, and copies them to a folder SQL Server can read.
3. It runs `01_schema.sql` (tables), `03_import.sql` (data), `02_procedures.sql`, `04_security.sql`.

`03_import.sql` uses a two-step pattern common in data integration:

- **Stage:** `BULK INSERT` loads each CSV, as plain text, into a table in the `stage` schema.
  Nothing can fail on type conversion at this point.
- **Transform and load:** one transaction converts types with `TRY_CONVERT`, turns `""` and `"?"`
  into `NULL`, removes duplicates with `ROW_NUMBER()`, and inserts into the real tables. A student
  can appear in several course runs, so the `Students` table takes their details from the latest run.
  At the end it prints rows-in-file versus rows-loaded so nothing disappears silently.

## 2. Showing a page: `GET /students?module=BBB&sort=AvgScore&dir=desc`

1. **Routing** sends the request to `StudentsController.Index`.
2. **Sign-in check:** the fallback authorization policy in `Program.cs` requires a signed-in user.
   Without the cookie, the user is redirected to `/account/login`.
3. **Model binding** fills an `EnrollmentSearch` from the query string.
4. `search.Normalize()` cleans every value: unknown sort columns become `StudentId`, page sizes
   must be 10/25/50/100, module codes must be three letters, and so on.
5. The repository calls `dbo.usp_Enrollments_Search` with typed parameters.
6. The procedure inserts matching rows into a temp table, sets `@TotalCount` (an OUTPUT
   parameter), works out each student's average score with `OUTER APPLY`, sorts with a fixed list
   of `CASE` expressions (no dynamic SQL), and returns one page with `OFFSET … FETCH NEXT`.
7. The Razor view `Views/Students/Index.cshtml` renders the table. Sort links and the pager are
   built from `search.ToRouteValues(...)`, so every link keeps the current filters.

## 3. Saving a form: `POST /students/edit/42`

1. The form includes a hidden **anti-forgery token**. `AutoValidateAntiforgeryTokenAttribute` (set
   in `Program.cs`) rejects any POST without a valid one, so another website can't submit forms as
   the signed-in user.
2. **Validation:** data annotations on `EnrollmentForm` (e.g. `[Range(1, 1000)]`) and its
   `Validate` method check the input. Errors are shown next to each field.
3. The form also carries `RowVersion`, the record's version when the page was opened.
4. `usp_Enrollment_Update` validates again, then inside a transaction compares versions. If someone
   else saved in between, it raises `THROW 50409`. The repository maps that to a
   `ConflictException`, and the controller shows "Someone else changed this record…".
5. Otherwise it updates `Students` and `Enrollments` and writes an `AuditLog` row describing what
   changed, all in the same transaction: either everything is saved or nothing is.

## 4. Deleting: `usp_Enrollment_Delete`

Children before parents: first the student's results for that course run, then the enrollment,
then (only if they have no other course) the student. All in one transaction, plus an audit row.

## 5. The at-risk report: `usp_Report_AtRisk`

For students still registered on the chosen day, it counts coursework (not exams) due by that day,
how much they submitted, and their average score. A student is flagged if they missed at least N
deadlines or averaged below the threshold. Because the dataset has final results, the second result
set compares outcomes for flagged and non-flagged students: a simple check of whether the rule is
useful before anyone would act on it.

## 6. Least privilege

The app connects as `student_app`, which has only `GRANT EXECUTE ON SCHEMA::dbo`. It works because
of **ownership chaining**: `dbo` owns both the procedures and the tables, so SQL Server checks
permission only on the procedure. Section 5 of `database/examples.sql` shows a direct `SELECT`
failing for that user.

---

## Ideas for extending it yourself

Each touches every layer (SQL → repository → controller → view → test):

1. **Enroll an existing student in another course.** A button on the details page, a small form,
   a new procedure that checks the student isn't already in that run (unique key), and a test.
2. **Engagement data.** Import `studentVle.csv` (10 million rows of clicks) into a summary table
   with total clicks per student per course run, show it on the details page, and add "low
   engagement" as an at-risk signal.
3. **Roles.** A read-only "Viewer" account that can browse but not edit, using
   `[Authorize(Roles = "Editor")]` on the POST actions.
4. **Audit log filters.** Filter by user, action or date range, with paging.
5. **A chart** of pass rates by region or deprivation band on the dashboard.
