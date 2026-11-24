using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SpardaControl.Views;

public sealed record PickerOption(string Label, bool IsChecked, Func<Task> Action);

public sealed record PickerCategory(string Label, bool IsChecked, IReadOnlyList<PickerOption>? Options = null, Func<Task>? Action = null, string? EmptyText = null);

public static class FunctionPicker
{
    static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    static FrameworkElement Row(string text, bool isChecked, bool arrow)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        FrameworkElement? mark = null;
        if (arrow)
            mark = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,0 L4,4 L0,8"), Stroke = Res("TextDimBrush"), StrokeThickness = 1.5,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            };
        else if (isChecked)
            mark = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Res("AccentBrush"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (mark != null)
        {
            Grid.SetColumn(mark, 1);
            g.Children.Add(mark);
        }
        return g;
    }

    public static void Show(FrameworkElement target, IReadOnlyList<PickerCategory> categories)
    {
        var popup = new Popup
        {
            PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false,
            AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = 4,
        };

        var left = new StackPanel { Width = 210 };
        var right = new StackPanel();
        var rightScroll = new ScrollViewer
        {
            Content = right, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var rightHost = new Border
        {
            Width = 230, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 0, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x36)), BorderThickness = new Thickness(1, 0, 0, 0),
            Child = rightScroll,
        };

        void Run(Func<Task> action)
        {
            popup.IsOpen = false;
            _ = action();
        }

        void ShowOptions(PickerCategory c)
        {
            right.Children.Clear();
            if (c.Options == null || c.Options.Count == 0)
            {
                right.Children.Add(new TextBlock
                {
                    Text = c.EmptyText ?? "Nada aqui.", Foreground = Res("TextMuteBrush"), FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 10, 12, 10),
                });
                return;
            }
            foreach (var o in c.Options)
            {
                var b = new Button { Style = (Style)Application.Current.Resources["PickerOption"], Content = Row(o.Label, o.IsChecked, false) };
                b.Click += (_, _) => Run(o.Action);
                right.Children.Add(b);
            }
            rightScroll.ScrollToTop();
        }

        PickerCategory? initial = null;
        foreach (var c in categories)
        {
            if (c.Label == "-")
            {
                left.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x36)), Margin = new Thickness(8, 4, 8, 4) });
                continue;
            }
            bool hasList = c.Action == null;
            if (hasList)
            {
                var rb = new RadioButton
                {
                    Style = (Style)Application.Current.Resources["PickerCategory"], GroupName = "pickerCat" + target.GetHashCode(),
                    Content = Row(c.Label, false, true),
                };
                rb.Checked += (_, _) => ShowOptions(c);
                left.Children.Add(rb);
                if (initial == null && c.IsChecked) { initial = c; rb.IsChecked = true; }
            }
            else
            {
                var b = new Button { Style = (Style)Application.Current.Resources["PickerOption"], Content = Row(c.Label, c.IsChecked, false) };
                b.Click += (_, _) => Run(c.Action!);
                left.Children.Add(b);
            }
        }
        if (initial == null)
        {
            var firstRb = left.Children.OfType<RadioButton>().FirstOrDefault();
            if (firstRb != null) firstRb.IsChecked = true;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(rightHost, 1);
        grid.Children.Add(rightHost);

        popup.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x32, 0x3B)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(6), Margin = new Thickness(0, 0, 16, 16),
            Effect = (System.Windows.Media.Effects.Effect)Application.Current.Resources["CardShadow"],
            Child = grid,
        };
        popup.IsOpen = true;
    }
}
