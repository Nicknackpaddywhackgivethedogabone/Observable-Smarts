using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace SkyWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private static readonly string[] KeyNames =
    {
        "CesiumIonToken",
        "OpenSkyClientId",
        "OpenSkyClientSecret",
        "OpenSkyUsername",
        "OpenSkyPassword",
        "AisHubApiKey",
        "UsgsM2MApiToken",
        "UsgsM2MUsername",
        "UsgsM2MPassword"
    };

    /// <summary>
    /// Set by OpenSourceImageryService when the USGS API token is rejected.
    /// </summary>
    public static volatile bool UsgsTokenExpired;

    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public ConfigController(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    /// <summary>
    /// Returns which API keys are configured (true/false), never the actual values.
    /// Also includes a usgsTokenExpired flag when the stored token has been rejected.
    /// </summary>
    [HttpGet("status")]
    public ActionResult GetKeyStatus()
    {
        var status = new Dictionary<string, object>();
        foreach (var key in KeyNames)
        {
            status[key] = !string.IsNullOrWhiteSpace(_config[key]);
        }
        status["usgsTokenExpired"] = UsgsTokenExpired;
        return Ok(status);
    }

    /// <summary>
    /// Saves API keys to appsettings.Local.json. Only non-empty values are written.
    /// </summary>
    [HttpPost("keys")]
    public ActionResult SaveKeys([FromBody] Dictionary<string, string> keys)
    {
        var localPath = Path.Combine(_env.ContentRootPath, "appsettings.Local.json");

        // Load existing file if it exists
        var existing = new Dictionary<string, object>();
        if (System.IO.File.Exists(localPath))
        {
            var json = System.IO.File.ReadAllText(localPath);
            existing = JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                       ?? new Dictionary<string, object>();
        }

        // Merge in new keys (only known key names, only non-empty values)
        var allowedKeys = new HashSet<string>(KeyNames);
        foreach (var kvp in keys)
        {
            if (allowedKeys.Contains(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            {
                existing[kvp.Key] = kvp.Value;
            }
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        System.IO.File.WriteAllText(localPath, JsonSerializer.Serialize(existing, options));

        return Ok(new { message = "Keys saved to appsettings.Local.json. Restart the app to apply changes." });
    }
}
