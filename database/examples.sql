/* =============================================================================
   examples.sql
   Queries to run by hand in VS Code (mssql extension) or DBeaver, connected as sa.
   Not run automatically. Run one block at a time and read the results.
   ============================================================================= */

USE StudentSuccess;
GO

/* ---------------------------------------------------------------------------
   1. Look at the data directly
   --------------------------------------------------------------------------- */
SELECT TOP (10) * FROM dbo.Students;
SELECT TOP (10) * FROM dbo.Enrollments;
SELECT * FROM dbo.CoursePresentations ORDER BY ModuleCode, PresentationCode;

-- Row counts for every table
SELECT t.name AS TableName, SUM(p.rows) AS TotalRows
FROM sys.tables AS t
INNER JOIN sys.partitions AS p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
GROUP BY t.name
ORDER BY t.name;

/* ---------------------------------------------------------------------------
   2. Joins and grouping: outcomes by region
   --------------------------------------------------------------------------- */
SELECT s.Region,
       COUNT(*) AS Enrollments,
       CAST(100.0 * SUM(CASE WHEN e.FinalResult IN (N'Pass', N'Distinction') THEN 1 ELSE 0 END) / COUNT(*) AS DECIMAL(5,1)) AS PassRatePct
FROM dbo.Enrollments AS e
INNER JOIN dbo.Students AS s ON s.StudentId = e.StudentId
GROUP BY s.Region
ORDER BY PassRatePct DESC;

-- Average score per assessment type and course
SELECT cp.ModuleCode, a.AssessmentType, COUNT(*) AS Results, CAST(AVG(r.Score) AS DECIMAL(5,1)) AS AvgScore
FROM dbo.AssessmentResults AS r
INNER JOIN dbo.Assessments AS a          ON a.AssessmentId = r.AssessmentId
INNER JOIN dbo.CoursePresentations AS cp ON cp.PresentationId = a.PresentationId
GROUP BY cp.ModuleCode, a.AssessmentType
ORDER BY cp.ModuleCode, a.AssessmentType;

/* ---------------------------------------------------------------------------
   3. The stored procedures the app uses
   --------------------------------------------------------------------------- */
DECLARE @Total INT;
EXEC dbo.usp_Enrollments_Search
    @ModuleCode = 'BBB', @FinalResult = N'Withdrawn',
    @SortColumn = N'AvgScore', @SortDirection = 'DESC',
    @PageNumber = 1, @PageSize = 10,
    @TotalCount = @Total OUTPUT;
SELECT @Total AS MatchingEnrollments;

EXEC dbo.usp_Dashboard_Get;

EXEC dbo.usp_Report_AtRisk @CutoffDay = 60, @ScoreThreshold = 40, @MinMissed = 1, @MaxRows = 20;

-- Detail for the first enrollment (three result sets)
DECLARE @FirstId INT = (SELECT MIN(EnrollmentId) FROM dbo.Enrollments);
EXEC dbo.usp_Enrollment_Get @EnrollmentId = @FirstId;

/* ---------------------------------------------------------------------------
   4. Validation and errors: each of these fails on purpose
   --------------------------------------------------------------------------- */
DECLARE @T INT;
BEGIN TRY
    EXEC dbo.usp_Enrollments_Search @SortColumn = N'Password; DROP TABLE dbo.Students', @TotalCount = @T OUTPUT;
END TRY
BEGIN CATCH
    SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;   -- 50400 Unknown sort column
END CATCH;

/* ---------------------------------------------------------------------------
   5. Least privilege: act as the app's database user
   --------------------------------------------------------------------------- */
EXECUTE AS USER = N'student_app';
    EXEC dbo.usp_Dashboard_Get;                  -- works: EXECUTE is granted
    BEGIN TRY
        SELECT TOP (1) * FROM dbo.Students;      -- fails: no direct read (error 229)
    END TRY
    BEGIN CATCH
        SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH;
    BEGIN TRY
        INSERT INTO dbo.Students (StudentId, Gender, Region, HighestEducation, AgeBand, HasDisability)
        VALUES (9999999, 'F', N'Scotland', N'HE Qualification', N'0-35', 0);   -- fails: no direct write (error 229)
    END TRY
    BEGIN CATCH
        SELECT ERROR_NUMBER() AS ErrorNumber, ERROR_MESSAGE() AS ErrorMessage;
    END CATCH;
REVERT;

/* ---------------------------------------------------------------------------
   6. Recent changes made through the app
   --------------------------------------------------------------------------- */
EXEC dbo.usp_AuditLog_List @Top = 20;

/* ---------------------------------------------------------------------------
   7. Add a student the same way the app's "Add student" button does,
      look at the new rows, then undo it all with ROLLBACK
   --------------------------------------------------------------------------- */
BEGIN TRANSACTION;

    DECLARE @Pid INT = (SELECT TOP (1) PresentationId FROM dbo.CoursePresentations
                        ORDER BY ModuleCode, PresentationCode);
    DECLARE @Region NVARCHAR(60) = (SELECT TOP (1) Region FROM dbo.Students ORDER BY Region);
    DECLARE @Education NVARCHAR(60) = (SELECT TOP (1) HighestEducation FROM dbo.Students ORDER BY HighestEducation);
    DECLARE @AgeBand NVARCHAR(10) = (SELECT TOP (1) AgeBand FROM dbo.Students ORDER BY AgeBand);
    DECLARE @NewStudentId INT, @NewEnrollmentId INT;

    EXEC dbo.usp_Enrollment_Create
        @Gender = 'F', @Region = @Region, @HighestEducation = @Education, @AgeBand = @AgeBand,
        @PresentationId = @Pid, @StudiedCredits = 60, @FinalResult = N'Pass',
        @ChangedBy = N'examples.sql',
        @NewStudentId = @NewStudentId OUTPUT, @NewEnrollmentId = @NewEnrollmentId OUTPUT;

    SELECT * FROM dbo.Students    WHERE StudentId = @NewStudentId;     -- the new student
    SELECT * FROM dbo.Enrollments WHERE StudentId = @NewStudentId;     -- and their enrollment
    SELECT TOP (1) * FROM dbo.AuditLog ORDER BY AuditId DESC;          -- and the audit row

ROLLBACK TRANSACTION;   -- undo: nothing above is kept. Change to COMMIT to keep the student.
