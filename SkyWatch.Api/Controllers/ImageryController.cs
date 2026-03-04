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
    /// Manually trigger an imagery refresh for all sources.
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
            usgs = scenes.Count(s => s.Source == "USGS"),
            nasaCmr = scenes.Count(s => s.Source == "NASA CMR")
        });
    }

    /// <summary>
    /// Manually trigger an imagery refresh for a single source (copernicus, usgs, nasa).
    /// </summary>
    [HttpPost("refresh/{source}")]
    public async Task<IActionResult> RefreshSource(string source, CancellationToken ct)
    {
        await _imageryService.RefreshImagerySourceAsync(source, ct);
        var scenes = _imageryService.GetRecentScenes();
        var sourceLabel = source.ToLowerInvariant() switch
        {
            "nasa" or "nasacmr" => "NASA CMR",
            _ => source
        };
        var count = scenes.Count(s => s.Source.Equals(sourceLabel, StringComparison.OrdinalIgnoreCase));
        return Ok(new
        {
            message = $"{sourceLabel} refresh complete",
            source = sourceLabel,
            sceneCount = count,
            totalScenes = scenes.Count
        });
    }
}
