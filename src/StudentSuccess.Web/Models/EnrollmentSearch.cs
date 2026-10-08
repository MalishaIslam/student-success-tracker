using System.Globalization;
using System.Text.RegularExpressions;

namespace StudentSuccess.Web.Models;

/// <summary>
/// Filters, sorting and paging for the students list, bound from the query
/// string (e.g. /students?module=BBB&amp;result=Fail&amp;sort=AvgScore&amp;dir=desc&amp;page=2).
/// Every value is cleaned by <see cref="Normalize"/> before it reaches the database.
/// </summary>
public sealed partial class EnrollmentSearch
{
    public static readonly IReadOnlyList<string> SortColumns =
        ["StudentId", "Course", "Region", "AgeBand", "Credits", "AvgScore", "Submitted", "FinalResult"];

    public static readonly IReadOnlyList<int> PageSizes = [10, 25, 50, 100];

    public const int DefaultPageSize = 25;
    public const int MaxExportRows = 50_000;
    public const string InProgress = "In progress";

    public string? StudentId { get; set; }
    public string? Module { get; set; }
    public string? Presentation { get; set; }
    public string? Region { get; set; }
    public string? AgeBand { get; set; }
    public string? Result { get; set; }
    public string? Sort { get; set; }
    public string? Dir { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>True when the student ID box held something that isn't a valid ID.</summary>
    public bool HasInvalidStudentId { get; private set; }

    public int? StudentIdValue { get; private set; }

    public bool IsDescending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);

    public bool HasFilters =>
        StudentIdValue is not null || Module is not null || Presentation is not null ||
        Region is not null || AgeBand is not null || Result is not null;

    /// <summary>Returns a cleaned copy: unknown or malformed values fall back to safe defaults.</summary>
    public EnrollmentSearch Normalize()
    {
        var clean = new EnrollmentSearch
        {
            StudentId = Blank(StudentId),
            Module = Blank(Module)?.ToUpperInvariant(),
            Presentation = Blank(Presentation)?.ToUpperInvariant(),
            Region = Limit(Blank(Region), 60),
            AgeBand = Limit(Blank(AgeBand), 10),
            Result = Blank(Result),
            Page = Math.Max(1, Page),
            PageSize = PageSizes.Contains(PageSize) ? PageSize : DefaultPageSize,
        };

        if (clean.StudentId is not null)
        {
            if (int.TryParse(clean.StudentId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                clean.StudentIdValue = id;
            }
            else
            {
                clean.HasInvalidStudentId = true;
            }
        }

        if (clean.Module is not null && !ModulePattern().IsMatch(clean.Module))
        {
            clean.Module = null;
        }

        if (clean.Presentation is not null && !PresentationPattern().IsMatch(clean.Presentation))
        {
            clean.Presentation = null;
        }

        if (clean.Result is not null &&
            !Lookups.FinalResults.Contains(clean.Result) && clean.Result != InProgress)
        {
            clean.Result = null;
        }

        // Map any capitalisation to the exact column name, or fall back to StudentId
        clean.Sort = SortColumns.FirstOrDefault(c => string.Equals(c, Blank(Sort), StringComparison.OrdinalIgnoreCase))
                     ?? "StudentId";
        clean.Dir = IsDescending ? "desc" : "asc";
        return clean;
    }

    /// <summary>Query-string values for links that keep the current filters, with some values replaced.</summary>
    public Dictionary<string, string> ToRouteValues(
        string? sort = null, string? dir = null, int? page = null, bool includePaging = true)
    {
        var values = new Dictionary<string, string>();
        void Put(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                values[key] = value;
            }
        }

        Put("studentId", StudentId);
        Put("module", Module);
        Put("presentation", Presentation);
        Put("region", Region);
        Put("ageBand", AgeBand);
        Put("result", Result);
        Put("sort", sort ?? Sort);
        Put("dir", dir ?? Dir);
        if (includePaging)
        {
            Put("page", (page ?? Page).ToString(CultureInfo.InvariantCulture));
            Put("pageSize", PageSize.ToString(CultureInfo.InvariantCulture));
        }
        return values;
    }

    /// <summary>Clicking the current sort column flips direction; another column starts ascending.</summary>
    public string NextDirectionFor(string column) =>
        string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase) && !IsDescending ? "desc" : "asc";

    /// <summary>Value for the aria-sort attribute so screen readers announce the sort.</summary>
    public string AriaSortFor(string column) =>
        !string.Equals(Sort, column, StringComparison.OrdinalIgnoreCase) ? "none"
        : IsDescending ? "descending" : "ascending";

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Limit(string? value, int max) => value is not null && value.Length > max ? value[..max] : value;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex ModulePattern();

    [GeneratedRegex("^[12][0-9]{3}[A-Z]$")]
    private static partial Regex PresentationPattern();
}
