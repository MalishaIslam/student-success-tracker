using Microsoft.AspNetCore.Mvc;
using StudentSuccess.Web.Data;
using StudentSuccess.Web.Models;

namespace StudentSuccess.Web.Controllers.Api;

/// <summary>Read-only JSON API. Requires the same sign-in as the website.</summary>
[ApiController]
[Route("api/enrollments")]
[Produces("application/json")]
public sealed class EnrollmentsApiController(IStudentRepository repository) : ControllerBase
{
    /// <summary>Search enrollments with the same filters, sorting and paging as the Students page.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<EnrollmentRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<EnrollmentRow>>> Search([FromQuery] EnrollmentSearch search, CancellationToken ct)
    {
        var clean = search.Normalize();
        if (clean.HasInvalidStudentId)
        {
            ModelState.AddModelError(nameof(EnrollmentSearch.StudentId), "Student ID must be a whole number.");
            return ValidationProblem(ModelState);
        }
        return Ok(await repository.SearchAsync(clean, ct));
    }

    /// <summary>One enrollment with the student's details and assessment results.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<EnrollmentDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnrollmentDetail>> Get(int id, CancellationToken ct)
    {
        var enrollment = await repository.GetEnrollmentAsync(id, ct);
        return enrollment is null ? NotFound() : Ok(enrollment);
    }
}

[ApiController]
[Route("api/reports")]
[Produces("application/json")]
public sealed class ReportsApiController(IStudentRepository repository) : ControllerBase
{
    /// <summary>Totals and outcomes for every course run.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType<Dashboard>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Dashboard>> Dashboard(CancellationToken ct) =>
        Ok(await repository.GetDashboardAsync(ct));

    /// <summary>Students flagged by the early-warning rule, and how flagged students finished.</summary>
    [HttpGet("at-risk")]
    [ProducesResponseType<AtRiskReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AtRiskReport>> AtRisk([FromQuery] AtRiskQuery query, CancellationToken ct) =>
        Ok(await repository.GetAtRiskAsync(query, ct));
}
