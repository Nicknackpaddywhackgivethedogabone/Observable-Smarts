using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SkyWatch.Core.Models;

namespace SkyWatch.Api.Services;

public class OpenSourceImageryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OpenSourceImageryService> _logger;
    private readonly IConfiguration _configuration;

    private const string ImageryCacheKey = "imagery_scenes";

    public OpenSourceImageryService(IHttpClientFactory httpClientFactory, IMemoryCache cache,
        ILogger<OpenSourceImageryService> logger, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task RefreshImageryAsync(CancellationToken ct = default)
    {
        var scenes = new List<ImageryScene>();

        await Task.WhenAll(
            FetchCopernicusScenes(scenes, ct),
            FetchUsgsScenes(scenes, ct)
        );

        _cache.Set(ImageryCacheKey, scenes, TimeSpan.FromMinutes(45));
        _logger.LogInformation("Imagery data refreshed: {Count} scenes", scenes.Count);
    }

    public List<ImageryScene> GetRecentScenes()
    {
        return _cache.Get<List<ImageryScene>>(ImageryCacheKey) ?? new List<ImageryScene>();
    }

    private async Task FetchCopernicusScenes(List<ImageryScene> scenes, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Copernicus");
            var since = DateTime.UtcNow.AddHours(-24).ToString("yyyy-MM-ddTHH:mm:ss.000Z");

            var url = $"https://catalogue.dataspace.copernicus.eu/odata/v1/Products?" +
                      $"$filter=ContentDate/Start gt {since}&" +
                      "$orderby=ContentDate/Start desc&" +
                      "$top=50&" +
                      "$select=Id,Name,ContentDate,GeoFootprint,S3Path";

            var response = await client.GetStringAsync(url, ct);
            var json = JsonDocument.Parse(response);

            if (json.RootElement.TryGetProperty("value", out var value))
            {
                foreach (var item in value.EnumerateArray())
                {
                    try
                    {
                        var scene = new ImageryScene
                        {
                            Id = item.GetProperty("Id").GetString() ?? "",
                            Source = "Copernicus",
                            Sensor = ExtractSensorFromName(item.GetProperty("Name").GetString() ?? ""),
                            AcquisitionDate = item.TryGetProperty("ContentDate", out var cd) &&
                                              cd.TryGetProperty("Start", out var start)
                                ? start.GetDateTime()
                                : DateTime.UtcNow,
                            ThumbnailUrl = $"https://catalogue.dataspace.copernicus.eu/odata/v1/Assets({item.GetProperty("Id").GetString()})/quicklook.jpg",
                            FullImageUrl = $"https://browser.dataspace.copernicus.eu/?zoom=10&id={item.GetProperty("Id").GetString()}"
                        };

                        if (item.TryGetProperty("GeoFootprint", out var footprint))
                        {
                            scene.Footprint = ParseGeoJsonPolygon(footprint);
                        }

                        scenes.Add(scene);
                    }
                    catch
                    {
                        // Skip malformed entries
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} Copernicus scenes", scenes.Count(s => s.Source == "Copernicus"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Copernicus imagery data");
        }
    }

    private async Task FetchUsgsScenes(List<ImageryScene> scenes, CancellationToken ct)
    {
        try
        {
            var username = _configuration["UsgsM2MUsername"];
            var password = _configuration["UsgsM2MPassword"];

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                _logger.LogDebug("USGS M2M credentials not configured, skipping USGS imagery");
                return;
            }

            var client = _httpClientFactory.CreateClient("USGS");
            var baseUrl = "https://m2m.cr.usgs.gov/api/api/json/stable";

            // Login
            var loginPayload = JsonSerializer.Serialize(new { username, password });
            var loginResponse = await client.PostAsync($"{baseUrl}/login",
                new StringContent(loginPayload, System.Text.Encoding.UTF8, "application/json"), ct);
            var loginJson = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync(ct));
            var apiKey = loginJson.RootElement.GetProperty("data").GetString();

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("USGS login failed");
                return;
            }

            client.DefaultRequestHeaders.Add("X-Auth-Token", apiKey);

            // Search for recent Landsat scenes
            var searchPayload = JsonSerializer.Serialize(new
            {
                datasetName = "landsat_ot_c2_l1",
                sceneFilter = new
                {
                    acquisitionFilter = new
                    {
                        start = DateTime.UtcNow.AddHours(-24).ToString("yyyy-MM-dd"),
                        end = DateTime.UtcNow.ToString("yyyy-MM-dd")
                    }
                },
                maxResults = 25
            });

            var searchResponse = await client.PostAsync($"{baseUrl}/scene-search",
                new StringContent(searchPayload, System.Text.Encoding.UTF8, "application/json"), ct);
            var searchJson = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync(ct));

            if (searchJson.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("results", out var results))
            {
                foreach (var item in results.EnumerateArray())
                {
                    try
                    {
                        var scene = new ImageryScene
                        {
                            Id = item.GetProperty("entityId").GetString() ?? "",
                            Source = "USGS",
                            Sensor = "Landsat OLI/TIRS",
                            AcquisitionDate = item.TryGetProperty("temporalCoverage", out var tc) &&
                                              tc.TryGetProperty("startDate", out var sd)
                                ? DateTime.Parse(sd.GetString()!)
                                : DateTime.UtcNow,
                            ThumbnailUrl = item.TryGetProperty("browse", out var browse) &&
                                           browse.GetArrayLength() > 0
                                ? browse[0].GetProperty("browsePath").GetString()
                                : null,
                            FullImageUrl = $"https://earthexplorer.usgs.gov/scene/metadata/full/{item.GetProperty("entityId").GetString()}",
                            CloudCoverPercent = item.TryGetProperty("cloudCover", out var cc)
                                ? cc.GetDouble()
                                : null
                        };

                        if (item.TryGetProperty("spatialCoverage", out var spatial))
                        {
                            scene.Footprint = ParseGeoJsonPolygon(spatial);
                        }

                        scenes.Add(scene);
                    }
                    catch
                    {
                        // Skip malformed entries
                    }
                }
            }

            // Logout
            try
            {
                await client.PostAsync($"{baseUrl}/logout",
                    new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), ct);
            }
            catch { /* Ignore logout failures */ }

            _logger.LogInformation("Loaded {Count} USGS scenes", scenes.Count(s => s.Source == "USGS"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch USGS imagery data");
        }
    }

    private static string ExtractSensorFromName(string name)
    {
        if (name.StartsWith("S2", StringComparison.OrdinalIgnoreCase)) return "Sentinel-2 MSI";
        if (name.StartsWith("S1", StringComparison.OrdinalIgnoreCase)) return "Sentinel-1 SAR";
        if (name.StartsWith("S3", StringComparison.OrdinalIgnoreCase)) return "Sentinel-3";
        if (name.StartsWith("S5", StringComparison.OrdinalIgnoreCase)) return "Sentinel-5P";
        return "Unknown";
    }

    private static GeoJsonPolygon? ParseGeoJsonPolygon(JsonElement element)
    {
        try
        {
            if (element.TryGetProperty("type", out var type) &&
                type.GetString() == "Polygon" &&
                element.TryGetProperty("coordinates", out var coords))
            {
                var rings = new List<double[][]>();
                foreach (var ring in coords.EnumerateArray())
                {
                    var points = new List<double[]>();
                    foreach (var point in ring.EnumerateArray())
                    {
                        var coordArray = point.EnumerateArray().Select(c => c.GetDouble()).ToArray();
                        points.Add(coordArray);
                    }
                    rings.Add(points.ToArray());
                }

                return new GeoJsonPolygon
                {
                    Type = "Polygon",
                    Coordinates = rings.ToArray()
                };
            }
        }
        catch { }

        return null;
    }
}
