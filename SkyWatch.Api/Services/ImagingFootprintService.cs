using SkyWatch.Core.Models;

namespace SkyWatch.Api.Services;

public class ImagingFootprintService
{
    private readonly TleService _tleService;

    // Known imaging satellites with NORAD IDs and swath widths
    private static readonly List<ImagingSatelliteConfig> KnownImagingSats = new()
    {
        new() { Name = "SENTINEL-2A", NoradId = 40697, SwathWidthKm = 290, Sensor = "MSI" },
        new() { Name = "SENTINEL-2B", NoradId = 42063, SwathWidthKm = 290, Sensor = "MSI" },
        new() { Name = "SENTINEL-1A", NoradId = 39634, SwathWidthKm = 250, Sensor = "SAR-C" },
        new() { Name = "SENTINEL-1B", NoradId = 41456, SwathWidthKm = 250, Sensor = "SAR-C" },
        new() { Name = "LANDSAT 8", NoradId = 39084, SwathWidthKm = 185, Sensor = "OLI/TIRS" },
        new() { Name = "LANDSAT 9", NoradId = 49260, SwathWidthKm = 185, Sensor = "OLI-2/TIRS-2" },
        new() { Name = "TERRA", NoradId = 25994, SwathWidthKm = 2330, Sensor = "MODIS" },
        new() { Name = "AQUA", NoradId = 27424, SwathWidthKm = 2330, Sensor = "MODIS" },
    };

    public ImagingFootprintService(TleService tleService)
    {
        _tleService = tleService;
    }

    public List<ImagingSatellitePosition> GetImagingSatellites()
    {
        var results = new List<ImagingSatellitePosition>();

        foreach (var config in KnownImagingSats)
        {
            var propagator = _tleService.GetPropagator(config.NoradId);
            if (propagator == null) continue;

            try
            {
                var pos = propagator.GetPosition(DateTime.UtcNow);

                var swath = ComputeSwathPolygon(pos.Latitude, pos.Longitude, pos.AltitudeKm, config.SwathWidthKm);

                results.Add(new ImagingSatellitePosition
                {
                    NoradId = config.NoradId,
                    Name = config.Name,
                    Sensor = config.Sensor,
                    Latitude = pos.Latitude,
                    Longitude = pos.Longitude,
                    AltitudeKm = pos.AltitudeKm,
                    SwathWidthKm = config.SwathWidthKm,
                    SwathFootprint = swath
                });
            }
            catch
            {
                // Skip if propagation fails
            }
        }

        return results;
    }

    public static List<ImagingSatelliteConfig> GetKnownImagingSatellites() => KnownImagingSats;

    /// <summary>
    /// Compute a rectangular swath polygon centered on the sub-satellite point.
    /// The swath extends swathWidthKm/2 to each side, and a length proportional
    /// to the swath width ahead/behind along the ground track.
    /// </summary>
    private static GeoJsonPolygon ComputeSwathPolygon(double latDeg, double lonDeg, double altKm, double swathWidthKm)
    {
        const double EarthRadiusKm = 6378.137;
        double halfSwathDeg = (swathWidthKm / 2.0) / EarthRadiusKm * (180.0 / Math.PI);

        // Create a simple rectangular footprint approximation
        // Length is roughly equal to swath width for a snapshot
        double halfLengthDeg = halfSwathDeg;

        double latRad = latDeg * Math.PI / 180.0;
        double cosLat = Math.Cos(latRad);
        if (cosLat < 0.01) cosLat = 0.01; // Avoid division by zero at poles

        double dLon = halfSwathDeg / cosLat;
        double dLat = halfLengthDeg;

        // Create polygon corners (closed ring, GeoJSON format: [lon, lat])
        var coordinates = new double[][][]
        {
            new double[][]
            {
                new[] { NormalizeLon(lonDeg - dLon), ClampLat(latDeg - dLat) },
                new[] { NormalizeLon(lonDeg + dLon), ClampLat(latDeg - dLat) },
                new[] { NormalizeLon(lonDeg + dLon), ClampLat(latDeg + dLat) },
                new[] { NormalizeLon(lonDeg - dLon), ClampLat(latDeg + dLat) },
                new[] { NormalizeLon(lonDeg - dLon), ClampLat(latDeg - dLat) }, // close the ring
            }
        };

        return new GeoJsonPolygon
        {
            Type = "Polygon",
            Coordinates = coordinates
        };
    }

    private static double NormalizeLon(double lon)
    {
        while (lon > 180) lon -= 360;
        while (lon < -180) lon += 360;
        return Math.Round(lon, 4);
    }

    private static double ClampLat(double lat) => Math.Round(Math.Clamp(lat, -90, 90), 4);
}
