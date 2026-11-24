using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace SpardaControl;

public partial class App : Application
{
    Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(true, "SpardaControl.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("O Sparda Control já está aberto.", "Sparda Control", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("Erro inesperado: " + args.Exception.Message, "Sparda Control", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("pt-BR")));
        base.OnStartup(e);
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
