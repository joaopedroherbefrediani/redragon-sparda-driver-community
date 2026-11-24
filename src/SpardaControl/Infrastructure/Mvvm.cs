using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace SpardaControl.Infrastructure;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute()) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
}

public sealed record Option(int Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class Debouncer
{
    readonly Dictionary<string, DispatcherTimer> _timers = [];

    public void Run(string key, TimeSpan delay, Action action)
    {
        if (!_timers.TryGetValue(key, out var t))
        {
            t = new DispatcherTimer { Interval = delay };
            _timers[key] = t;
        }
        t.Stop();
        t.Tick -= OnTick;
        t.Tag = action;
        t.Tick += OnTick;
        t.Start();
    }

    static void OnTick(object? sender, EventArgs e)
    {
        var t = (DispatcherTimer)sender!;
        t.Stop();
        (t.Tag as Action)?.Invoke();
    }
}
