using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using StudentSuccess.Web.Controllers;
using StudentSuccess.Web.Data;
using StudentSuccess.Web.Models;

namespace StudentSuccess.Tests;

public class StudentsControllerTests
{
    private readonly FakeStudentRepository _repo = new();

    private StudentsController Controller()
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test")),
        };
        return new StudentsController(_repo)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NullTempDataProvider()),
            Url = new StubUrlHelper(),
        };
    }

    private async Task<EnrollmentForm> FormForFirstEnrollment()
    {
        var detail = await _repo.GetEnrollmentAsync(_repo.FirstEnrollmentId, default);
        return EnrollmentForm.From(detail!);
    }

    [Fact]
    public async Task Index_lists_the_first_page()
    {
        var result = await Controller().Index(new EnrollmentSearch { PageSize = 10 }, default);

        var model = Assert.IsType<StudentsIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(_repo.EnrollmentCount, model.Results.TotalCount);
        Assert.Equal(10, model.Results.Items.Count);
    }

    [Fact]
    public async Task Index_filters_and_sorts()
    {
        var result = await Controller().Index(new EnrollmentSearch { Module = "aaa", Sort = "avgscore", Dir = "desc", PageSize = 100 }, default);

        var items = Assert.IsType<StudentsIndexViewModel>(Assert.IsType<ViewResult>(result).Model).Results.Items;
        Assert.All(items, r => Assert.Equal("AAA", r.ModuleCode));
        var scores = items.Where(r => r.AvgScore.HasValue).Select(r => r.AvgScore!.Value).ToList();
        Assert.Equal(scores.OrderByDescending(x => x), scores);
    }

    [Fact]
    public async Task Index_with_a_page_past_the_end_redirects_to_the_last_page()
    {
        var result = await Controller().Index(new EnrollmentSearch { Page = 999, PageSize = 10 }, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task Index_with_a_bad_student_id_shows_an_error_without_querying()
    {
        var controller = Controller();
        var result = await controller.Index(new EnrollmentSearch { StudentId = "abc" }, default);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Details_of_unknown_enrollment_is_404()
    {
        Assert.IsType<NotFoundResult>(await Controller().Details(999999, default));
    }

    [Fact]
    public async Task Create_with_valid_form_adds_student_and_redirects_to_details()
    {
        var before = _repo.EnrollmentCount;
        var form = await FormForFirstEnrollment();
        form.RowVersion = null;
        form.FinalResult = null;

        var result = await Controller().Create(form, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(before + 1, _repo.EnrollmentCount);
    }

    [Fact]
    public async Task Create_without_course_redisplays_form()
    {
        var form = await FormForFirstEnrollment();
        form.PresentationId = null;
        var controller = Controller();

        var result = await controller.Create(form, default);

        Assert.Equal("Form", Assert.IsType<ViewResult>(result).ViewName);
        Assert.True(controller.ModelState.ContainsKey(nameof(EnrollmentForm.PresentationId)));
    }

    [Fact]
    public async Task Create_rejected_by_database_rules_shows_the_message()
    {
        var form = await FormForFirstEnrollment();
        form.Region = "Atlantis";   // not a region in the data
        var controller = Controller();

        var result = await controller.Create(form, default);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, e => e.ErrorMessage.Contains("region"));
    }

    [Fact]
    public async Task Edit_saves_changes()
    {
        var form = await FormForFirstEnrollment();
        form.FinalResult = "Distinction";

        var result = await Controller().Edit(_repo.FirstEnrollmentId, form, default);

        Assert.IsType<RedirectToActionResult>(result);
        var saved = await _repo.GetEnrollmentAsync(_repo.FirstEnrollmentId, default);
        Assert.Equal("Distinction", saved!.FinalResult);
    }

    [Fact]
    public async Task Edit_with_an_old_version_reports_a_conflict()
    {
        var stale = await FormForFirstEnrollment();
        var fresh = await FormForFirstEnrollment();
        await Controller().Edit(_repo.FirstEnrollmentId, fresh, default);   // someone else saves first

        var controller = Controller();
        var result = await controller.Edit(_repo.FirstEnrollmentId, stale, default);

        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, e => e.ErrorMessage.Contains("Someone else"));
    }

    [Fact]
    public async Task Edit_with_a_tampered_version_is_rejected()
    {
        var form = await FormForFirstEnrollment();
        form.RowVersion = "not-base64!";
        var controller = Controller();

        var result = await controller.Edit(_repo.FirstEnrollmentId, form, default);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Delete_removes_the_enrollment()
    {
        var before = _repo.EnrollmentCount;

        var result = await Controller().DeleteConfirmed(_repo.FirstEnrollmentId, default);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(before - 1, _repo.EnrollmentCount);
    }

    [Fact]
    public async Task Deleting_twice_shows_a_message_instead_of_failing()
    {
        var controller = Controller();
        var id = _repo.FirstEnrollmentId;
        await controller.DeleteConfirmed(id, default);

        await controller.DeleteConfirmed(id, default);

        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Saving_a_score_adds_or_updates_the_result()
    {
        var detail = await _repo.GetEnrollmentAsync(_repo.FirstEnrollmentId, default);
        var assessment = detail!.Assessments.First();

        await Controller().SaveScore(detail.EnrollmentId, new ScoreForm { AssessmentId = assessment.AssessmentId, Score = 88, SubmittedDay = 10 }, default);

        var after = await _repo.GetEnrollmentAsync(detail.EnrollmentId, default);
        Assert.Equal(88m, after!.Assessments.First(a => a.AssessmentId == assessment.AssessmentId).Score);
    }

    [Fact]
    public async Task Saving_a_score_for_another_course_is_refused()
    {
        var controller = Controller();

        await controller.SaveScore(_repo.FirstEnrollmentId, new ScoreForm { AssessmentId = 999999, Score = 50 }, default);

        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Export_returns_csv_for_every_matching_row()
    {
        var result = await Controller().Export(new EnrollmentSearch { Module = "BBB" }, default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.StartsWith("text/csv", file.ContentType);
        var text = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.DoesNotContain(",AAA,", text);
        Assert.Contains(",BBB,", text);
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "/students/" + actionContext.Action?.ToLowerInvariant();
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => url?.StartsWith('/') == true;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }
}

public class ReportTests
{
    [Fact]
    public async Task At_risk_report_flags_students_with_missed_deadlines()
    {
        var repo = new FakeStudentRepository();

        var report = await repo.GetAtRiskAsync(new AtRiskQuery { CutoffDay = 120, MinMissed = 1, ScoreThreshold = 40 }, default);

        Assert.NotEmpty(report.Rows);
        Assert.All(report.Rows, r => Assert.True(r.MissedCount >= 1 || r.AvgScore < 40));
        Assert.NotNull(report.Flagged);
    }

    [Fact]
    public async Task Dashboard_totals_match_the_data()
    {
        var repo = new FakeStudentRepository();

        var d = await repo.GetDashboardAsync(default);

        Assert.Equal(repo.EnrollmentCount, d.Totals.Enrollments);
        Assert.Equal(d.Totals.Enrollments, d.Courses.Sum(c => c.Enrollments));
        Assert.All(d.Courses, c => Assert.Equal(c.Enrollments, c.Distinction + c.Pass + c.Fail + c.Withdrawn + c.InProgress));
    }
}
