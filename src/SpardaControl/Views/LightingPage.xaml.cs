using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpardaControl.ViewModels;

namespace SpardaControl.Views;

public partial class LightingPage : UserControl
{
    public LightingPage()
    {
        InitializeComponent();
    }

    MainViewModel Vm => (MainViewModel)DataContext;

    void Color_Click(object sender, MouseButtonEventArgs e)
    {
        var el = (FrameworkElement)sender;
        if (!el.IsEnabled) return;
        ColorPicker.Show(el, Vm.LightColor, c => _ = Vm.SetLightColorAsync(c));
    }
}
