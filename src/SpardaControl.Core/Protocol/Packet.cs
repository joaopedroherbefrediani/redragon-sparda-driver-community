namespace SpardaControl.Core.Protocol;

public static class Packet
{
    public const byte ReportId = 8;
    public const int Size = 16;
    public const int MaxData = 10;
    public const byte Magic = 0x55;

    public static byte[] Build(Command cmd, int address = 0, ReadOnlySpan<byte> data = default, int? length = null)
    {
        if (data.Length > MaxData) throw new ArgumentException("Max 10 bytes per packet", nameof(data));
        var p = new byte[Size];
        p[0] = (byte)cmd;
        p[1] = 0;
        p[2] = (byte)(address >> 8);
        p[3] = (byte)address;
        p[4] = (byte)(length ?? data.Length);
        data.CopyTo(p.AsSpan(5));
        p[15] = Checksum(p);
        return p;
    }

    public static byte Checksum(ReadOnlySpan<byte> p)
    {
        int sum = 0;
        for (int i = 0; i < 15; i++) sum += p[i];
        return (byte)(Magic - sum - ReportId);
    }

    public static byte RecordChecksum(ReadOnlySpan<byte> bytesWithoutChecksum)
    {
        int sum = 0;
        foreach (var b in bytesWithoutChecksum) sum += b;
        return (byte)(Magic - sum);
    }

    public static bool IsValidPair(byte a, byte b) => (byte)(a + b) == Magic;
}
