using Microsoft.AspNetCore.Mvc;
using SkyWatch.Api.Services;

namespace SkyWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CaptureController : ControllerBase
{
    private readonly DataCaptureService _captureService;

    public CaptureController(DataCaptureService captureService)
    {
        _captureService = captureService;
    }

    /// <summary>
    /// Get current capture status and log file info.
    /// </summary>
    [HttpGet("status")]
    public ActionResult<DataCaptureStatus> GetStatus()
    {
        return Ok(_captureService.GetStatus());
    }

    /// <summary>
    /// Enable or disable data capture.
    /// </summary>
    [HttpPost("toggle")]
    public IActionResult Toggle([FromBody] CaptureToggleRequest request)
    {
        _captureService.SetEnabled(request.Enabled);
        return Ok(_captureService.GetStatus());
    }

    /// <summary>
    /// Download a specific stream's log file.
    /// </summary>
    [HttpGet("download/{streamName}")]
    public IActionResult Download(string streamName)
    {
        var (stream, fileName) = _captureService.GetLogFile(streamName);
        if (stream == null)
            return NotFound(new { message = $"No log file for stream '{streamName}'" });
        return File(stream, "application/x-ndjson", fileName);
    }

    /// <summary>
    /// Clear all captured log files.
    /// </summary>
    [HttpDelete("clear")]
    public IActionResult Clear()
    {
        _captureService.ClearLogs();
        return Ok(new { message = "All capture logs cleared" });
    }
}

public class CaptureToggleRequest
{
    public bool Enabled { get; set; }
}
