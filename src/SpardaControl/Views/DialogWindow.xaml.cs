using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SpardaControl.Core.Models;
using SpardaControl.Services;

namespace SpardaControl.Views;

public partial class DialogWindow : Window
{
    public Func<bool>? Validate { get; set; }

    public DialogWindow()
    {
        InitializeComponent();
    }

    void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Validate != null && !Validate()) return;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    static DialogWindow Create(string title, string message, FrameworkElement? body = null, string ok = "OK", bool danger = false)
    {
        var w = new DialogWindow { Owner = Application.Current.MainWindow };
        w.TitleText.Text = title;
        w.MessageText.Text = message;
        w.MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        w.BodyHost.Content = body;
        w.BodyHost.Visibility = body == null ? Visibility.Collapsed : Visibility.Visible;
        w.OkButton.Content = ok;
        if (danger) w.OkButton.Background = (Brush)w.FindResource("AccentBrush");
        return w;
    }

    public static bool Confirm(string title, string message, string ok = "Confirmar", bool danger = false) =>
        Create(title, message, null, ok, danger).ShowDialog() == true;

    public static void Info(string title, string message)
    {
        var w = Create(title, message);
        w.CancelButton.Visibility = Visibility.Collapsed;
        w.ShowDialog();
    }

    static TextBox NumberBox(int value) => new() { Text = value.ToString(), Width = 120, HorizontalAlignment = HorizontalAlignment.Left };

    static TextBlock FieldLabel(string text) => new()
    {
        Text = text, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6),
        Foreground = (Brush)Application.Current.Resources["TextDimBrush"],
    };

    public static (int Times, int Interval)? FireKey(int times = 3, int interval = 30)
    {
        var timesBox = NumberBox(times);
        var intervalBox = NumberBox(interval);
        var panel = new StackPanel();
        panel.Children.Add(FieldLabel("Repetições (0 a 3)"));
        panel.Children.Add(timesBox);
        panel.Children.Add(new FrameworkElement { Height = 14 });
        panel.Children.Add(FieldLabel("Intervalo em ms (10 a 255)"));
        panel.Children.Add(intervalBox);
        var w = Create("Disparo rápido",
            "Repete o clique esquerdo várias vezes a cada toque. Com 0 repetições, o disparo continua enquanto o botão estiver pressionado.",
            panel, "Aplicar");
        w.Validate = () =>
            int.TryParse(timesBox.Text, out var t) && t is >= 0 and <= 3 &&
            int.TryParse(intervalBox.Text, out var i) && i is >= 10 and <= 255;
        if (w.ShowDialog() != true) return null;
        return (int.Parse(timesBox.Text), int.Parse(intervalBox.Text));
    }

    public static List<ComboKey>? Combo()
    {
        var keys = new List<ComboKey>();
        var chips = new WrapPanel { MinHeight = 40 };
        var hint = new TextBlock
        {
            Text = "Pressione as teclas aqui…", FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextMuteBrush"], Margin = new Thickness(4, 8, 0, 8),
        };
        chips.Children.Add(hint);
        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x10, 0x14)),
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 4),
            Child = chips,
        };
        var clear = new Button { Content = "Limpar", Style = (Style)Application.Current.Resources["GhostButton"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Focusable = false };
        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(clear);

        void Render()
        {
            chips.Children.Clear();
            if (keys.Count == 0) { chips.Children.Add(hint); return; }
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) chips.Children.Add(new TextBlock { Text = "+", Margin = new Thickness(2, 6, 8, 0), Foreground = (Brush)Application.Current.Resources["TextDimBrush"] });
                chips.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x24, 0x27, 0x2E)), CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 4),
                    Child = new TextBlock { Text = KeyCodes.Describe(keys[i].Type, keys[i].Value), FontWeight = FontWeights.SemiBold, FontSize = 13 },
                });
            }
        }

        clear.Click += (_, _) => { keys.Clear(); Render(); };
        var w = Create("Combo de teclas", "Digite a combinação (até 5 teclas), por exemplo Ctrl + C. O botão do mouse vai enviar essa combinação.", panel, "Aplicar");
        w.OkButton.Focusable = false;
        w.CancelButton.Focusable = false;
        w.CancelButton.IsCancel = false;
        w.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var code = KeyMapper.ToCode(KeyMapper.RealKey(e));
            var k = code == null ? null : KeyCodes.FromCode(code);
            if (k == null || e.IsRepeat || keys.Count >= 5) return;
            var ck = new ComboKey(k.Value.Type, k.Value.Value);
            if (keys.Contains(ck)) return;
            keys.Add(ck);
            Render();
        };
        w.Validate = () => keys.Count > 0;
        return w.ShowDialog() == true ? keys : null;
    }
}
