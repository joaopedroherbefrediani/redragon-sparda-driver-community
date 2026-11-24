using System.Text;
using SpardaControl.Core.Protocol;

namespace SpardaControl.Core.Models;

public sealed class MacroEvent
{
    public bool Press { get; set; } = true;
    public int Type { get; set; }
    public int Value { get; set; }
    public int Delay { get; set; } = 10;

    public MacroEvent Clone() => new() { Press = Press, Type = Type, Value = Value, Delay = Delay };
}

public static class MacroCycle
{
    public const int UntilThisPressed = 253;
    public const int UntilThisReleased = 254;
    public const int UntilAnyPressed = 255;
    public const int MaxTimes = 250;
}

public sealed class Macro
{
    public const int MaxEvents = 70;
    public const int MaxNameBytes = 30;

    public string Name { get; set; } = "";
    public int Cycle { get; set; } = 1;
    public List<MacroEvent> Events { get; set; } = [];

    public Macro Clone() => new() { Name = Name, Cycle = Cycle, Events = Events.Select(e => e.Clone()).ToList() };

    public byte[] ToFlash()
    {
        var name = Encoding.UTF8.GetBytes(Name);
        if (name.Length > MaxNameBytes) name = name[..MaxNameBytes];
        int count = Math.Min(Events.Count, MaxEvents);
        var buf = new byte[33 + 5 * count];
        Array.Fill(buf, (byte)0xFF);
        buf[0] = (byte)name.Length;
        name.CopyTo(buf, 1);
        buf[31] = (byte)count;
        int sum = count;
        for (int i = 0; i < count; i++)
        {
            var e = Events[i];
            int o = 32 + 5 * i;
            buf[o] = (byte)(((e.Press ? 2 : 1) << 6) | (e.Type & 0x0F));
            buf[o + 1] = (byte)e.Value;
            buf[o + 2] = (byte)(e.Value >> 8);
            int d = Math.Clamp(e.Delay, 0, 65535);
            buf[o + 3] = (byte)(d >> 8);
            buf[o + 4] = (byte)d;
            for (int k = 0; k < 5; k++) sum += buf[o + k];
        }
        buf[32 + 5 * count] = (byte)(Packet.Magic - sum);
        return buf;
    }

    public static Macro? FromFlash(ReadOnlySpan<byte> slot)
    {
        int nameLen = slot[0];
        int count = slot[31];
        if (nameLen == 0 || nameLen > MaxNameBytes || count > MaxEvents) return null;
        var m = new Macro { Name = Encoding.UTF8.GetString(slot.Slice(1, nameLen)) };
        for (int i = 0; i < count; i++)
        {
            int o = 32 + 5 * i;
            if (o + 5 > slot.Length) break;
            byte h = slot[o];
            m.Events.Add(new MacroEvent
            {
                Press = (h >> 6) == 2,
                Type = h & 0x0F,
                Value = slot[o + 1] | (slot[o + 2] << 8),
                Delay = (slot[o + 3] << 8) | slot[o + 4],
            });
        }
        return m;
    }
}
