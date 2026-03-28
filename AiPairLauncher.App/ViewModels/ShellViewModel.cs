using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiPairLauncher.App.Services;
using AiPairLauncher.App.ViewModels.Pages;

namespace AiPairLauncher.App.ViewModels;

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly NavigationService _navigationService;
    private object _currentPageViewModel;
    private bool _isLogDrawerOpen;

    public ShellViewModel(MainWindowViewModel core, NavigationService navigationService)
    {
        Core = core ?? throw new ArgumentNullException(nameof(core));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        SharedState = new SharedSessionState(core);

        Dashboard = new DashboardPageViewModel(core, SharedState);
        SessionDetail = new SessionDetailPageViewModel(core, SharedState);
        ContextBridge = new ContextBridgePageViewModel(core, SharedState);
        Automation = new AutomationPageViewModel(core, SharedState);
        Settings = new SettingsPageViewModel(core, SharedState);

        NavigationItems =
        [
            new NavigationItemViewModel { PageKey = NavigationPageKeys.Dashboard, Label = "仪表盘" },
            new NavigationItemViewModel { PageKey = NavigationPageKeys.SessionDetail, Label = "会话详情" },
            new NavigationItemViewModel { PageKey = NavigationPageKeys.ContextBridge, Label = "上下文桥接" },
            new NavigationItemViewModel { PageKey = NavigationPageKeys.Automation, Label = "自动编排" },
            new NavigationItemViewModel { PageKey = NavigationPageKeys.Settings, Label = "设置" },
        ];

        _currentPageViewModel = Dashboard;
        _navigationService.PropertyChanged += NavigationServiceOnPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel Core { get; }

    public SharedSessionState SharedState { get; }

    public DashboardPageViewModel Dashboard { get; }

    public SessionDetailPageViewModel SessionDetail { get; }

    public ContextBridgePageViewModel ContextBridge { get; }

    public AutomationPageViewModel Automation { get; }

    public SettingsPageViewModel Settings { get; }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public string CurrentPageKey => _navigationService.CurrentPageKey;

    public object CurrentPageViewModel
    {
        get => _currentPageViewModel;
        private set
        {
            if (ReferenceEquals(_currentPageViewModel, value))
            {
                return;
            }

            _currentPageViewModel = value;
            OnPropertyChanged();
        }
    }

    public bool IsLogDrawerOpen
    {
        get => _isLogDrawerOpen;
        set
        {
            if (_isLogDrawerOpen == value)
            {
                return;
            }

            _isLogDrawerOpen = value;
            OnPropertyChanged();
        }
    }

    public void Navigate(string pageKey, object? parameter = null)
    {
        _navigationService.Navigate(pageKey, parameter);
    }

    public void ToggleLogDrawer()
    {
        IsLogDrawerOpen = !IsLogDrawerOpen;
    }

    private void NavigationServiceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(NavigationService.CurrentPageKey), StringComparison.Ordinal))
        {
            return;
        }

        CurrentPageViewModel = _navigationService.CurrentPageKey switch
        {
            NavigationPageKeys.SessionDetail => SessionDetail,
            NavigationPageKeys.ContextBridge => ContextBridge,
            NavigationPageKeys.Automation => Automation,
            NavigationPageKeys.Settings => Settings,
            _ => Dashboard,
        };

        OnPropertyChanged(nameof(CurrentPageKey));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
