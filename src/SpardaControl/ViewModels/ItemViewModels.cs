using System.Windows.Media;
using SpardaControl.Core.Models;
using SpardaControl.Infrastructure;

namespace SpardaControl.ViewModels;

public sealed class KeySlotViewModel : ObservableObject
{
    public int Index { get; init; }
    public int Number { get; init; }
    public string Role { get; init; } = "";
    public double BadgeLeft { get; init; }
    public double BadgeTop { get; init; }

    string _function = "";
    public string Function { get => _function; set => Set(ref _function, value); }

    bool _isHighlighted;
    public bool IsHighlighted { get => _isHighlighted; set => Set(ref _isHighlighted, value); }

    bool _isCustom;
    public bool IsCustom { get => _isCustom; set => Set(ref _isCustom, value); }
}

public sealed class DpiStageViewModel : ObservableObject
{
    public int Index { get; init; }
    public int Number => Index + 1;

    int _value;
    public int Value { get => _value; set => Set(ref _value, value); }

    Color _color;
    public Color Color { get => _color; set => Set(ref _color, value); }

    bool _isCurrent;
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
}

public sealed class MacroEventViewModel : ObservableObject
{
    public bool Press { get; init; }
    public int Type { get; init; }
    public int Value { get; init; }

    int _delay;
    public int Delay
    {
        get => _delay;
        set => Set(ref _delay, Math.Clamp(value, 0, 65535));
    }

    public string Action => Press ? "Pressionar" : "Soltar";
    public string KeyName => KeyCodes.Describe(Type, Value);
    public bool IsMouse => Type == HidEventType.Mouse;

    public MacroEvent ToModel() => new() { Press = Press, Type = Type, Value = Value, Delay = Delay };

    public static MacroEventViewModel From(MacroEvent e) =>
        new() { Press = e.Press, Type = e.Type, Value = e.Value, Delay = e.Delay };
}

public sealed class MacroItemViewModel : ObservableObject
{
    public MacroItemViewModel(Macro model) { Model = model; }

    public Macro Model { get; private set; }

    public string Name => Model.Name;
    public string Summary => $"{Model.Events.Count} eventos · {CycleText(Model.Cycle)}";

    string _assignedTo = "";
    public string AssignedTo { get => _assignedTo; set => Set(ref _assignedTo, value); }

    public void Replace(Macro m)
    {
        Model = m;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Summary));
    }

    public static string CycleText(int cycle) => cycle switch
    {
        MacroCycle.UntilThisReleased => "enquanto segurar",
        MacroCycle.UntilAnyPressed => "até qualquer botão",
        MacroCycle.UntilThisPressed => "até apertar de novo",
        1 => "1 vez",
        _ => $"{cycle} vezes",
    };
}
