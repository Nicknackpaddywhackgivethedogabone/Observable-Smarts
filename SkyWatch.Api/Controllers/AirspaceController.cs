using System.Text.Json;
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
    /// Returns FAA Special Use Airspace data as a merged GeoJSON FeatureCollection.
    /// Combines both Prohibited and Restricted area datasets.
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
        var allFeatures = new List<JsonElement>();

        foreach (var url in new[] { FaaProhibitedUrl, FaaRestrictedUrl })
        {
            try
            {
                var json = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("features", out var features) &&
                    features.ValueKind == JsonValueKind.Array)
                {
                    foreach (var feature in features.EnumerateArray())
                    {
                        allFeatures.Add(feature.Clone());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Airspace] Failed to fetch {url}: {ex.Message}");
            }
        }

        if (allFeatures.Count == 0) return null;

        // Merge into a single FeatureCollection
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "FeatureCollection");
            writer.WriteStartArray("features");
            foreach (var feature in allFeatures)
            {
                feature.WriteTo(writer);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
