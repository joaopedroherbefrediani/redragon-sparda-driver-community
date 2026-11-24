namespace SpardaControl.Core.Models;

public static class HidEventType
{
    public const int Modifier = 0;
    public const int Keyboard = 1;
    public const int Consumer = 2;
    public const int Mouse = 4;
}

public readonly record struct HidKey(int Type, int Value, string Name);

public static class KeyCodes
{
    static readonly (string Code, string Name, int Type, int Value)[] Table =
    [
        ("Escape","Esc",1,41),("F1","F1",1,58),("F2","F2",1,59),("F3","F3",1,60),("F4","F4",1,61),("F5","F5",1,62),
        ("F6","F6",1,63),("F7","F7",1,64),("F8","F8",1,65),("F9","F9",1,66),("F10","F10",1,67),("F11","F11",1,68),("F12","F12",1,69),
        ("Backquote","`",1,53),("Digit1","1",1,30),("Digit2","2",1,31),("Digit3","3",1,32),("Digit4","4",1,33),("Digit5","5",1,34),
        ("Digit6","6",1,35),("Digit7","7",1,36),("Digit8","8",1,37),("Digit9","9",1,38),("Digit0","0",1,39),("Minus","-",1,45),
        ("Equal","=",1,46),("Backspace","Backspace",1,42),("Tab","Tab",1,43),("KeyQ","Q",1,20),("KeyW","W",1,26),("KeyE","E",1,8),
        ("KeyR","R",1,21),("KeyT","T",1,23),("KeyY","Y",1,28),("KeyU","U",1,24),("KeyI","I",1,12),("KeyO","O",1,18),("KeyP","P",1,19),
        ("BracketLeft","[",1,47),("BracketRight","]",1,48),("Backslash","\\",1,49),("CapsLock","CapsLock",1,57),("KeyA","A",1,4),
        ("KeyS","S",1,22),("KeyD","D",1,7),("KeyF","F",1,9),("KeyG","G",1,10),("KeyH","H",1,11),("KeyJ","J",1,13),("KeyK","K",1,14),
        ("KeyL","L",1,15),("Semicolon",";",1,51),("Quote","'",1,52),("Enter","Enter",1,40),("KeyZ","Z",1,29),("KeyX","X",1,27),
        ("KeyC","C",1,6),("KeyV","V",1,25),("KeyB","B",1,5),("KeyN","N",1,17),("KeyM","M",1,16),("Comma",",",1,54),("Period",".",1,55),
        ("Slash","/",1,56),("Space","Espaço",1,44),("ContextMenu","Menu",1,101),("PrintScreen","PrtScr",1,70),("ScrollLock","ScrLk",1,71),
        ("Pause","Pause",1,72),("Insert","Insert",1,73),("Home","Home",1,74),("PageUp","PgUp",1,75),("Delete","Del",1,76),("End","End",1,77),
        ("PageDown","PgDn",1,78),("ArrowUp","↑",1,82),("ArrowLeft","←",1,80),("ArrowDown","↓",1,81),("ArrowRight","→",1,79),
        ("NumLock","NumLock",1,83),("NumpadDivide","Num /",1,84),("NumpadMultiply","Num *",1,85),("NumpadSubtract","Num -",1,86),
        ("NumpadAdd","Num +",1,87),("NumpadDecimal","Num .",1,99),("NumpadEnter","Num Enter",1,88),("Numpad1","Num 1",1,89),
        ("Numpad2","Num 2",1,90),("Numpad3","Num 3",1,91),("Numpad4","Num 4",1,92),("Numpad5","Num 5",1,93),("Numpad6","Num 6",1,94),
        ("Numpad7","Num 7",1,95),("Numpad8","Num 8",1,96),("Numpad9","Num 9",1,97),("Numpad0","Num 0",1,98),
        ("IntlBackslash","\\ (ABNT)",1,100),("IntlRo","/ (ABNT)",1,135),
        ("ControlLeft","Ctrl Esq",0,1),("ShiftLeft","Shift Esq",0,2),("AltLeft","Alt Esq",0,4),("MetaLeft","Win Esq",0,8),
        ("ControlRight","Ctrl Dir",0,16),("ShiftRight","Shift Dir",0,32),("AltRight","Alt Dir",0,64),("MetaRight","Win Dir",0,128),
    ];

    static readonly Dictionary<string, HidKey> ByCode =
        Table.ToDictionary(t => t.Code, t => new HidKey(t.Type, t.Value, t.Name));

    public static HidKey? FromCode(string code) => ByCode.TryGetValue(code, out var k) ? k : null;

    public static readonly (int Value, string Name)[] MouseButtons =
    [
        (1, "Clique esquerdo"), (2, "Clique direito"), (4, "Clique do meio"), (16, "Avançar"), (8, "Voltar"),
    ];

    public static readonly (int Usage, string Name)[] Media =
    [
        (0x0183, "Player de mídia"), (0x00CD, "Play/Pause"), (0x00B5, "Próxima faixa"), (0x00B6, "Faixa anterior"),
        (0x00B7, "Parar"), (0x00E2, "Mudo"), (0x00E9, "Volume +"), (0x00EA, "Volume -"), (0x018A, "E-mail"),
        (0x0192, "Calculadora"), (0x0194, "Meu computador"), (0x0223, "Página inicial"), (0x0221, "Pesquisar"),
        (0x0225, "Página seguinte"), (0x0224, "Página anterior"), (0x0226, "Parar página"), (0x0227, "Atualizar página"),
        (0x022A, "Favoritos"),
    ];

    public static string Describe(int type, int value)
    {
        switch (type)
        {
            case HidEventType.Mouse:
                foreach (var (v, n) in MouseButtons) if (v == value) return n;
                return $"Mouse 0x{value:X2}";
            case HidEventType.Consumer:
                foreach (var (u, n) in Media) if (u == value) return n;
                return $"Mídia 0x{value:X4}";
            default:
                foreach (var t in Table) if (t.Type == type && t.Value == value) return t.Name;
                return type == HidEventType.Modifier ? $"Mod 0x{value:X2}" : $"Tecla 0x{value:X2}";
        }
    }
}
