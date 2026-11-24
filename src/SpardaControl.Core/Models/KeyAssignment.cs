using SpardaControl.Core.Protocol;

namespace SpardaControl.Core.Models;

public readonly record struct ComboKey(int Type, int Value);

public sealed record KeyAssignment(KeyFunctionType Type, int Param)
{
    public IReadOnlyList<ComboKey>? Combo { get; init; }
    public string? MacroName { get; init; }

    public static KeyAssignment Mouse(int param) => new(KeyFunctionType.MouseKey, param);

    public const int LeftClick = 0x0100, RightClick = 0x0200, MiddleClick = 0x0400, Backward = 0x0800, Forward = 0x1000;
    public const int DpiLoop = 0x0100, DpiUp = 0x0200, DpiDown = 0x0300;
    public const int ScrollLeft = 0x0100, ScrollRight = 0x0200;

    public bool IsLeftClick => Type == KeyFunctionType.MouseKey && Param == LeftClick;

    public byte[] ToRecord()
    {
        var r = new byte[4];
        r[0] = (byte)Type;
        if (Type == KeyFunctionType.DpiLock)
        {
            r[1] = (byte)Param;
            r[2] = (byte)(Param >> 8);
        }
        else
        {
            r[1] = (byte)(Param >> 8);
            r[2] = (byte)Param;
        }
        r[3] = Packet.RecordChecksum(r.AsSpan(0, 3));
        return r;
    }

    public static KeyAssignment FromRecord(ReadOnlySpan<byte> r)
    {
        var type = (KeyFunctionType)r[0];
        int param = type == KeyFunctionType.DpiLock ? r[1] | (r[2] << 8) : (r[1] << 8) | r[2];
        return new KeyAssignment(type, param);
    }

    public string Describe()
    {
        switch (Type)
        {
            case KeyFunctionType.Disable: return "Desativado";
            case KeyFunctionType.MouseKey:
                return Param switch
                {
                    LeftClick => "Clique esquerdo",
                    RightClick => "Clique direito",
                    MiddleClick => "Clique da roda",
                    Backward => "Voltar",
                    Forward => "Avançar",
                    _ => $"Botão 0x{Param:X4}",
                };
            case KeyFunctionType.DpiSwitch:
                return Param switch { DpiLoop => "DPI (ciclo)", DpiUp => "DPI +", DpiDown => "DPI -", _ => "DPI" };
            case KeyFunctionType.LeftRightRoll:
                return Param == ScrollLeft ? "Rolar para esquerda" : "Rolar para direita";
            case KeyFunctionType.FireKey:
                return $"Disparo rápido ({Param & 0xFF}x, {Param >> 8}ms)";
            case KeyFunctionType.ShortcutKey:
                if (Combo is { Count: 1 } c && c[0].Type == HidEventType.Consumer)
                    return KeyCodes.Describe(c[0].Type, c[0].Value);
                if (Combo is { Count: > 0 })
                    return string.Join(" + ", Combo.Select(k => KeyCodes.Describe(k.Type, k.Value)));
                return "Combo de teclas";
            case KeyFunctionType.Macro:
                return "Macro: " + (MacroName ?? "?");
            case KeyFunctionType.ReportRateSwitch: return "Trocar polling rate";
            case KeyFunctionType.LightSwitch: return "Trocar iluminação";
            case KeyFunctionType.ProfileSwitch: return "Trocar perfil";
            case KeyFunctionType.DpiLock: return $"Travar DPI ({Param})";
            case KeyFunctionType.UpDownRoll: return "Rolagem";
            default: return $"Função {(int)Type}";
        }
    }
}
