namespace SkyWatch.Core.Models;

public class ImagingSatelliteConfig
{
    public string Name { get; set; } = string.Empty;
    public int NoradId { get; set; }
    public double SwathWidthKm { get; set; }
    public string Sensor { get; set; } = string.Empty;
}

public class ImagingSatellitePosition
{
    public int NoradId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sensor { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeKm { get; set; }
    public double SwathWidthKm { get; set; }
    public GeoJsonPolygon? SwathFootprint { get; set; }
}

public class GeoJsonPolygon
{
    public string Type { get; set; } = "Polygon";
    public double[][][] Coordinates { get; set; } = Array.Empty<double[][]>();
}
