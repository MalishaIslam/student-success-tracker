using StudentSuccess.Web.Models;

namespace StudentSuccess.Web.Data;

/// <summary>
/// Everything the app asks of the database. Controllers depend on this interface,
/// not on SQL Server, so they can be tested with an in-memory fake.
/// </summary>
public interface IStudentRepository
{
    Task<Lookups> GetLookupsAsync(CancellationToken ct);

    /// <param name="search">A search that has already been through <see cref="EnrollmentSearch.Normalize"/>.</param>
    Task<PagedResult<EnrollmentRow>> SearchAsync(EnrollmentSearch search, CancellationToken ct);

    Task<EnrollmentDetail?> GetEnrollmentAsync(int enrollmentId, CancellationToken ct);

    Task<(int StudentId, int EnrollmentId)> CreateEnrollmentAsync(EnrollmentInput input, string changedBy, CancellationToken ct);

    Task UpdateEnrollmentAsync(int enrollmentId, byte[] rowVersion, EnrollmentInput input, string changedBy, CancellationToken ct);

    /// <returns>True when the student record was also deleted (it was their only course).</returns>
    Task<bool> DeleteEnrollmentAsync(int enrollmentId, string changedBy, CancellationToken ct);

    Task SaveResultAsync(int enrollmentId, ScoreInput input, string changedBy, CancellationToken ct);

    Task DeleteResultAsync(int enrollmentId, int assessmentId, string changedBy, CancellationToken ct);

    Task<Dashboard> GetDashboardAsync(CancellationToken ct);

    Task<AtRiskReport> GetAtRiskAsync(AtRiskQuery query, CancellationToken ct);

    Task<IReadOnlyList<AuditEntry>> GetAuditLogAsync(int top, CancellationToken ct);
}
