using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SpardaControl.Services;
using SpardaControl.ViewModels;

namespace SpardaControl;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;
    bool _closingHandled;

    static readonly Dictionary<string, (string Title, string Subtitle)> Pages = new()
    {
        ["buttons"] = ("Botões", "Escolha a função de cada botão do mouse."),
        ["dpi"] = ("DPI e sensor", "Estágios de DPI, polling rate e ajustes do sensor."),
        ["macros"] = ("Macros", "Grave sequências de teclas e cliques e atribua a um botão."),
        ["light"] = ("Iluminação", "Efeitos de luz do mouse."),
        ["settings"] = ("Configurações", "Energia, conexão e informações do dispositivo."),
    };

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmChanged;
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        Loaded += async (_, _) =>
        {
            await _vm.ConnectLoopAsync();
        };
        PreviewKeyDown += OnPreviewKey;
        PreviewKeyUp += OnPreviewKey;
        UpdateStatus();
    }

    void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (!_vm.Macros.IsRecording) return;
        var code = KeyMapper.ToCode(KeyMapper.RealKey(e));
        if (code != null) _vm.Macros.RecordKey(code, e.IsDown);
        e.Handled = true;
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ToastVisible):
                AnimateToast(_vm.ToastVisible);
                break;
            case nameof(MainViewModel.ToastIsError):
                ToastDot.Fill = (Brush)FindResource(_vm.ToastIsError ? "AccentBrush" : "SuccessBrush");
                break;
            case nameof(MainViewModel.State):
            case nameof(MainViewModel.IsAsleep):
                UpdateStatus();
                break;
            case nameof(MainViewModel.HasLight):
                if (!_vm.HasLight && PageLight.Visibility == Visibility.Visible) NavButtons.IsChecked = true;
                break;
        }
    }

    void UpdateStatus()
    {
        (string text, string brush) = _vm.State switch
        {
            ConnectionState.Ready when _vm.IsAsleep => ("Mouse em repouso", "WarningBrush"),
            ConnectionState.Ready => ("Conectado", "SuccessBrush"),
            ConnectionState.Offline => ("Mouse dormindo", "WarningBrush"),
            ConnectionState.Loading or ConnectionState.Searching => ("Conectando…", "WarningBrush"),
            _ => ("Desconectado", "TextMuteBrush"),
        };
        StatusText.Text = text;
        StatusDot.Fill = (Brush)FindResource(brush);
    }

    void AnimateToast(bool show)
    {
        var dur = TimeSpan.FromMilliseconds(180);
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, dur));
        ToastT.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(show ? 0 : 12, dur) { EasingFunction = new CubicEase() });
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageButtons == null) return;
        var tag = (string)((RadioButton)sender).Tag;
        PageButtons.Visibility = tag == "buttons" ? Visibility.Visible : Visibility.Collapsed;
        PageDpi.Visibility = tag == "dpi" ? Visibility.Visible : Visibility.Collapsed;
        PageMacros.Visibility = tag == "macros" ? Visibility.Visible : Visibility.Collapsed;
        PageLight.Visibility = tag == "light" ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        var (title, subtitle) = Pages[tag];
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
        PageScroll.ScrollToTop();
        if (tag != "macros" && _vm.Macros.IsRecording) _vm.Macros.StopRecord();
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closingHandled) { base.OnClosing(e); return; }
        e.Cancel = true;
        _closingHandled = true;
        Hide();
        await _vm.ShutdownAsync();
        Close();
    }
}
