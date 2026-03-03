using SkyWatch.Core.Models;

namespace SkyWatch.Core.TleParsing;

/// <summary>
/// Parses two-line element (TLE) sets from standard Celestrak 3-line format.
/// </summary>
public static class TleParser
{
    public static List<TleRecord> Parse(string tleText, SatelliteCategory defaultCategory = SatelliteCategory.Unknown)
    {
        var records = new List<TleRecord>();
        var lines = tleText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.TrimEnd('\r'))
                           .ToArray();

        int i = 0;
        while (i < lines.Length)
        {
            // Skip blank lines
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                i++;
                continue;
            }

            // Determine if this is a 3-line or 2-line format
            if (i + 2 < lines.Length &&
                lines[i + 1].Length >= 1 && lines[i + 1][0] == '1' &&
                lines[i + 2].Length >= 1 && lines[i + 2][0] == '2')
            {
                // 3-line format: name, line1, line2
                var name = lines[i].Trim();
                var line1 = lines[i + 1].Trim();
                var line2 = lines[i + 2].Trim();

                var noradId = ParseNoradId(line1);
                var category = CategorizeFromName(name, defaultCategory);

                records.Add(new TleRecord
                {
                    Name = name,
                    Line1 = line1,
                    Line2 = line2,
                    NoradId = noradId,
                    Category = category
                });

                i += 3;
            }
            else if (lines[i].Length >= 1 && lines[i][0] == '1' &&
                     i + 1 < lines.Length && lines[i + 1].Length >= 1 && lines[i + 1][0] == '2')
            {
                // 2-line format: line1, line2 (no name)
                var line1 = lines[i].Trim();
                var line2 = lines[i + 1].Trim();
                var noradId = ParseNoradId(line1);

                records.Add(new TleRecord
                {
                    Name = $"NORAD {noradId}",
                    Line1 = line1,
                    Line2 = line2,
                    NoradId = noradId,
                    Category = defaultCategory
                });

                i += 2;
            }
            else
            {
                i++;
            }
        }

        return records;
    }

    private static int ParseNoradId(string line1)
    {
        if (line1.Length < 7) return 0;
        var idStr = line1.Substring(2, 5).Trim();
        return int.TryParse(idStr, out var id) ? id : 0;
    }

    private static SatelliteCategory CategorizeFromName(string name, SatelliteCategory defaultCategory)
    {
        var upper = name.ToUpperInvariant();

        if (upper.Contains("STARLINK")) return SatelliteCategory.Starlink;
        if (upper.Contains("ISS") || upper.Contains("ZARYA")) return SatelliteCategory.ISS;
        if (upper.Contains("GPS") || upper.Contains("NAVSTAR")) return SatelliteCategory.GPS;
        if (upper.Contains("DEB") || upper.Contains("DEBRIS") || upper.Contains("R/B")) return SatelliteCategory.Debris;
        if (upper.Contains("SENTINEL") || upper.Contains("LANDSAT") || upper.Contains("MODIS") ||
            upper.Contains("TERRA") || upper.Contains("AQUA") || upper.Contains("WORLDVIEW") ||
            upper.Contains("PLEIADES") || upper.Contains("SPOT")) return SatelliteCategory.EarthObservation;
        if (upper.Contains("NOAA") || upper.Contains("GOES") || upper.Contains("METEOSAT") ||
            upper.Contains("HIMAWARI")) return SatelliteCategory.Weather;
        if (upper.Contains("INTELSAT") || upper.Contains("SES") || upper.Contains("ASTRA") ||
            upper.Contains("IRIDIUM") || upper.Contains("GLOBALSTAR")) return SatelliteCategory.Communications;

        return defaultCategory;
    }
}
