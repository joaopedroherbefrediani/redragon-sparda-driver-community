using SpardaControl.Core.Models;

namespace SpardaControl.Core.Protocol;

public static class DpiCodec
{
    public readonly record struct Raw(int Value, int Ex);

    public static Raw Encode(SensorInfo sensor, int dpi)
    {
        var ranges = sensor.Range;
        int t = ranges.Count - 1;
        while (t > 0 && dpi < ranges[t].Min) t--;
        int ex = ranges[t].DpiEx;
        int mul = t == 3 ? 4 : (t == 1 || t == 2) ? 2 : 1;
        bool om76 = sensor.Name == "OM76";
        if (om76 && ex == 34) mul = 10;
        else if (om76 && ex == 51) mul = 20;
        int a = dpi / mul;
        if (sensor.Values is { Count: > 0 } values)
        {
            int idx = (a - ranges[0].Min) / ranges[0].Step;
            a = values[Math.Clamp(idx, 0, values.Count - 1)];
        }
        else
        {
            a /= ranges[0].Step;
            if (!((om76 && dpi > 10000) || sensor.Name == "S312")) a -= 1;
        }
        return new Raw(Math.Max(0, a), ex);
    }

    public static int Decode(SensorInfo sensor, int value, int exBits)
    {
        var ranges = sensor.Range;
        if (ranges.Count == 0) return value;
        bool doubled = (exBits & 1) == 1;
        bool high = (exBits & 2) == 2;
        bool om76 = sensor.Name == "OM76";
        int a;
        if (sensor.Values is { Count: > 0 } values)
        {
            int n = values.IndexOf(value);
            if (n < 0) n = 0;
            a = n * ranges[0].Step + ranges[0].Min;
        }
        else if (om76)
            a = value * ranges[0].Step + (high ? 0 : ranges[0].Min);
        else if (sensor.Name == "S312")
            a = value * ranges[0].Step;
        else
            a = (value + 1) * ranges[0].Step;
        if (doubled) a *= 2;
        if (high) a *= om76 ? 10 : 2;
        return a;
    }

    public static byte[] EncodeRecord(SensorInfo sensor, int dpiX, int dpiY)
    {
        var x = Encode(sensor, dpiX);
        var y = Encode(sensor, dpiY);
        var r = new byte[4];
        r[0] = (byte)x.Value;
        r[1] = (byte)y.Value;
        int xh = (x.Value >> 8) & 3, yh = (y.Value >> 8) & 3;
        r[2] = (byte)((xh << 2) | (yh << 6) | (x.Ex & 3) | ((y.Ex & 3) << 4));
        r[3] = Packet.RecordChecksum(r.AsSpan(0, 3));
        return r;
    }

    public static (int X, int Y) DecodeRecord(SensorInfo sensor, ReadOnlySpan<byte> rec)
    {
        byte b = rec[2];
        int xv = rec[0] + (((b >> 2) & 3) << 8);
        int yv = rec[1] + (((b >> 6) & 3) << 8);
        return (Decode(sensor, xv, b & 3), Decode(sensor, yv, (b >> 4) & 3));
    }

    public static int Snap(SensorInfo sensor, int dpi, int maxDpi)
    {
        var ranges = sensor.Range;
        if (ranges.Count == 0) return dpi;
        dpi = Math.Clamp(dpi, ranges[0].Min, maxDpi);
        for (int i = ranges.Count - 1; i >= 0; i--)
        {
            if (dpi > ranges[i].Min - ranges[i].Step)
            {
                int step = ranges[i].Step;
                int v = (int)Math.Ceiling(dpi / (double)step) * step;
                if (v > maxDpi) v = (maxDpi / step) * step;
                return Math.Max(v, ranges[0].Min);
            }
        }
        return dpi;
    }

    public static int StepAt(SensorInfo sensor, int dpi)
    {
        var ranges = sensor.Range;
        for (int i = ranges.Count - 1; i >= 0; i--)
            if (dpi > ranges[i].Min - ranges[i].Step) return ranges[i].Step;
        return ranges.Count > 0 ? ranges[0].Step : 50;
    }
}
