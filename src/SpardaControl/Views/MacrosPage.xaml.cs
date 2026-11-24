using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SpardaControl.Core.Models;
using SpardaControl.ViewModels;

namespace SpardaControl.Views;

public partial class MacrosPage : UserControl
{
    public MacrosPage()
    {
        InitializeComponent();
    }

    MacrosViewModel Vm => ((MainViewModel)DataContext).Macros;

    void InsertMouse_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        foreach (var (value, name) in KeyCodes.MouseButtons)
        {
            var sub = new MenuItem { Header = name };
            var click = new MenuItem { Header = "Clique (pressionar e soltar)" };
            click.Click += (_, _) => { Vm.InsertMouseClick(value); ScrollToEnd(); };
            var press = new MenuItem { Header = "Só pressionar" };
            press.Click += (_, _) => { Vm.InsertMouseEvent(value, true); ScrollToEnd(); };
            var release = new MenuItem { Header = "Só soltar" };
            release.Click += (_, _) => { Vm.InsertMouseEvent(value, false); ScrollToEnd(); };
            sub.Items.Add(click);
            sub.Items.Add(press);
            sub.Items.Add(release);
            menu.Items.Add(sub);
        }
        menu.IsOpen = true;
    }

    void ScrollToEnd()
    {
        if (EventList.Items.Count > 0) EventList.ScrollIntoView(EventList.Items[^1]);
    }

    void Up_Click(object sender, RoutedEventArgs e) => Vm.MoveEvent((MacroEventViewModel)((FrameworkElement)sender).Tag, -1);
    void Down_Click(object sender, RoutedEventArgs e) => Vm.MoveEvent((MacroEventViewModel)((FrameworkElement)sender).Tag, +1);
}
