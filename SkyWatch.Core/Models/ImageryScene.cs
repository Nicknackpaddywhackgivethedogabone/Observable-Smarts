namespace SkyWatch.Core.Models;

public class ImageryScene
{
    public string Id { get; set; } = string.Empty;
    public string Sensor { get; set; } = string.Empty;
    public DateTime AcquisitionDate { get; set; }
    public double? CloudCoverPercent { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? FullImageUrl { get; set; }
    public GeoJsonPolygon? Footprint { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class RegionOfInterestRequest
{
    public GeoJsonPolygon Polygon { get; set; } = new();
    public int HoursAhead { get; set; } = 24;
}

public class SatellitePassPrediction
{
    public int NoradId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sensor { get; set; } = string.Empty;
    public DateTime PassTime { get; set; }
    public double MaxElevationDeg { get; set; }
    public double SwathWidthKm { get; set; }
}
