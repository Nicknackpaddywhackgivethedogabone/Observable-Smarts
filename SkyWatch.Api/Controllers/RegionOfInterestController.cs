using Microsoft.AspNetCore.Mvc;
using SkyWatch.Api.Services;
using SkyWatch.Core.Models;

namespace SkyWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RegionOfInterestController : ControllerBase
{
    private readonly RegionOfInterestService _roiService;

    public RegionOfInterestController(RegionOfInterestService roiService)
    {
        _roiService = roiService;
    }

    /// <summary>
    /// Predict which imaging satellites will pass over a region in the next N hours.
    /// </summary>
    [HttpPost("predict")]
    public ActionResult<List<SatellitePassPrediction>> PredictPasses([FromBody] RegionOfInterestRequest request)
    {
        if (request.Polygon.Coordinates.Length == 0)
            return BadRequest(new { message = "Polygon coordinates required" });

        var predictions = _roiService.PredictPasses(request.Polygon, request.HoursAhead);
        return Ok(predictions);
    }
}
