using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpardaControl.ViewModels;

namespace SpardaControl.Views;

public partial class DpiPage : UserControl
{
    public DpiPage()
    {
        InitializeComponent();
    }

    MainViewModel Vm => (MainViewModel)DataContext;

    async void Stage_Click(object sender, MouseButtonEventArgs e)
    {
        var stage = (DpiStageViewModel)((FrameworkElement)sender).Tag;
        if (!stage.IsCurrent) await Vm.SelectStageAsync(stage);
    }

    void Color_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var el = (FrameworkElement)sender;
        var stage = (DpiStageViewModel)el.Tag;
        ColorPicker.Show(el, stage.Color, c => _ = Vm.SetStageColorAsync(stage, c));
    }

    void Minus_Click(object sender, RoutedEventArgs e) => Vm.StepDpi(-1);
    void Plus_Click(object sender, RoutedEventArgs e) => Vm.StepDpi(+1);

    void DpiBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Keyboard.ClearFocus();
    }
}
