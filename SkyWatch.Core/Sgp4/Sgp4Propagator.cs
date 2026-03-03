using SkyWatch.Core.Models;

namespace SkyWatch.Core.Sgp4;

/// <summary>
/// Simplified SGP4 propagator for computing satellite positions from TLE data.
/// This is a simplified implementation suitable for visualization purposes.
/// </summary>
public class Sgp4Propagator
{
    private const double Deg2Rad = Math.PI / 180.0;
    private const double Rad2Deg = 180.0 / Math.PI;
    private const double TwoPi = 2.0 * Math.PI;
    private const double MinutesPerDay = 1440.0;
    private const double EarthRadiusKm = 6378.137;
    private const double Mu = 398600.4418; // km^3/s^2
    private const double J2 = 1.08263e-3;
    private const double Ke = 7.43669161e-2; // (earthRadius^3 / mu)^0.5 * 60 (in 1/min)
    private const double Xke = 0.0743669161;

    // Parsed orbital elements
    private readonly double _epoch;
    private readonly double _inclination;
    private readonly double _raan;
    private readonly double _eccentricity;
    private readonly double _argPerigee;
    private readonly double _meanAnomaly;
    private readonly double _meanMotion; // rev/day
    private readonly double _bstar;
    private readonly int _noradId;
    private readonly string _name;
    private readonly SatelliteCategory _category;

    // Derived values
    private readonly double _n0; // mean motion in rad/min
    private readonly double _a0; // semi-major axis
    private readonly double _perigee;
    private readonly bool _isDeepSpace;

    public Sgp4Propagator(TleRecord tle)
    {
        _noradId = tle.NoradId;
        _name = tle.Name;
        _category = tle.Category;

        ParseTle(tle.Line1, tle.Line2,
            out _epoch, out _inclination, out _raan, out _eccentricity,
            out _argPerigee, out _meanAnomaly, out _meanMotion, out _bstar);

        _n0 = _meanMotion * TwoPi / MinutesPerDay;
        double a1 = Math.Pow(Xke / _n0, 2.0 / 3.0);
        double cosI = Math.Cos(_inclination);
        double d1 = 0.75 * J2 * (3.0 * cosI * cosI - 1.0) /
                     Math.Pow(1.0 - _eccentricity * _eccentricity, 1.5);
        double del1 = d1 / (a1 * a1);
        double a0 = a1 * (1.0 - del1 / 3.0 - del1 * del1 - 134.0 / 81.0 * del1 * del1 * del1);
        _a0 = a0 * EarthRadiusKm;
        _perigee = (_a0 * (1.0 - _eccentricity)) - EarthRadiusKm;
        _isDeepSpace = _meanMotion < 6.4; // ~225 min period
    }

    private static void ParseTle(string line1, string line2,
        out double epoch, out double inclination, out double raan,
        out double eccentricity, out double argPerigee, out double meanAnomaly,
        out double meanMotion, out double bstar)
    {
        // Line 1: epoch
        var epochYear = int.Parse(line1.Substring(18, 2).Trim());
        var epochDay = double.Parse(line1.Substring(20, 12).Trim(),
            System.Globalization.CultureInfo.InvariantCulture);

        int fullYear = epochYear < 57 ? 2000 + epochYear : 1900 + epochYear;
        var jan1 = new DateTime(fullYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var epochDateTime = jan1.AddDays(epochDay - 1.0);
        epoch = (epochDateTime - new DateTime(1949, 12, 31, 0, 0, 0, DateTimeKind.Utc)).TotalDays;

        // BSTAR drag
        bstar = ParseBstarField(line1.Substring(53, 8).Trim());

        // Line 2: orbital elements
        inclination = double.Parse(line2.Substring(8, 8).Trim(),
            System.Globalization.CultureInfo.InvariantCulture) * Deg2Rad;
        raan = double.Parse(line2.Substring(17, 8).Trim(),
            System.Globalization.CultureInfo.InvariantCulture) * Deg2Rad;

        var eccStr = "0." + line2.Substring(26, 7).Trim();
        eccentricity = double.Parse(eccStr, System.Globalization.CultureInfo.InvariantCulture);

        argPerigee = double.Parse(line2.Substring(34, 8).Trim(),
            System.Globalization.CultureInfo.InvariantCulture) * Deg2Rad;
        meanAnomaly = double.Parse(line2.Substring(43, 8).Trim(),
            System.Globalization.CultureInfo.InvariantCulture) * Deg2Rad;
        meanMotion = double.Parse(line2.Substring(52, 11).Trim(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static double ParseBstarField(string field)
    {
        if (string.IsNullOrWhiteSpace(field)) return 0;

        // Format: ±NNNNN±E  (implied decimal point)
        field = field.Trim();
        if (field.Length < 2) return 0;

        try
        {
            // Handle sign
            double sign = 1.0;
            if (field[0] == '-') { sign = -1.0; field = field[1..]; }
            else if (field[0] == '+') { field = field[1..]; }

            // Find the exponent part
            int expIdx = field.LastIndexOfAny(new[] { '+', '-' });
            if (expIdx <= 0)
            {
                return sign * double.Parse("0." + field, System.Globalization.CultureInfo.InvariantCulture);
            }

            var mantissa = "0." + field[..expIdx];
            var exponent = field[expIdx..];
            return sign * double.Parse(mantissa, System.Globalization.CultureInfo.InvariantCulture) *
                   Math.Pow(10.0, double.Parse(exponent, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Compute satellite position at the given UTC time.
    /// Returns (latitude, longitude, altitude_km, velocity_km_s).
    /// </summary>
    public SatellitePosition GetPosition(DateTime utcTime)
    {
        var epochDateTime = new DateTime(1949, 12, 31, 0, 0, 0, DateTimeKind.Utc).AddDays(_epoch);
        double tsince = (utcTime - epochDateTime).TotalMinutes;

        // Secular perturbations
        double cosI = Math.Cos(_inclination);
        double sinI = Math.Sin(_inclination);
        double e2 = _eccentricity * _eccentricity;
        double beta2 = 1.0 - e2;
        double beta = Math.Sqrt(beta2);

        double a1 = Math.Pow(Xke / _n0, 2.0 / 3.0);
        double theta2 = cosI * cosI;
        double d1 = 0.75 * J2 * (3.0 * theta2 - 1.0) / (beta2 * beta);
        double del1 = d1 / (a1 * a1);
        double a0dp = a1 * (1.0 - del1 / 3.0 - del1 * del1 - 134.0 / 81.0 * del1 * del1 * del1);
        double n0dp = _n0 / (1.0 + del1);

        // Secular rates
        double xi = 1.0 / (a0dp * beta2);
        double xi2 = xi * xi;
        double c1 = 1.5 * J2 * xi2 * n0dp;

        double raanDot = -c1 * cosI;
        double argPDot = c1 * (2.5 * theta2 - 0.5);
        double mDot = n0dp;

        // Propagated elements
        double raan = _raan + raanDot * tsince;
        double argP = _argPerigee + argPDot * tsince;
        double meanAnom = _meanAnomaly + mDot * tsince;

        // Drag effects (simplified)
        double ecc = _eccentricity - _bstar * c1 * tsince;
        if (ecc < 1e-6) ecc = 1e-6;
        if (ecc > 0.999) ecc = 0.999;

        // Solve Kepler's equation
        meanAnom = NormalizeAngle(meanAnom);
        double ea = SolveKepler(meanAnom, ecc);

        // True anomaly
        double sinEa = Math.Sin(ea);
        double cosEa = Math.Cos(ea);
        double trueAnomaly = Math.Atan2(
            Math.Sqrt(1.0 - ecc * ecc) * sinEa,
            cosEa - ecc);

        // Distance from center of earth (in earth radii)
        double r = a0dp * (1.0 - ecc * cosEa);

        // Position in orbital plane
        double u = trueAnomaly + argP;
        double sinU = Math.Sin(u);
        double cosU = Math.Cos(u);
        double sinRaan = Math.Sin(raan);
        double cosRaan = Math.Cos(raan);

        // ECI coordinates (in earth radii)
        double x = r * (cosU * cosRaan - sinU * sinRaan * cosI);
        double y = r * (cosU * sinRaan + sinU * cosRaan * cosI);
        double z = r * sinU * sinI;

        // Velocity in ECI (simplified)
        double vFactor = Math.Sqrt(Mu / (r * EarthRadiusKm));
        double vx = vFactor * (-sinU * cosRaan - cosU * sinRaan * cosI);
        double vy = vFactor * (-sinU * sinRaan + cosU * cosRaan * cosI);
        double vz = vFactor * cosU * sinI;
        double velocity = Math.Sqrt(vx * vx + vy * vy + vz * vz);

        // Convert ECI to geodetic
        double rKm = r * EarthRadiusKm;
        EciToGeodetic(x * EarthRadiusKm, y * EarthRadiusKm, z * EarthRadiusKm, utcTime,
            out double lat, out double lon, out double alt);

        return new SatellitePosition
        {
            NoradId = _noradId,
            Name = _name,
            Latitude = lat,
            Longitude = lon,
            AltitudeKm = alt,
            VelocityKmS = velocity,
            Category = _category,
            Timestamp = utcTime
        };
    }

    public List<SatelliteTrackPoint> GetTrack(DateTime startUtc, int durationMinutes, int stepSeconds = 60)
    {
        var points = new List<SatelliteTrackPoint>();
        var current = startUtc;
        var end = startUtc.AddMinutes(durationMinutes);

        while (current <= end)
        {
            var pos = GetPosition(current);
            points.Add(new SatelliteTrackPoint
            {
                Latitude = pos.Latitude,
                Longitude = pos.Longitude,
                AltitudeKm = pos.AltitudeKm,
                Timestamp = current
            });
            current = current.AddSeconds(stepSeconds);
        }

        return points;
    }

    private static double SolveKepler(double meanAnomaly, double ecc)
    {
        double ea = meanAnomaly;
        for (int i = 0; i < 50; i++)
        {
            double delta = ea - ecc * Math.Sin(ea) - meanAnomaly;
            if (Math.Abs(delta) < 1e-12) break;
            ea -= delta / (1.0 - ecc * Math.Cos(ea));
        }
        return ea;
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= TwoPi;
        if (angle < 0) angle += TwoPi;
        return angle;
    }

    private static void EciToGeodetic(double xKm, double yKm, double zKm, DateTime utc,
        out double latDeg, out double lonDeg, out double altKm)
    {
        // GMST calculation
        double jd = ToJulianDate(utc);
        double t = (jd - 2451545.0) / 36525.0;
        double gmst = 280.46061837 + 360.98564736629 * (jd - 2451545.0) +
                       t * t * (0.000387933 - t / 38710000.0);
        gmst = ((gmst % 360.0) + 360.0) % 360.0;

        double lon = Math.Atan2(yKm, xKm) * Rad2Deg - gmst;
        lon = ((lon % 360.0) + 540.0) % 360.0 - 180.0;

        double rXY = Math.Sqrt(xKm * xKm + yKm * yKm);
        double lat = Math.Atan2(zKm, rXY) * Rad2Deg;

        // Iterative for geodetic latitude (WGS84)
        double f = 1.0 / 298.257223563;
        double e2 = 2.0 * f - f * f;
        for (int i = 0; i < 5; i++)
        {
            double sinLat = Math.Sin(lat * Deg2Rad);
            double N = EarthRadiusKm / Math.Sqrt(1.0 - e2 * sinLat * sinLat);
            lat = Math.Atan2(zKm + N * e2 * sinLat, rXY) * Rad2Deg;
        }

        double sinLatFinal = Math.Sin(lat * Deg2Rad);
        double Nfinal = EarthRadiusKm / Math.Sqrt(1.0 - e2 * sinLatFinal * sinLatFinal);
        altKm = rXY / Math.Cos(lat * Deg2Rad) - Nfinal;

        latDeg = lat;
        lonDeg = lon;
    }

    private static double ToJulianDate(DateTime utc)
    {
        int y = utc.Year;
        int m = utc.Month;
        double d = utc.Day + utc.Hour / 24.0 + utc.Minute / 1440.0 + utc.Second / 86400.0;

        if (m <= 2)
        {
            y--;
            m += 12;
        }

        int A = y / 100;
        int B = 2 - A + A / 4;

        return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + B - 1524.5;
    }
}
