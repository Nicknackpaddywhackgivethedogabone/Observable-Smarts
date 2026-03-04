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
            var client = _httpClientFactory.CreateClient("OpenSky");

            var username = _configuration["OpenSkyUsername"];
            var password = _configuration["OpenSkyPassword"];

            string url = "https://opensky-network.org/api/states/all";

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                var credentials = Convert.ToBase64String(
                    System.Text.Encoding.ASCII.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var httpResponse = await client.SendAsync(request, ct);
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

                var flight = new FlightPosition
                {
                    Icao24 = icao24,
                    Callsign = arr[1].GetString()?.Trim(),
                    Longitude = lon,
                    Latitude = lat,
                    AltitudeM = arr[7].ValueKind == JsonValueKind.Number ? arr[7].GetDouble() : null,
                    VelocityMs = arr[9].ValueKind == JsonValueKind.Number ? arr[9].GetDouble() : null,
                    Heading = arr[10].ValueKind == JsonValueKind.Number ? arr[10].GetDouble() : null,
                    OnGround = arr[8].ValueKind == JsonValueKind.True,
                    Category = ClassifyFlight(icao24, arr[1].GetString()?.Trim()),
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

    private static FlightCategory ClassifyFlight(string icao24, string? callsign)
    {
        if (string.IsNullOrEmpty(icao24)) return FlightCategory.Unknown;

        // Military ICAO24 ranges (approximate)
        var icaoUpper = icao24.ToUpperInvariant();
        if (icaoUpper.StartsWith("AE") || icaoUpper.StartsWith("AF") ||
            icaoUpper.StartsWith("43C") || icaoUpper.StartsWith("43D"))
            return FlightCategory.Military;

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
