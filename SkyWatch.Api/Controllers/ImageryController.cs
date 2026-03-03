using Microsoft.AspNetCore.Mvc;
using SkyWatch.Api.Services;
using SkyWatch.Core.Models;

namespace SkyWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImageryController : ControllerBase
{
    private readonly OpenSourceImageryService _imageryService;

    public ImageryController(OpenSourceImageryService imageryService)
    {
        _imageryService = imageryService;
    }

    /// <summary>
    /// Get recent satellite imagery scenes with footprint GeoJSON and metadata.
    /// </summary>
    [HttpGet("recent")]
    public ActionResult<List<ImageryScene>> GetRecent()
    {
        return Ok(_imageryService.GetRecentScenes());
    }
}
