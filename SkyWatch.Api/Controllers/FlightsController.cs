using Microsoft.AspNetCore.Mvc;
using SkyWatch.Api.Services;
using SkyWatch.Core.Models;

namespace SkyWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FlightsController : ControllerBase
{
    private readonly FlightService _flightService;

    public FlightsController(FlightService flightService)
    {
        _flightService = flightService;
    }

    /// <summary>
    /// Get current aircraft positions.
    /// </summary>
    [HttpGet]
    public ActionResult<List<FlightPosition>> GetAll([FromQuery] FlightCategory? category = null)
    {
        return Ok(_flightService.GetFlights(category));
    }

    /// <summary>
    /// Get detail for a single aircraft by ICAO24 address.
    /// </summary>
    [HttpGet("{icao24}")]
    public ActionResult<FlightPosition> Get(string icao24)
    {
        var flight = _flightService.GetFlight(icao24);
        if (flight == null)
            return NotFound(new { message = $"Flight {icao24} not found" });
        return Ok(flight);
    }
}
