using System.Data;
using System.Data.Common;
using StudentSuccess.Web.Models;

namespace StudentSuccess.Web.Data;

/// <summary>
/// Calls the SQL Server stored procedures with plain ADO.NET.
/// Each method: open a pooled connection, run one procedure, map rows to records.
/// </summary>
public sealed class SqlStudentRepository(IDbConnectionFactory connections) : IStudentRepository
{
    private const int TimeoutSeconds = 30;

    public Task<Lookups> GetLookupsAsync(CancellationToken ct) =>
        RunAsync("dbo.usp_Lookups_Get", null, async cmd =>
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var presentations = await r.ReadAllAsync(x => new PresentationOption(
                x.Int("PresentationId"), x.Str("ModuleCode"), x.Str("PresentationCode"), x.IntOrNull("LengthDays")), ct);
            await r.NextResultAsync(ct);
            var regions = await r.ReadAllAsync(x => x.Str("Region"), ct);
            await r.NextResultAsync(ct);
            var ageBands = await r.ReadAllAsync(x => x.Str("AgeBand"), ct);
            await r.NextResultAsync(ct);
            var education = await r.ReadAllAsync(x => x.Str("HighestEducation"), ct);
            await r.NextResultAsync(ct);
            var imd = await r.ReadAllAsync(x => x.Str("ImdBand"), ct);
            return new Lookups(presentations, regions, ageBands, education, imd);
        }, ct);

    public Task<PagedResult<EnrollmentRow>> SearchAsync(EnrollmentSearch s, CancellationToken ct) =>
        RunAsync("dbo.usp_Enrollments_Search", cmd =>
        {
            cmd.Add("@StudentId", DbType.Int32, s.StudentIdValue);
            cmd.Add("@ModuleCode", DbType.AnsiStringFixedLength, s.Module, 3);
            cmd.Add("@PresentationCode", DbType.AnsiStringFixedLength, s.Presentation, 5);
            cmd.Add("@Region", DbType.String, s.Region, 60);
            cmd.Add("@AgeBand", DbType.String, s.AgeBand, 10);
            cmd.Add("@FinalResult", DbType.String, s.Result, 12);
            cmd.Add("@SortColumn", DbType.String, s.Sort, 20);
            cmd.Add("@SortDirection", DbType.AnsiString, s.IsDescending ? "DESC" : "ASC", 4);
            cmd.Add("@PageNumber", DbType.Int32, s.Page);
            cmd.Add("@PageSize", DbType.Int32, s.PageSize);
            cmd.Add("@TotalCount", DbType.Int32, null, direction: ParameterDirection.Output);
        },
        async cmd =>
        {
            List<EnrollmentRow> rows;
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                rows = await r.ReadAllAsync(x => new EnrollmentRow(
                    x.Int("EnrollmentId"), x.Int("StudentId"), x.Str("ModuleCode"), x.Str("PresentationCode"),
                    x.Str("Gender"), x.Str("Region"), x.Str("AgeBand"), x.Int("StudiedCredits"),
                    x.DecOrNull("AvgScore"), x.Int("SubmittedCount"), x.StrOrNull("FinalResult")), ct);
            }
            // Output parameters are only available after the reader is closed
            var total = Convert.ToInt32(cmd.Parameters["@TotalCount"].Value);
            return new PagedResult<EnrollmentRow>(rows, total, s.Page, s.PageSize);
        }, ct);

    public Task<EnrollmentDetail?> GetEnrollmentAsync(int enrollmentId, CancellationToken ct) =>
        RunAsync("dbo.usp_Enrollment_Get", cmd => cmd.Add("@EnrollmentId", DbType.Int32, enrollmentId),
        async cmd =>
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
            {
                return null;
            }

            var head = new
            {
                EnrollmentId = r.Int("EnrollmentId"),
                StudentId = r.Int("StudentId"),
                Gender = r.Str("Gender"),
                Region = r.Str("Region"),
                HighestEducation = r.Str("HighestEducation"),
                ImdBand = r.StrOrNull("ImdBand"),
                AgeBand = r.Str("AgeBand"),
                HasDisability = r.Bool("HasDisability"),
                PresentationId = r.Int("PresentationId"),
                ModuleCode = r.Str("ModuleCode"),
                PresentationCode = r.Str("PresentationCode"),
                LengthDays = r.IntOrNull("LengthDays"),
                PreviousAttempts = r.Int("PreviousAttempts"),
                StudiedCredits = r.Int("StudiedCredits"),
                RegistrationDay = r.IntOrNull("RegistrationDay"),
                UnregistrationDay = r.IntOrNull("UnregistrationDay"),
                FinalResult = r.StrOrNull("FinalResult"),
                UpdatedAt = r.UtcDate("UpdatedAt"),
                RowVer = r.Bytes("RowVer"),
            };

            await r.NextResultAsync(ct);
            var assessments = await r.ReadAllAsync(x => new AssessmentRow(
                x.Int("AssessmentId"), x.Str("AssessmentType"), x.IntOrNull("DueDay"), x.Dec("WeightPercent"),
                x.IntOrNull("SubmittedDay"), !x.IsDBNull(x.GetOrdinal("IsBanked")) && x.Bool("IsBanked"),
                x.DecOrNull("Score"), x.Bool("HasResult")), ct);

            await r.NextResultAsync(ct);
            var others = await r.ReadAllAsync(x => new OtherEnrollment(
                x.Int("EnrollmentId"), x.Str("ModuleCode"), x.Str("PresentationCode"), x.StrOrNull("FinalResult")), ct);

            return (EnrollmentDetail?)new EnrollmentDetail(
                head.EnrollmentId, head.StudentId, head.Gender, head.Region, head.HighestEducation, head.ImdBand,
                head.AgeBand, head.HasDisability, head.PresentationId, head.ModuleCode, head.PresentationCode,
                head.LengthDays, head.PreviousAttempts, head.StudiedCredits, head.RegistrationDay,
                head.UnregistrationDay, head.FinalResult, head.UpdatedAt, head.RowVer, assessments, others);
        }, ct);

    public Task<(int StudentId, int EnrollmentId)> CreateEnrollmentAsync(EnrollmentInput input, string changedBy, CancellationToken ct) =>
        RunAsync("dbo.usp_Enrollment_Create", cmd =>
        {
            AddEnrollmentFields(cmd, input);
            cmd.Add("@PresentationId", DbType.Int32, input.PresentationId);
            cmd.Add("@ChangedBy", DbType.String, changedBy, 100);
            cmd.Add("@NewStudentId", DbType.Int32, null, direction: ParameterDirection.Output);
            cmd.Add("@NewEnrollmentId", DbType.Int32, null, direction: ParameterDirection.Output);
        },
        async cmd =>
        {
            await cmd.ExecuteNonQueryAsync(ct);
            return (Convert.ToInt32(cmd.Parameters["@NewStudentId"].Value),
                    Convert.ToInt32(cmd.Parameters["@NewEnrollmentId"].Value));
        }, ct);

    public Task UpdateEnrollmentAsync(int enrollmentId, byte[] rowVersion, EnrollmentInput input, string changedBy, CancellationToken ct) =>
        RunAsync("dbo.usp_Enrollment_Update", cmd =>
        {
            cmd.Add("@EnrollmentId", DbType.Int32, enrollmentId);
            cmd.Add("@RowVer", DbType.Binary, rowVersion, 8);
            AddEnrollmentFields(cmd, input);
            cmd.Add("@ChangedBy", DbType.String, changedBy, 100);
        },
        async cmd => await cmd.ExecuteNonQueryAsync(ct), ct);

    public Task<bool> DeleteEnrollmentAsync(int enrollmentId, string changedBy, CancellationToken ct) =>
        RunAsync("dbo.usp_Enrollment_Delete", cmd =>
        {
            cmd.Add("@EnrollmentId", DbType.Int32, enrollmentId);
            cmd.Add("@ChangedBy", DbType.String, changedBy, 100);
            cmd.Add("@StudentDeleted", DbType.Boolean, null, direction: ParameterDirection.Output);
        },
        async cmd =>
        {
            await cmd.ExecuteNonQueryAsync(ct);
            return Convert.ToBoolean(cmd.Parameters["@StudentDeleted"].Value);
        }, ct);

    public Task SaveResultAsync(int enrollmentId, ScoreInput input, string changedBy, CancellationToken ct) =>
        RunAsync("dbo.usp_AssessmentResult_Save", cmd =>
        {
            cmd.Add("@EnrollmentId", DbType.Int32, enrollmentId);
            cmd.Add("@AssessmentId", DbType.Int32, input.AssessmentId);
            cmd.Add("@SubmittedDay", DbType.Int16, (short?)input.SubmittedDay);
            cmd.Add("@Score", DbType.Decimal, input.Score);
            cmd.Add("@IsBanked", DbType.Boolean, input.IsBanked);
            cmd.Add("@ChangedBy", DbType.String, changedBy, 100);
        },
        async cmd => await cmd.ExecuteNonQueryAsync(ct), ct);

    public Task DeleteResultAsync(int enrollmentId, int assessmentId, string changedBy, CancellationToken ct) =>
        RunAsync("dbo.usp_AssessmentResult_Delete", cmd =>
        {
            cmd.Add("@EnrollmentId", DbType.Int32, enrollmentId);
            cmd.Add("@AssessmentId", DbType.Int32, assessmentId);
            cmd.Add("@ChangedBy", DbType.String, changedBy, 100);
        },
        async cmd => await cmd.ExecuteNonQueryAsync(ct), ct);

    public Task<Dashboard> GetDashboardAsync(CancellationToken ct) =>
        RunAsync("dbo.usp_Dashboard_Get", null, async cmd =>
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            await r.ReadAsync(ct);
            var totals = new DashboardTotals(
                r.Int("Students"), r.Int("Enrollments"), r.Int("CourseRuns"), r.Int("AssessmentResults"),
                r.DecOrNull("PassRatePct"), r.DecOrNull("WithdrawalRatePct"));
            await r.NextResultAsync(ct);
            var courses = await r.ReadAllAsync(x => new CourseOutcome(
                x.Int("PresentationId"), x.Str("ModuleCode"), x.Str("PresentationCode"), x.Int("Enrollments"),
                x.Int("Distinction"), x.Int("Pass"), x.Int("Fail"), x.Int("Withdrawn"), x.Int("InProgress"),
                x.DecOrNull("PassRatePct")), ct);
            return new Dashboard(totals, courses);
        }, ct);

    public Task<AtRiskReport> GetAtRiskAsync(AtRiskQuery q, CancellationToken ct) =>
        RunAsync("dbo.usp_Report_AtRisk", cmd =>
        {
            cmd.Add("@PresentationId", DbType.Int32, q.PresentationId);
            cmd.Add("@CutoffDay", DbType.Int16, (short)q.CutoffDay);
            cmd.Add("@ScoreThreshold", DbType.Decimal, q.ScoreThreshold);
            cmd.Add("@MinMissed", DbType.Byte, (byte)q.MinMissed);
            cmd.Add("@MaxRows", DbType.Int32, AtRiskQuery.MaxRows);
        },
        async cmd =>
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var rows = await r.ReadAllAsync(x => new AtRiskRow(
                x.Int("EnrollmentId"), x.Int("StudentId"), x.Str("ModuleCode"), x.Str("PresentationCode"),
                x.Int("DueCount"), x.Int("SubmittedCount"), x.Int("MissedCount"), x.DecOrNull("AvgScore"),
                x.StrOrNull("FinalResult")), ct);
            await r.NextResultAsync(ct);
            var groups = await r.ReadAllAsync(x => new AtRiskGroup(
                x.Bool("IsFlagged"), x.Int("Students"), x.Int("Passed"), x.Int("FailedOrWithdrew"),
                x.Int("InProgress"), x.DecOrNull("FailedOrWithdrewPct")), ct);
            return new AtRiskReport(rows,
                groups.FirstOrDefault(g => g.IsFlagged),
                groups.FirstOrDefault(g => !g.IsFlagged));
        }, ct);

    public Task<IReadOnlyList<AuditEntry>> GetAuditLogAsync(int top, CancellationToken ct) =>
        RunAsync("dbo.usp_AuditLog_List", cmd => cmd.Add("@Top", DbType.Int32, top),
        async cmd =>
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            IReadOnlyList<AuditEntry> rows = await r.ReadAllAsync(x => new AuditEntry(
                Convert.ToInt64(x.GetValue(x.GetOrdinal("AuditId"))), x.UtcDate("ChangedAt"), x.Str("ChangedBy"),
                x.Str("Action"), x.Str("EntityName"), x.Str("EntityKey"), x.StrOrNull("Details")), ct);
            return rows;
        }, ct);

    // ---------------------------------------------------------------------

    private static void AddEnrollmentFields(DbCommand cmd, EnrollmentInput i)
    {
        cmd.Add("@Gender", DbType.AnsiStringFixedLength, i.Gender, 1);
        cmd.Add("@Region", DbType.String, i.Region, 60);
        cmd.Add("@HighestEducation", DbType.String, i.HighestEducation, 60);
        cmd.Add("@ImdBand", DbType.String, i.ImdBand, 10);
        cmd.Add("@AgeBand", DbType.String, i.AgeBand, 10);
        cmd.Add("@HasDisability", DbType.Boolean, i.HasDisability);
        cmd.Add("@PreviousAttempts", DbType.Byte, (byte)i.PreviousAttempts);
        cmd.Add("@StudiedCredits", DbType.Int16, (short)i.StudiedCredits);
        cmd.Add("@RegistrationDay", DbType.Int16, (short?)i.RegistrationDay);
        cmd.Add("@UnregistrationDay", DbType.Int16, (short?)i.UnregistrationDay);
        cmd.Add("@FinalResult", DbType.String, i.FinalResult, 12);
    }

    /// <summary>Opens a connection, runs one stored procedure, and maps known SQL errors.</summary>
    private async Task<T> RunAsync<T>(string procedure, Action<DbCommand>? addParameters,
        Func<DbCommand, Task<T>> run, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = procedure;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = TimeoutSeconds;
        addParameters?.Invoke(cmd);

        try
        {
            await connection.OpenAsync(ct);
            return await run(cmd);
        }
        catch (DbException ex) when (SqlErrorMapper.TryMap(ex, out var mapped))
        {
            throw mapped;   // e.g. THROW 50409 in SQL becomes a ConflictException
        }
    }
}
