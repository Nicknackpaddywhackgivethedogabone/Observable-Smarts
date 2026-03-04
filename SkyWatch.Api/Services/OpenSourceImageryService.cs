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
            FetchUsgsScenes(scenes, ct),
            FetchNasaCmrScenes(scenes, ct)
        );

        _cache.Set(ImageryCacheKey, scenes, TimeSpan.FromMinutes(45));
        _logger.LogInformation("Imagery data refreshed: {Count} scenes", scenes.Count);

        ReportSourceStatus(scenes, "Copernicus");
        ReportSourceStatus(scenes, "USGS");
        ReportSourceStatus(scenes, "NASA CMR");
    }

    /// <summary>
    /// Refresh a single imagery source instead of all three.
    /// </summary>
    public async Task RefreshImagerySourceAsync(string source, CancellationToken ct)
    {
        var existing = _cache.Get<List<ImageryScene>>(ImageryCacheKey) ?? new List<ImageryScene>();
        var newScenes = new List<ImageryScene>();

        string normalizedSource;
        switch (source.ToLowerInvariant())
        {
            case "copernicus":
                normalizedSource = "Copernicus";
                await FetchCopernicusScenes(newScenes, ct);
                break;
            case "usgs":
                normalizedSource = "USGS";
                await FetchUsgsScenes(newScenes, ct);
                break;
            case "nasa":
            case "nasacmr":
                normalizedSource = "NASA CMR";
                await FetchNasaCmrScenes(newScenes, ct);
                break;
            default:
                return;
        }

        // Replace scenes from this source, keep others
        existing.RemoveAll(s => s.Source == normalizedSource);
        existing.AddRange(newScenes);
        _cache.Set(ImageryCacheKey, existing, TimeSpan.FromMinutes(45));

        ReportSourceStatus(existing, normalizedSource);
    }

    private void ReportSourceStatus(List<ImageryScene> scenes, string source)
    {
        var count = scenes.Count(s => s.Source == source);
        if (count > 0)
            _apiStatus.ReportSuccess(source, count);
        else
            _apiStatus.ReportFailure(source, "0 scenes returned");
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
            usgsScenes = cached?.Count(s => s.Source == "USGS") ?? 0,
            nasaCmrScenes = cached?.Count(s => s.Source == "NASA CMR") ?? 0
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

            // 15-second timeout so we fail fast if Copernicus is unreachable
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            var httpResponse = await client.GetAsync(url, timeoutCts.Token);
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

                        lock (scenes) { scenes.Add(scene); }
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

            // Fall back to login-token (replaces deprecated /login endpoint)
            if (authToken == null && hasCredentials)
            {
                // USGS M2M now uses /login-token with { username, token } where
                // token = the application token (stored in UsgsM2MPassword config key)
                var loginPayload = JsonSerializer.Serialize(new { username, token = password });
                var loginResponse = await client.PostAsync($"{baseUrl}/login-token",
                    new StringContent(loginPayload, System.Text.Encoding.UTF8, "application/json"), ct);
                var loginBody = await loginResponse.Content.ReadAsStringAsync(ct);

                _capture.LogData("diagnostics", new
                {
                    source = "USGS",
                    step = "login-token",
                    httpStatus = (int)loginResponse.StatusCode,
                    responsePreview = loginBody.Length > 500 ? loginBody[..500] : loginBody
                });

                if (loginResponse.IsSuccessStatusCode)
                {
                    var loginJson = JsonDocument.Parse(loginBody);
                    authToken = loginJson.RootElement.GetProperty("data").GetString();
                }
                else
                {
                    _logger.LogWarning("USGS login-token returned HTTP {StatusCode}", (int)loginResponse.StatusCode);
                }
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

                        lock (scenes) { scenes.Add(scene); }
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

    private async Task FetchNasaCmrScenes(List<ImageryScene> scenes, CancellationToken ct)
    {
        string url = "";
        try
        {
            var client = _httpClientFactory.CreateClient("NasaCMR");
            var since = DateTime.UtcNow.AddHours(-24).ToString("yyyy-MM-ddTHH:mm:ss.000Z");

            url = $"https://cmr.earthdata.nasa.gov/search/granules.json" +
                  $"?short_name=MOD09GA&provider=LPCLOUD" +
                  $"&temporal={since}," +
                  $"&page_size=25&sort_key=-start_date";

            var httpResponse = await client.GetAsync(url, ct);
            var statusCode = (int)httpResponse.StatusCode;
            var response = await httpResponse.Content.ReadAsStringAsync(ct);

            _capture.LogData("diagnostics", new
            {
                source = "NASA CMR",
                url,
                httpStatus = statusCode,
                responseLength = response.Length,
                responsePreview = response.Length > 2000 ? response[..2000] + "...[truncated]" : response,
                success = httpResponse.IsSuccessStatusCode
            });

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("NASA CMR returned HTTP {StatusCode}", statusCode);
                _apiStatus.ReportFailure("NASA CMR", $"HTTP {statusCode}", statusCode);
                return;
            }

            var json = JsonDocument.Parse(response);

            if (json.RootElement.TryGetProperty("feed", out var feed) &&
                feed.TryGetProperty("entry", out var entries))
            {
                foreach (var item in entries.EnumerateArray())
                {
                    try
                    {
                        var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                        var timeStart = item.TryGetProperty("time_start", out var ts) ? ts.GetString() : null;

                        var scene = new ImageryScene
                        {
                            Id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : Guid.NewGuid().ToString(),
                            Source = "NASA CMR",
                            Sensor = "MODIS Terra",
                            AcquisitionDate = timeStart != null ? DateTime.Parse(timeStart) : DateTime.UtcNow,
                        };

                        // Extract browse image URL from links
                        if (item.TryGetProperty("links", out var links))
                        {
                            foreach (var link in links.EnumerateArray())
                            {
                                var rel = link.TryGetProperty("rel", out var r) ? r.GetString() : "";
                                var href = link.TryGetProperty("href", out var h) ? h.GetString() : "";
                                if (rel != null && rel.Contains("browse") && href != null)
                                {
                                    scene.ThumbnailUrl = href;
                                    break;
                                }
                            }
                        }

                        // Parse bounding box → polygon footprint
                        if (item.TryGetProperty("boxes", out var boxes) && boxes.GetArrayLength() > 0)
                        {
                            var boxStr = boxes[0].GetString();
                            if (boxStr != null)
                            {
                                // CMR box format: "south west north east"
                                var parts = boxStr.Split(' ');
                                if (parts.Length == 4 &&
                                    double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var south) &&
                                    double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var west) &&
                                    double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var north) &&
                                    double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var east))
                                {
                                    scene.Footprint = new GeoJsonPolygon
                                    {
                                        Type = "Polygon",
                                        Coordinates = new[]
                                        {
                                            new[]
                                            {
                                                new[] { west, south },
                                                new[] { east, south },
                                                new[] { east, north },
                                                new[] { west, north },
                                                new[] { west, south }
                                            }
                                        }
                                    };
                                }
                            }
                        }

                        // Data download URL
                        if (item.TryGetProperty("links", out var dataLinks))
                        {
                            foreach (var link in dataLinks.EnumerateArray())
                            {
                                var href = link.TryGetProperty("href", out var h) ? h.GetString() : "";
                                var rel = link.TryGetProperty("rel", out var r) ? r.GetString() : "";
                                if (href != null && href.Contains("e4ftl01.cr.usgs.gov"))
                                {
                                    scene.FullImageUrl = href;
                                    break;
                                }
                            }
                        }

                        lock (scenes) { scenes.Add(scene); }
                    }
                    catch (Exception ex)
                    {
                        _capture.LogData("diagnostics", new
                        {
                            source = "NASA CMR",
                            issue = "malformed_granule_entry",
                            error = ex.Message
                        });
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} NASA CMR scenes", scenes.Count(s => s.Source == "NASA CMR"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch NASA CMR imagery data");
            _apiStatus.ReportFailure("NASA CMR", ex.Message);
            _capture.LogData("diagnostics", new
            {
                source = "NASA CMR",
                url,
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
