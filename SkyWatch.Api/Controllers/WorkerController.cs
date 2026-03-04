using Microsoft.AspNetCore.Mvc;
using SkyWatch.Api.Services;

namespace SkyWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorkerController : ControllerBase
{
    private readonly WorkerToggleService _toggle;

    public WorkerController(WorkerToggleService toggle)
    {
        _toggle = toggle;
    }

    [HttpPost("{stream}/enable")]
    public IActionResult Enable(string stream)
    {
        _toggle.SetEnabled(stream, true);
        return Ok(new { stream, enabled = true });
    }

    [HttpPost("{stream}/disable")]
    public IActionResult Disable(string stream)
    {
        _toggle.SetEnabled(stream, false);
        return Ok(new { stream, enabled = false });
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(_toggle.GetStatus());
    }
}
