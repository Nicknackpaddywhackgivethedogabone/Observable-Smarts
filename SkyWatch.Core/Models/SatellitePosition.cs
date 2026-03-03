namespace SkyWatch.Core.Models;

public class SatellitePosition
{
    public int NoradId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeKm { get; set; }
    public double VelocityKmS { get; set; }
    public SatelliteCategory Category { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SatelliteTrackPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeKm { get; set; }
    public DateTime Timestamp { get; set; }
}
