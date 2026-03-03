using Microsoft.AspNetCore.Mvc;
using SkyWatch.Api.Services;
using SkyWatch.Core.Models;

namespace SkyWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ShipsController : ControllerBase
{
    private readonly ShipService _shipService;

    public ShipsController(ShipService shipService)
    {
        _shipService = shipService;
    }

    /// <summary>
    /// Get current vessel positions.
    /// </summary>
    [HttpGet]
    public ActionResult<List<ShipPosition>> GetAll([FromQuery] VesselType? type = null)
    {
        return Ok(_shipService.GetShips(type));
    }

    /// <summary>
    /// Get detail for a single vessel by MMSI.
    /// </summary>
    [HttpGet("{mmsi}")]
    public ActionResult<ShipPosition> Get(string mmsi)
    {
        var ship = _shipService.GetShip(mmsi);
        if (ship == null)
            return NotFound(new { message = $"Ship {mmsi} not found" });
        return Ok(ship);
    }
}
