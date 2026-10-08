/* =============================================================================
   01_schema.sql
   Creates the StudentSuccess database and its tables.

   Data model (based on the Open University Learning Analytics Dataset):

     Modules 1--* CoursePresentations 1--* Enrollments *--1 Students
                         |                                  |
                         1--* Assessments 1--* AssessmentResults *--1

   A "module" is a course (AAA, BBB, ...). A "presentation" is one run of it,
   e.g. 2013J = the run starting October 2013 (B = February start).

   WARNING: drops and recreates every table. init-db.sh only runs this script
   on first setup or when RESET_DB=1, so edits made in the app are kept.
   ============================================================================= */

IF DB_ID(N'StudentSuccess') IS NULL
BEGIN
    CREATE DATABASE StudentSuccess;
END;
GO

USE StudentSuccess;
GO

DROP TABLE IF EXISTS dbo.AuditLog;
DROP TABLE IF EXISTS dbo.AssessmentResults;
DROP TABLE IF EXISTS dbo.Assessments;
DROP TABLE IF EXISTS dbo.Enrollments;
DROP TABLE IF EXISTS dbo.Students;
DROP TABLE IF EXISTS dbo.CoursePresentations;
DROP TABLE IF EXISTS dbo.Modules;
GO

/* -----------------------------------------------------------------------------
   Courses
   ----------------------------------------------------------------------------- */
CREATE TABLE dbo.Modules
(
    ModuleCode CHAR(3) NOT NULL,

    CONSTRAINT PK_Modules      PRIMARY KEY CLUSTERED (ModuleCode),
    CONSTRAINT CK_Modules_Code CHECK (ModuleCode LIKE '[A-Z][A-Z][A-Z]')
);
GO

CREATE TABLE dbo.CoursePresentations
(
    PresentationId   INT IDENTITY(1, 1) NOT NULL,
    ModuleCode       CHAR(3)            NOT NULL,
    PresentationCode CHAR(5)            NOT NULL,   -- e.g. 2013J
    LengthDays       SMALLINT           NULL,       -- length of this run in days

    CONSTRAINT PK_CoursePresentations         PRIMARY KEY CLUSTERED (PresentationId),
    CONSTRAINT UQ_CoursePresentations_Codes   UNIQUE (ModuleCode, PresentationCode),
    CONSTRAINT FK_CoursePresentations_Modules FOREIGN KEY (ModuleCode) REFERENCES dbo.Modules (ModuleCode),
    CONSTRAINT CK_CoursePresentations_Code    CHECK (PresentationCode LIKE '[12][0-9][0-9][0-9][A-Z]'),
    CONSTRAINT CK_CoursePresentations_Length  CHECK (LengthDays IS NULL OR LengthDays BETWEEN 1 AND 500)
);
GO

/* -----------------------------------------------------------------------------
   Students (one row per person) and Enrollments (one row per person per course run)
   ----------------------------------------------------------------------------- */
CREATE TABLE dbo.Students
(
    StudentId        INT          NOT NULL,
    Gender           CHAR(1)      NOT NULL,
    Region           NVARCHAR(60) NOT NULL,
    HighestEducation NVARCHAR(60) NOT NULL,
    ImdBand          NVARCHAR(10) NULL,     -- deprivation band of home area; NULL = unknown
    AgeBand          NVARCHAR(10) NOT NULL,
    HasDisability    BIT          NOT NULL CONSTRAINT DF_Students_HasDisability DEFAULT (0),

    CONSTRAINT PK_Students        PRIMARY KEY CLUSTERED (StudentId),
    CONSTRAINT CK_Students_Id     CHECK (StudentId > 0),
    CONSTRAINT CK_Students_Gender CHECK (Gender IN ('F', 'M'))
);
GO

CREATE NONCLUSTERED INDEX IX_Students_Region ON dbo.Students (Region);
GO

CREATE TABLE dbo.Enrollments
(
    EnrollmentId      INT IDENTITY(1, 1) NOT NULL,
    StudentId         INT                NOT NULL,
    PresentationId    INT                NOT NULL,
    PreviousAttempts  TINYINT            NOT NULL CONSTRAINT DF_Enrollments_PreviousAttempts DEFAULT (0),
    StudiedCredits    SMALLINT           NOT NULL,
    RegistrationDay   SMALLINT           NULL,   -- days relative to course start (negative = before start)
    UnregistrationDay SMALLINT           NULL,   -- NULL = did not unregister
    FinalResult       NVARCHAR(12)       NULL,   -- NULL = still in progress
    CreatedAt         DATETIME2(0)       NOT NULL CONSTRAINT DF_Enrollments_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt         DATETIME2(0)       NOT NULL CONSTRAINT DF_Enrollments_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    RowVer            ROWVERSION         NOT NULL,   -- changes on every update; used to detect conflicting edits

    CONSTRAINT PK_Enrollments                      PRIMARY KEY CLUSTERED (EnrollmentId),
    CONSTRAINT UQ_Enrollments_StudentPresentation  UNIQUE (StudentId, PresentationId),
    CONSTRAINT FK_Enrollments_Students             FOREIGN KEY (StudentId) REFERENCES dbo.Students (StudentId),
    CONSTRAINT FK_Enrollments_CoursePresentations  FOREIGN KEY (PresentationId) REFERENCES dbo.CoursePresentations (PresentationId),
    CONSTRAINT CK_Enrollments_PreviousAttempts     CHECK (PreviousAttempts <= 20),
    CONSTRAINT CK_Enrollments_StudiedCredits       CHECK (StudiedCredits BETWEEN 1 AND 1000),
    CONSTRAINT CK_Enrollments_FinalResult          CHECK (FinalResult IN (N'Pass', N'Distinction', N'Fail', N'Withdrawn'))
);
GO

CREATE NONCLUSTERED INDEX IX_Enrollments_PresentationId ON dbo.Enrollments (PresentationId) INCLUDE (StudentId, FinalResult);
CREATE NONCLUSTERED INDEX IX_Enrollments_FinalResult    ON dbo.Enrollments (FinalResult) INCLUDE (PresentationId);
GO

/* -----------------------------------------------------------------------------
   Assessments and results
   ----------------------------------------------------------------------------- */
CREATE TABLE dbo.Assessments
(
    AssessmentId   INT          NOT NULL,
    PresentationId INT          NOT NULL,
    AssessmentType NVARCHAR(4)  NOT NULL,   -- TMA = tutor marked, CMA = computer marked, Exam
    DueDay         SMALLINT     NULL,       -- days after course start; NULL for some final exams
    WeightPercent  DECIMAL(5,2) NOT NULL,

    CONSTRAINT PK_Assessments                     PRIMARY KEY CLUSTERED (AssessmentId),
    CONSTRAINT FK_Assessments_CoursePresentations FOREIGN KEY (PresentationId) REFERENCES dbo.CoursePresentations (PresentationId),
    CONSTRAINT CK_Assessments_Type                CHECK (AssessmentType IN (N'TMA', N'CMA', N'Exam')),
    CONSTRAINT CK_Assessments_Weight              CHECK (WeightPercent BETWEEN 0 AND 100)
);
GO

CREATE NONCLUSTERED INDEX IX_Assessments_PresentationId ON dbo.Assessments (PresentationId) INCLUDE (AssessmentType, DueDay);
GO

CREATE TABLE dbo.AssessmentResults
(
    StudentId    INT          NOT NULL,
    AssessmentId INT          NOT NULL,
    SubmittedDay SMALLINT     NULL,
    IsBanked     BIT          NOT NULL CONSTRAINT DF_AssessmentResults_IsBanked DEFAULT (0),  -- carried over from an earlier attempt
    Score        DECIMAL(5,2) NULL,

    CONSTRAINT PK_AssessmentResults             PRIMARY KEY CLUSTERED (StudentId, AssessmentId),
    CONSTRAINT FK_AssessmentResults_Students    FOREIGN KEY (StudentId) REFERENCES dbo.Students (StudentId),
    CONSTRAINT FK_AssessmentResults_Assessments FOREIGN KEY (AssessmentId) REFERENCES dbo.Assessments (AssessmentId),
    CONSTRAINT CK_AssessmentResults_Score       CHECK (Score IS NULL OR Score BETWEEN 0 AND 100)
);
GO

CREATE NONCLUSTERED INDEX IX_AssessmentResults_AssessmentId ON dbo.AssessmentResults (AssessmentId) INCLUDE (Score);
GO

/* -----------------------------------------------------------------------------
   Audit log: every change made through the app, by whom and when
   ----------------------------------------------------------------------------- */
CREATE TABLE dbo.AuditLog
(
    AuditId    BIGINT IDENTITY(1, 1) NOT NULL,
    ChangedAt  DATETIME2(0)          NOT NULL CONSTRAINT DF_AuditLog_ChangedAt DEFAULT (SYSUTCDATETIME()),
    ChangedBy  NVARCHAR(100)         NOT NULL,
    Action     NVARCHAR(10)          NOT NULL,
    EntityName NVARCHAR(40)          NOT NULL,
    EntityKey  NVARCHAR(40)          NOT NULL,
    Details    NVARCHAR(1000)        NULL,

    CONSTRAINT PK_AuditLog        PRIMARY KEY CLUSTERED (AuditId),
    CONSTRAINT CK_AuditLog_Action CHECK (Action IN (N'Create', N'Update', N'Delete'))
);
GO

PRINT N'01_schema.sql: tables created.';
GO
