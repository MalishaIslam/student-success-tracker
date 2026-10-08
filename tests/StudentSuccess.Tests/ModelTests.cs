using System.ComponentModel.DataAnnotations;
using StudentSuccess.Web.Data;
using StudentSuccess.Web.Infrastructure;
using StudentSuccess.Web.Models;

namespace StudentSuccess.Tests;

public class EnrollmentSearchTests
{
    [Theory]
    [InlineData("avgscore", "AvgScore")]
    [InlineData("REGION", "Region")]
    [InlineData("StudentId", "StudentId")]
    [InlineData("password; DROP TABLE dbo.Students", "StudentId")]
    [InlineData(null, "StudentId")]
    public void Sort_column_is_mapped_to_the_allowed_list(string? requested, string expected)
    {
        Assert.Equal(expected, new EnrollmentSearch { Sort = requested }.Normalize().Sort);
    }

    [Theory]
    [InlineData("desc", "desc")]
    [InlineData("DESC", "desc")]
    [InlineData("asc", "asc")]
    [InlineData("sideways", "asc")]
    public void Direction_is_asc_or_desc(string dir, string expected)
    {
        Assert.Equal(expected, new EnrollmentSearch { Dir = dir }.Normalize().Dir);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(7, 7)]
    public void Page_is_at_least_one(int page, int expected)
    {
        Assert.Equal(expected, new EnrollmentSearch { Page = page }.Normalize().Page);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(1000000, 25)]
    [InlineData(7, 25)]
    public void Page_size_must_be_an_offered_value(int size, int expected)
    {
        Assert.Equal(expected, new EnrollmentSearch { PageSize = size }.Normalize().PageSize);
    }

    [Fact]
    public void Numeric_student_id_is_parsed()
    {
        var s = new EnrollmentSearch { StudentId = " 11391 " }.Normalize();
        Assert.Equal(11391, s.StudentIdValue);
        Assert.False(s.HasInvalidStudentId);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-4")]
    [InlineData("1 OR 1=1")]
    public void Non_numeric_student_id_is_flagged(string value)
    {
        var s = new EnrollmentSearch { StudentId = value }.Normalize();
        Assert.True(s.HasInvalidStudentId);
        Assert.Null(s.StudentIdValue);
    }

    [Theory]
    [InlineData("bbb", "BBB")]
    [InlineData("BBBB", null)]
    [InlineData("B1B", null)]
    public void Module_code_must_be_three_letters(string value, string? expected)
    {
        Assert.Equal(expected, new EnrollmentSearch { Module = value }.Normalize().Module);
    }

    [Theory]
    [InlineData("2013j", "2013J")]
    [InlineData("13J", null)]
    public void Presentation_code_must_look_like_2013J(string value, string? expected)
    {
        Assert.Equal(expected, new EnrollmentSearch { Presentation = value }.Normalize().Presentation);
    }

    [Theory]
    [InlineData("Pass", "Pass")]
    [InlineData("In progress", "In progress")]
    [InlineData("Excellent", null)]
    public void Result_filter_must_be_a_known_result(string value, string? expected)
    {
        Assert.Equal(expected, new EnrollmentSearch { Result = value }.Normalize().Result);
    }

    [Fact]
    public void Route_values_keep_filters_and_override_paging()
    {
        var s = new EnrollmentSearch { Module = "BBB", Result = "Fail", Sort = "Region", Dir = "desc", Page = 3 }.Normalize();

        var values = s.ToRouteValues(page: 1);

        Assert.Equal("BBB", values["module"]);
        Assert.Equal("Fail", values["result"]);
        Assert.Equal("Region", values["sort"]);
        Assert.Equal("1", values["page"]);
        Assert.False(values.ContainsKey("region"));
    }

    [Fact]
    public void Export_link_values_leave_out_paging()
    {
        var values = new EnrollmentSearch { Module = "AAA" }.Normalize().ToRouteValues(includePaging: false);
        Assert.False(values.ContainsKey("page"));
        Assert.False(values.ContainsKey("pageSize"));
    }

    [Fact]
    public void Clicking_the_sorted_column_flips_direction()
    {
        var s = new EnrollmentSearch { Sort = "AvgScore", Dir = "asc" }.Normalize();
        Assert.Equal("desc", s.NextDirectionFor("AvgScore"));
        Assert.Equal("asc", s.NextDirectionFor("Region"));
        Assert.Equal("ascending", s.AriaSortFor("AvgScore"));
        Assert.Equal("none", s.AriaSortFor("Region"));
    }
}

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 1, 25, 1, 0, 0)]
    [InlineData(100, 1, 25, 4, 1, 25)]
    [InlineData(101, 5, 25, 5, 101, 101)]
    [InlineData(32593, 2, 50, 652, 51, 100)]
    public void Paging_math(int total, int page, int size, int pages, int first, int last)
    {
        var r = new PagedResult<int>([], total, page, size);
        Assert.Equal(pages, r.TotalPages);
        Assert.Equal(first, r.FirstItem);
        Assert.Equal(last, r.LastItem);
    }
}

public class FormValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static EnrollmentForm ValidForm() => new()
    {
        Gender = "F", Region = "Scotland", HighestEducation = "HE Qualification", AgeBand = "0-35",
        PresentationId = 1, StudiedCredits = 60, PreviousAttempts = 0, RegistrationDay = -20,
    };

    [Fact]
    public void Valid_form_passes()
    {
        Assert.Empty(Validate(ValidForm()));
    }

    [Fact]
    public void Missing_required_fields_fail()
    {
        var errors = Validate(new EnrollmentForm());
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(EnrollmentForm.Gender)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(EnrollmentForm.Region)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(EnrollmentForm.AgeBand)));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Female")]
    public void Gender_must_be_F_or_M(string gender)
    {
        var form = ValidForm();
        form.Gender = gender;
        Assert.Contains(Validate(form), e => e.MemberNames.Contains(nameof(EnrollmentForm.Gender)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Credits_out_of_range_fail(int credits)
    {
        var form = ValidForm();
        form.StudiedCredits = credits;
        Assert.Contains(Validate(form), e => e.MemberNames.Contains(nameof(EnrollmentForm.StudiedCredits)));
    }

    [Fact]
    public void Unregistering_before_registering_fails()
    {
        var form = ValidForm();
        form.RegistrationDay = 10;
        form.UnregistrationDay = 5;
        Assert.Contains(Validate(form), e => e.MemberNames.Contains(nameof(EnrollmentForm.UnregistrationDay)));
    }

    [Fact]
    public void Unknown_final_result_fails()
    {
        var form = ValidForm();
        form.FinalResult = "Excellent";
        Assert.Contains(Validate(form), e => e.MemberNames.Contains(nameof(EnrollmentForm.FinalResult)));
    }

    [Fact]
    public void Blank_optional_values_become_null()
    {
        var form = ValidForm();
        form.ImdBand = "  ";
        form.FinalResult = "";
        var input = form.ToInput();
        Assert.Null(input.ImdBand);
        Assert.Null(input.FinalResult);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.5)]
    public void Score_must_be_0_to_100(double score)
    {
        Assert.NotEmpty(Validate(new ScoreForm { AssessmentId = 1, Score = (decimal)score }));
    }

    [Fact]
    public void At_risk_settings_are_range_checked()
    {
        Assert.Empty(Validate(new AtRiskQuery()));
        Assert.NotEmpty(Validate(new AtRiskQuery { CutoffDay = 0 }));
        Assert.NotEmpty(Validate(new AtRiskQuery { MinMissed = 50 }));
        Assert.NotEmpty(Validate(new AtRiskQuery { ScoreThreshold = 101 }));
    }
}

public class CsvExportTests
{
    [Theory]
    [InlineData("Scotland", "Scotland")]
    [InlineData("A, B", "\"A, B\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "\"'+1\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("", "")]
    public void Text_values_are_quoted_and_formula_safe(string input, string expected)
    {
        Assert.Equal(expected, CsvExport.Text(input));
    }

    [Fact]
    public void Export_has_header_and_one_line_per_row()
    {
        var rows = new[]
        {
            new EnrollmentRow(1, 100, "AAA", "2013J", "F", "Scotland", "0-35", 60, 71.5m, 4, "Pass"),
            new EnrollmentRow(2, 101, "BBB", "2014B", "M", "Wales", "35-55", 120, null, 0, null),
        };

        var lines = CsvExport.Build(rows).TrimEnd().Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("EnrollmentId,StudentId", lines[0]);
        Assert.Equal("1,100,AAA,2013J,F,Scotland,0-35,60,71.5,4,Pass", lines[1]);
        Assert.Equal("2,101,BBB,2014B,M,Wales,35-55,120,,0,In progress", lines[2]);
    }
}

public class SecurityTests
{
    private static CredentialChecker Checker(string? password) =>
        new(Microsoft.Extensions.Options.Options.Create(new AuthOptions { Username = "admin", Password = password }));

    [Fact]
    public void Correct_credentials_are_accepted()
    {
        Assert.True(Checker("S3cret-pass").IsValid("admin", "S3cret-pass"));
        Assert.True(Checker("S3cret-pass").IsValid(" admin ", "S3cret-pass"));
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("Admin", "S3cret-pass")]
    [InlineData("admin", "S3cret-pass ")]
    [InlineData(null, "S3cret-pass")]
    [InlineData("admin", null)]
    public void Wrong_credentials_are_rejected(string? user, string? password)
    {
        Assert.False(Checker("S3cret-pass").IsValid(user, password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nobody_can_sign_in_without_a_configured_password(string? password)
    {
        var checker = Checker(password);
        Assert.False(checker.IsConfigured);
        Assert.False(checker.IsValid("admin", ""));
    }

    [Theory]
    [InlineData(SqlErrorMapper.InvalidInput, 400)]
    [InlineData(SqlErrorMapper.NotFound, 404)]
    [InlineData(SqlErrorMapper.Conflict, 409)]
    [InlineData(SqlErrorMapper.UniqueConstraintViolation, 409)]
    [InlineData(SqlErrorMapper.UniqueIndexViolation, 409)]
    public void Known_sql_errors_map_to_http_codes(int number, int status)
    {
        Assert.Equal(status, SqlErrorMapper.Map(number, "msg")!.StatusCode);
    }

    [Theory]
    [InlineData(1205)]
    [InlineData(18456)]
    public void Unexpected_sql_errors_are_not_mapped(int number)
    {
        Assert.Null(SqlErrorMapper.Map(number, "internal detail"));
    }
}
