using Microsoft.AspNetCore.Mvc;
using SkyWatch.Api.Services;
using SkyWatch.Core.Models;

namespace SkyWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SatellitesController : ControllerBase
{
    private readonly TleService _tleService;
    private readonly ImagingFootprintService _imagingService;

    public SatellitesController(TleService tleService, ImagingFootprintService imagingService)
    {
        _tleService = tleService;
        _imagingService = imagingService;
    }

    /// <summary>
    /// Get current positions of all tracked satellites.
    /// </summary>
    [HttpGet]
    public ActionResult<List<SatellitePosition>> GetAll([FromQuery] SatelliteCategory? category = null)
    {
        var positions = _tleService.GetCurrentPositions(category);
        return Ok(positions);
    }

    /// <summary>
    /// Get predicted ground track for a specific satellite.
    /// </summary>
    [HttpGet("{noradId}/track")]
    public ActionResult<List<SatelliteTrackPoint>> GetTrack(int noradId, [FromQuery] int minutes = 90)
    {
        var track = _tleService.GetTrack(noradId, minutes);
        if (track == null)
            return NotFound(new { message = $"Satellite {noradId} not found" });
        return Ok(track);
    }

    /// <summary>
    /// Get imaging satellites with current position and swath footprint.
    /// </summary>
    [HttpGet("imaging")]
    public ActionResult<List<ImagingSatellitePosition>> GetImaging()
    {
        var imaging = _imagingService.GetImagingSatellites();
        return Ok(imaging);
    }
}
