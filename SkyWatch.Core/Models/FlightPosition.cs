namespace SkyWatch.Core.Models;

public class FlightPosition
{
    public string Icao24 { get; set; } = string.Empty;
    public string? Callsign { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AltitudeM { get; set; }
    public double? VelocityMs { get; set; }
    public double? Heading { get; set; }
    public bool OnGround { get; set; }
    public FlightCategory Category { get; set; } = FlightCategory.Unknown;
    public DateTime Timestamp { get; set; }
    public List<TrailPoint> Trail { get; set; } = new();
}

public class TrailPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeM { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum FlightCategory
{
    Unknown,
    Commercial,
    Cargo,
    Military,
    GeneralAviation
}
