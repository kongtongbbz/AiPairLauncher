using System.ComponentModel;
using AiPairLauncher.App.Models;

namespace AiPairLauncher.App.ViewModels;

public sealed class SharedSessionState : INotifyPropertyChanged
{
    private readonly MainWindowViewModel _core;

    public SharedSessionState(MainWindowViewModel core)
    {
        _core = core ?? throw new ArgumentNullException(nameof(core));
        _core.PropertyChanged += CoreOnPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ManagedSessionRecord? SelectedSessionRecord => _core.SelectedSessionRecord;

    public bool HasSelectedSession => _core.HasSelectedSession;

    private void CoreOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MainWindowViewModel.SelectedSessionRecord), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(MainWindowViewModel.HasSelectedSession), StringComparison.Ordinal))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSessionRecord)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedSession)));
        }
    }
}
