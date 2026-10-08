using Microsoft.AspNetCore.Mvc;
using StudentSuccess.Web.Data;
using StudentSuccess.Web.Models;

namespace StudentSuccess.Web.Controllers;

public sealed class ReportsController(IStudentRepository repository) : Controller
{
    // GET /reports/atrisk?cutoffDay=60&scoreThreshold=40&minMissed=1&presentationId=3
    public async Task<IActionResult> AtRisk([FromQuery] AtRiskQuery query, CancellationToken ct)
    {
        var lookups = await repository.GetLookupsAsync(ct);
        var report = ModelState.IsValid ? await repository.GetAtRiskAsync(query, ct) : null;
        return View(new AtRiskViewModel(query, report, lookups));
    }

    public async Task<IActionResult> Audit(CancellationToken ct) =>
        View(await repository.GetAuditLogAsync(200, ct));
}
