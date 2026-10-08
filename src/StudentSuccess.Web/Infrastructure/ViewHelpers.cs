using System.Globalization;
using StudentSuccess.Web.Models;

namespace StudentSuccess.Web.Infrastructure;

/// <summary>Formatting shared by the views.</summary>
public static class ViewHelpers
{
    public static string ResultClass(string? result) => result switch
    {
        "Distinction" => "badge badge-distinction",
        "Pass" => "badge badge-pass",
        "Fail" => "badge badge-fail",
        "Withdrawn" => "badge badge-withdrawn",
        _ => "badge badge-progress",
    };

    public static string ResultText(string? result) => result ?? EnrollmentSearch.InProgress;

    public static string Score(decimal? score) =>
        score?.ToString("0.#", CultureInfo.InvariantCulture) ?? "–";

    public static string Pct(decimal? pct) =>
        pct is null ? "–" : pct.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    /// <summary>Days are relative to the course start: -20 means 20 days before it began.</summary>
    public static string Day(int? day) => day switch
    {
        null => "–",
        < 0 => $"{-day} days before start",
        0 => "Start day",
        _ => $"Day {day}",
    };

    public static string Gender(string gender) => gender switch
    {
        "F" => "Female",
        "M" => "Male",
        _ => gender,
    };

    public static string AssessmentType(string type) => type switch
    {
        "TMA" => "Tutor-marked",
        "CMA" => "Computer-marked",
        "Exam" => "Exam",
        _ => type,
    };

    public static string Number(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Width for a bar segment, formatted for CSS regardless of server culture.</summary>
    public static string Width(decimal pct) => pct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
