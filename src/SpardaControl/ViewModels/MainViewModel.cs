using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SpardaControl.Core;
using SpardaControl.Core.Models;
using SpardaControl.Core.Protocol;
using SpardaControl.Infrastructure;
using SpardaControl.Services;

namespace SpardaControl.ViewModels;

public enum ConnectionState { Searching, NotFound, Offline, Loading, Ready, Unsupported }

public sealed partial class MainViewModel : ObservableObject
{
    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly Debouncer _debouncer = new();
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.4) };
    MouseDevice? _dev;
    bool _loading;
    bool _waitingOnline;
    bool _connecting;

    public MainViewModel()
    {
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastVisible = false; };
        Macros = new MacrosViewModel(this);
        RetryCommand = new RelayCommand(() => _ = ConnectLoopAsync());
    }

    public MacrosViewModel Macros { get; }
    public RelayCommand RetryCommand { get; }
    internal MouseDevice? Device => _dev;

    ConnectionState _state = ConnectionState.Searching;
    public ConnectionState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(StatusTitle));
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(ShowRetry));
        }
    }

    public bool IsReady => State == ConnectionState.Ready;
    public bool IsBusy => State is ConnectionState.Searching or ConnectionState.Loading;
    public bool ShowRetry => State is ConnectionState.NotFound or ConnectionState.Unsupported;

    string _unsupportedText = "";

    public string StatusTitle => State switch
    {
        ConnectionState.Searching => "Procurando o mouse…",
        ConnectionState.NotFound => "Mouse não encontrado",
        ConnectionState.Offline => "Mouse desligado ou dormindo",
        ConnectionState.Loading => "Lendo configurações…",
        ConnectionState.Unsupported => "Modelo não suportado",
        _ => "",
    };

    public string StatusMessage => State switch
    {
        ConnectionState.Searching => "Verificando o receptor 2.4G e a conexão USB.",
        ConnectionState.NotFound => "Conecte o receptor 2.4G ou o cabo USB do mouse. A busca continua automaticamente.",
        ConnectionState.Offline => "O receptor foi encontrado. Ligue o mouse ou mexa nele para acordá-lo.",
        ConnectionState.Loading => "Só um instante.",
        ConnectionState.Unsupported => _unsupportedText,
        _ => "",
    };

    public async Task ConnectLoopAsync()
    {
        if (_connecting) return;
        _connecting = true;
        try
        {
            while (true)
            {
                if (_dev == null)
                {
                    State = ConnectionState.Searching;
                    MouseDevice? dev = null;
                    try
                    {
                        dev = await Task.Run(() => MouseDevice.ConnectAsync());
                    }
                    catch (NotSupportedException ex)
                    {
                        _unsupportedText = ex.Message;
                        State = ConnectionState.Unsupported;
                        return;
                    }
                    catch { dev = null; }

                    if (dev == null)
                    {
                        State = ConnectionState.NotFound;
                        await Task.Delay(2000);
                        continue;
                    }
                    Attach(dev);
                }

                bool online;
                try { online = await _dev!.CheckOnlineAsync(); }
                catch { Detach(); continue; }

                if (!online)
                {
                    State = ConnectionState.Offline;
                    await Task.Delay(1500);
                    continue;
                }

                State = ConnectionState.Loading;
                try
                {
                    await Task.Run(() => _dev!.LoadAllAsync());
                }
                catch (DeviceOfflineException) { State = ConnectionState.Offline; await Task.Delay(1500); continue; }
                catch { Detach(); await Task.Delay(1000); continue; }

                OnDeviceReady();
                State = ConnectionState.Ready;
                return;
            }
        }
        finally { _connecting = false; }
    }

    void Attach(MouseDevice dev)
    {
        _dev = dev;
        dev.Disconnected += () => _ui.BeginInvoke(() =>
        {
            if (_dev != dev) return;
            Detach();
            ShowToast("Mouse desconectado", true);
            _ = ConnectLoopAsync();
        });
        dev.BatteryChanged += () => _ui.BeginInvoke(UpdateBattery);
        dev.SettingsChanged += () => _ui.BeginInvoke(() => { LoadFromDevice(); });
        dev.OnlineChanged += online => _ui.BeginInvoke(() =>
        {
            if (State != ConnectionState.Ready) return;
            IsAsleep = !online;
            if (!online) _ = WatchWakeAsync();
        });
    }

    void Detach()
    {
        var d = _dev;
        _dev = null;
        if (d != null) { try { d.Dispose(); } catch { } }
    }

    bool _isAsleep;
    public bool IsAsleep { get => _isAsleep; set => Set(ref _isAsleep, value); }

    async Task WatchWakeAsync()
    {
        if (_waitingOnline) return;
        _waitingOnline = true;
        try
        {
            while (_dev != null && IsAsleep)
            {
                await Task.Delay(1200);
                try { if (_dev != null && await _dev.CheckOnlineAsync()) IsAsleep = false; }
                catch { break; }
            }
        }
        finally { _waitingOnline = false; }
    }

    public async Task ShutdownAsync()
    {
        if (_dev != null)
        {
            try { await _dev.CloseAsync(); } catch { }
            Detach();
        }
    }

    string _toastText = "";
    public string ToastText { get => _toastText; set => Set(ref _toastText, value); }
    bool _toastIsError;
    public bool ToastIsError { get => _toastIsError; set => Set(ref _toastIsError, value); }
    bool _toastVisible;
    public bool ToastVisible { get => _toastVisible; set => Set(ref _toastVisible, value); }

    public void ShowToast(string text, bool error = false)
    {
        ToastText = text;
        ToastIsError = error;
        ToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(error ? 4 : 1.8);
        _toastTimer.Start();
    }

    internal async Task<bool> Apply(Func<MouseDevice, Task> op, string? okText = "Salvo no mouse")
    {
        if (_loading || _dev == null || State != ConnectionState.Ready) return false;
        var dev = _dev;
        try
        {
            await op(dev);
            if (okText != null) ShowToast(okText);
            return true;
        }
        catch (DeviceOfflineException)
        {
            ShowToast("O mouse está em repouso. Mexa o mouse e tente de novo.", true);
            LoadFromDevice();
            IsAsleep = true;
            _ = WatchWakeAsync();
        }
        catch (Exception ex)
        {
            ShowToast("Falha ao gravar: " + ex.Message, true);
            LoadFromDevice();
        }
        return false;
    }

    void Debounced(string key, Func<MouseDevice, Task> op) =>
        _debouncer.Run(key, TimeSpan.FromMilliseconds(220), () => _ = Apply(op));

    public string ModelTitle => _dev == null ? "Redragon Sparda" : "Redragon Sparda";
    public string ModelSubtitle => _dev == null ? "" :
        $"Sensor {_dev.Model.Sensor} · até {_dev.Model.MaxDpi:N0} DPI · {(_dev.IsWired ? "USB" : "2.4G")}";

    int _profile;
    public int Profile
    {
        get => _profile;
        set
        {
            if (!Set(ref _profile, value) || _loading) return;
            _ = Apply(async d => { await d.SetProfileAsync(value); _ui.Invoke(LoadFromDevice); }, $"Perfil {value + 1} ativado");
        }
    }

    public IReadOnlyList<Option> ProfileOptions { get; } =
        [new(0, "Perfil 1"), new(1, "Perfil 2"), new(2, "Perfil 3"), new(3, "Perfil 4")];

    bool _supportsProfiles;
    public bool SupportsProfiles { get => _supportsProfiles; set => Set(ref _supportsProfiles, value); }

    int _batteryLevel;
    public int BatteryLevel { get => _batteryLevel; set => Set(ref _batteryLevel, value); }
    bool _batteryCharging;
    public bool BatteryCharging { get => _batteryCharging; set => Set(ref _batteryCharging, value); }
    bool _hasBattery;
    public bool HasBattery { get => _hasBattery; set => Set(ref _hasBattery, value); }

    void UpdateBattery()
    {
        var b = _dev?.Battery;
        HasBattery = b != null && !(_dev?.IsWired ?? false);
        if (b == null) return;
        BatteryLevel = b.Level;
        BatteryCharging = b.Charging;
    }

    public bool HasSensorMode => _dev?.Model.HasSensorMode == true;
    public bool HasLod => _dev?.Model.HasLod == true;
    public bool HasRipple => _dev?.Model.HasRipple == true;
    public bool HasAngle => _dev?.Model.HasAngle == true;
    public bool HasMotionSync => _dev?.Model.HasMotionSync == true;
    public bool HasPerformance => _dev?.Model.HasPerformance == true;
    public bool HasDpiEffect => _dev?.Model.HasDpiEffect == true;
    public bool HasLight => _dev?.Model.HasLightEffect == true;
    public bool HasLongDistance => _dev is { IsWired: false, LongDistance: not null };
    public bool HasSensorCard => HasSensorMode || HasLod || HasRipple || HasAngle || HasMotionSync || HasPerformance;

    public string InfoModel => _dev == null ? "--" : $"cid {_dev.Cid} · mid {_dev.Mid}";
    public string InfoSensor => _dev == null ? "--" : $"{_dev.Model.Sensor} (máx. {_dev.Model.MaxDpi:N0} DPI)";
    public string InfoConnection => _dev == null ? "--" :
        _dev.IsWired ? $"Cabo USB · até {_dev.MaxReportRate} Hz" : $"Receptor 2.4G · até {_dev.MaxReportRate} Hz";
    public string InfoReceiver => _dev == null ? "--" : $"{_dev.ReceiverName} (PID 0x{_dev.ProductId:X4})";
    public string InfoMouseFw => _dev?.MouseFirmware ?? "--";
    public string InfoDongleFw => _dev == null || _dev.IsWired ? "--" : _dev.DongleFirmware;

    void OnDeviceReady()
    {
        IsAsleep = false;
        BuildStaticLists();
        LoadFromDevice();
        Macros.ImportFromDevice(_dev!);
        foreach (var name in new[]
                 {
                     nameof(ModelTitle), nameof(ModelSubtitle), nameof(HasSensorMode), nameof(HasLod), nameof(HasRipple),
                     nameof(HasAngle), nameof(HasMotionSync), nameof(HasPerformance), nameof(HasDpiEffect), nameof(HasLight),
                     nameof(HasLongDistance), nameof(HasSensorCard), nameof(InfoModel), nameof(InfoSensor), nameof(InfoConnection),
                     nameof(InfoReceiver), nameof(InfoMouseFw), nameof(InfoDongleFw), nameof(DpiStageCountOptions),
                     nameof(ReportRateOptions), nameof(DebounceOptions), nameof(LodOptions), nameof(DpiMin), nameof(DpiMax),
                 })
            OnPropertyChanged(name);
    }

    public ObservableCollection<KeySlotViewModel> Keys { get; } = [];
    public ObservableCollection<DpiStageViewModel> DpiStages { get; } = [];
    public IReadOnlyList<Option> DpiStageCountOptions { get; private set; } = [];
    public IReadOnlyList<Option> ReportRateOptions { get; private set; } = [];
    public IReadOnlyList<Option> DebounceOptions { get; private set; } = [];
    public IReadOnlyList<Option> LodOptions { get; private set; } = [];

    public IReadOnlyList<Option> SensorModeOptions { get; } = [new(0, "LP · economia"), new(1, "HP · desempenho")];

    public IReadOnlyList<Option> PerformanceOptions { get; } =
        [new(1, "10 s"), new(3, "30 s"), new(6, "1 min"), new(12, "2 min"), new(30, "5 min"), new(60, "10 min"), new(90, "15 min")];

    public IReadOnlyList<Option> SleepOptions { get; } =
        [new(1, "10 segundos"), new(3, "30 segundos"), new(6, "1 minuto"), new(12, "2 minutos"), new(18, "3 minutos"),
         new(30, "5 minutos"), new(60, "10 minutos"), new(90, "15 minutos")];

    public IReadOnlyList<Option> DpiEffectOptions { get; } = [new(0, "Desligado"), new(1, "Fixo"), new(2, "Respirando")];

    public IReadOnlyList<Option> LightModeOptions { get; } =
        [new(0, "Desligado"), new(1, "Arco-íris"), new(2, "Respiração"), new(3, "Cor fixa"), new(4, "Neon"),
         new(5, "Respiração arco-íris"), new(6, "Arco-íris fixo")];

    public int DpiMin => _dev?.Model.SensorInfo.Range.FirstOrDefault()?.Min ?? 50;
    public int DpiMax => _dev?.Model.MaxDpi ?? 26000;

    static readonly (int Type, int Param, string Role, double X, double Y)[] Layout =
    [
        (1, KeyAssignment.LeftClick, "Botão esquerdo", 96, 125),
        (1, KeyAssignment.RightClick, "Botão direito", 204, 125),
        (1, KeyAssignment.MiddleClick, "Roda (clique)", 150, 84),
        (1, KeyAssignment.Forward, "Lateral frontal", 30, 153),
        (1, KeyAssignment.Backward, "Lateral traseiro", 30, 222),
        (2, KeyAssignment.DpiLoop, "Botão de DPI", 150, 163),
    ];

    void BuildStaticLists()
    {
        var m = _dev!.Model;
        Keys.Clear();
        var ordered = m.Keys.OrderBy(k =>
        {
            int pos = Array.FindIndex(Layout, l => l.Type == k.Type && l.Param == k.Param);
            return pos < 0 ? 100 + k.Index : pos;
        }).ToList();
        int n = 1;
        foreach (var k in ordered)
        {
            var l = Array.Find(Layout, x => x.Type == k.Type && x.Param == k.Param);
            Keys.Add(new KeySlotViewModel
            {
                Index = k.Index,
                Number = n++,
                Role = l.Role ?? $"Botão {k.Index + 1}",
                BadgeLeft = (l.Role == null ? 150 : l.X) - 13,
                BadgeTop = (l.Role == null ? 300 + 30 * n : l.Y) - 13,
            });
        }

        DpiStageCountOptions = Enumerable.Range(1, Math.Max(1, m.DpiStages)).Select(i => new Option(i, i.ToString())).ToList();
        ReportRateOptions = new[] { 125, 250, 500, 1000, 2000, 4000, 8000 }
            .Where(r => r <= _dev.MaxReportRate).Select(r => new Option(r, r >= 1000 ? $"{r / 1000}K" : r.ToString())).ToList();
        DebounceOptions = Enumerable.Range(0, m.MaxDebounce + 1).Select(i => new Option(i, $"{i} ms")).ToList();
        LodOptions = m.Sensor switch
        {
            "3950" => [new(3, "0,7 mm"), new(1, "1 mm"), new(2, "2 mm")],
            "3955" => [new(1, "0,7 mm"), new(2, "0,9 mm"), new(3, "1,2 mm"), new(4, "1,4 mm"), new(5, "1,5 mm")],
            _ => [new(1, "1 mm"), new(2, "2 mm")],
        };
    }

    internal void LoadFromDevice()
    {
        if (_dev == null) return;
        var s = _dev.Settings;
        _loading = true;
        try
        {
            SupportsProfiles = _dev.SupportsProfiles;
            Profile = _dev.Profile;
            UpdateBattery();

            foreach (var slot in Keys)
            {
                var a = s.Keys[slot.Index];
                slot.Function = a.Describe();
                var def = _dev.Model.Keys.First(k => k.Index == slot.Index);
                slot.IsCustom = (int)a.Type != def.Type || a.Param != def.Param;
            }
            Debounce = s.DebounceTime;

            DpiStageCount = Math.Clamp(s.MaxDpiStage, 1, Math.Max(1, _dev.Model.DpiStages));
            RebuildStages();
            ReportRate = ReportRateOptions.Any(o => o.Value == s.ReportRate) ? s.ReportRate : ReportRateOptions.Last().Value;
            UpdateSensorModeDisplay();
            Lod = s.Lod;
            Ripple = s.Ripple;
            AngleSnap = s.Angle;
            MotionSync = s.MotionSync;
            PerformanceState = s.PerformanceState;
            PerformanceTime = s.Performance;
            DpiEffectMode = s.DpiEffectOn ? s.DpiEffectMode : 0;
            DpiEffectBrightness = Math.Clamp(s.DpiEffectBrightness, 1, 10);
            DpiEffectSpeed = Math.Clamp(s.DpiEffectSpeed, 1, 5);

            LightMode = s.LightOn ? s.LightMode : 0;
            LightColor = Color.FromRgb(s.LightR, s.LightG, s.LightB);
            LightBrightness = s.LightBrightness;
            LightSpeed = s.LightSpeed;
            MovingOffLight = s.MovingOffLight;

            SleepTime = s.SleepTime;
            LongDistance = _dev.LongDistance == true;
            Macros.RefreshAssignments();
        }
        finally { _loading = false; }
    }

    int _debounce;
    public int Debounce
    {
        get => _debounce;
        set
        {
            if (!Set(ref _debounce, value)) return;
            OnPropertyChanged(nameof(ShowDebounceWarning));
            if (!_loading) _ = Apply(d => d.SetDebounceAsync(value));
        }
    }

    public bool ShowDebounceWarning => _dev != null && Debounce < _dev.Model.TipsDebounce;

    bool KeepsLeftClick(KeySlotViewModel slot, KeyAssignment next)
    {
        if (_dev == null || next.IsLeftClick) return true;
        return Keys.Any(k => k != slot && _dev.Settings.Keys[k.Index].IsLeftClick);
    }

    internal async Task AssignAsync(KeySlotViewModel slot, KeyAssignment a)
    {
        if (!KeepsLeftClick(slot, a)) { ShowToast("Mantenha o clique esquerdo em pelo menos um botão.", true); return; }
        if (await Apply(d => d.SetKeyAsync(slot.Index, a))) LoadFromDevice();
    }

    internal async Task AssignMediaAsync(KeySlotViewModel slot, int usage)
    {
        if (!KeepsLeftClick(slot, new KeyAssignment(KeyFunctionType.ShortcutKey, 0))) { ShowToast("Mantenha o clique esquerdo em pelo menos um botão.", true); return; }
        if (await Apply(d => d.SetMediaKeyAsync(slot.Index, usage))) LoadFromDevice();
    }

    internal async Task AssignComboAsync(KeySlotViewModel slot, IReadOnlyList<ComboKey> keys)
    {
        if (!KeepsLeftClick(slot, new KeyAssignment(KeyFunctionType.ShortcutKey, 0))) { ShowToast("Mantenha o clique esquerdo em pelo menos um botão.", true); return; }
        if (await Apply(d => d.SetComboAsync(slot.Index, keys))) LoadFromDevice();
    }

    internal async Task AssignFireKeyAsync(KeySlotViewModel slot, int times, int interval)
    {
        if (!KeepsLeftClick(slot, new KeyAssignment(KeyFunctionType.FireKey, 0))) { ShowToast("Mantenha o clique esquerdo em pelo menos um botão.", true); return; }
        if (await Apply(d => d.SetFireKeyAsync(slot.Index, times, interval))) LoadFromDevice();
    }

    internal async Task AssignMacroAsync(KeySlotViewModel slot, Macro macro)
    {
        if (!KeepsLeftClick(slot, new KeyAssignment(KeyFunctionType.Macro, 0))) { ShowToast("Mantenha o clique esquerdo em pelo menos um botão.", true); return; }
        if (macro.Events.Count == 0) { ShowToast("Essa macro está vazia.", true); return; }
        if (await Apply(d => d.SetMacroKeyAsync(slot.Index, macro), $"Macro \"{macro.Name}\" atribuída")) LoadFromDevice();
    }

    internal async Task ResetButtonsAsync()
    {
        if (_dev == null) return;
        bool ok = await Apply(async d =>
        {
            foreach (var k in d.Model.Keys)
                await d.SetKeyAsync(k.Index, new KeyAssignment((KeyFunctionType)k.Type, k.Param));
        }, "Botões restaurados");
        if (ok) LoadFromDevice();
    }

    internal KeyAssignment? CurrentAssignment(KeySlotViewModel slot) => _dev?.Settings.Keys[slot.Index];

    int _dpiStageCount = 4;
    public int DpiStageCount
    {
        get => _dpiStageCount;
        set
        {
            if (!Set(ref _dpiStageCount, value) || _loading) return;
            _ = SetStageCountAsync(value);
        }
    }

    async Task SetStageCountAsync(int count)
    {
        if (await Apply(d => d.SetDpiStageCountAsync(count))) LoadFromDevice();
    }

    void RebuildStages()
    {
        var s = _dev!.Settings;
        DpiStages.Clear();
        for (int i = 0; i < DpiStageCount; i++)
        {
            var d = s.Dpis[i];
            DpiStages.Add(new DpiStageViewModel
            {
                Index = i,
                Value = d.X,
                Color = Color.FromRgb(d.R, d.G, d.B),
                IsCurrent = i == s.CurrentDpi,
            });
        }
        var cur = DpiStages.FirstOrDefault(x => x.IsCurrent) ?? DpiStages.FirstOrDefault();
        _selectedStage = cur;
        OnPropertyChanged(nameof(SelectedStage));
        _selectedDpi = cur?.Value ?? 800;
        OnPropertyChanged(nameof(SelectedDpi));
        OnPropertyChanged(nameof(SelectedDpiText));
    }

    DpiStageViewModel? _selectedStage;
    public DpiStageViewModel? SelectedStage => _selectedStage;

    internal async Task SelectStageAsync(DpiStageViewModel stage)
    {
        if (_dev == null) return;
        if (await Apply(d => d.SetCurrentDpiAsync(stage.Index), $"DPI {stage.Number} ativo"))
        {
            foreach (var s in DpiStages) s.IsCurrent = s == stage;
            _selectedStage = stage;
            OnPropertyChanged(nameof(SelectedStage));
            _loading = true;
            SelectedDpi = stage.Value;
            _loading = false;
        }
    }

    int _selectedDpi = 800;
    public int SelectedDpi
    {
        get => _selectedDpi;
        set
        {
            if (_dev == null) { Set(ref _selectedDpi, value); return; }
            int snapped = DpiCodec.Snap(_dev.Model.SensorInfo, value, _dev.Model.MaxDpi);
            if (!Set(ref _selectedDpi, snapped)) return;
            OnPropertyChanged(nameof(SelectedDpiText));
            if (_loading || _selectedStage == null) return;
            var stage = _selectedStage;
            stage.Value = snapped;
            Debounced("dpi", d => d.SetDpiValueAsync(stage.Index, snapped));
        }
    }

    public string SelectedDpiText
    {
        get => _selectedDpi.ToString();
        set
        {
            if (int.TryParse(value.Replace(".", "").Replace(" ", ""), out var v)) SelectedDpi = v;
            OnPropertyChanged();
        }
    }

    internal void StepDpi(int direction)
    {
        if (_dev == null) return;
        int step = DpiCodec.StepAt(_dev.Model.SensorInfo, direction > 0 ? SelectedDpi : SelectedDpi - 1);
        SelectedDpi = SelectedDpi + direction * step;
    }

    internal async Task SetStageColorAsync(DpiStageViewModel stage, Color c)
    {
        stage.Color = c;
        await Apply(d => d.SetDpiColorAsync(stage.Index, c.R, c.G, c.B));
    }

    int _reportRate = 1000;
    public int ReportRate
    {
        get => _reportRate;
        set
        {
            if (!Set(ref _reportRate, value) || _loading) return;
            _ = SetRateAsync(value);
        }
    }

    async Task SetRateAsync(int rate)
    {
        if (await Apply(d => d.SetReportRateAsync(rate), $"Polling rate: {rate} Hz")) UpdateSensorModeDisplay();
    }

    int _sensorMode;
    public int SensorMode
    {
        get => _sensorMode;
        set
        {
            if (!Set(ref _sensorMode, value) || _loading || SensorModeLocked) return;
            _ = Apply(d => d.SetSensorModeAsync(value));
        }
    }

    bool _sensorModeLocked;
    public bool SensorModeLocked
    {
        get => _sensorModeLocked;
        set { if (Set(ref _sensorModeLocked, value)) OnPropertyChanged(nameof(SensorModeEditable)); }
    }

    public bool SensorModeEditable => !_sensorModeLocked;

    string _sensorModeNote = "";
    public string SensorModeNote { get => _sensorModeNote; set => Set(ref _sensorModeNote, value); }

    void UpdateSensorModeDisplay()
    {
        if (_dev == null) return;
        var (display, locked) = _dev.SensorModeDisplay();
        bool was = _loading;
        _loading = true;
        SensorModeLocked = locked;
        SensorMode = display == 256 ? -1 : display;
        SensorModeNote = !locked ? "" :
            display == 256 ? (_dev.IsWired ? "No cabo o sensor sempre opera no modo com fio." : "Em 2K Hz ou mais, o sensor opera sempre no desempenho máximo (modo com fio).")
                           : "Definido automaticamente pela polling rate.";
        _loading = was;
    }

    int _lod = 1;
    public int Lod { get => _lod; set { if (Set(ref _lod, value) && !_loading) _ = Apply(d => d.SetLodAsync(value)); } }

    bool _ripple;
    public bool Ripple { get => _ripple; set { if (Set(ref _ripple, value) && !_loading) _ = Apply(d => d.SetRippleAsync(value)); } }

    bool _angleSnap;
    public bool AngleSnap { get => _angleSnap; set { if (Set(ref _angleSnap, value) && !_loading) _ = Apply(d => d.SetAngleSnapAsync(value)); } }

    bool _motionSync;
    public bool MotionSync { get => _motionSync; set { if (Set(ref _motionSync, value) && !_loading) _ = Apply(d => d.SetMotionSyncAsync(value)); } }

    bool _performanceState;
    public bool PerformanceState { get => _performanceState; set { if (Set(ref _performanceState, value) && !_loading) _ = Apply(d => d.SetPerformanceStateAsync(value)); } }

    int _performanceTime = 6;
    public int PerformanceTime { get => _performanceTime; set { if (Set(ref _performanceTime, value) && !_loading) _ = Apply(d => d.SetPerformanceTimeAsync(value)); } }

    int _dpiEffectMode;
    public int DpiEffectMode
    {
        get => _dpiEffectMode;
        set
        {
            if (!Set(ref _dpiEffectMode, value)) return;
            OnPropertyChanged(nameof(DpiBrightnessEnabled));
            OnPropertyChanged(nameof(DpiSpeedEnabled));
            if (!_loading) _ = Apply(d => d.SetDpiEffectModeAsync(value));
        }
    }

    public bool DpiBrightnessEnabled => DpiEffectMode == 1;
    public bool DpiSpeedEnabled => DpiEffectMode == 2;

    int _dpiEffectBrightness = 5;
    public int DpiEffectBrightness
    {
        get => _dpiEffectBrightness;
        set { if (Set(ref _dpiEffectBrightness, value) && !_loading) Debounced("dpiBright", d => d.SetDpiEffectBrightnessAsync(value)); }
    }

    int _dpiEffectSpeed = 3;
    public int DpiEffectSpeed
    {
        get => _dpiEffectSpeed;
        set { if (Set(ref _dpiEffectSpeed, value) && !_loading) Debounced("dpiSpeed", d => d.SetDpiEffectSpeedAsync(value)); }
    }

    int _lightMode;
    public int LightMode
    {
        get => _lightMode;
        set
        {
            if (!Set(ref _lightMode, value)) return;
            OnPropertyChanged(nameof(LightColorEnabled));
            OnPropertyChanged(nameof(LightBrightnessEnabled));
            OnPropertyChanged(nameof(LightSpeedEnabled));
            if (!_loading) _ = Apply(d => d.SetLightModeAsync(value));
        }
    }

    public bool LightColorEnabled => LightMode is 2 or 3;
    public bool LightBrightnessEnabled => LightMode != 0;
    public bool LightSpeedEnabled => LightMode is not (0 or 3 or 6);

    Color _lightColor = Colors.Red;
    public Color LightColor { get => _lightColor; set => Set(ref _lightColor, value); }

    internal Task SetLightColorAsync(Color c)
    {
        LightColor = c;
        return Apply(d => d.SetLightColorAsync(c.R, c.G, c.B));
    }

    int _lightBrightness;
    public int LightBrightness
    {
        get => _lightBrightness;
        set { if (Set(ref _lightBrightness, value) && !_loading) Debounced("lightBright", d => d.SetLightBrightnessAsync(value)); }
    }

    int _lightSpeed;
    public int LightSpeed
    {
        get => _lightSpeed;
        set { if (Set(ref _lightSpeed, value) && !_loading) Debounced("lightSpeed", d => d.SetLightSpeedAsync(value)); }
    }

    bool _movingOffLight;
    public bool MovingOffLight { get => _movingOffLight; set { if (Set(ref _movingOffLight, value) && !_loading) _ = Apply(d => d.SetMovingOffLightAsync(value)); } }

    int _sleepTime = 6;
    public int SleepTime { get => _sleepTime; set { if (Set(ref _sleepTime, value) && !_loading) _ = Apply(d => d.SetSleepTimeAsync(value)); } }

    bool _longDistance;
    public bool LongDistance
    {
        get => _longDistance;
        set { if (Set(ref _longDistance, value) && !_loading) _ = Apply(d => d.SetLongDistanceAsync(value), value ? "Modo longa distância ativado" : "Modo longa distância desativado"); }
    }

    string _pairStatus = "";
    public string PairStatus { get => _pairStatus; set => Set(ref _pairStatus, value); }
    bool _isPairing;
    public bool IsPairing { get => _isPairing; set => Set(ref _isPairing, value); }

    internal async Task PairAsync()
    {
        if (_dev == null || IsPairing) return;
        IsPairing = true;
        PairStatus = "Pareando… aproxime o mouse do receptor.";
        try
        {
            var progress = new Progress<int>(s => PairStatus = $"Pareando… {s}s restantes");
            bool ok = await _dev.PairAsync(progress);
            PairStatus = ok ? "Pareado com sucesso!" : "Não foi possível parear. Tente novamente.";
            if (ok) { Detach(); _ = ConnectLoopAsync(); }
        }
        catch (Exception ex) { PairStatus = "Erro: " + ex.Message; }
        finally { IsPairing = false; }
    }

    internal async Task RestoreAsync()
    {
        if (await Apply(d => d.RestoreAsync(), "Configurações de fábrica restauradas")) LoadFromDevice();
    }

    sealed class BackupFile
    {
        public string Format { get; set; } = "SpardaControl";
        public int Version { get; set; } = 1;
        public int Cid { get; set; }
        public int Mid { get; set; }
        public string Flash { get; set; } = "";
        public List<Macro> Macros { get; set; } = [];
    }

    internal void Export(string path)
    {
        if (_dev == null) return;
        var file = new BackupFile
        {
            Cid = _dev.Cid,
            Mid = _dev.Mid,
            Flash = Convert.ToBase64String(_dev.SnapshotFlash()),
            Macros = Macros.Items.Select(m => m.Model).ToList(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        ShowToast("Configuração exportada");
    }

    internal async Task ImportAsync(string path)
    {
        if (_dev == null) return;
        BackupFile? file;
        try { file = JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(path)); }
        catch { file = null; }
        if (file == null || file.Format != "SpardaControl") { ShowToast("Arquivo de configuração inválido.", true); return; }
        if (file.Cid != _dev.Cid || file.Mid != _dev.Mid) { ShowToast("Esse arquivo é de outro modelo de mouse.", true); return; }
        var snap = Convert.FromBase64String(file.Flash);
        if (await Apply(d => d.RestoreSnapshotAsync(snap), "Configuração importada"))
        {
            Macros.Merge(file.Macros);
            LoadFromDevice();
        }
    }
}
