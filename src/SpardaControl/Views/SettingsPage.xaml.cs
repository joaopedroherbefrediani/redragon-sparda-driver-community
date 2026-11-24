using System.Windows;
using System.Windows.Controls;
using SpardaControl.Services;
using SpardaControl.ViewModels;

namespace SpardaControl.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        DataFolderText.Text = "Macros salvas em: " + MacroLibrary.Folder;
    }

    MainViewModel Vm => (MainViewModel)DataContext;

    async void Pair_Click(object sender, RoutedEventArgs e) => await Vm.PairAsync();
}
