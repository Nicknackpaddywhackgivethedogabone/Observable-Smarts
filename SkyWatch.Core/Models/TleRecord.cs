namespace SkyWatch.Core.Models;

public class TleRecord
{
    public string Name { get; set; } = string.Empty;
    public string Line1 { get; set; } = string.Empty;
    public string Line2 { get; set; } = string.Empty;
    public int NoradId { get; set; }
    public SatelliteCategory Category { get; set; } = SatelliteCategory.Unknown;
}

public enum SatelliteCategory
{
    Unknown,
    EarthObservation,
    ISS,
    Starlink,
    GPS,
    Debris,
    Weather,
    Communications,
    Military
}
