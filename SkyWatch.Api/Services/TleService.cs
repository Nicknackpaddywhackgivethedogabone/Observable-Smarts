using Microsoft.Extensions.Caching.Memory;
using SkyWatch.Core.Models;
using SkyWatch.Core.Sgp4;
using SkyWatch.Core.TleParsing;

namespace SkyWatch.Api.Services;

public class TleService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TleService> _logger;
    private readonly ApiStatusService _apiStatus;

    private const string TleCacheKey = "tle_records";
    private const string PropagatorsCacheKey = "sgp4_propagators";
    private const string SourceName = "Celestrak";

    // Celestrak TLE feed URLs
    private static readonly Dictionary<string, (string Url, SatelliteCategory Category)> TleFeeds = new()
    {
        ["stations"] = ("https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=tle", SatelliteCategory.ISS),
        ["active"] = ("https://celestrak.org/NORAD/elements/gp.php?GROUP=active&FORMAT=tle", SatelliteCategory.Unknown),
        ["visual"] = ("https://celestrak.org/NORAD/elements/gp.php?GROUP=visual&FORMAT=tle", SatelliteCategory.Unknown),
        ["weather"] = ("https://celestrak.org/NORAD/elements/gp.php?GROUP=weather&FORMAT=tle", SatelliteCategory.Weather),
        ["resource"] = ("https://celestrak.org/NORAD/elements/gp.php?GROUP=resource&FORMAT=tle", SatelliteCategory.EarthObservation),
        ["starlink"] = ("https://celestrak.org/NORAD/elements/gp.php?GROUP=starlink&FORMAT=tle", SatelliteCategory.Starlink),
        ["gps"] = ("https://celestrak.org/NORAD/elements/gp.php?GROUP=gps-ops&FORMAT=tle", SatelliteCategory.GPS),
    };

    public TleService(IHttpClientFactory httpClientFactory, IMemoryCache cache, ILogger<TleService> logger,
        ApiStatusService apiStatus)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _apiStatus = apiStatus;
    }

    public async Task RefreshTleDataAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing TLE data from Celestrak...");
        var allRecords = new Dictionary<int, TleRecord>();
        var client = _httpClientFactory.CreateClient("Celestrak");

        foreach (var (name, (url, defaultCategory)) in TleFeeds)
        {
            try
            {
                var response = await client.GetStringAsync(url, ct);
                var records = TleParser.Parse(response, defaultCategory);
                foreach (var record in records)
                {
                    allRecords[record.NoradId] = record;
                }
                _logger.LogInformation("Loaded {Count} TLEs from {Feed}", records.Count, name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load TLE feed: {Feed}", name);
            }
        }

        var tleList = allRecords.Values.ToList();
        _cache.Set(TleCacheKey, tleList, TimeSpan.FromHours(5));

        // Pre-build propagators
        var propagators = new Dictionary<int, Sgp4Propagator>();
        foreach (var tle in tleList)
        {
            try
            {
                propagators[tle.NoradId] = new Sgp4Propagator(tle);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to create propagator for {Name} ({NoradId})", tle.Name, tle.NoradId);
            }
        }
        _cache.Set(PropagatorsCacheKey, propagators, TimeSpan.FromHours(5));

        _logger.LogInformation("TLE refresh complete. {Count} satellites loaded, {PropCount} propagators built.",
            tleList.Count, propagators.Count);
        _apiStatus.ReportSuccess(SourceName, tleList.Count);
    }

    public List<SatellitePosition> GetCurrentPositions(SatelliteCategory? categoryFilter = null)
    {
        if (!_cache.TryGetValue(PropagatorsCacheKey, out Dictionary<int, Sgp4Propagator>? propagators) ||
            propagators == null)
        {
            return new List<SatellitePosition>();
        }

        var now = DateTime.UtcNow;
        var positions = new List<SatellitePosition>();

        foreach (var (_, propagator) in propagators)
        {
            try
            {
                var pos = propagator.GetPosition(now);
                if (categoryFilter == null || pos.Category == categoryFilter.Value)
                {
                    // Sanity check on position
                    if (pos.Latitude >= -90 && pos.Latitude <= 90 &&
                        pos.Longitude >= -180 && pos.Longitude <= 180 &&
                        pos.AltitudeKm > 0 && pos.AltitudeKm < 100000)
                    {
                        positions.Add(pos);
                    }
                }
            }
            catch
            {
                // Skip satellites that fail to propagate
            }
        }

        return positions;
    }

    public List<SatelliteTrackPoint>? GetTrack(int noradId, int durationMinutes = 90)
    {
        if (!_cache.TryGetValue(PropagatorsCacheKey, out Dictionary<int, Sgp4Propagator>? propagators) ||
            propagators == null || !propagators.TryGetValue(noradId, out var propagator))
        {
            return null;
        }

        try
        {
            return propagator.GetTrack(DateTime.UtcNow, durationMinutes, 30);
        }
        catch
        {
            return null;
        }
    }

    public Sgp4Propagator? GetPropagator(int noradId)
    {
        if (_cache.TryGetValue(PropagatorsCacheKey, out Dictionary<int, Sgp4Propagator>? propagators) &&
            propagators != null && propagators.TryGetValue(noradId, out var propagator))
        {
            return propagator;
        }
        return null;
    }

    public List<TleRecord> GetTleRecords()
    {
        if (_cache.TryGetValue(TleCacheKey, out List<TleRecord>? records) && records != null)
            return records;
        return new List<TleRecord>();
    }
}
