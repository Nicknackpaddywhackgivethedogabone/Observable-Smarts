using SkyWatch.Core.Models;

namespace SkyWatch.Api.Services;

public class RegionOfInterestService
{
    private readonly TleService _tleService;
    private readonly ImagingFootprintService _imagingService;

    public RegionOfInterestService(TleService tleService, ImagingFootprintService imagingService)
    {
        _tleService = tleService;
        _imagingService = imagingService;
    }

    /// <summary>
    /// Given a GeoJSON polygon region and a time window, predict which imaging satellites
    /// will pass over the region.
    /// </summary>
    public List<SatellitePassPrediction> PredictPasses(GeoJsonPolygon region, int hoursAhead = 24)
    {
        var predictions = new List<SatellitePassPrediction>();
        var imagingSats = ImagingFootprintService.GetKnownImagingSatellites();
        var now = DateTime.UtcNow;
        var end = now.AddHours(hoursAhead);

        // Get the bounding box of the region for quick filtering
        var (minLat, maxLat, minLon, maxLon) = GetBoundingBox(region);

        foreach (var sat in imagingSats)
        {
            var propagator = _tleService.GetPropagator(sat.NoradId);
            if (propagator == null) continue;

            // Sample position every 30 seconds over the time window
            var current = now;
            var stepSeconds = 30;
            var lastPassTime = DateTime.MinValue;

            while (current <= end)
            {
                try
                {
                    var pos = propagator.GetPosition(current);

                    // Check if sub-satellite point + swath covers the region
                    double halfSwathDeg = (sat.SwathWidthKm / 2.0) / 6378.137 * (180.0 / Math.PI);

                    if (pos.Latitude >= minLat - halfSwathDeg && pos.Latitude <= maxLat + halfSwathDeg &&
                        pos.Longitude >= minLon - halfSwathDeg && pos.Longitude <= maxLon + halfSwathDeg)
                    {
                        // Avoid duplicate passes (must be at least 10 minutes apart)
                        if ((current - lastPassTime).TotalMinutes > 10)
                        {
                            predictions.Add(new SatellitePassPrediction
                            {
                                NoradId = sat.NoradId,
                                Name = sat.Name,
                                Sensor = sat.Sensor,
                                PassTime = current,
                                SwathWidthKm = sat.SwathWidthKm,
                                MaxElevationDeg = 90 - Math.Abs(pos.Latitude - (minLat + maxLat) / 2.0)
                            });
                            lastPassTime = current;
                        }
                    }
                }
                catch
                {
                    // Skip propagation errors
                }

                current = current.AddSeconds(stepSeconds);
            }
        }

        return predictions.OrderBy(p => p.PassTime).ToList();
    }

    private static (double minLat, double maxLat, double minLon, double maxLon) GetBoundingBox(GeoJsonPolygon polygon)
    {
        double minLat = 90, maxLat = -90, minLon = 180, maxLon = -180;

        if (polygon.Coordinates.Length > 0)
        {
            foreach (var point in polygon.Coordinates[0])
            {
                if (point.Length >= 2)
                {
                    var lon = point[0];
                    var lat = point[1];
                    if (lat < minLat) minLat = lat;
                    if (lat > maxLat) maxLat = lat;
                    if (lon < minLon) minLon = lon;
                    if (lon > maxLon) maxLon = lon;
                }
            }
        }

        return (minLat, maxLat, minLon, maxLon);
    }
}
