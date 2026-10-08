/* =============================================================================
   04_security.sql
   Creates the least-privilege login the web app uses.

   student_app can only EXECUTE stored procedures in the dbo schema. It cannot
   read or change tables directly, touch the staging schema, or alter anything.
   The procedures still work because dbo owns both them and the tables
   (ownership chaining).

   The password comes from sqlcmd at run time, never from this file:
       sqlcmd ... -v AppPassword="<password>" -i 04_security.sql
   ============================================================================= */

USE master;
GO

IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'student_app')
    CREATE LOGIN student_app
        WITH PASSWORD = N'$(AppPassword)',
             DEFAULT_DATABASE = StudentSuccess,
             CHECK_POLICY = ON;
ELSE
    ALTER LOGIN student_app WITH PASSWORD = N'$(AppPassword)';
GO

USE StudentSuccess;
GO

IF USER_ID(N'student_app') IS NULL
    CREATE USER student_app FOR LOGIN student_app;
GO

IF DATABASE_PRINCIPAL_ID(N'app_executor') IS NULL
    CREATE ROLE app_executor;
GO

GRANT EXECUTE ON SCHEMA::dbo TO app_executor;
GO

IF IS_ROLEMEMBER(N'app_executor', N'student_app') = 0
    ALTER ROLE app_executor ADD MEMBER student_app;
GO

PRINT N'04_security.sql: student_app login ready (EXECUTE permission only).';
GO
