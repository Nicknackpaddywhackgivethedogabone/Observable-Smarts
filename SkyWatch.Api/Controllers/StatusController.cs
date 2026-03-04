using Microsoft.AspNetCore.Mvc;
using SkyWatch.Api.Services;

namespace SkyWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly ApiStatusService _apiStatus;

    public StatusController(ApiStatusService apiStatus)
    {
        _apiStatus = apiStatus;
    }

    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(_apiStatus.GetAllStatuses());
    }
}
