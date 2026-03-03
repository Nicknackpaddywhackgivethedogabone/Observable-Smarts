namespace SkyWatch.Core.Models;

public class ShipPosition
{
    public string Mmsi { get; set; } = string.Empty;
    public string? Name { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? SpeedKnots { get; set; }
    public double? Heading { get; set; }
    public string? Destination { get; set; }
    public string? Flag { get; set; }
    public VesselType VesselType { get; set; } = VesselType.Unknown;
    public DateTime Timestamp { get; set; }
    public List<ShipTrailPoint> Trail { get; set; } = new();
}

public class ShipTrailPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum VesselType
{
    Unknown,
    Cargo,
    Tanker,
    Passenger,
    Fishing,
    MilitaryGovernment,
    Pleasure,
    Tug,
    HighSpeedCraft
}
