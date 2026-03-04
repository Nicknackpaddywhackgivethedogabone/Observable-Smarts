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
    private readonly ApiStatusService _apiStatus;
    private readonly DataCaptureService _capture;

    private const string ImageryCacheKey = "imagery_scenes";

    public OpenSourceImageryService(IHttpClientFactory httpClientFactory, IMemoryCache cache,
        ILogger<OpenSourceImageryService> logger, IConfiguration configuration, ApiStatusService apiStatus,
        DataCaptureService capture)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _configuration = configuration;
        _apiStatus = apiStatus;
        _capture = capture;
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

        var copernicusCount = scenes.Count(s => s.Source == "Copernicus");
        var usgsCount = scenes.Count(s => s.Source == "USGS");

        // Always report status so the ticker shows something
        if (copernicusCount > 0)
            _apiStatus.ReportSuccess("Copernicus", copernicusCount);
        else
            _apiStatus.ReportFailure("Copernicus", "0 scenes returned");

        if (usgsCount > 0)
            _apiStatus.ReportSuccess("USGS", usgsCount);
    }

    public List<ImageryScene> GetRecentScenes()
    {
        return _cache.Get<List<ImageryScene>>(ImageryCacheKey) ?? new List<ImageryScene>();
    }

    /// <summary>
    /// Run a lightweight diagnostic check against imagery APIs and return structured results.
    /// </summary>
    public async Task<object> RunDiagnosticsAsync(CancellationToken ct)
    {
        var results = new Dictionary<string, object>();

        // Cache status
        var cached = _cache.Get<List<ImageryScene>>(ImageryCacheKey);
        results["cache"] = new
        {
            scenesInCache = cached?.Count ?? 0,
            copernicusScenes = cached?.Count(s => s.Source == "Copernicus") ?? 0,
            usgsScenes = cached?.Count(s => s.Source == "USGS") ?? 0
        };

        // Test Copernicus
        try
        {
            var client = _httpClientFactory.CreateClient("Copernicus");
            var since = DateTime.UtcNow.AddHours(-24).ToString("yyyy-MM-ddTHH:mm:ss.000Z");
            var url = $"https://catalogue.dataspace.copernicus.eu/odata/v1/Products?" +
                      $"$filter=ContentDate/Start gt {since}&$orderby=ContentDate/Start desc&$top=1";

            var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var statusCode = (int)response.StatusCode;
            var preview = body.Length > 500 ? body[..500] + "..." : body;

            int resultCount = 0;
            try
            {
                var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("value", out var val))
                    resultCount = val.GetArrayLength();
            }
            catch { }

            results["copernicus"] = new
            {
                reachable = response.IsSuccessStatusCode,
                httpStatus = statusCode,
                resultCount,
                responsePreview = preview,
                url
            };
        }
        catch (Exception ex)
        {
            results["copernicus"] = new
            {
                reachable = false,
                error = ex.Message,
                errorType = ex.GetType().Name
            };
        }

        // Test USGS credentials
        var hasToken = !string.IsNullOrEmpty(_configuration["UsgsM2MApiToken"]);
        var hasCredentials = !string.IsNullOrEmpty(_configuration["UsgsM2MUsername"]) &&
                             !string.IsNullOrEmpty(_configuration["UsgsM2MPassword"]);

        results["usgs"] = new
        {
            apiTokenConfigured = hasToken,
            usernamePasswordConfigured = hasCredentials,
            anyCredentials = hasToken || hasCredentials
        };

        return results;
    }

    private async Task FetchCopernicusScenes(List<ImageryScene> scenes, CancellationToken ct)
    {
        string url = "";
        try
        {
            var client = _httpClientFactory.CreateClient("Copernicus");
            var since = DateTime.UtcNow.AddHours(-24).ToString("yyyy-MM-ddTHH:mm:ss.000Z");

            url = $"https://catalogue.dataspace.copernicus.eu/odata/v1/Products?" +
                      $"$filter=ContentDate/Start gt {since}&" +
                      "$orderby=ContentDate/Start desc&" +
                      "$top=50&" +
                      "$select=Id,Name,ContentDate,GeoFootprint,S3Path";

            var httpResponse = await client.GetAsync(url, ct);
            var statusCode = (int)httpResponse.StatusCode;
            var response = await httpResponse.Content.ReadAsStringAsync(ct);

            // Always log raw response to diagnostics
            _capture.LogData("diagnostics", new
            {
                source = "Copernicus",
                url,
                httpStatus = statusCode,
                responseLength = response.Length,
                responsePreview = response.Length > 2000 ? response[..2000] + "...[truncated]" : response,
                success = httpResponse.IsSuccessStatusCode
            });

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Copernicus returned HTTP {StatusCode}: {Body}",
                    statusCode, response.Length > 200 ? response[..200] : response);
                _apiStatus.ReportFailure("Copernicus", $"HTTP {statusCode}", statusCode);
                return;
            }

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
                    catch (Exception ex)
                    {
                        _capture.LogData("diagnostics", new
                        {
                            source = "Copernicus",
                            issue = "malformed_scene_entry",
                            error = ex.Message
                        });
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} Copernicus scenes", scenes.Count(s => s.Source == "Copernicus"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Copernicus imagery data");
            _apiStatus.ReportFailure("Copernicus", ex.Message);
            _capture.LogData("diagnostics", new
            {
                source = "Copernicus",
                url,
                error = ex.Message,
                errorType = ex.GetType().Name,
                stackTrace = ex.StackTrace
            });
        }
    }

    private async Task FetchUsgsScenes(List<ImageryScene> scenes, CancellationToken ct)
    {
        try
        {
            var apiToken = _configuration["UsgsM2MApiToken"];
            var username = _configuration["UsgsM2MUsername"];
            var password = _configuration["UsgsM2MPassword"];

            var hasToken = !string.IsNullOrEmpty(apiToken);
            var hasCredentials = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);

            if (!hasToken && !hasCredentials)
            {
                _logger.LogDebug("USGS M2M credentials not configured, skipping USGS imagery");
                _capture.LogData("diagnostics", new
                {
                    source = "USGS",
                    issue = "no_credentials_configured",
                    hasToken,
                    hasCredentials
                });
                return;
            }

            var client = _httpClientFactory.CreateClient("USGS");
            var baseUrl = "https://m2m.cr.usgs.gov/api/api/json/stable";

            string? authToken = null;

            // Try the API token first
            if (hasToken)
            {
                var valid = await ValidateUsgsToken(client, baseUrl, apiToken!, ct);
                if (valid)
                {
                    authToken = apiToken;
                    SkyWatch.Api.Controllers.ConfigController.UsgsTokenExpired = false;
                    _logger.LogDebug("Using USGS API token");
                }
                else
                {
                    SkyWatch.Api.Controllers.ConfigController.UsgsTokenExpired = true;
                    _logger.LogWarning("USGS API token is expired or invalid — falling back to username/password");
                    _capture.LogData("diagnostics", new
                    {
                        source = "USGS",
                        issue = "token_expired_or_invalid"
                    });
                }
            }

            // Fall back to username/password login
            if (authToken == null && hasCredentials)
            {
                var loginPayload = JsonSerializer.Serialize(new { username, password });
                var loginResponse = await client.PostAsync($"{baseUrl}/login",
                    new StringContent(loginPayload, System.Text.Encoding.UTF8, "application/json"), ct);
                var loginBody = await loginResponse.Content.ReadAsStringAsync(ct);

                _capture.LogData("diagnostics", new
                {
                    source = "USGS",
                    step = "login",
                    httpStatus = (int)loginResponse.StatusCode,
                    responsePreview = loginBody.Length > 500 ? loginBody[..500] : loginBody
                });

                var loginJson = JsonDocument.Parse(loginBody);
                authToken = loginJson.RootElement.GetProperty("data").GetString();
            }

            if (string.IsNullOrEmpty(authToken))
            {
                _logger.LogWarning("USGS authentication failed (all methods)");
                _apiStatus.ReportFailure("USGS", "Authentication failed");
                _capture.LogData("diagnostics", new
                {
                    source = "USGS",
                    issue = "auth_failed_all_methods"
                });
                return;
            }

            client.DefaultRequestHeaders.Add("X-Auth-Token", authToken);

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
            var searchBody = await searchResponse.Content.ReadAsStringAsync(ct);

            _capture.LogData("diagnostics", new
            {
                source = "USGS",
                step = "scene-search",
                httpStatus = (int)searchResponse.StatusCode,
                responseLength = searchBody.Length,
                responsePreview = searchBody.Length > 2000 ? searchBody[..2000] + "...[truncated]" : searchBody
            });

            var searchJson = JsonDocument.Parse(searchBody);

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
            _apiStatus.ReportFailure("USGS", ex.Message);
            _capture.LogData("diagnostics", new
            {
                source = "USGS",
                error = ex.Message,
                errorType = ex.GetType().Name,
                stackTrace = ex.StackTrace
            });
        }
    }

    private async Task<bool> ValidateUsgsToken(HttpClient client, string baseUrl, string token, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/dataset-catalogs");
            request.Headers.Add("X-Auth-Token", token);
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return false;

            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            // USGS M2M returns errorCode != null when auth fails
            if (json.RootElement.TryGetProperty("errorCode", out var errorCode) &&
                errorCode.ValueKind != JsonValueKind.Null)
            {
                return false;
            }
            return true;
        }
        catch
        {
            return false;
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
