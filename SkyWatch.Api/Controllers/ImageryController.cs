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

    /// <summary>
    /// Run live diagnostics against imagery APIs (Copernicus, USGS).
    /// </summary>
    [HttpGet("diagnostics")]
    public async Task<IActionResult> Diagnostics(CancellationToken ct)
    {
        var result = await _imageryService.RunDiagnosticsAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Manually trigger an imagery refresh instead of waiting for the 30-minute cycle.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        await _imageryService.RefreshImageryAsync(ct);
        var scenes = _imageryService.GetRecentScenes();
        return Ok(new
        {
            message = "Imagery refresh complete",
            totalScenes = scenes.Count,
            copernicus = scenes.Count(s => s.Source == "Copernicus"),
            usgs = scenes.Count(s => s.Source == "USGS")
        });
    }
}
