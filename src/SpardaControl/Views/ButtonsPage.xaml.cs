using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SpardaControl.Core.Models;
using SpardaControl.Core.Protocol;
using SpardaControl.ViewModels;

namespace SpardaControl.Views;

public partial class ButtonsPage : UserControl
{
    public ButtonsPage()
    {
        InitializeComponent();
    }

    MainViewModel Vm => (MainViewModel)DataContext;

    void Key_MouseEnter(object sender, MouseEventArgs e) => ((KeySlotViewModel)((FrameworkElement)sender).Tag).IsHighlighted = true;
    void Key_MouseLeave(object sender, MouseEventArgs e) => ((KeySlotViewModel)((FrameworkElement)sender).Tag).IsHighlighted = false;

    void Key_Click(object sender, RoutedEventArgs e)
    {
        var el = (FrameworkElement)sender;
        OpenMenu(el, (KeySlotViewModel)el.Tag);
    }

    void Badge_Click(object sender, MouseButtonEventArgs e)
    {
        var el = (FrameworkElement)sender;
        OpenMenu(el, (KeySlotViewModel)el.Tag);
    }

    void OpenMenu(FrameworkElement target, KeySlotViewModel slot)
    {
        var current = Vm.CurrentAssignment(slot);
        bool Is(KeyFunctionType t, int p) => current != null && current.Type == t && current.Param == p;
        PickerOption Opt(string label, Func<Task> action, bool isChecked) => new(label, isChecked, action);

        var mouseOpts = new[]
            {
                (KeyAssignment.LeftClick, "Clique esquerdo"), (KeyAssignment.RightClick, "Clique direito"),
                (KeyAssignment.MiddleClick, "Clique da roda"), (KeyAssignment.Backward, "Voltar"), (KeyAssignment.Forward, "Avançar"),
            }
            .Select(x => Opt(x.Item2, () => Vm.AssignAsync(slot, KeyAssignment.Mouse(x.Item1)), Is(KeyFunctionType.MouseKey, x.Item1)))
            .ToList();

        var dpiOpts = new List<PickerOption>
        {
            Opt("Ciclo de DPI", () => Vm.AssignAsync(slot, new(KeyFunctionType.DpiSwitch, KeyAssignment.DpiLoop)), Is(KeyFunctionType.DpiSwitch, KeyAssignment.DpiLoop)),
            Opt("DPI +", () => Vm.AssignAsync(slot, new(KeyFunctionType.DpiSwitch, KeyAssignment.DpiUp)), Is(KeyFunctionType.DpiSwitch, KeyAssignment.DpiUp)),
            Opt("DPI -", () => Vm.AssignAsync(slot, new(KeyFunctionType.DpiSwitch, KeyAssignment.DpiDown)), Is(KeyFunctionType.DpiSwitch, KeyAssignment.DpiDown)),
        };

        var scrollOpts = new List<PickerOption>
        {
            Opt("Rolar para esquerda", () => Vm.AssignAsync(slot, new(KeyFunctionType.LeftRightRoll, KeyAssignment.ScrollLeft)), Is(KeyFunctionType.LeftRightRoll, KeyAssignment.ScrollLeft)),
            Opt("Rolar para direita", () => Vm.AssignAsync(slot, new(KeyFunctionType.LeftRightRoll, KeyAssignment.ScrollRight)), Is(KeyFunctionType.LeftRightRoll, KeyAssignment.ScrollRight)),
        };

        int? currentMedia = current is { Type: KeyFunctionType.ShortcutKey, Combo: { Count: 1 } c } && c[0].Type == HidEventType.Consumer ? c[0].Value : null;
        var mediaOpts = KeyCodes.Media.Select(x => Opt(x.Name, () => Vm.AssignMediaAsync(slot, x.Usage), currentMedia == x.Usage)).ToList();

        var macroOpts = Vm.Macros.Items.Select(m =>
        {
            var model = m.Model;
            return Opt(model.Name, () => Vm.AssignMacroAsync(slot, model), current is { Type: KeyFunctionType.Macro } && current.MacroName == model.Name);
        }).ToList();

        var categories = new List<PickerCategory>
        {
            new("Botões do mouse", mouseOpts.Any(o => o.IsChecked), mouseOpts),
            new("DPI", dpiOpts.Any(o => o.IsChecked), dpiOpts),
            new("Rolagem horizontal", scrollOpts.Any(o => o.IsChecked), scrollOpts),
            new("Multimídia", mediaOpts.Any(o => o.IsChecked), mediaOpts),
            new("Macro", macroOpts.Any(o => o.IsChecked), macroOpts, EmptyText: "Nenhuma macro ainda. Crie uma na aba Macros."),
            new("-", false),
            new("Combo de teclas…", current?.Type == KeyFunctionType.ShortcutKey && currentMedia == null, Action: async () =>
            {
                var keys = DialogWindow.Combo();
                if (keys != null) await Vm.AssignComboAsync(slot, keys);
            }),
            new("Disparo rápido…", current?.Type == KeyFunctionType.FireKey, Action: async () =>
            {
                var cur = current?.Type == KeyFunctionType.FireKey ? current.Param : (30 << 8) | 3;
                var r = DialogWindow.FireKey(cur & 0xFF, cur >> 8);
                if (r != null) await Vm.AssignFireKeyAsync(slot, r.Value.Times, r.Value.Interval);
            }),
            new("Trocar polling rate", current?.Type == KeyFunctionType.ReportRateSwitch,
                Action: () => Vm.AssignAsync(slot, new(KeyFunctionType.ReportRateSwitch, 0))),
            new("-", false),
            new("Desativar botão", current?.Type == KeyFunctionType.Disable,
                Action: () => Vm.AssignAsync(slot, new(KeyFunctionType.Disable, 0))),
        };

        FunctionPicker.Show(target, categories);
    }

    async void ResetButtons_Click(object sender, RoutedEventArgs e)
    {
        if (DialogWindow.Confirm("Restaurar botões", "Todos os botões voltam para a função original de fábrica.", "Restaurar"))
            await Vm.ResetButtonsAsync();
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Exportar configuração",
            Filter = "Configuração Sparda (*.sparda.json)|*.sparda.json",
            FileName = $"sparda-perfil{Vm.Profile + 1}-{DateTime.Now:yyyyMMdd}.sparda.json",
        };
        if (dlg.ShowDialog() == true) Vm.Export(dlg.FileName);
    }

    async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Importar configuração", Filter = "Configuração Sparda (*.sparda.json)|*.sparda.json|Todos os arquivos|*.*" };
        if (dlg.ShowDialog() != true) return;
        if (DialogWindow.Confirm("Importar configuração", $"Substituir a configuração do perfil {Vm.Profile + 1} pela do arquivo?", "Importar"))
            await Vm.ImportAsync(dlg.FileName);
    }

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (DialogWindow.Confirm("Restaurar configurações de fábrica",
                "Botões, DPI, sensor e demais ajustes do perfil atual voltam ao padrão de fábrica. Essa ação não pode ser desfeita.",
                "Restaurar", danger: true))
            await Vm.RestoreAsync();
    }
}
