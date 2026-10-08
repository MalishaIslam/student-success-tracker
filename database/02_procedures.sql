/* =============================================================================
   02_procedures.sql
   Every read and write the web app performs goes through these procedures.
   The app's database login can only EXECUTE them (see 04_security.sql).

   Safe to re-run at any time (CREATE OR ALTER); init-db.sh runs it on every
   start so procedure changes apply without reloading the data.

   Custom errors raised with THROW, mapped to HTTP status codes by the app:
     50400 invalid input   50404 not found   50409 conflict
   ============================================================================= */

USE StudentSuccess;
GO

/* =============================================================================
   LOOKUPS: values for the filter and form drop-down lists
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_Lookups_Get
AS
BEGIN
    SET NOCOUNT ON;

    SELECT PresentationId, ModuleCode, PresentationCode, LengthDays
    FROM dbo.CoursePresentations
    ORDER BY ModuleCode, PresentationCode;

    SELECT DISTINCT Region FROM dbo.Students ORDER BY Region;

    SELECT DISTINCT AgeBand FROM dbo.Students ORDER BY AgeBand;

    SELECT DISTINCT HighestEducation FROM dbo.Students ORDER BY HighestEducation;

    SELECT DISTINCT ImdBand FROM dbo.Students WHERE ImdBand IS NOT NULL ORDER BY ImdBand;
END;
GO

/* =============================================================================
   SEARCH: filter + sort + page, used by the Students page, the API and CSV export
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_Enrollments_Search
    @StudentId        INT          = NULL,
    @ModuleCode       CHAR(3)      = NULL,
    @PresentationCode CHAR(5)      = NULL,
    @Region           NVARCHAR(60) = NULL,
    @AgeBand          NVARCHAR(10) = NULL,
    @FinalResult      NVARCHAR(12) = NULL,   -- 'Pass', 'Distinction', 'Fail', 'Withdrawn' or 'In progress'
    @SortColumn       NVARCHAR(20) = N'StudentId',
    @SortDirection    CHAR(4)      = 'ASC',
    @PageNumber       INT          = 1,
    @PageSize         INT          = 25,
    @TotalCount       INT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Sorting is chosen from a fixed list (no dynamic SQL), so input can never become code
    IF @SortColumn NOT IN (N'StudentId', N'Course', N'Region', N'AgeBand', N'Credits', N'AvgScore', N'Submitted', N'FinalResult')
        THROW 50400, N'Unknown sort column.', 1;

    IF @SortDirection NOT IN ('ASC', 'DESC')
        THROW 50400, N'Sort direction must be ASC or DESC.', 1;

    IF @PageNumber < 1 OR @PageSize NOT BETWEEN 1 AND 100000
        THROW 50400, N'Invalid page number or page size.', 1;

    CREATE TABLE #Filtered
    (
        EnrollmentId   INT          NOT NULL PRIMARY KEY,
        StudentId      INT          NOT NULL,
        PresentationId INT          NOT NULL,
        ModuleCode     CHAR(3)      NOT NULL,
        PresentationCode CHAR(5)    NOT NULL,
        Gender         CHAR(1)      NOT NULL,
        Region         NVARCHAR(60) NOT NULL,
        AgeBand        NVARCHAR(10) NOT NULL,
        StudiedCredits SMALLINT     NOT NULL,
        FinalResult    NVARCHAR(12) NULL
    );

    INSERT INTO #Filtered
    SELECT e.EnrollmentId, e.StudentId, e.PresentationId, cp.ModuleCode, cp.PresentationCode,
           s.Gender, s.Region, s.AgeBand, e.StudiedCredits, e.FinalResult
    FROM dbo.Enrollments AS e
    INNER JOIN dbo.Students AS s             ON s.StudentId = e.StudentId
    INNER JOIN dbo.CoursePresentations AS cp ON cp.PresentationId = e.PresentationId
    WHERE (@StudentId IS NULL        OR e.StudentId = @StudentId)
      AND (@ModuleCode IS NULL       OR cp.ModuleCode = @ModuleCode)
      AND (@PresentationCode IS NULL OR cp.PresentationCode = @PresentationCode)
      AND (@Region IS NULL           OR s.Region = @Region)
      AND (@AgeBand IS NULL          OR s.AgeBand = @AgeBand)
      AND (@FinalResult IS NULL
           OR (@FinalResult = N'In progress' AND e.FinalResult IS NULL)
           OR e.FinalResult = @FinalResult)
    OPTION (RECOMPILE);   -- optional filters: build a plan for the filters actually used

    SET @TotalCount = @@ROWCOUNT;

    SELECT f.EnrollmentId, f.StudentId, f.ModuleCode, f.PresentationCode, f.Gender, f.Region,
           f.AgeBand, f.StudiedCredits, sc.AvgScore, sc.SubmittedCount, f.FinalResult
    FROM #Filtered AS f
    OUTER APPLY
    (
        SELECT CAST(AVG(r.Score) AS DECIMAL(5,1)) AS AvgScore,
               COUNT(*)                          AS SubmittedCount
        FROM dbo.AssessmentResults AS r
        INNER JOIN dbo.Assessments AS a ON a.AssessmentId = r.AssessmentId
        WHERE r.StudentId = f.StudentId
          AND a.PresentationId = f.PresentationId
    ) AS sc
    ORDER BY
        CASE WHEN @SortColumn = N'StudentId'   AND @SortDirection = 'ASC'  THEN f.StudentId END ASC,
        CASE WHEN @SortColumn = N'StudentId'   AND @SortDirection = 'DESC' THEN f.StudentId END DESC,
        CASE WHEN @SortColumn = N'Course'      AND @SortDirection = 'ASC'  THEN f.ModuleCode + f.PresentationCode END ASC,
        CASE WHEN @SortColumn = N'Course'      AND @SortDirection = 'DESC' THEN f.ModuleCode + f.PresentationCode END DESC,
        CASE WHEN @SortColumn = N'Region'      AND @SortDirection = 'ASC'  THEN f.Region END ASC,
        CASE WHEN @SortColumn = N'Region'      AND @SortDirection = 'DESC' THEN f.Region END DESC,
        CASE WHEN @SortColumn = N'AgeBand'     AND @SortDirection = 'ASC'  THEN f.AgeBand END ASC,
        CASE WHEN @SortColumn = N'AgeBand'     AND @SortDirection = 'DESC' THEN f.AgeBand END DESC,
        CASE WHEN @SortColumn = N'Credits'     AND @SortDirection = 'ASC'  THEN f.StudiedCredits END ASC,
        CASE WHEN @SortColumn = N'Credits'     AND @SortDirection = 'DESC' THEN f.StudiedCredits END DESC,
        CASE WHEN @SortColumn = N'AvgScore'    AND @SortDirection = 'ASC'  THEN sc.AvgScore END ASC,
        CASE WHEN @SortColumn = N'AvgScore'    AND @SortDirection = 'DESC' THEN sc.AvgScore END DESC,
        CASE WHEN @SortColumn = N'Submitted'   AND @SortDirection = 'ASC'  THEN sc.SubmittedCount END ASC,
        CASE WHEN @SortColumn = N'Submitted'   AND @SortDirection = 'DESC' THEN sc.SubmittedCount END DESC,
        CASE WHEN @SortColumn = N'FinalResult' AND @SortDirection = 'ASC'  THEN f.FinalResult END ASC,
        CASE WHEN @SortColumn = N'FinalResult' AND @SortDirection = 'DESC' THEN f.FinalResult END DESC,
        f.EnrollmentId ASC   -- tie-breaker so paging is stable
    OFFSET (@PageNumber - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END;
GO

/* =============================================================================
   DETAIL: one enrollment, its student, its assessments and the student's other courses
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_Enrollment_Get
    @EnrollmentId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @StudentId INT, @PresentationId INT;

    SELECT @StudentId = StudentId, @PresentationId = PresentationId
    FROM dbo.Enrollments
    WHERE EnrollmentId = @EnrollmentId;

    -- 1. Enrollment and student (empty when not found)
    SELECT e.EnrollmentId, s.StudentId, s.Gender, s.Region, s.HighestEducation, s.ImdBand, s.AgeBand,
           s.HasDisability, e.PresentationId, cp.ModuleCode, cp.PresentationCode, cp.LengthDays,
           e.PreviousAttempts, e.StudiedCredits, e.RegistrationDay, e.UnregistrationDay,
           e.FinalResult, e.UpdatedAt, e.RowVer
    FROM dbo.Enrollments AS e
    INNER JOIN dbo.Students AS s             ON s.StudentId = e.StudentId
    INNER JOIN dbo.CoursePresentations AS cp ON cp.PresentationId = e.PresentationId
    WHERE e.EnrollmentId = @EnrollmentId;

    -- 2. Every assessment in this course run, with the student's result if there is one
    SELECT a.AssessmentId, a.AssessmentType, a.DueDay, a.WeightPercent,
           r.SubmittedDay, r.IsBanked, r.Score,
           CAST(CASE WHEN r.AssessmentId IS NULL THEN 0 ELSE 1 END AS BIT) AS HasResult
    FROM dbo.Assessments AS a
    LEFT JOIN dbo.AssessmentResults AS r
           ON r.AssessmentId = a.AssessmentId AND r.StudentId = @StudentId
    WHERE a.PresentationId = @PresentationId
    ORDER BY CASE WHEN a.DueDay IS NULL THEN 1 ELSE 0 END, a.DueDay, a.AssessmentId;

    -- 3. The student's other course runs
    SELECT e.EnrollmentId, cp.ModuleCode, cp.PresentationCode, e.FinalResult
    FROM dbo.Enrollments AS e
    INNER JOIN dbo.CoursePresentations AS cp ON cp.PresentationId = e.PresentationId
    WHERE e.StudentId = @StudentId
      AND e.EnrollmentId <> @EnrollmentId
    ORDER BY cp.PresentationCode, cp.ModuleCode;
END;
GO

/* =============================================================================
   VALIDATION shared by create and update
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_Internal_ValidateEnrollment
    @Gender            CHAR(1),
    @Region            NVARCHAR(60),
    @HighestEducation  NVARCHAR(60),
    @ImdBand           NVARCHAR(10),
    @AgeBand           NVARCHAR(10),
    @PreviousAttempts  TINYINT,
    @StudiedCredits    SMALLINT,
    @RegistrationDay   SMALLINT,
    @UnregistrationDay SMALLINT,
    @FinalResult       NVARCHAR(12)
AS
BEGIN
    SET NOCOUNT ON;

    IF @Gender IS NULL OR @Gender NOT IN ('F', 'M')
        THROW 50400, N'Gender must be F or M.', 1;

    -- Category values must be ones that already exist in the data, so typos
    -- can't create new categories
    IF NOT EXISTS (SELECT 1 FROM dbo.Students WHERE Region = @Region)
        THROW 50400, N'Choose a region from the list.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Students WHERE HighestEducation = @HighestEducation)
        THROW 50400, N'Choose an education level from the list.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.Students WHERE AgeBand = @AgeBand)
        THROW 50400, N'Choose an age band from the list.', 1;

    IF @ImdBand IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Students WHERE ImdBand = @ImdBand)
        THROW 50400, N'Choose a deprivation band from the list.', 1;

    IF @PreviousAttempts IS NULL OR @PreviousAttempts > 20
        THROW 50400, N'Previous attempts must be between 0 and 20.', 1;

    IF @StudiedCredits IS NULL OR @StudiedCredits NOT BETWEEN 1 AND 1000
        THROW 50400, N'Studied credits must be between 1 and 1000.', 1;

    IF @RegistrationDay IS NOT NULL AND @RegistrationDay NOT BETWEEN -400 AND 400
        THROW 50400, N'Registration day must be between -400 and 400.', 1;

    IF @UnregistrationDay IS NOT NULL AND @UnregistrationDay NOT BETWEEN -400 AND 800
        THROW 50400, N'Unregistration day must be between -400 and 800.', 1;

    IF @RegistrationDay IS NOT NULL AND @UnregistrationDay IS NOT NULL AND @UnregistrationDay < @RegistrationDay
        THROW 50400, N'Unregistration day cannot be before the registration day.', 1;

    IF @FinalResult IS NOT NULL AND @FinalResult NOT IN (N'Pass', N'Distinction', N'Fail', N'Withdrawn')
        THROW 50400, N'Final result must be Pass, Distinction, Fail, Withdrawn or empty.', 1;
END;
GO

/* =============================================================================
   CREATE: a new student and their first enrollment, in one transaction
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_Enrollment_Create
    @Gender            CHAR(1),
    @Region            NVARCHAR(60),
    @HighestEducation  NVARCHAR(60),
    @ImdBand           NVARCHAR(10) = NULL,
    @AgeBand           NVARCHAR(10),
    @HasDisability     BIT          = 0,
    @PresentationId    INT,
    @PreviousAttempts  TINYINT      = 0,
    @StudiedCredits    SMALLINT,
    @RegistrationDay   SMALLINT     = NULL,
    @UnregistrationDay SMALLINT     = NULL,
    @FinalResult       NVARCHAR(12) = NULL,
    @ChangedBy         NVARCHAR(100),
    @NewStudentId      INT          OUTPUT,
    @NewEnrollmentId   INT          OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.usp_Internal_ValidateEnrollment
        @Gender, @Region, @HighestEducation, @ImdBand, @AgeBand,
        @PreviousAttempts, @StudiedCredits, @RegistrationDay, @UnregistrationDay, @FinalResult;

    IF NOT EXISTS (SELECT 1 FROM dbo.CoursePresentations WHERE PresentationId = @PresentationId)
        THROW 50400, N'Choose a course from the list.', 1;

    BEGIN TRANSACTION;

        -- UPDLOCK + HOLDLOCK: two people adding at once can't get the same new ID
        SELECT @NewStudentId = ISNULL(MAX(StudentId), 0) + 1
        FROM dbo.Students WITH (UPDLOCK, HOLDLOCK);

        INSERT INTO dbo.Students (StudentId, Gender, Region, HighestEducation, ImdBand, AgeBand, HasDisability)
        VALUES (@NewStudentId, @Gender, @Region, @HighestEducation, @ImdBand, @AgeBand, ISNULL(@HasDisability, 0));

        INSERT INTO dbo.Enrollments (StudentId, PresentationId, PreviousAttempts, StudiedCredits,
                                     RegistrationDay, UnregistrationDay, FinalResult)
        VALUES (@NewStudentId, @PresentationId, @PreviousAttempts, @StudiedCredits,
                @RegistrationDay, @UnregistrationDay, @FinalResult);

        SET @NewEnrollmentId = CAST(SCOPE_IDENTITY() AS INT);

        INSERT INTO dbo.AuditLog (ChangedBy, Action, EntityName, EntityKey, Details)
        SELECT @ChangedBy, N'Create', N'Enrollment', CAST(@NewEnrollmentId AS NVARCHAR(40)),
               CONCAT(N'New student ', @NewStudentId, N' enrolled in ', ModuleCode, N' ', PresentationCode)
        FROM dbo.CoursePresentations
        WHERE PresentationId = @PresentationId;

    COMMIT TRANSACTION;
END;
GO

/* =============================================================================
   UPDATE: student details + enrollment, with a check for conflicting edits
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_Enrollment_Update
    @EnrollmentId      INT,
    @RowVer            BINARY(8),       -- the version the user loaded; must still match
    @Gender            CHAR(1),
    @Region            NVARCHAR(60),
    @HighestEducation  NVARCHAR(60),
    @ImdBand           NVARCHAR(10) = NULL,
    @AgeBand           NVARCHAR(10),
    @HasDisability     BIT          = 0,
    @PreviousAttempts  TINYINT      = 0,
    @StudiedCredits    SMALLINT,
    @RegistrationDay   SMALLINT     = NULL,
    @UnregistrationDay SMALLINT     = NULL,
    @FinalResult       NVARCHAR(12) = NULL,
    @ChangedBy         NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.usp_Internal_ValidateEnrollment
        @Gender, @Region, @HighestEducation, @ImdBand, @AgeBand,
        @PreviousAttempts, @StudiedCredits, @RegistrationDay, @UnregistrationDay, @FinalResult;

    BEGIN TRANSACTION;

        DECLARE @StudentId INT, @CurrentRowVer BINARY(8),
                @OldResult NVARCHAR(12), @OldCredits SMALLINT, @OldUnreg SMALLINT,
                @OldRegion NVARCHAR(60), @OldAgeBand NVARCHAR(10);

        SELECT @StudentId = e.StudentId, @CurrentRowVer = e.RowVer,
               @OldResult = e.FinalResult, @OldCredits = e.StudiedCredits, @OldUnreg = e.UnregistrationDay,
               @OldRegion = s.Region, @OldAgeBand = s.AgeBand
        FROM dbo.Enrollments AS e WITH (UPDLOCK)
        INNER JOIN dbo.Students AS s ON s.StudentId = e.StudentId
        WHERE e.EnrollmentId = @EnrollmentId;

        IF @StudentId IS NULL
            THROW 50404, N'Enrollment not found.', 1;

        IF @CurrentRowVer <> @RowVer
            THROW 50409, N'Someone else changed this record after you opened it. Reload the page to see their changes.', 1;

        UPDATE dbo.Students
        SET Gender = @Gender, Region = @Region, HighestEducation = @HighestEducation,
            ImdBand = @ImdBand, AgeBand = @AgeBand, HasDisability = ISNULL(@HasDisability, 0)
        WHERE StudentId = @StudentId;

        UPDATE dbo.Enrollments
        SET PreviousAttempts = @PreviousAttempts, StudiedCredits = @StudiedCredits,
            RegistrationDay = @RegistrationDay, UnregistrationDay = @UnregistrationDay,
            FinalResult = @FinalResult, UpdatedAt = SYSUTCDATETIME()
        WHERE EnrollmentId = @EnrollmentId;

        -- Record what changed (CONCAT_WS skips the NULLs for unchanged fields)
        INSERT INTO dbo.AuditLog (ChangedBy, Action, EntityName, EntityKey, Details)
        VALUES (@ChangedBy, N'Update', N'Enrollment', CAST(@EnrollmentId AS NVARCHAR(40)),
            NULLIF(CONCAT_WS(N'; ',
                CASE WHEN ISNULL(@OldResult, N'') <> ISNULL(@FinalResult, N'')
                     THEN CONCAT(N'Result: ', ISNULL(@OldResult, N'in progress'), N' -> ', ISNULL(@FinalResult, N'in progress')) END,
                CASE WHEN @OldCredits <> @StudiedCredits
                     THEN CONCAT(N'Credits: ', @OldCredits, N' -> ', @StudiedCredits) END,
                CASE WHEN ISNULL(@OldUnreg, -9999) <> ISNULL(@UnregistrationDay, -9999)
                     THEN CONCAT(N'Unregistered day: ', ISNULL(CAST(@OldUnreg AS NVARCHAR(10)), N'none'), N' -> ',
                                 ISNULL(CAST(@UnregistrationDay AS NVARCHAR(10)), N'none')) END,
                CASE WHEN @OldRegion <> @Region     THEN CONCAT(N'Region: ', @OldRegion, N' -> ', @Region) END,
                CASE WHEN @OldAgeBand <> @AgeBand   THEN CONCAT(N'Age band: ', @OldAgeBand, N' -> ', @AgeBand) END
            ), N''));

    COMMIT TRANSACTION;
END;
GO

/* =============================================================================
   DELETE: an enrollment, its results, and the student if this was their only course
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_Enrollment_Delete
    @EnrollmentId   INT,
    @ChangedBy      NVARCHAR(100),
    @StudentDeleted BIT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @StudentDeleted = 0;

    BEGIN TRANSACTION;

        DECLARE @StudentId INT, @PresentationId INT, @Course NVARCHAR(20);

        SELECT @StudentId = e.StudentId, @PresentationId = e.PresentationId,
               @Course = CONCAT(cp.ModuleCode, N' ', cp.PresentationCode)
        FROM dbo.Enrollments AS e WITH (UPDLOCK)
        INNER JOIN dbo.CoursePresentations AS cp ON cp.PresentationId = e.PresentationId
        WHERE e.EnrollmentId = @EnrollmentId;

        IF @StudentId IS NULL
            THROW 50404, N'Enrollment not found.', 1;

        -- Children first: results for this course run, then the enrollment
        DELETE r
        FROM dbo.AssessmentResults AS r
        INNER JOIN dbo.Assessments AS a ON a.AssessmentId = r.AssessmentId
        WHERE r.StudentId = @StudentId
          AND a.PresentationId = @PresentationId;

        DELETE FROM dbo.Enrollments WHERE EnrollmentId = @EnrollmentId;

        IF NOT EXISTS (SELECT 1 FROM dbo.Enrollments WHERE StudentId = @StudentId)
        BEGIN
            DELETE FROM dbo.AssessmentResults WHERE StudentId = @StudentId;
            DELETE FROM dbo.Students WHERE StudentId = @StudentId;
            SET @StudentDeleted = 1;
        END;

        INSERT INTO dbo.AuditLog (ChangedBy, Action, EntityName, EntityKey, Details)
        VALUES (@ChangedBy, N'Delete', N'Enrollment', CAST(@EnrollmentId AS NVARCHAR(40)),
                CONCAT(N'Student ', @StudentId, N' removed from ', @Course,
                       CASE WHEN @StudentDeleted = 1 THEN N' (student record deleted: no other courses)' END));

    COMMIT TRANSACTION;
END;
GO

/* =============================================================================
   SCORES: add or change one assessment result, or remove it
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_AssessmentResult_Save
    @EnrollmentId INT,
    @AssessmentId INT,
    @SubmittedDay SMALLINT     = NULL,
    @Score        DECIMAL(5,2) = NULL,
    @IsBanked     BIT          = 0,
    @ChangedBy    NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Score IS NOT NULL AND @Score NOT BETWEEN 0 AND 100
        THROW 50400, N'Score must be between 0 and 100.', 1;

    IF @SubmittedDay IS NOT NULL AND @SubmittedDay NOT BETWEEN -400 AND 800
        THROW 50400, N'Submitted day must be between -400 and 800.', 1;

    BEGIN TRANSACTION;

        DECLARE @StudentId INT, @OldScore DECIMAL(5,2), @Existed BIT = 0;

        SELECT @StudentId = e.StudentId
        FROM dbo.Enrollments AS e
        INNER JOIN dbo.Assessments AS a ON a.PresentationId = e.PresentationId
        WHERE e.EnrollmentId = @EnrollmentId
          AND a.AssessmentId = @AssessmentId;

        IF @StudentId IS NULL
            THROW 50404, N'That assessment does not belong to this enrollment.', 1;

        SELECT @OldScore = Score, @Existed = 1
        FROM dbo.AssessmentResults WITH (UPDLOCK, HOLDLOCK)
        WHERE StudentId = @StudentId AND AssessmentId = @AssessmentId;

        IF @Existed = 1
            UPDATE dbo.AssessmentResults
            SET SubmittedDay = @SubmittedDay, Score = @Score, IsBanked = ISNULL(@IsBanked, 0)
            WHERE StudentId = @StudentId AND AssessmentId = @AssessmentId;
        ELSE
            INSERT INTO dbo.AssessmentResults (StudentId, AssessmentId, SubmittedDay, IsBanked, Score)
            VALUES (@StudentId, @AssessmentId, @SubmittedDay, ISNULL(@IsBanked, 0), @Score);

        INSERT INTO dbo.AuditLog (ChangedBy, Action, EntityName, EntityKey, Details)
        VALUES (@ChangedBy,
                CASE WHEN @Existed = 1 THEN N'Update' ELSE N'Create' END,
                N'AssessmentResult',
                CONCAT(@StudentId, N'/', @AssessmentId),
                CONCAT(N'Score: ', CASE WHEN @Existed = 1 THEN ISNULL(CAST(@OldScore AS NVARCHAR(10)), N'none') ELSE N'none' END,
                       N' -> ', ISNULL(CAST(@Score AS NVARCHAR(10)), N'none')));

    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_AssessmentResult_Delete
    @EnrollmentId INT,
    @AssessmentId INT,
    @ChangedBy    NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

        DECLARE @StudentId INT =
        (
            SELECT e.StudentId
            FROM dbo.Enrollments AS e
            INNER JOIN dbo.Assessments AS a ON a.PresentationId = e.PresentationId
            WHERE e.EnrollmentId = @EnrollmentId AND a.AssessmentId = @AssessmentId
        );

        DELETE FROM dbo.AssessmentResults
        WHERE StudentId = @StudentId AND AssessmentId = @AssessmentId;

        IF @@ROWCOUNT = 0
            THROW 50404, N'There is no result to delete for that assessment.', 1;

        INSERT INTO dbo.AuditLog (ChangedBy, Action, EntityName, EntityKey, Details)
        VALUES (@ChangedBy, N'Delete', N'AssessmentResult', CONCAT(@StudentId, N'/', @AssessmentId), N'Result removed');

    COMMIT TRANSACTION;
END;
GO

/* =============================================================================
   DASHBOARD: totals and outcomes per course run
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_Dashboard_Get
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Totals
    SELECT st.Students, en.Enrollments, cr.CourseRuns, ar.AssessmentResults,
           en.PassRatePct, en.WithdrawalRatePct
    FROM (SELECT COUNT(*) AS Students FROM dbo.Students) AS st
    CROSS JOIN (SELECT COUNT(*) AS CourseRuns FROM dbo.CoursePresentations) AS cr
    CROSS JOIN (SELECT COUNT(*) AS AssessmentResults FROM dbo.AssessmentResults) AS ar
    CROSS JOIN
    (
        SELECT COUNT(*) AS Enrollments,
               CAST(100.0 * SUM(CASE WHEN FinalResult IN (N'Pass', N'Distinction') THEN 1 ELSE 0 END)
                    / NULLIF(SUM(CASE WHEN FinalResult IS NOT NULL THEN 1 ELSE 0 END), 0) AS DECIMAL(5,1)) AS PassRatePct,
               CAST(100.0 * SUM(CASE WHEN FinalResult = N'Withdrawn' THEN 1 ELSE 0 END)
                    / NULLIF(SUM(CASE WHEN FinalResult IS NOT NULL THEN 1 ELSE 0 END), 0) AS DECIMAL(5,1)) AS WithdrawalRatePct
        FROM dbo.Enrollments
    ) AS en;

    -- 2. Outcomes for each course run
    SELECT cp.PresentationId, cp.ModuleCode, cp.PresentationCode,
           COUNT(e.EnrollmentId)                                             AS Enrollments,
           SUM(CASE WHEN e.FinalResult = N'Distinction' THEN 1 ELSE 0 END)   AS Distinction,
           SUM(CASE WHEN e.FinalResult = N'Pass'        THEN 1 ELSE 0 END)   AS Pass,
           SUM(CASE WHEN e.FinalResult = N'Fail'        THEN 1 ELSE 0 END)   AS Fail,
           SUM(CASE WHEN e.FinalResult = N'Withdrawn'   THEN 1 ELSE 0 END)   AS Withdrawn,
           SUM(CASE WHEN e.EnrollmentId IS NOT NULL AND e.FinalResult IS NULL THEN 1 ELSE 0 END) AS InProgress,
           CAST(100.0 * SUM(CASE WHEN e.FinalResult IN (N'Pass', N'Distinction') THEN 1 ELSE 0 END)
                / NULLIF(SUM(CASE WHEN e.FinalResult IS NOT NULL THEN 1 ELSE 0 END), 0) AS DECIMAL(5,1)) AS PassRatePct
    FROM dbo.CoursePresentations AS cp
    LEFT JOIN dbo.Enrollments AS e ON e.PresentationId = cp.PresentationId
    GROUP BY cp.PresentationId, cp.ModuleCode, cp.PresentationCode
    ORDER BY cp.ModuleCode, cp.PresentationCode;
END;
GO

/* =============================================================================
   AT-RISK REPORT
   Among students still registered on @CutoffDay, flag those who by that day had
   missed at least @MinMissed coursework deadlines or were averaging below
   @ScoreThreshold. The summary compares final outcomes of flagged vs others,
   which shows how useful the early-warning rule would have been.
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_Report_AtRisk
    @PresentationId INT          = NULL,
    @CutoffDay      SMALLINT     = 60,
    @ScoreThreshold DECIMAL(5,2) = 40,
    @MinMissed      TINYINT      = 1,
    @MaxRows        INT          = 500
AS
BEGIN
    SET NOCOUNT ON;

    IF @CutoffDay NOT BETWEEN 1 AND 400
        THROW 50400, N'Cutoff day must be between 1 and 400.', 1;
    IF @ScoreThreshold NOT BETWEEN 0 AND 100
        THROW 50400, N'Score threshold must be between 0 and 100.', 1;
    IF @MinMissed NOT BETWEEN 1 AND 20
        THROW 50400, N'Missed deadlines must be between 1 and 20.', 1;
    IF @MaxRows NOT BETWEEN 1 AND 100000
        THROW 50400, N'Row limit must be between 1 and 100000.', 1;

    CREATE TABLE #Signals
    (
        EnrollmentId   INT          NOT NULL PRIMARY KEY,
        StudentId      INT          NOT NULL,
        PresentationId INT          NOT NULL,
        DueCount       INT          NOT NULL,
        SubmittedCount INT          NOT NULL,
        AvgScore       DECIMAL(5,1) NULL,
        FinalResult    NVARCHAR(12) NULL,
        IsFlagged      BIT          NOT NULL DEFAULT 0
    );

    -- Coursework (not exams) due on or before the cutoff, per enrollment
    INSERT INTO #Signals (EnrollmentId, StudentId, PresentationId, DueCount, SubmittedCount, AvgScore, FinalResult)
    SELECT e.EnrollmentId, e.StudentId, e.PresentationId,
           COUNT(a.AssessmentId),
           COUNT(r.AssessmentId),
           CAST(AVG(r.Score) AS DECIMAL(5,1)),
           e.FinalResult
    FROM dbo.Enrollments AS e
    LEFT JOIN dbo.Assessments AS a
           ON a.PresentationId = e.PresentationId
          AND a.AssessmentType <> N'Exam'
          AND a.DueDay <= @CutoffDay
    LEFT JOIN dbo.AssessmentResults AS r
           ON r.AssessmentId = a.AssessmentId
          AND r.StudentId = e.StudentId
    WHERE (@PresentationId IS NULL OR e.PresentationId = @PresentationId)
      AND (e.UnregistrationDay IS NULL OR e.UnregistrationDay > @CutoffDay)   -- still registered at cutoff
    GROUP BY e.EnrollmentId, e.StudentId, e.PresentationId, e.FinalResult
    OPTION (RECOMPILE);

    UPDATE #Signals
    SET IsFlagged = 1
    WHERE (DueCount - SubmittedCount) >= @MinMissed
       OR AvgScore < @ScoreThreshold;

    -- 1. Flagged students, most missed deadlines first
    SELECT TOP (@MaxRows)
           s.EnrollmentId, s.StudentId, cp.ModuleCode, cp.PresentationCode,
           s.DueCount, s.SubmittedCount, s.DueCount - s.SubmittedCount AS MissedCount,
           s.AvgScore, s.FinalResult
    FROM #Signals AS s
    INNER JOIN dbo.CoursePresentations AS cp ON cp.PresentationId = s.PresentationId
    WHERE s.IsFlagged = 1
    ORDER BY MissedCount DESC, s.AvgScore ASC, s.EnrollmentId;

    -- 2. How flagged and not-flagged students actually finished
    SELECT IsFlagged,
           COUNT(*)                                                                   AS Students,
           SUM(CASE WHEN FinalResult IN (N'Pass', N'Distinction') THEN 1 ELSE 0 END)  AS Passed,
           SUM(CASE WHEN FinalResult IN (N'Fail', N'Withdrawn') THEN 1 ELSE 0 END)    AS FailedOrWithdrew,
           SUM(CASE WHEN FinalResult IS NULL THEN 1 ELSE 0 END)                       AS InProgress,
           CAST(100.0 * SUM(CASE WHEN FinalResult IN (N'Fail', N'Withdrawn') THEN 1 ELSE 0 END)
                / NULLIF(SUM(CASE WHEN FinalResult IS NOT NULL THEN 1 ELSE 0 END), 0) AS DECIMAL(5,1)) AS FailedOrWithdrewPct
    FROM #Signals
    GROUP BY IsFlagged
    ORDER BY IsFlagged DESC;
END;
GO

/* =============================================================================
   AUDIT LOG: most recent changes first
   ============================================================================= */
CREATE OR ALTER PROCEDURE dbo.usp_AuditLog_List
    @Top INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    IF @Top NOT BETWEEN 1 AND 1000
        THROW 50400, N'Top must be between 1 and 1000.', 1;

    SELECT TOP (@Top) AuditId, ChangedAt, ChangedBy, Action, EntityName, EntityKey, Details
    FROM dbo.AuditLog
    ORDER BY AuditId DESC;
END;
GO

PRINT N'02_procedures.sql: stored procedures created.';
GO
