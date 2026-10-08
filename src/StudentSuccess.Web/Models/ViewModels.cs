namespace StudentSuccess.Web.Models;

// What each page needs to render

public sealed record StudentsIndexViewModel(EnrollmentSearch Search, PagedResult<EnrollmentRow> Results, Lookups Lookups);

public sealed record StudentDetailsViewModel(EnrollmentDetail Enrollment, ScoreForm NewScore);

public sealed record StudentFormViewModel(EnrollmentForm Form, Lookups Lookups, EnrollmentDetail? Existing)
{
    public bool IsEdit => Existing is not null;
}

public sealed record AtRiskViewModel(AtRiskQuery Query, AtRiskReport? Report, Lookups Lookups);
