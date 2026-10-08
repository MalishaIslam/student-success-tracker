namespace StudentSuccess.Web.Models;

// Records returned by the repository. Each mirrors a stored procedure result set.

public sealed record PresentationOption(int Id, string ModuleCode, string PresentationCode, int? LengthDays)
{
    public string Label => $"{ModuleCode} {PresentationCode}";
}

public sealed record Lookups(
    IReadOnlyList<PresentationOption> Presentations,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> AgeBands,
    IReadOnlyList<string> EducationLevels,
    IReadOnlyList<string> ImdBands)
{
    public static readonly IReadOnlyList<string> FinalResults = ["Pass", "Distinction", "Fail", "Withdrawn"];

    public IReadOnlyList<string> Modules => Presentations.Select(p => p.ModuleCode).Distinct().Order().ToList();

    public IReadOnlyList<string> PresentationCodes => Presentations.Select(p => p.PresentationCode).Distinct().Order().ToList();

    public static Lookups Empty { get; } = new([], [], [], [], []);
}

/// <summary>One row in the students table: a student in one course run.</summary>
public sealed record EnrollmentRow(
    int EnrollmentId,
    int StudentId,
    string ModuleCode,
    string PresentationCode,
    string Gender,
    string Region,
    string AgeBand,
    int StudiedCredits,
    decimal? AvgScore,
    int SubmittedCount,
    string? FinalResult);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
    public int FirstItem => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;
    public int LastItem => Math.Min(Page * PageSize, TotalCount);
}

public sealed record EnrollmentDetail(
    int EnrollmentId,
    int StudentId,
    string Gender,
    string Region,
    string HighestEducation,
    string? ImdBand,
    string AgeBand,
    bool HasDisability,
    int PresentationId,
    string ModuleCode,
    string PresentationCode,
    int? LengthDays,
    int PreviousAttempts,
    int StudiedCredits,
    int? RegistrationDay,
    int? UnregistrationDay,
    string? FinalResult,
    DateTime UpdatedAtUtc,
    byte[] RowVersion,
    IReadOnlyList<AssessmentRow> Assessments,
    IReadOnlyList<OtherEnrollment> OtherEnrollments)
{
    public string Course => $"{ModuleCode} {PresentationCode}";

    /// <summary>Simple average of the scores recorded for this course run (same as the students list).</summary>
    public decimal? AverageScore
    {
        get
        {
            var scored = Assessments.Where(a => a.Score.HasValue).ToList();
            return scored.Count == 0 ? null : Math.Round(scored.Average(a => a.Score!.Value), 1);
        }
    }
}

public sealed record AssessmentRow(
    int AssessmentId,
    string AssessmentType,
    int? DueDay,
    decimal WeightPercent,
    int? SubmittedDay,
    bool IsBanked,
    decimal? Score,
    bool HasResult);

public sealed record OtherEnrollment(int EnrollmentId, string ModuleCode, string PresentationCode, string? FinalResult);

/// <summary>Values written by create and update.</summary>
public sealed record EnrollmentInput(
    string Gender,
    string Region,
    string HighestEducation,
    string? ImdBand,
    string AgeBand,
    bool HasDisability,
    int PresentationId,
    int PreviousAttempts,
    int StudiedCredits,
    int? RegistrationDay,
    int? UnregistrationDay,
    string? FinalResult);

public sealed record ScoreInput(int AssessmentId, int? SubmittedDay, decimal? Score, bool IsBanked);

public sealed record DashboardTotals(
    int Students,
    int Enrollments,
    int CourseRuns,
    int AssessmentResults,
    decimal? PassRatePct,
    decimal? WithdrawalRatePct);

public sealed record CourseOutcome(
    int PresentationId,
    string ModuleCode,
    string PresentationCode,
    int Enrollments,
    int Distinction,
    int Pass,
    int Fail,
    int Withdrawn,
    int InProgress,
    decimal? PassRatePct)
{
    public string Course => $"{ModuleCode} {PresentationCode}";

    /// <summary>Share of enrollments as a percentage, for the bar chart.</summary>
    public decimal Share(int count) => Enrollments == 0 ? 0 : Math.Round(100m * count / Enrollments, 1);
}

public sealed record Dashboard(DashboardTotals Totals, IReadOnlyList<CourseOutcome> Courses);

public sealed record AtRiskRow(
    int EnrollmentId,
    int StudentId,
    string ModuleCode,
    string PresentationCode,
    int DueCount,
    int SubmittedCount,
    int MissedCount,
    decimal? AvgScore,
    string? FinalResult);

public sealed record AtRiskGroup(bool IsFlagged, int Students, int Passed, int FailedOrWithdrew, int InProgress, decimal? FailedOrWithdrewPct);

public sealed record AtRiskReport(IReadOnlyList<AtRiskRow> Rows, AtRiskGroup? Flagged, AtRiskGroup? NotFlagged);

public sealed record AuditEntry(
    long AuditId,
    DateTime ChangedAtUtc,
    string ChangedBy,
    string Action,
    string EntityName,
    string EntityKey,
    string? Details);
