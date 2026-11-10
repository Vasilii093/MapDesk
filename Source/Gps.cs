using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Globalization;
using System.IO.Ports;

namespace AlpineQuestPort
{
    public sealed class GpsFixEventArgs : EventArgs
    {
        public GeoPoint Point { get; private set; }
        public double AccuracyMeters { get; private set; }
        public GpsFixEventArgs(GeoPoint point, double accuracyMeters) { Point = point; AccuracyMeters = accuracyMeters; }
    }

    // Events may be raised on a provider thread; UI consumers must marshal to their UI thread.
    public sealed class GpsService : IDisposable
    {
        private readonly object gate = new object();
        private GeoCoordinateWatcher watcher;
        private SerialPort serial;
        private string pending = String.Empty;
        private string status = "Геопозиция выключена";
        private DateTime? nmeaDate;
        private bool disposed;
        private int generation;
        public event EventHandler<GpsFixEventArgs> Fix;
        public event EventHandler StatusChanged;
        public string Status { get { lock (gate) return status; } }

        public void StartWindows()
        {
            EnsureNotDisposed();
            Stop();
            GeoCoordinateWatcher provider = new GeoCoordinateWatcher(GeoPositionAccuracy.High);
            provider.MovementThreshold = 1;
            provider.PositionChanged += WindowsPosition;
            provider.StatusChanged += WindowsStatus;
            lock (gate) { watcher = provider; generation++; }
            SetStatus("Геопозиция Windows: поиск местоположения");
            try { provider.Start(); }
            catch { Stop(); throw; }
        }

        public void StartSerial(string port, int baud)
        {
            EnsureNotDisposed();
            if (String.IsNullOrWhiteSpace(port)) throw new ArgumentException("Укажите COM-порт.", "port");
            if (baud < 300 || baud > 1000000) throw new ArgumentOutOfRangeException("baud");
            Stop();
            SerialPort device = new SerialPort(port, baud, Parity.None, 8, StopBits.One);
            device.Handshake = Handshake.None;
            device.ReadTimeout = 500;
            device.WriteTimeout = 500;
            device.DtrEnable = false;
            device.RtsEnable = false;
            device.DataReceived += SerialData;
            lock (gate) { serial = device; pending = String.Empty; nmeaDate = null; generation++; }
            SetStatus("GPS NMEA: ожидание координат (" + port + ")");
            try { device.Open(); }
            catch { Stop(); throw; }
        }

        public void Stop()
        {
            GeoCoordinateWatcher oldWatcher;
            SerialPort oldSerial;
            lock (gate)
            {
                oldWatcher = watcher; oldSerial = serial;
                watcher = null; serial = null; pending = String.Empty; nmeaDate = null; generation++;
            }
            if (oldWatcher != null)
            {
                oldWatcher.PositionChanged -= WindowsPosition;
                oldWatcher.StatusChanged -= WindowsStatus;
                oldWatcher.Stop();
                oldWatcher.Dispose();
            }
            if (oldSerial != null)
            {
                oldSerial.DataReceived -= SerialData;
                oldSerial.Dispose();
            }
            SetStatus("Геопозиция выключена");
        }

        private void EnsureNotDisposed()
        {
            if (disposed) throw new ObjectDisposedException("GpsService");
        }

        private void WindowsStatus(object sender, GeoPositionStatusChangedEventArgs args)
        {
            lock (gate) { if (!Object.ReferenceEquals(sender, watcher)) return; }
            switch (args.Status)
            {
                case GeoPositionStatus.Disabled: SetStatus("Геопозиция Windows недоступна: проверьте разрешение и службу местоположения"); break;
                case GeoPositionStatus.Initializing: SetStatus("Геопозиция Windows: определение местоположения"); break;
                case GeoPositionStatus.NoData: SetStatus("Геопозиция Windows: координаты пока недоступны"); break;
                case GeoPositionStatus.Ready: SetStatus("Геопозиция Windows активна (источник может быть Wi-Fi)"); break;
            }
        }

        private void WindowsPosition(object sender, GeoPositionChangedEventArgs<GeoCoordinate> args)
        {
            int token;
            lock (gate) { if (!Object.ReferenceEquals(sender, watcher)) return; token = generation; }
            GeoCoordinate location = args.Position.Location;
            if (location.IsUnknown || !GeoMath.IsValidCoordinate(location.Latitude, location.Longitude)) return;
            GeoPoint point = new GeoPoint(location.Latitude, location.Longitude);
            if (GeoMath.IsFinite(location.Altitude)) point.Elevation = location.Altitude;
            point.Time = args.Position.Timestamp;
            Publish(point, location.HorizontalAccuracy, token);
        }

        private void SerialData(object sender, SerialDataReceivedEventArgs args)
        {
            SerialPort device = sender as SerialPort;
            if (device == null) return;
            int token;
            lock (gate) { if (!Object.ReferenceEquals(device, serial)) return; token = generation; }
            string incoming;
            try { incoming = device.ReadExisting(); }
            catch (Exception ex) { if (ex is System.IO.IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException) { SetStatus("GPS NMEA: соединение с портом потеряно"); return; } throw; }
            List<string> sentences = new List<string>();
            lock (gate)
            {
                if (token != generation) return;
                // A valid NMEA sentence is short. Discard malformed/unbounded data.
                if (pending.Length + incoming.Length > 32768) { pending = String.Empty; return; }
                pending += incoming;
                int end;
                while ((end = pending.IndexOf('\n')) >= 0)
                {
                    string line = pending.Substring(0, end).Trim();
                    pending = pending.Substring(end + 1);
                    if (line.Length <= 1024) sentences.Add(line);
                }
                if (pending.Length > 1024) pending = String.Empty;
            }
            foreach (string sentence in sentences)
            {
                GeoPoint point;
                DateTime? date;
                lock (gate) { if (token != generation) return; date = nmeaDate; }
                if (NmeaParser.TryParse(sentence, ref date, out point))
                {
                    lock (gate) { if (token != generation) return; nmeaDate = date; }
                    SetStatus("GPS NMEA: координаты получены (" + device.PortName + ")");
                    Publish(point, Double.NaN, token);
                }
            }
        }

        private void Publish(GeoPoint point, double accuracy, int token)
        {
            EventHandler<GpsFixEventArgs> handler;
            lock (gate) { if (token != generation || disposed) return; handler = Fix; }
            if (handler != null) handler(this, new GpsFixEventArgs(point, accuracy));
        }

        private void SetStatus(string value)
        {
            EventHandler handler;
            lock (gate) { if (status == value) return; status = value; handler = StatusChanged; }
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (disposed) return;
            Stop(); disposed = true;
        }
    }

    public static class NmeaParser
    {
        // Requires checksum and a valid fix. Supports GNSS talker IDs and RMC/GGA.
        public static bool TryParse(string sentence, ref DateTime? date, out GeoPoint point)
        {
            point = null;
            if (String.IsNullOrEmpty(sentence) || sentence.Length > 1024 || sentence[0] != '$') return false;
            int star = sentence.IndexOf('*');
            if (star < 2 || star + 3 != sentence.Length) return false;
            int expected;
            if (!Int32.TryParse(sentence.Substring(star + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out expected)) return false;
            int actual = 0;
            for (int i = 1; i < star; i++) { if (sentence[i] > 127) return false; actual ^= sentence[i]; }
            if (actual != expected) return false;
            string[] parts = sentence.Substring(1, star - 1).Split(',');
            if (parts[0].Length != 5) return false;
            string kind = parts[0].Substring(2);
            bool rmc = kind == "RMC";
            if (!rmc && kind != "GGA") return false;
            if (parts.Length < (rmc ? 10 : 11)) return false;
            if (rmc)
            {
                if (parts[2] != "A" || (parts.Length > 12 && parts[12] == "N")) return false;
            }
            else
            {
                int quality;
                if (!Int32.TryParse(parts[6], NumberStyles.None, CultureInfo.InvariantCulture, out quality) || quality < 1 || quality > 8) return false;
            }
            double lat, lon;
            int first = rmc ? 3 : 2;
            if (!Coordinate(parts[first], parts[first + 1], true, out lat) || !Coordinate(parts[first + 2], parts[first + 3], false, out lon)) return false;
            DateTime stampDate;
            if (rmc)
            {
                string d = parts[9];
                int day, month, year;
                if (d.Length != 6 || !Digits(d) || !Int32.TryParse(d.Substring(0, 2), out day) || !Int32.TryParse(d.Substring(2, 2), out month) || !Int32.TryParse(d.Substring(4, 2), out year)) return false;
                try { stampDate = new DateTime(year >= 80 ? 1900 + year : 2000 + year, month, day, 0, 0, 0, DateTimeKind.Utc); }
                catch (ArgumentOutOfRangeException) { return false; }
            }
            else stampDate = date.HasValue ? DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc) : DateTime.UtcNow.Date;
            DateTimeOffset timestamp;
            if (!Time(parts[1], stampDate, out timestamp)) return false;
            if (!rmc && date.HasValue)
            {
                // GGA has no date. Continue across UTC midnight after the last known fix.
                double hours = (timestamp.UtcDateTime - date.Value).TotalHours;
                if (hours < -12) timestamp = timestamp.AddDays(1);
                else if (hours > 12) timestamp = timestamp.AddDays(-1);
            }
            GeoPoint parsed = new GeoPoint(lat, lon);
            parsed.Time = timestamp;
            if (!rmc && parts[9].Length > 0)
            {
                double altitude;
                if (parts[10] != "M" || !Double.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out altitude) || !GeoMath.IsFinite(altitude)) return false;
                parsed.Elevation = altitude;
            }
            date = timestamp.UtcDateTime;
            point = parsed;
            return true;
        }

        private static bool Digits(string value)
        {
            foreach (char ch in value) if (ch < '0' || ch > '9') return false;
            return value.Length > 0;
        }

        private static bool Coordinate(string value, string direction, bool latitude, out double coordinate)
        {
            coordinate = 0;
            int digits = latitude ? 2 : 3;
            if (value.Length < digits + 2 || !Digits(value.Substring(0, digits + 2))) return false;
            if (value.Length > digits + 2 && (value[digits + 2] != '.' || !Digits(value.Substring(digits + 3)))) return false;
            int degrees;
            double minutes;
            if (!Int32.TryParse(value.Substring(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out degrees) || !Double.TryParse(value.Substring(digits), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out minutes)) return false;
            if (minutes < 0 || minutes >= 60 || (direction != (latitude ? "N" : "E") && direction != (latitude ? "S" : "W"))) return false;
            coordinate = degrees + minutes / 60;
            if (coordinate > (latitude ? 90 : 180)) return false;
            if (direction == "S" || direction == "W") coordinate = -coordinate;
            return true;
        }

        private static bool Time(string value, DateTime date, out DateTimeOffset timestamp)
        {
            timestamp = default(DateTimeOffset);
            if (value.Length < 6 || !Digits(value.Substring(0, 6))) return false;
            if (value.Length > 6 && (value[6] != '.' || !Digits(value.Substring(7)))) return false;
            int hour = Int32.Parse(value.Substring(0, 2), CultureInfo.InvariantCulture);
            int minute = Int32.Parse(value.Substring(2, 2), CultureInfo.InvariantCulture);
            double second;
            if (hour > 23 || minute > 59 || !Double.TryParse(value.Substring(4), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out second) || second >= 60) return false;
            timestamp = new DateTimeOffset(date.Date.AddHours(hour).AddMinutes(minute).AddSeconds(second), TimeSpan.Zero);
            return true;
        }
    }
}
