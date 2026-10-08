using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StudentSuccess.Web.Data;
using StudentSuccess.Web.Models;

namespace StudentSuccess.Web.Infrastructure;

/// <summary>
/// For /api requests, turns expected errors into JSON "problem details".
/// Page requests fall through to the friendly error page (/Home/Error).
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not DomainException domain || !context.Request.Path.StartsWithSegments("/api"))
        {
            return false;
        }

        context.Response.StatusCode = domain.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = domain.StatusCode, Title = domain.Title, Detail = domain.Message },
        });
    }
}

/// <summary>GET /health reports Unhealthy when SQL Server can't be reached.</summary>
public sealed class DatabaseHealthCheck(IDbConnectionFactory connections) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var connection = connections.Create();
            await connection.OpenAsync(ct);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(ct);
            return HealthCheckResult.Healthy("SQL Server is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL Server is not reachable.", ex);
        }
    }
}

/// <summary>Builds the CSV export of the students list.</summary>
public static class CsvExport
{
    public static readonly string[] Header =
        ["EnrollmentId", "StudentId", "Module", "Presentation", "Gender", "Region", "AgeBand",
         "StudiedCredits", "AvgScore", "AssessmentsSubmitted", "FinalResult"];

    public static string Build(IEnumerable<EnrollmentRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Header));
        foreach (var r in rows)
        {
            sb.Append(r.EnrollmentId.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(r.StudentId.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Text(r.ModuleCode)).Append(',')
              .Append(Text(r.PresentationCode)).Append(',')
              .Append(Text(r.Gender)).Append(',')
              .Append(Text(r.Region)).Append(',')
              .Append(Text(r.AgeBand)).Append(',')
              .Append(r.StudiedCredits.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(r.AvgScore?.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.SubmittedCount.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Text(r.FinalResult ?? EnrollmentSearch.InProgress))
              .AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Quotes a text value for CSV. Values starting with = + - @ are prefixed with
    /// an apostrophe so a spreadsheet won't run them as formulas (CSV injection).
    /// </summary>
    public static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 || value.StartsWith('\'')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
