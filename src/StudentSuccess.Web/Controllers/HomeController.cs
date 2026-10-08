using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StudentSuccess.Web.Data;

namespace StudentSuccess.Web.Controllers;

public sealed class HomeController(IStudentRepository repository) : Controller
{
    /// <summary>Dashboard: totals and outcomes for every course run.</summary>
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await repository.GetDashboardAsync(ct));

    /// <summary>Shown for unexpected errors. Details go to the server log, not the page.</summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var error = HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Error;
        if (error is DomainException domain)
        {
            Response.StatusCode = domain.StatusCode;
            ViewData["Message"] = domain.Message;
        }
        else
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
        ViewData["RequestId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View();
    }

    /// <summary>Friendly page for 404 and other status codes.</summary>
    [AllowAnonymous]
    [Route("home/status/{code:int}")]
    public IActionResult Status(int code)
    {
        ViewData["Code"] = code;
        return View("Status");
    }
}
