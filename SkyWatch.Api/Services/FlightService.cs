using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SkyWatch.Core.Models;

namespace SkyWatch.Api.Services;

public class FlightService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FlightService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ApiStatusService _apiStatus;

    private const string FlightsCacheKey = "flights_data";
    private const string TrailsCacheKey = "flights_trails";
    private const string SourceName = "OpenSky";
    private const string OAuthTokenCacheKey = "opensky_oauth_token";
    private const string OAuthTokenEndpoint = "https://auth.opensky-network.org/auth/realms/opensky-network/protocol/openid-connect/token";

    public FlightService(IHttpClientFactory httpClientFactory, IMemoryCache cache,
        ILogger<FlightService> logger, IConfiguration configuration, ApiStatusService apiStatus)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _configuration = configuration;
        _apiStatus = apiStatus;
    }

    public async Task RefreshFlightDataAsync(CancellationToken ct = default)
    {
        try
        {
            string url = "https://opensky-network.org/api/states/all";

            var httpResponse = await FetchOpenSky(url, ct);

            // If OAuth2 token was rejected, invalidate and retry once with a fresh token
            if (httpResponse != null && httpResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _cache.Remove(OAuthTokenCacheKey);
                _logger.LogWarning("OpenSky returned 401 — retrying with fresh token / anonymous");
                httpResponse = await FetchOpenSky(url, ct);
            }

            if (httpResponse == null)
            {
                _apiStatus.ReportFailure(SourceName, "Request failed");
                return;
            }

            var statusCode = (int)httpResponse.StatusCode;

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorBody = await httpResponse.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("OpenSky returned HTTP {StatusCode}: {Body}",
                    statusCode, errorBody.Length > 200 ? errorBody[..200] : errorBody);
                _apiStatus.ReportFailure(SourceName, $"HTTP {statusCode}", statusCode);
                return;
            }

            var response = await httpResponse.Content.ReadAsStringAsync(ct);
            var json = JsonDocument.Parse(response);

            var flights = new List<FlightPosition>();
            var statesArray = json.RootElement.GetProperty("states");

            foreach (var state in statesArray.EnumerateArray())
            {
                var arr = state.EnumerateArray().ToArray();
                if (arr.Length < 17) continue;

                var icao24 = arr[0].GetString() ?? "";
                var lat = arr[6].ValueKind == JsonValueKind.Number ? arr[6].GetDouble() : (double?)null;
                var lon = arr[5].ValueKind == JsonValueKind.Number ? arr[5].GetDouble() : (double?)null;

                if (lat == null || lon == null) continue;

                var emitterCat = arr.Length > 17 && arr[17].ValueKind == JsonValueKind.Number
                    ? (int?)arr[17].GetInt32() : null;
                var callsign = arr[1].GetString()?.Trim();

                var flight = new FlightPosition
                {
                    Icao24 = icao24,
                    Callsign = callsign,
                    Longitude = lon,
                    Latitude = lat,
                    AltitudeM = arr[7].ValueKind == JsonValueKind.Number ? arr[7].GetDouble() : null,
                    VelocityMs = arr[9].ValueKind == JsonValueKind.Number ? arr[9].GetDouble() : null,
                    Heading = arr[10].ValueKind == JsonValueKind.Number ? arr[10].GetDouble() : null,
                    OnGround = arr[8].ValueKind == JsonValueKind.True,
                    OriginCountry = arr[2].GetString(),
                    VerticalRate = arr[11].ValueKind == JsonValueKind.Number ? arr[11].GetDouble() : null,
                    Squawk = arr[14].ValueKind != JsonValueKind.Null ? arr[14].GetString() : null,
                    EmitterCategory = emitterCat,
                    Category = ClassifyFlight(icao24, callsign, emitterCat),
                    Timestamp = DateTime.UtcNow
                };

                flights.Add(flight);
            }

            // Update trails
            var trails = _cache.Get<Dictionary<string, List<TrailPoint>>>(TrailsCacheKey)
                         ?? new Dictionary<string, List<TrailPoint>>();

            foreach (var flight in flights)
            {
                if (flight.Latitude == null || flight.Longitude == null) continue;

                if (!trails.ContainsKey(flight.Icao24))
                    trails[flight.Icao24] = new List<TrailPoint>();

                trails[flight.Icao24].Add(new TrailPoint
                {
                    Latitude = flight.Latitude.Value,
                    Longitude = flight.Longitude.Value,
                    AltitudeM = flight.AltitudeM ?? 0,
                    Timestamp = flight.Timestamp
                });

                // Keep last 5 trail points
                if (trails[flight.Icao24].Count > 5)
                    trails[flight.Icao24] = trails[flight.Icao24].TakeLast(5).ToList();

                flight.Trail = trails[flight.Icao24];
            }

            _cache.Set(FlightsCacheKey, flights, TimeSpan.FromMinutes(2));
            _cache.Set(TrailsCacheKey, trails, TimeSpan.FromMinutes(10));

            _logger.LogInformation("Flight data refreshed: {Count} aircraft", flights.Count);
            _apiStatus.ReportSuccess(SourceName, flights.Count, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh flight data from OpenSky");
            _apiStatus.ReportFailure(SourceName, ex.Message);
        }
    }

    private async Task<HttpResponseMessage?> FetchOpenSky(string url, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("OpenSky");
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Prefer OAuth2 client credentials (new OpenSky auth)
            var clientId = _configuration["OpenSkyClientId"];
            var clientSecret = _configuration["OpenSkyClientSecret"];

            if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            {
                var token = await GetOAuthTokenAsync(clientId, clientSecret, ct);
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
            }
            else
            {
                // Legacy fallback: Basic Auth (deprecated by OpenSky)
                var username = _configuration["OpenSkyUsername"];
                var password = _configuration["OpenSkyPassword"];

                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    var credentials = Convert.ToBase64String(
                        System.Text.Encoding.ASCII.GetBytes($"{username}:{password}"));
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                }
            }

            return await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSky HTTP request failed");
            return null;
        }
    }

    private async Task<string?> GetOAuthTokenAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        if (_cache.TryGetValue(OAuthTokenCacheKey, out string? cachedToken))
            return cachedToken;

        try
        {
            var authClient = _httpClientFactory.CreateClient("OpenSkyAuth");
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            });

            var response = await authClient.PostAsync(OAuthTokenEndpoint, form, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("OpenSky OAuth token request failed: HTTP {Status} — {Body}",
                    (int)response.StatusCode, body.Length > 200 ? body[..200] : body);
                return null;
            }

            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var accessToken = json.RootElement.GetProperty("access_token").GetString();

            if (!string.IsNullOrEmpty(accessToken))
            {
                // Cache for 25 minutes (tokens expire after 30)
                _cache.Set(OAuthTokenCacheKey, accessToken, TimeSpan.FromMinutes(25));
            }

            return accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to obtain OpenSky OAuth token");
            return null;
        }
    }

    public List<FlightPosition> GetFlights(FlightCategory? categoryFilter = null)
    {
        var flights = _cache.Get<List<FlightPosition>>(FlightsCacheKey) ?? new List<FlightPosition>();
        if (categoryFilter != null)
            return flights.Where(f => f.Category == categoryFilter.Value).ToList();
        return flights;
    }

    public FlightPosition? GetFlight(string icao24)
    {
        var flights = _cache.Get<List<FlightPosition>>(FlightsCacheKey);
        return flights?.FirstOrDefault(f => f.Icao24.Equals(icao24, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AircraftMetadata?> GetAircraftMetadataAsync(string icao24, CancellationToken ct = default)
    {
        var cacheKey = $"aircraft_meta_{icao24}";
        if (_cache.TryGetValue(cacheKey, out AircraftMetadata? cached))
            return cached;

        try
        {
            var client = _httpClientFactory.CreateClient("OpenSky");
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://opensky-network.org/api/metadata/aircraft/icao/{icao24}");

            // Add auth if available
            var clientId = _configuration["OpenSkyClientId"];
            var clientSecret = _configuration["OpenSkyClientSecret"];
            if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            {
                var token = await GetOAuthTokenAsync(clientId, clientSecret, ct);
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;

            var metadata = new AircraftMetadata
            {
                Icao24 = icao24,
                Manufacturer = root.TryGetProperty("manufacturerName", out var m) ? m.GetString() : null,
                Model = root.TryGetProperty("model", out var mo) ? mo.GetString() : null,
                Operator = root.TryGetProperty("operator", out var op) ? op.GetString() : null,
                Owner = root.TryGetProperty("owner", out var ow) ? ow.GetString() : null,
                Registration = root.TryGetProperty("registration", out var r) ? r.GetString() : null,
                TypeCode = root.TryGetProperty("typecode", out var tc) ? tc.GetString() : null,
            };

            _cache.Set(cacheKey, metadata, TimeSpan.FromHours(24));
            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch aircraft metadata for {Icao24}", icao24);
            return null;
        }
    }

    private static FlightCategory ClassifyFlight(string icao24, string? callsign, int? emitterCategory = null)
    {
        if (string.IsNullOrEmpty(icao24)) return FlightCategory.Unknown;

        // Military ICAO24 ranges (approximate)
        var icaoUpper = icao24.ToUpperInvariant();
        if (icaoUpper.StartsWith("AE") || icaoUpper.StartsWith("AF") ||
            icaoUpper.StartsWith("43C") || icaoUpper.StartsWith("43D"))
            return FlightCategory.Military;

        // Use ADS-B emitter category when available
        if (emitterCategory.HasValue && emitterCategory.Value > 0)
        {
            return emitterCategory.Value switch
            {
                2 => FlightCategory.GeneralAviation,  // Light < 15,500 lbs
                3 or 4 or 5 or 6 => FlightCategory.Commercial, // Small/Large/Heavy
                7 => FlightCategory.Military,          // High performance > 5g
                8 => FlightCategory.GeneralAviation,   // Rotorcraft
                9 or 10 or 11 or 12 => FlightCategory.GeneralAviation, // Glider/balloon/para/ultralight
                14 => FlightCategory.GeneralAviation,  // UAV
                _ => FlightCategory.Unknown
            };
        }

        if (string.IsNullOrEmpty(callsign)) return FlightCategory.Unknown;

        // Common commercial airline prefixes
        var cs = callsign.ToUpperInvariant();
        string[] commercialPrefixes = { "AAL", "UAL", "DAL", "SWA", "BAW", "DLH", "AFR", "KLM",
            "RYR", "EZY", "THY", "SIA", "QFA", "CPA", "ANA", "JAL", "ETH" };
        string[] cargoPrefixes = { "FDX", "UPS", "GTI", "CLX", "BOX", "ABW" };

        foreach (var pfx in commercialPrefixes)
            if (cs.StartsWith(pfx)) return FlightCategory.Commercial;
        foreach (var pfx in cargoPrefixes)
            if (cs.StartsWith(pfx)) return FlightCategory.Cargo;

        // If callsign starts with a letter pattern like N followed by digits, likely GA
        if (cs.Length >= 2 && cs[0] == 'N' && char.IsDigit(cs[1]))
            return FlightCategory.GeneralAviation;

        return FlightCategory.Commercial; // Default assumption
    }
}
