using AiPairLauncher.App.Services;
using Xunit;

namespace AiPairLauncher.Tests;

public sealed class NavigationServiceTests
{
    [Fact(DisplayName = "test_navigation_service_tracks_history_and_parameters")]
    public void NavigationServiceTracksHistoryAndParameters()
    {
        var service = new NavigationService();

        service.Navigate(NavigationPageKeys.SessionDetail, "session-1");
        service.Navigate(NavigationPageKeys.Automation, "session-2");

        Assert.True(service.CanGoBack);
        Assert.Equal("session-1", service.GetParameter<string>(NavigationPageKeys.SessionDetail));
        Assert.Equal("session-2", service.GetParameter<string>(NavigationPageKeys.Automation));

        service.GoBack();

        Assert.Equal(NavigationPageKeys.SessionDetail, service.CurrentPageKey);
    }
}
