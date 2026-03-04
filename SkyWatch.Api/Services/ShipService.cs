using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SkyWatch.Core.Models;

namespace SkyWatch.Api.Services;

public class ShipService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ShipService> _logger;
    private readonly IConfiguration _configuration;

    private const string ShipsCacheKey = "ships_data";
    private const string TrailsCacheKey = "ships_trails";

    public ShipService(IHttpClientFactory httpClientFactory, IMemoryCache cache,
        ILogger<ShipService> logger, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task RefreshShipDataAsync(CancellationToken ct = default)
    {
        try
        {
            var apiKey = _configuration["AisHubApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("AISHub API key not configured. Ship data will not be available.");
                return;
            }

            var client = _httpClientFactory.CreateClient("AisHub");
            var url = $"http://data.aishub.net/ws.php?username={apiKey}&format=1&output=json&compress=0";

            var response = await client.GetStringAsync(url, ct);

            // AISHub returns an array of arrays: [metadata, [data,...]]
            var json = JsonDocument.Parse(response);
            var root = json.RootElement;

            var ships = new List<ShipPosition>();

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= 2)
            {
                var dataArray = root[1];
                foreach (var vessel in dataArray.EnumerateArray())
                {
                    try
                    {
                        var ship = new ShipPosition
                        {
                            Mmsi = vessel.GetProperty("MMSI").ToString(),
                            Name = vessel.TryGetProperty("NAME", out var name) ? name.GetString() : null,
                            Latitude = vessel.TryGetProperty("LATITUDE", out var lat) ? lat.GetDouble() / 600000.0 : null,
                            Longitude = vessel.TryGetProperty("LONGITUDE", out var lon) ? lon.GetDouble() / 600000.0 : null,
                            SpeedKnots = vessel.TryGetProperty("SPEED", out var spd) ? spd.GetDouble() / 10.0 : null,
                            Heading = vessel.TryGetProperty("HEADING", out var hdg) ? hdg.GetDouble() : null,
                            Destination = vessel.TryGetProperty("DESTINATION", out var dest) ? dest.GetString() : null,
                            VesselType = ClassifyVessel(
                                vessel.TryGetProperty("TYPE", out var vtype) ? vtype.GetInt32() : 0),
                            Timestamp = DateTime.UtcNow
                        };

                        if (ship.Latitude != null && ship.Longitude != null)
                            ships.Add(ship);
                    }
                    catch
                    {
                        // Skip malformed records
                    }
                }
            }

            // Update trails
            var trails = _cache.Get<Dictionary<string, List<ShipTrailPoint>>>(TrailsCacheKey)
                         ?? new Dictionary<string, List<ShipTrailPoint>>();

            foreach (var ship in ships)
            {
                if (ship.Latitude == null || ship.Longitude == null) continue;

                if (!trails.ContainsKey(ship.Mmsi))
                    trails[ship.Mmsi] = new List<ShipTrailPoint>();

                trails[ship.Mmsi].Add(new ShipTrailPoint
                {
                    Latitude = ship.Latitude.Value,
                    Longitude = ship.Longitude.Value,
                    Timestamp = ship.Timestamp
                });

                if (trails[ship.Mmsi].Count > 5)
                    trails[ship.Mmsi] = trails[ship.Mmsi].TakeLast(5).ToList();

                ship.Trail = trails[ship.Mmsi];
            }

            _cache.Set(ShipsCacheKey, ships, TimeSpan.FromMinutes(5));
            _cache.Set(TrailsCacheKey, trails, TimeSpan.FromMinutes(30));

            _logger.LogInformation("Ship data refreshed: {Count} vessels", ships.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh ship data from AISHub");
        }
    }

    public List<ShipPosition> GetShips(VesselType? typeFilter = null)
    {
        var ships = _cache.Get<List<ShipPosition>>(ShipsCacheKey) ?? new List<ShipPosition>();
        if (typeFilter != null)
            return ships.Where(s => s.VesselType == typeFilter.Value).ToList();
        return ships;
    }

    public ShipPosition? GetShip(string mmsi)
    {
        var ships = _cache.Get<List<ShipPosition>>(ShipsCacheKey);
        return ships?.FirstOrDefault(s => s.Mmsi == mmsi);
    }

    private static VesselType ClassifyVessel(int aisType)
    {
        return aisType switch
        {
            >= 70 and <= 79 => VesselType.Cargo,
            >= 80 and <= 89 => VesselType.Tanker,
            >= 60 and <= 69 => VesselType.Passenger,
            30 => VesselType.Fishing,
            36 or 37 => VesselType.Pleasure,
            35 or 38 or 39 => VesselType.MilitaryGovernment,
            >= 40 and <= 49 => VesselType.HighSpeedCraft,
            >= 50 and <= 57 => VesselType.Tug,
            _ => VesselType.Unknown
        };
    }
}
