using System.Text;
using Microsoft.AspNetCore.Mvc;
using StudentSuccess.Web.Data;
using StudentSuccess.Web.Infrastructure;
using StudentSuccess.Web.Models;

namespace StudentSuccess.Web.Controllers;

/// <summary>
/// The main data screens: list (search, filter, sort, page), details, add, edit,
/// delete, scores, and CSV export.
/// </summary>
public sealed class StudentsController(IStudentRepository repository) : Controller
{
    private const string MessageKey = "Message";
    private const string ErrorKey = "Error";

    // GET /students?module=BBB&result=Fail&sort=AvgScore&dir=desc&page=2
    public async Task<IActionResult> Index([FromQuery] EnrollmentSearch search, CancellationToken ct)
    {
        var clean = search.Normalize();
        var lookups = await repository.GetLookupsAsync(ct);

        if (clean.HasInvalidStudentId)
        {
            ModelState.AddModelError(nameof(EnrollmentSearch.StudentId), "Student ID must be a whole number.");
            var empty = new PagedResult<EnrollmentRow>([], 0, 1, clean.PageSize);
            return View(new StudentsIndexViewModel(clean, empty, lookups));
        }

        var results = await repository.SearchAsync(clean, ct);

        // Asked for a page past the end (e.g. after filtering): go to the last page
        if (results.Items.Count == 0 && results.TotalCount > 0 && clean.Page > results.TotalPages)
        {
            return RedirectToAction(nameof(Index), clean.ToRouteValues(page: results.TotalPages));
        }

        return View(new StudentsIndexViewModel(clean, results, lookups));
    }

    // GET /students/export?...same filters... -> CSV file
    public async Task<IActionResult> Export([FromQuery] EnrollmentSearch search, CancellationToken ct)
    {
        var clean = search.Normalize();
        if (clean.HasInvalidStudentId)
        {
            return BadRequest();
        }

        clean.Page = 1;
        clean.PageSize = EnrollmentSearch.MaxExportRows;
        var results = await repository.SearchAsync(clean, ct);

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(CsvExport.Build(results.Items))).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"students-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var enrollment = await repository.GetEnrollmentAsync(id, ct);
        if (enrollment is null)
        {
            return NotFound();
        }

        var firstMissing = enrollment.Assessments.FirstOrDefault(a => !a.HasResult)?.AssessmentId ?? 0;
        return View(new StudentDetailsViewModel(enrollment, new ScoreForm { AssessmentId = firstMissing }));
    }

    // ----- Add -------------------------------------------------------------

    public async Task<IActionResult> Create(CancellationToken ct) =>
        View("Form", new StudentFormViewModel(new EnrollmentForm(), await repository.GetLookupsAsync(ct), null));

    [HttpPost]
    public async Task<IActionResult> Create(EnrollmentForm form, CancellationToken ct)
    {
        if (form.PresentationId is null or <= 0)
        {
            ModelState.AddModelError(nameof(EnrollmentForm.PresentationId), "Choose a course.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                var (studentId, enrollmentId) = await repository.CreateEnrollmentAsync(form.ToInput(), User.ChangedBy(), ct);
                TempData[MessageKey] = $"Student {studentId} was added.";
                return RedirectToAction(nameof(Details), new { id = enrollmentId });
            }
            catch (DomainException ex) when (ex.StatusCode is 400 or 409)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        return View("Form", new StudentFormViewModel(form, await repository.GetLookupsAsync(ct), null));
    }

    // ----- Edit ------------------------------------------------------------

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var enrollment = await repository.GetEnrollmentAsync(id, ct);
        if (enrollment is null)
        {
            return NotFound();
        }

        return View("Form", new StudentFormViewModel(EnrollmentForm.From(enrollment), await repository.GetLookupsAsync(ct), enrollment));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, EnrollmentForm form, CancellationToken ct)
    {
        var existing = await repository.GetEnrollmentAsync(id, ct);
        if (existing is null)
        {
            return NotFound();
        }

        byte[]? rowVersion = null;
        try
        {
            rowVersion = Convert.FromBase64String(form.RowVersion ?? "");
        }
        catch (FormatException)
        {
        }

        if (rowVersion is not { Length: 8 })
        {
            ModelState.AddModelError(string.Empty, "The form is out of date. Reload the page and try again.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                await repository.UpdateEnrollmentAsync(id, rowVersion!, form.ToInput(), User.ChangedBy(), ct);
                TempData[MessageKey] = $"Changes to student {existing.StudentId} were saved.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (DomainException ex) when (ex.StatusCode is 400 or 409)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }
        }

        form.PresentationId = existing.PresentationId;
        return View("Form", new StudentFormViewModel(form, await repository.GetLookupsAsync(ct), existing));
    }

    // ----- Delete ----------------------------------------------------------

    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var enrollment = await repository.GetEnrollmentAsync(id, ct);
        return enrollment is null ? NotFound() : View(enrollment);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken ct)
    {
        try
        {
            var studentDeleted = await repository.DeleteEnrollmentAsync(id, User.ChangedBy(), ct);
            TempData[MessageKey] = studentDeleted
                ? "The enrollment and the student record were deleted (it was their only course)."
                : "The enrollment was deleted. The student's other courses were kept.";
        }
        catch (RecordNotFoundException)
        {
            TempData[ErrorKey] = "That record no longer exists. It may have been deleted already.";
        }
        return RedirectToAction(nameof(Index));
    }

    // ----- Scores ----------------------------------------------------------

    [HttpPost]
    public async Task<IActionResult> SaveScore(int id, ScoreForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData[ErrorKey] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }
        else
        {
            try
            {
                await repository.SaveResultAsync(id, form.ToInput(), User.ChangedBy(), ct);
                TempData[MessageKey] = $"Result for assessment {form.AssessmentId} was saved.";
            }
            catch (DomainException ex) when (ex.StatusCode is 400 or 404)
            {
                TempData[ErrorKey] = ex.Message;
            }
        }
        return Redirect(Url.Action(nameof(Details), new { id }) + "#assessments");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteScore(int id, int assessmentId, CancellationToken ct)
    {
        try
        {
            await repository.DeleteResultAsync(id, assessmentId, User.ChangedBy(), ct);
            TempData[MessageKey] = $"Result for assessment {assessmentId} was removed.";
        }
        catch (DomainException ex) when (ex.StatusCode is 400 or 404)
        {
            TempData[ErrorKey] = ex.Message;
        }
        return Redirect(Url.Action(nameof(Details), new { id }) + "#assessments");
    }
}
