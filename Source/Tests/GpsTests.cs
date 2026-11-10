using System;
using System.Globalization;
using System.Threading;
using AlpineQuestPort;

static class GpsTests
{
    static int count;
    static void Check(bool value, string reason) { count++; if (!value) throw new Exception(reason); }
    static string Nmea(string body) { int sum = 0; foreach (char ch in body) sum ^= ch; return "$" + body + "*" + sum.ToString("X2"); }
    static int Main()
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        DateTime? date = null; GeoPoint point;
        Check(NmeaParser.TryParse("$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A", ref date, out point), "Known RMC checksum and fix");
        Check(Math.Abs(point.Lat - 48.1173) < 1e-7 && Math.Abs(point.Lon - 11.51666666667) < 1e-7, "Degrees and minutes conversion");
        Check(point.Time.Value.Year == 1994 && point.Time.Value.Hour == 12, "Date and UTC time");
        Check(!NmeaParser.TryParse("$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6B", ref date, out point), "Invalid checksum rejected");
        Check(!NmeaParser.TryParse(Nmea("GPRMC,123519,V,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W"), ref date, out point), "Invalid position rejected");
        Check(!NmeaParser.TryParse(Nmea("GPRMC,123519,A,4867.038,N,01131.000,E,022.4,084.4,230394,003.1,W"), ref date, out point), "Invalid minutes rejected");
        Check(!NmeaParser.TryParse(Nmea("GPRMC,123519,A,4807.038,Q,01131.000,E,022.4,084.4,230394,003.1,W"), ref date, out point), "Invalid hemisphere rejected");
        Check(!NmeaParser.TryParse(Nmea("GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,310294,003.1,W"), ref date, out point), "Impossible date rejected");
        Check(NmeaParser.TryParse(Nmea("GNRMC,235959.500,A,4807.038,S,01131.000,W,0,0,121026,,,A"), ref date, out point), "GNSS talker and southern/western position");
        Check(point.Lat < 0 && point.Lon < 0 && point.Time.Value.Year == 2026, "Signed coordinate and modern year");
        Check(NmeaParser.TryParse(Nmea("GNGGA,000001.000,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,"), ref date, out point), "GGA fix accepted");
        Check(point.Elevation == 545.4 && point.Time.Value.Day == 13 && point.Time.Value.Month == 10, "Altitude and UTC midnight rollover");
        Check(!NmeaParser.TryParse(Nmea("GNGGA,000001.000,4807.038,N,01131.000,E,0,08,0.9,545.4,M,46.9,M,,"), ref date, out point), "GGA no-fix rejected");
        Check(!NmeaParser.TryParse(Nmea("GNGGA,000001.000,4807.038,N,01131.000,E,1,08,0.9,NaN,M,46.9,M,,"), ref date, out point), "NaN altitude rejected");
        Check(!NmeaParser.TryParse(Nmea("GNGGA,246001.000,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,"), ref date, out point), "Invalid clock rejected");
        Check(!NmeaParser.TryParse("$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W", ref date, out point), "Missing checksum rejected");
        using (GpsService service = new GpsService()) { Check(service.Status.Contains("выключена"), "Does not start providers automatically"); service.Stop(); }
        Console.WriteLine("PASS: " + count + " GPS/parser checks; hardware providers not activated");
        return 0;
    }
}
