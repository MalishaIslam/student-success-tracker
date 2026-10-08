using System.ComponentModel.DataAnnotations;

namespace StudentSuccess.Web.Models;

/// <summary>
/// The add/edit student form. Validated here (so users see messages next to each
/// field) and again in the stored procedure (so the database stays correct
/// whatever calls it).
/// </summary>
public sealed class EnrollmentForm : IValidatableObject
{
    [Required(ErrorMessage = "Choose a gender.")]
    [RegularExpression("^[FM]$", ErrorMessage = "Gender must be F or M.")]
    public string? Gender { get; set; }

    [Required(ErrorMessage = "Choose a region.")]
    [StringLength(60)]
    public string? Region { get; set; }

    [Required(ErrorMessage = "Choose an education level.")]
    [StringLength(60)]
    [Display(Name = "Highest education")]
    public string? HighestEducation { get; set; }

    [StringLength(10)]
    [Display(Name = "Deprivation band (IMD)")]
    public string? ImdBand { get; set; }

    [Required(ErrorMessage = "Choose an age band.")]
    [StringLength(10)]
    [Display(Name = "Age band")]
    public string? AgeBand { get; set; }

    [Display(Name = "Has a declared disability")]
    public bool HasDisability { get; set; }

    /// <summary>Only used when adding; the course can't be changed afterwards.</summary>
    [Display(Name = "Course")]
    public int? PresentationId { get; set; }

    [Range(0, 20, ErrorMessage = "Previous attempts must be between 0 and 20.")]
    [Display(Name = "Previous attempts")]
    public int PreviousAttempts { get; set; }

    [Range(1, 1000, ErrorMessage = "Studied credits must be between 1 and 1000.")]
    [Display(Name = "Studied credits")]
    public int StudiedCredits { get; set; } = 60;

    [Range(-400, 400, ErrorMessage = "Registration day must be between -400 and 400.")]
    [Display(Name = "Registration day")]
    public int? RegistrationDay { get; set; }

    [Range(-400, 800, ErrorMessage = "Unregistration day must be between -400 and 800.")]
    [Display(Name = "Unregistration day")]
    public int? UnregistrationDay { get; set; }

    [Display(Name = "Final result")]
    public string? FinalResult { get; set; }

    /// <summary>Version of the record when the edit form was opened (base64), to detect conflicting edits.</summary>
    public string? RowVersion { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RegistrationDay is not null && UnregistrationDay is not null && UnregistrationDay < RegistrationDay)
        {
            yield return new ValidationResult(
                "Unregistration day cannot be before the registration day.", [nameof(UnregistrationDay)]);
        }

        if (!string.IsNullOrEmpty(FinalResult) && !Lookups.FinalResults.Contains(FinalResult))
        {
            yield return new ValidationResult("Choose a final result from the list.", [nameof(FinalResult)]);
        }
    }

    public EnrollmentInput ToInput() => new(
        Gender!.Trim(),
        Region!.Trim(),
        HighestEducation!.Trim(),
        string.IsNullOrWhiteSpace(ImdBand) ? null : ImdBand.Trim(),
        AgeBand!.Trim(),
        HasDisability,
        PresentationId ?? 0,
        PreviousAttempts,
        StudiedCredits,
        RegistrationDay,
        UnregistrationDay,
        string.IsNullOrWhiteSpace(FinalResult) ? null : FinalResult);

    public static EnrollmentForm From(EnrollmentDetail d) => new()
    {
        Gender = d.Gender,
        Region = d.Region,
        HighestEducation = d.HighestEducation,
        ImdBand = d.ImdBand,
        AgeBand = d.AgeBand,
        HasDisability = d.HasDisability,
        PresentationId = d.PresentationId,
        PreviousAttempts = d.PreviousAttempts,
        StudiedCredits = d.StudiedCredits,
        RegistrationDay = d.RegistrationDay,
        UnregistrationDay = d.UnregistrationDay,
        FinalResult = d.FinalResult,
        RowVersion = Convert.ToBase64String(d.RowVersion),
    };
}

/// <summary>Add or change one assessment score.</summary>
public sealed class ScoreForm
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose an assessment.")]
    public int AssessmentId { get; set; }

    [Range(-400, 800, ErrorMessage = "Submitted day must be between -400 and 800.")]
    public int? SubmittedDay { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Score must be between 0 and 100.")]
    public decimal? Score { get; set; }

    public bool IsBanked { get; set; }

    public ScoreInput ToInput() => new(AssessmentId, SubmittedDay, Score, IsBanked);
}

/// <summary>Settings for the at-risk report, bound from the query string.</summary>
public sealed class AtRiskQuery
{
    [Display(Name = "Course")]
    public int? PresentationId { get; set; }

    [Range(1, 400, ErrorMessage = "Cutoff day must be between 1 and 400.")]
    [Display(Name = "Check on day")]
    public int CutoffDay { get; set; } = 60;

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Score threshold must be between 0 and 100.")]
    [Display(Name = "Flag average score below")]
    public decimal ScoreThreshold { get; set; } = 40;

    [Range(1, 20, ErrorMessage = "Missed deadlines must be between 1 and 20.")]
    [Display(Name = "Flag missed deadlines of at least")]
    public int MinMissed { get; set; } = 1;

    public const int MaxRows = 500;
}

public sealed class LoginForm
{
    [Required(ErrorMessage = "Enter your username.")]
    [StringLength(100)]
    public string? Username { get; set; }

    [Required(ErrorMessage = "Enter your password.")]
    [StringLength(200)]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    public string? ReturnUrl { get; set; }
}
