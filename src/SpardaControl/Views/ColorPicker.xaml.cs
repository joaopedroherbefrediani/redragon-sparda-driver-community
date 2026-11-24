using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SpardaControl.Views;

public partial class ColorPicker : UserControl
{
    bool _sync;

    public event Action<Color>? ColorPicked;

    public static readonly Color[] PresetColors =
    [
        Color.FromRgb(255, 0, 0), Color.FromRgb(255, 80, 0), Color.FromRgb(255, 170, 0), Color.FromRgb(255, 255, 0),
        Color.FromRgb(140, 255, 0), Color.FromRgb(0, 255, 0), Color.FromRgb(0, 255, 160), Color.FromRgb(0, 255, 255),
        Color.FromRgb(0, 140, 255), Color.FromRgb(0, 0, 255), Color.FromRgb(140, 0, 255), Color.FromRgb(255, 0, 255),
        Color.FromRgb(255, 0, 120), Color.FromRgb(255, 255, 255), Color.FromRgb(255, 120, 120), Color.FromRgb(120, 200, 255),
        Color.FromRgb(255, 200, 120), Color.FromRgb(180, 120, 255),
    ];

    public ColorPicker()
    {
        InitializeComponent();
        Presets.ItemsSource = PresetColors;
    }

    public Color Current
    {
        get => Color.FromRgb((byte)SR.Value, (byte)SG.Value, (byte)SB.Value);
        set
        {
            _sync = true;
            SR.Value = value.R; SG.Value = value.G; SB.Value = value.B;
            _sync = false;
            UpdatePreview();
        }
    }

    void UpdatePreview()
    {
        var c = Current;
        PreviewSwatch.Background = new SolidColorBrush(c);
        HexBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    void Rgb_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync || HexBox == null) return;
        UpdatePreview();
    }

    void Preset_Click(object sender, RoutedEventArgs e)
    {
        Current = (Color)((FrameworkElement)sender).Tag;
        Pick();
    }

    void Hex_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Hex_Commit(sender, e); Pick(); }
    }

    void Hex_Commit(object sender, RoutedEventArgs e)
    {
        var t = HexBox.Text.Trim().TrimStart('#');
        if (t.Length == 6 && int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            Current = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        else UpdatePreview();
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        Hex_Commit(sender, e);
        Pick();
    }

    void Pick()
    {
        ColorPicked?.Invoke(Current);
        if (Parent is Popup p) p.IsOpen = false;
    }

    public static void Show(FrameworkElement target, Color initial, Action<Color> picked)
    {
        var picker = new ColorPicker { Current = initial };
        var popup = new Popup
        {
            Child = picker, PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false,
            AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = 6,
        };
        picker.ColorPicked += picked;
        popup.IsOpen = true;
    }
}
