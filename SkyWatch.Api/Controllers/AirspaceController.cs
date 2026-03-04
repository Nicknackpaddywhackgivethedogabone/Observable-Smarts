using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace SkyWatch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AirspaceController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;

    private const string CacheKey = "airspace_sua";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    // FAA AIS Open Data Portal — Special Use Airspace (Prohibited + Restricted areas)
    private const string FaaProhibitedUrl =
        "https://services6.arcgis.com/ssFJjBXIUyZDrSYZ/arcgis/rest/services/Prohibited_Areas/FeatureServer/0/query?where=1%3D1&outFields=*&f=geojson";

    private const string FaaRestrictedUrl =
        "https://services6.arcgis.com/ssFJjBXIUyZDrSYZ/arcgis/rest/services/Restricted_Areas/FeatureServer/0/query?where=1%3D1&outFields=*&f=geojson";

    public AirspaceController(IHttpClientFactory httpClientFactory, IMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
    }

    /// <summary>
    /// Returns FAA Special Use Airspace data as passthrough GeoJSON.
    /// Falls back to an empty collection on failure.
    /// </summary>
    [HttpGet("sua")]
    public async Task<ActionResult> GetSpecialUseAirspace([FromQuery] string? type = null)
    {
        var data = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await FetchFaaAirspace();
        });

        return Content(data ?? "{\"type\":\"FeatureCollection\",\"features\":[]}", "application/json");
    }

    private async Task<string?> FetchFaaAirspace()
    {
        var client = _httpClientFactory.CreateClient("Celestrak"); // reuse a client with timeout
        var features = new List<string>();

        foreach (var url in new[] { FaaProhibitedUrl, FaaRestrictedUrl })
        {
            try
            {
                var json = await client.GetStringAsync(url);
                features.Add(json);
            }
            catch (Exception ex)
            {
                // Log but continue — partial data is better than none
                Console.WriteLine($"[Airspace] Failed to fetch {url}: {ex.Message}");
            }
        }

        if (features.Count == 0) return null;

        // Return the first successful response (could merge in the future)
        return features[0];
    }
}
