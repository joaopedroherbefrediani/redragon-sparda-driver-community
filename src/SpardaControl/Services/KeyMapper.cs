using System.Windows.Input;

namespace SpardaControl.Services;

public static class KeyMapper
{
    public static Key RealKey(KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.DeadCharProcessed => e.DeadCharProcessedKey,
        _ => e.Key,
    };

    public static string? ToCode(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return "Key" + (char)('A' + (k - Key.A));
        if (k >= Key.D0 && k <= Key.D9) return "Digit" + (k - Key.D0);
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Numpad" + (k - Key.NumPad0);
        if (k >= Key.F1 && k <= Key.F12) return "F" + (k - Key.F1 + 1);
        return k switch
        {
            Key.Escape => "Escape",
            Key.OemTilde => "Backquote",
            Key.OemMinus => "Minus",
            Key.OemPlus => "Equal",
            Key.Back => "Backspace",
            Key.Tab => "Tab",
            Key.OemOpenBrackets => "BracketLeft",
            Key.OemCloseBrackets => "BracketRight",
            Key.OemPipe => "Backslash",
            Key.CapsLock => "CapsLock",
            Key.OemSemicolon => "Semicolon",
            Key.OemQuotes => "Quote",
            Key.Enter => "Enter",
            Key.OemComma => "Comma",
            Key.OemPeriod => "Period",
            Key.OemQuestion => "Slash",
            Key.Space => "Space",
            Key.Apps => "ContextMenu",
            Key.PrintScreen => "PrintScreen",
            Key.Scroll => "ScrollLock",
            Key.Pause => "Pause",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.PageUp => "PageUp",
            Key.Delete => "Delete",
            Key.End => "End",
            Key.PageDown => "PageDown",
            Key.Up => "ArrowUp",
            Key.Left => "ArrowLeft",
            Key.Down => "ArrowDown",
            Key.Right => "ArrowRight",
            Key.NumLock => "NumLock",
            Key.Divide => "NumpadDivide",
            Key.Multiply => "NumpadMultiply",
            Key.Subtract => "NumpadSubtract",
            Key.Add => "NumpadAdd",
            Key.Decimal => "NumpadDecimal",
            Key.Oem102 => "IntlBackslash",
            Key.AbntC1 => "IntlRo",
            Key.AbntC2 => "NumpadDecimal",
            Key.LeftCtrl => "ControlLeft",
            Key.RightCtrl => "ControlRight",
            Key.LeftShift => "ShiftLeft",
            Key.RightShift => "ShiftRight",
            Key.LeftAlt => "AltLeft",
            Key.RightAlt => "AltRight",
            Key.LWin => "MetaLeft",
            Key.RWin => "MetaRight",
            _ => null,
        };
    }
}
