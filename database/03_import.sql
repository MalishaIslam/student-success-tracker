/* =============================================================================
   03_import.sql
   Loads the Open University Learning Analytics Dataset (OULAD) CSV files.

   Two steps, a common pattern for data integrations:
     1. BULK INSERT each CSV, as text, into a staging table (schema "stage").
     2. Clean, convert, de-duplicate and copy into the real tables in one
        transaction. Missing values in the files ("" or "?") become NULL.

   Run by init-db.sh with:  sqlcmd ... -v ImportDir="/import" -i 03_import.sql
   The folder is inside the SQL Server container; init-db.sh copies the CSVs
   there after normalising line endings.

   Dataset: Kuzilek, Hlosta & Zdrahal (2017), Open University Learning Analytics
   dataset, Scientific Data 4:170171. Licence: CC BY 4.0.
   ============================================================================= */

USE StudentSuccess;
GO

SET NOCOUNT ON;
GO

IF SCHEMA_ID(N'stage') IS NULL
    EXEC (N'CREATE SCHEMA stage');
GO

DROP TABLE IF EXISTS stage.Courses;
DROP TABLE IF EXISTS stage.Assessments;
DROP TABLE IF EXISTS stage.StudentInfo;
DROP TABLE IF EXISTS stage.StudentRegistration;
DROP TABLE IF EXISTS stage.StudentAssessment;
GO

-- Staging tables mirror the CSV columns exactly, all as text
CREATE TABLE stage.Courses
(
    code_module                NVARCHAR(100) NULL,
    code_presentation          NVARCHAR(100) NULL,
    module_presentation_length NVARCHAR(100) NULL
);

CREATE TABLE stage.Assessments
(
    code_module       NVARCHAR(100) NULL,
    code_presentation NVARCHAR(100) NULL,
    id_assessment     NVARCHAR(100) NULL,
    assessment_type   NVARCHAR(100) NULL,
    [date]            NVARCHAR(100) NULL,
    [weight]          NVARCHAR(100) NULL
);

CREATE TABLE stage.StudentInfo
(
    code_module          NVARCHAR(100) NULL,
    code_presentation    NVARCHAR(100) NULL,
    id_student           NVARCHAR(100) NULL,
    gender               NVARCHAR(100) NULL,
    region               NVARCHAR(100) NULL,
    highest_education    NVARCHAR(100) NULL,
    imd_band             NVARCHAR(100) NULL,
    age_band             NVARCHAR(100) NULL,
    num_of_prev_attempts NVARCHAR(100) NULL,
    studied_credits      NVARCHAR(100) NULL,
    disability           NVARCHAR(100) NULL,
    final_result         NVARCHAR(100) NULL
);

CREATE TABLE stage.StudentRegistration
(
    code_module         NVARCHAR(100) NULL,
    code_presentation   NVARCHAR(100) NULL,
    id_student          NVARCHAR(100) NULL,
    date_registration   NVARCHAR(100) NULL,
    date_unregistration NVARCHAR(100) NULL
);

CREATE TABLE stage.StudentAssessment
(
    id_assessment  NVARCHAR(100) NULL,
    id_student     NVARCHAR(100) NULL,
    date_submitted NVARCHAR(100) NULL,
    is_banked      NVARCHAR(100) NULL,
    score          NVARCHAR(100) NULL
);
GO

/* -----------------------------------------------------------------------------
   Step 1: BULK INSERT (FORMAT = 'CSV' understands quoted fields; FIRSTROW = 2 skips the header)
   ----------------------------------------------------------------------------- */
PRINT N'Loading CSV files into staging tables...';

BULK INSERT stage.Courses
FROM '$(ImportDir)/courses.csv'
WITH (FORMAT = 'CSV', FIRSTROW = 2, FIELDTERMINATOR = ',', ROWTERMINATOR = '0x0a', TABLOCK);

BULK INSERT stage.Assessments
FROM '$(ImportDir)/assessments.csv'
WITH (FORMAT = 'CSV', FIRSTROW = 2, FIELDTERMINATOR = ',', ROWTERMINATOR = '0x0a', TABLOCK);

BULK INSERT stage.StudentInfo
FROM '$(ImportDir)/studentInfo.csv'
WITH (FORMAT = 'CSV', FIRSTROW = 2, FIELDTERMINATOR = ',', ROWTERMINATOR = '0x0a', TABLOCK);

BULK INSERT stage.StudentRegistration
FROM '$(ImportDir)/studentRegistration.csv'
WITH (FORMAT = 'CSV', FIRSTROW = 2, FIELDTERMINATOR = ',', ROWTERMINATOR = '0x0a', TABLOCK);

BULK INSERT stage.StudentAssessment
FROM '$(ImportDir)/studentAssessment.csv'
WITH (FORMAT = 'CSV', FIRSTROW = 2, FIELDTERMINATOR = ',', ROWTERMINATOR = '0x0a', TABLOCK);
GO

/* -----------------------------------------------------------------------------
   Step 2: clean and copy into the real tables, all or nothing
   ----------------------------------------------------------------------------- */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Modules and course presentations
INSERT INTO dbo.Modules (ModuleCode)
SELECT DISTINCT c.ModuleCode
FROM stage.Courses AS s
CROSS APPLY (SELECT UPPER(NULLIF(NULLIF(TRIM(s.code_module), N''), N'?')) AS ModuleCode) AS c
WHERE c.ModuleCode LIKE '[A-Z][A-Z][A-Z]';

INSERT INTO dbo.CoursePresentations (ModuleCode, PresentationCode, LengthDays)
SELECT c.ModuleCode, c.PresentationCode, MAX(TRY_CONVERT(SMALLINT, c.LengthDays))
FROM stage.Courses AS s
CROSS APPLY (SELECT
    UPPER(NULLIF(NULLIF(TRIM(s.code_module), N''), N'?'))                AS ModuleCode,
    UPPER(NULLIF(NULLIF(TRIM(s.code_presentation), N''), N'?'))          AS PresentationCode,
    NULLIF(NULLIF(TRIM(s.module_presentation_length), N''), N'?')        AS LengthDays
) AS c
WHERE c.ModuleCode IN (SELECT ModuleCode FROM dbo.Modules)
  AND c.PresentationCode LIKE '[12][0-9][0-9][0-9][A-Z]'
GROUP BY c.ModuleCode, c.PresentationCode;

-- Students: one row per person. A person can appear in several course runs;
-- their details are taken from the most recent run (2014J sorts after 2013B).
WITH Info AS
(
    SELECT
        TRY_CONVERT(INT, c.StudentId) AS StudentId,
        c.Gender, c.Region, c.HighestEducation, c.ImdBand, c.AgeBand, c.Disability,
        ROW_NUMBER() OVER (PARTITION BY TRY_CONVERT(INT, c.StudentId)
                           ORDER BY c.PresentationCode DESC, c.ModuleCode) AS rn
    FROM stage.StudentInfo AS s
    CROSS APPLY (SELECT
        NULLIF(NULLIF(TRIM(s.id_student), N''), N'?')          AS StudentId,
        UPPER(NULLIF(NULLIF(TRIM(s.code_module), N''), N'?'))  AS ModuleCode,
        UPPER(NULLIF(NULLIF(TRIM(s.code_presentation), N''), N'?')) AS PresentationCode,
        UPPER(NULLIF(NULLIF(TRIM(s.gender), N''), N'?'))       AS Gender,
        NULLIF(NULLIF(TRIM(s.region), N''), N'?')              AS Region,
        NULLIF(NULLIF(TRIM(s.highest_education), N''), N'?')   AS HighestEducation,
        NULLIF(NULLIF(TRIM(s.imd_band), N''), N'?')            AS ImdBand,
        NULLIF(NULLIF(TRIM(s.age_band), N''), N'?')            AS AgeBand,
        UPPER(NULLIF(NULLIF(TRIM(s.disability), N''), N'?'))   AS Disability
    ) AS c
    WHERE TRY_CONVERT(INT, c.StudentId) > 0
)
INSERT INTO dbo.Students (StudentId, Gender, Region, HighestEducation, ImdBand, AgeBand, HasDisability)
SELECT StudentId, Gender, LEFT(Region, 60), LEFT(HighestEducation, 60), LEFT(ImdBand, 10), LEFT(AgeBand, 10),
       CASE WHEN Disability = N'Y' THEN 1 ELSE 0 END
FROM Info
WHERE rn = 1
  AND Gender IN ('F', 'M')
  AND Region IS NOT NULL
  AND HighestEducation IS NOT NULL
  AND AgeBand IS NOT NULL;

-- Enrollments: studentInfo joined with studentRegistration on module + run + student
WITH Info AS
(
    SELECT c.*,
           ROW_NUMBER() OVER (PARTITION BY c.ModuleCode, c.PresentationCode, c.StudentId ORDER BY (SELECT NULL)) AS rn
    FROM stage.StudentInfo AS s
    CROSS APPLY (SELECT
        UPPER(NULLIF(NULLIF(TRIM(s.code_module), N''), N'?'))       AS ModuleCode,
        UPPER(NULLIF(NULLIF(TRIM(s.code_presentation), N''), N'?')) AS PresentationCode,
        TRY_CONVERT(INT, NULLIF(NULLIF(TRIM(s.id_student), N''), N'?'))           AS StudentId,
        TRY_CONVERT(TINYINT, NULLIF(NULLIF(TRIM(s.num_of_prev_attempts), N''), N'?')) AS PreviousAttempts,
        TRY_CONVERT(SMALLINT, NULLIF(NULLIF(TRIM(s.studied_credits), N''), N'?'))  AS StudiedCredits,
        NULLIF(NULLIF(TRIM(s.final_result), N''), N'?')                           AS FinalResult
    ) AS c
),
Reg AS
(
    SELECT c.*,
           ROW_NUMBER() OVER (PARTITION BY c.ModuleCode, c.PresentationCode, c.StudentId ORDER BY (SELECT NULL)) AS rn
    FROM stage.StudentRegistration AS s
    CROSS APPLY (SELECT
        UPPER(NULLIF(NULLIF(TRIM(s.code_module), N''), N'?'))       AS ModuleCode,
        UPPER(NULLIF(NULLIF(TRIM(s.code_presentation), N''), N'?')) AS PresentationCode,
        TRY_CONVERT(INT, NULLIF(NULLIF(TRIM(s.id_student), N''), N'?'))                 AS StudentId,
        TRY_CONVERT(SMALLINT, NULLIF(NULLIF(TRIM(s.date_registration), N''), N'?'))     AS RegistrationDay,
        TRY_CONVERT(SMALLINT, NULLIF(NULLIF(TRIM(s.date_unregistration), N''), N'?'))   AS UnregistrationDay
    ) AS c
)
INSERT INTO dbo.Enrollments (StudentId, PresentationId, PreviousAttempts, StudiedCredits, RegistrationDay, UnregistrationDay, FinalResult)
SELECT i.StudentId,
       cp.PresentationId,
       ISNULL(i.PreviousAttempts, 0),
       i.StudiedCredits,
       r.RegistrationDay,
       r.UnregistrationDay,
       CASE WHEN i.FinalResult IN (N'Pass', N'Distinction', N'Fail', N'Withdrawn') THEN i.FinalResult END
FROM Info AS i
INNER JOIN dbo.CoursePresentations AS cp
        ON cp.ModuleCode = i.ModuleCode AND cp.PresentationCode = i.PresentationCode
INNER JOIN dbo.Students AS st
        ON st.StudentId = i.StudentId
LEFT JOIN Reg AS r
       ON r.ModuleCode = i.ModuleCode AND r.PresentationCode = i.PresentationCode
      AND r.StudentId = i.StudentId AND r.rn = 1
WHERE i.rn = 1
  AND i.StudiedCredits BETWEEN 1 AND 1000
  AND ISNULL(i.PreviousAttempts, 0) <= 20;

-- Assessments
WITH A AS
(
    SELECT c.*,
           ROW_NUMBER() OVER (PARTITION BY c.AssessmentId ORDER BY (SELECT NULL)) AS rn
    FROM stage.Assessments AS s
    CROSS APPLY (SELECT
        UPPER(NULLIF(NULLIF(TRIM(s.code_module), N''), N'?'))       AS ModuleCode,
        UPPER(NULLIF(NULLIF(TRIM(s.code_presentation), N''), N'?')) AS PresentationCode,
        TRY_CONVERT(INT, NULLIF(NULLIF(TRIM(s.id_assessment), N''), N'?'))         AS AssessmentId,
        NULLIF(NULLIF(TRIM(s.assessment_type), N''), N'?')                         AS AssessmentType,
        TRY_CONVERT(SMALLINT, NULLIF(NULLIF(TRIM(s.[date]), N''), N'?'))           AS DueDay,
        TRY_CONVERT(DECIMAL(5,2), NULLIF(NULLIF(TRIM(s.[weight]), N''), N'?'))     AS WeightPercent
    ) AS c
    WHERE c.AssessmentId IS NOT NULL
)
INSERT INTO dbo.Assessments (AssessmentId, PresentationId, AssessmentType, DueDay, WeightPercent)
SELECT a.AssessmentId, cp.PresentationId, a.AssessmentType, a.DueDay, a.WeightPercent
FROM A AS a
INNER JOIN dbo.CoursePresentations AS cp
        ON cp.ModuleCode = a.ModuleCode AND cp.PresentationCode = a.PresentationCode
WHERE a.rn = 1
  AND a.AssessmentType IN (N'TMA', N'CMA', N'Exam')
  AND a.WeightPercent BETWEEN 0 AND 100;

-- Assessment results
WITH R AS
(
    SELECT c.*,
           ROW_NUMBER() OVER (PARTITION BY c.StudentId, c.AssessmentId ORDER BY (SELECT NULL)) AS rn
    FROM stage.StudentAssessment AS s
    CROSS APPLY (SELECT
        TRY_CONVERT(INT, NULLIF(NULLIF(TRIM(s.id_assessment), N''), N'?'))           AS AssessmentId,
        TRY_CONVERT(INT, NULLIF(NULLIF(TRIM(s.id_student), N''), N'?'))              AS StudentId,
        TRY_CONVERT(SMALLINT, NULLIF(NULLIF(TRIM(s.date_submitted), N''), N'?'))     AS SubmittedDay,
        NULLIF(NULLIF(TRIM(s.is_banked), N''), N'?')                                 AS IsBanked,
        TRY_CONVERT(DECIMAL(5,2), NULLIF(NULLIF(TRIM(s.score), N''), N'?'))          AS Score
    ) AS c
)
INSERT INTO dbo.AssessmentResults (StudentId, AssessmentId, SubmittedDay, IsBanked, Score)
SELECT r.StudentId, r.AssessmentId, r.SubmittedDay,
       CASE WHEN r.IsBanked = N'1' THEN 1 ELSE 0 END,
       r.Score
FROM R AS r
INNER JOIN dbo.Students AS st    ON st.StudentId = r.StudentId
INNER JOIN dbo.Assessments AS a  ON a.AssessmentId = r.AssessmentId
WHERE r.rn = 1
  AND (r.Score IS NULL OR r.Score BETWEEN 0 AND 100);

COMMIT TRANSACTION;
GO

/* -----------------------------------------------------------------------------
   Report what was loaded versus what was in the files, then drop staging
   ----------------------------------------------------------------------------- */
SELECT N'Course runs'        AS [Table], (SELECT COUNT(*) FROM stage.Courses)             AS RowsInFile, (SELECT COUNT(*) FROM dbo.CoursePresentations) AS RowsLoaded
UNION ALL SELECT N'Students',           (SELECT COUNT(DISTINCT id_student) FROM stage.StudentInfo), (SELECT COUNT(*) FROM dbo.Students)
UNION ALL SELECT N'Enrollments',        (SELECT COUNT(*) FROM stage.StudentInfo),         (SELECT COUNT(*) FROM dbo.Enrollments)
UNION ALL SELECT N'Assessments',        (SELECT COUNT(*) FROM stage.Assessments),         (SELECT COUNT(*) FROM dbo.Assessments)
UNION ALL SELECT N'Assessment results', (SELECT COUNT(*) FROM stage.StudentAssessment),   (SELECT COUNT(*) FROM dbo.AssessmentResults);
GO

DROP TABLE IF EXISTS stage.Courses;
DROP TABLE IF EXISTS stage.Assessments;
DROP TABLE IF EXISTS stage.StudentInfo;
DROP TABLE IF EXISTS stage.StudentRegistration;
DROP TABLE IF EXISTS stage.StudentAssessment;
GO

DECLARE @Enrollments INT = (SELECT COUNT(*) FROM dbo.Enrollments);
IF @Enrollments = 0
    THROW 50500, N'Import finished but no enrollments were loaded. Check that the CSV files are the OULAD files.', 1;

PRINT CONCAT(N'03_import.sql: loaded ', @Enrollments, N' enrollments.');
GO
