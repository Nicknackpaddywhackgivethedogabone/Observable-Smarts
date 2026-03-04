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
    public string? OriginCountry { get; set; }
    public double? VerticalRate { get; set; }
    public string? Squawk { get; set; }
    public int? EmitterCategory { get; set; }
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

public class AircraftMetadata
{
    public string Icao24 { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Operator { get; set; }
    public string? Owner { get; set; }
    public string? Registration { get; set; }
    public string? TypeCode { get; set; }
}

public enum FlightCategory
{
    Unknown,
    Commercial,
    Cargo,
    Military,
    GeneralAviation
}
