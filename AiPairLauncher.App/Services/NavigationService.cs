using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AiPairLauncher.App.Services;

public static class NavigationPageKeys
{
    public const string Dashboard = "dashboard";
    public const string SessionDetail = "session-detail";
    public const string ContextBridge = "context-bridge";
    public const string Automation = "automation";
    public const string Settings = "settings";
}

public sealed class NavigationService : INotifyPropertyChanged
{
    private string _currentPageKey = NavigationPageKeys.Dashboard;
    private readonly Stack<string> _history = new();
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.Ordinal);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentPageKey
    {
        get => _currentPageKey;
        private set
        {
            if (string.Equals(_currentPageKey, value, StringComparison.Ordinal))
            {
                return;
            }

            _currentPageKey = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPageKey)));
        }
    }

    public bool CanGoBack => _history.Count > 0;

    public void Navigate(string pageKey, object? parameter = null)
    {
        var normalizedPageKey = pageKey switch
        {
            NavigationPageKeys.SessionDetail => NavigationPageKeys.SessionDetail,
            NavigationPageKeys.ContextBridge => NavigationPageKeys.ContextBridge,
            NavigationPageKeys.Automation => NavigationPageKeys.Automation,
            NavigationPageKeys.Settings => NavigationPageKeys.Settings,
            _ => NavigationPageKeys.Dashboard,
        };

        if (!string.Equals(_currentPageKey, normalizedPageKey, StringComparison.Ordinal))
        {
            _history.Push(_currentPageKey);
        }

        _parameters[normalizedPageKey] = parameter;
        CurrentPageKey = normalizedPageKey;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
    }

    public void GoBack()
    {
        if (_history.Count == 0)
        {
            return;
        }

        CurrentPageKey = _history.Pop();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanGoBack)));
    }

    public T? GetParameter<T>(string pageKey)
    {
        if (!_parameters.TryGetValue(pageKey, out var value) || value is null)
        {
            return default;
        }

        return value is T typedValue ? typedValue : default;
    }
}
