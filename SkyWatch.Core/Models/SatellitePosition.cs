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

    // Orbital parameters
    public string? IntlDesignator { get; set; }
    public double? InclinationDeg { get; set; }
    public double? PeriodMinutes { get; set; }
    public double? ApogeeKm { get; set; }
    public double? PerigeeKm { get; set; }
    public double? EccentricityValue { get; set; }
    public string? EpochAge { get; set; }
}

public class SatelliteTrackPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeKm { get; set; }
    public DateTime Timestamp { get; set; }
}
