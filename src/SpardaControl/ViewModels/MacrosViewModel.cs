using System.Collections.ObjectModel;
using System.Diagnostics;
using SpardaControl.Core;
using SpardaControl.Core.Models;
using SpardaControl.Core.Protocol;
using SpardaControl.Infrastructure;
using SpardaControl.Services;

namespace SpardaControl.ViewModels;

public sealed class MacrosViewModel : ObservableObject
{
    readonly MainViewModel _main;
    readonly Stopwatch _clock = new();
    readonly HashSet<string> _pressed = [];
    string? _editingOriginalName;

    public MacrosViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var m in MacroLibrary.Load()) Items.Add(new MacroItemViewModel(m));
        NewCommand = new RelayCommand(NewMacro);
        DeleteCommand = new RelayCommand(DeleteSelected, () => SelectedItem != null);
        SaveCommand = new RelayCommand(() => _ = SaveAsync(), () => HasEditor);
        RecordCommand = new RelayCommand(ToggleRecord, () => HasEditor);
        ClearCommand = new RelayCommand(() => { Events.Clear(); MarkDirty(); }, () => HasEditor && Events.Count > 0);
        RemoveEventCommand = new RelayCommand(p => { if (p is MacroEventViewModel e) { Events.Remove(e); MarkDirty(); } });
        Events.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (MacroEventViewModel ev in e.NewItems)
                    ev.PropertyChanged += (_, _) => MarkDirty();
            OnPropertyChanged(nameof(EventCountText));
        };
    }

    public ObservableCollection<MacroItemViewModel> Items { get; } = [];
    public ObservableCollection<MacroEventViewModel> Events { get; } = [];

    public RelayCommand NewCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RecordCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand RemoveEventCommand { get; }

    MacroItemViewModel? _selectedItem;
    public MacroItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (IsRecording) StopRecord();
            if (!Set(ref _selectedItem, value)) return;
            LoadEditor(value?.Model);
            OnPropertyChanged(nameof(HasEditor));
        }
    }

    public bool HasEditor => SelectedItem != null;

    string _editName = "";
    public string EditName { get => _editName; set { if (Set(ref _editName, value)) MarkDirty(); } }

    int _cycleMode;
    public int CycleMode { get => _cycleMode; set { if (Set(ref _cycleMode, value)) MarkDirty(); } }

    int _cycleTimes = 1;
    public int CycleTimes { get => _cycleTimes; set { if (Set(ref _cycleTimes, Math.Clamp(value, 1, MacroCycle.MaxTimes))) MarkDirty(); } }

    bool _autoDelay = true;
    public bool AutoDelay { get => _autoDelay; set => Set(ref _autoDelay, value); }

    int _defaultDelay = 20;
    public int DefaultDelay { get => _defaultDelay; set => Set(ref _defaultDelay, Math.Clamp(value, 0, 65535)); }

    bool _isRecording;
    public bool IsRecording { get => _isRecording; private set => Set(ref _isRecording, value); }

    bool _isDirty;
    public bool IsDirty { get => _isDirty; private set => Set(ref _isDirty, value); }

    public string EventCountText => $"{Events.Count} / {Macro.MaxEvents} eventos";

    void MarkDirty()
    {
        if (_loadingEditor) return;
        IsDirty = true;
        OnPropertyChanged(nameof(EventCountText));
    }

    bool _loadingEditor;

    void LoadEditor(Macro? m)
    {
        _loadingEditor = true;
        Events.Clear();
        if (m != null)
        {
            _editingOriginalName = m.Name;
            EditName = m.Name;
            foreach (var e in m.Events) Events.Add(MacroEventViewModel.From(e));
            if (m.Cycle is MacroCycle.UntilThisPressed or MacroCycle.UntilThisReleased or MacroCycle.UntilAnyPressed)
            {
                CycleMode = m.Cycle;
                CycleTimes = 1;
            }
            else
            {
                CycleMode = 0;
                CycleTimes = Math.Clamp(m.Cycle, 1, MacroCycle.MaxTimes);
            }
        }
        _loadingEditor = false;
        IsDirty = false;
        OnPropertyChanged(nameof(EventCountText));
    }

    void Persist() => MacroLibrary.Save(Items.Select(i => i.Model));

    string UniqueName(string baseName)
    {
        string name = baseName;
        int n = 2;
        while (Items.Any(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"{baseName} {n++}";
        return name;
    }

    void NewMacro()
    {
        var m = new Macro { Name = UniqueName("Macro"), Cycle = 1 };
        var item = new MacroItemViewModel(m);
        Items.Add(item);
        Persist();
        SelectedItem = item;
        RefreshAssignments();
    }

    void DeleteSelected()
    {
        if (SelectedItem == null) return;
        if (AssignedKeys(SelectedItem.Name).Any())
        {
            _main.ShowToast("Essa macro está em uso. Troque a função do botão antes de excluí-la.", true);
            return;
        }
        Items.Remove(SelectedItem);
        Persist();
        SelectedItem = Items.FirstOrDefault();
    }

    internal void ImportFromDevice(MouseDevice dev)
    {
        bool changed = false;
        foreach (var m in dev.KeyMacros.Values)
        {
            if (Items.Any(i => i.Name == m.Name)) continue;
            Items.Add(new MacroItemViewModel(m.Clone()));
            changed = true;
        }
        if (changed) Persist();
        RefreshAssignments();
        if (SelectedItem == null && Items.Count > 0) SelectedItem = Items[0];
    }

    internal void Merge(IEnumerable<Macro> macros)
    {
        foreach (var m in macros)
        {
            var existing = Items.FirstOrDefault(i => i.Name == m.Name);
            if (existing != null) existing.Replace(m);
            else Items.Add(new MacroItemViewModel(m));
        }
        Persist();
        RefreshAssignments();
    }

    IEnumerable<KeySlotViewModel> AssignedKeys(string macroName)
    {
        var dev = _main.Device;
        if (dev == null) yield break;
        foreach (var slot in _main.Keys)
        {
            var a = dev.Settings.Keys[slot.Index];
            if (a.Type == KeyFunctionType.Macro && a.MacroName == macroName) yield return slot;
        }
    }

    internal void RefreshAssignments()
    {
        foreach (var item in Items)
        {
            var keys = AssignedKeys(item.Name).Select(k => k.Number.ToString()).ToList();
            item.AssignedTo = keys.Count == 0 ? "" : "Botão " + string.Join(", ", keys);
        }
    }

    async Task SaveAsync()
    {
        if (SelectedItem == null) return;
        if (IsRecording) StopRecord();
        var name = EditName.Trim();
        if (name.Length == 0) { _main.ShowToast("Dê um nome para a macro.", true); return; }
        if (System.Text.Encoding.UTF8.GetByteCount(name) > Macro.MaxNameBytes) { _main.ShowToast("Nome muito longo (máx. 30 caracteres).", true); return; }
        if (Items.Any(i => i != SelectedItem && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _main.ShowToast("Já existe uma macro com esse nome.", true);
            return;
        }
        if (Events.Count > Macro.MaxEvents) { _main.ShowToast($"Máximo de {Macro.MaxEvents} eventos.", true); return; }

        var macro = new Macro
        {
            Name = name,
            Cycle = CycleMode == 0 ? CycleTimes : CycleMode,
            Events = Events.Select(e => e.ToModel()).ToList(),
        };
        var assigned = AssignedKeys(_editingOriginalName ?? name).ToList();
        SelectedItem.Replace(macro);
        _editingOriginalName = name;
        Persist();
        IsDirty = false;

        if (assigned.Count > 0)
        {
            bool ok = await _main.Apply(async d =>
            {
                foreach (var slot in assigned) await d.SetMacroKeyAsync(slot.Index, macro);
            }, "Macro salva e atualizada no mouse");
            if (ok) _main.LoadFromDevice();
        }
        else _main.ShowToast("Macro salva. Atribua-a a um botão na aba Botões.");
        RefreshAssignments();
    }

    public void InsertMouseClick(int buttonValue)
    {
        if (!HasEditor || Events.Count + 2 > Macro.MaxEvents) return;
        Events.Add(new MacroEventViewModel { Press = true, Type = HidEventType.Mouse, Value = buttonValue, Delay = DefaultDelay });
        Events.Add(new MacroEventViewModel { Press = false, Type = HidEventType.Mouse, Value = buttonValue, Delay = DefaultDelay });
        MarkDirty();
    }

    public void InsertMouseEvent(int buttonValue, bool press)
    {
        if (!HasEditor || Events.Count >= Macro.MaxEvents) return;
        Events.Add(new MacroEventViewModel { Press = press, Type = HidEventType.Mouse, Value = buttonValue, Delay = DefaultDelay });
        MarkDirty();
    }

    public void MoveEvent(MacroEventViewModel e, int direction)
    {
        int i = Events.IndexOf(e);
        int j = i + direction;
        if (i < 0 || j < 0 || j >= Events.Count) return;
        Events.Move(i, j);
        MarkDirty();
    }

    void ToggleRecord()
    {
        if (IsRecording) StopRecord();
        else StartRecord();
    }

    void StartRecord()
    {
        if (!HasEditor) return;
        _pressed.Clear();
        _clock.Restart();
        IsRecording = true;
    }

    public void StopRecord()
    {
        IsRecording = false;
        _clock.Stop();
        foreach (var code in _pressed.ToList()) RecordKey(code, false, force: true);
        _pressed.Clear();
    }

    public bool RecordKey(string code, bool press, bool force = false)
    {
        if (!IsRecording && !force) return false;
        var key = KeyCodes.FromCode(code);
        if (key == null) return true;
        if (press && !_pressed.Add(code)) return true;
        if (!press && !_pressed.Remove(code) && !force) return true;
        if (Events.Count >= Macro.MaxEvents)
        {
            if (!force) { StopRecord(); _main.ShowToast($"Limite de {Macro.MaxEvents} eventos atingido.", true); }
            return true;
        }
        long elapsed = _clock.ElapsedMilliseconds;
        _clock.Restart();
        if (Events.Count > 0)
            Events[^1].Delay = AutoDelay ? (int)Math.Min(elapsed, 65535) : DefaultDelay;
        Events.Add(new MacroEventViewModel { Press = press, Type = key.Value.Type, Value = key.Value.Value, Delay = DefaultDelay });
        MarkDirty();
        return true;
    }
}
