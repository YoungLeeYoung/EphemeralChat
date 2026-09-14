using System.IO;
using System.Windows;
using EphemeralChat.App.ViewModels;
using EphemeralChat.Security.Identity;
using EphemeralChat.Security.Identity.Persistence;

namespace EphemeralChat.App;

public partial class App : Application
{
    private LocalIdentity? _identity;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var store = new DpapiFileIdentityStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EphemeralChat"),
            new DpapiIdentityKeyProtector());
        var service = new LocalIdentityService(store);

        var startup = new IdentityStartupService(service).LoadOrRecover(PromptIdentityRecovery);

        var viewModel = new MainViewModel();
        if (startup.Identity is { } identity)
        {
            _identity = identity;
            viewModel.SetLocalIdentity(identity);
        }

        var mainWindow = new Views.MainWindow
        {
            DataContext = viewModel
        };

        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private IdentityRecoveryChoice PromptIdentityRecovery(string failureDescription)
    {
        MessageBoxResult result = MessageBox.Show(
            failureDescription,
            "EphemeralChat — Identity problem",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes
            ? IdentityRecoveryChoice.Rebuild
            : IdentityRecoveryChoice.Cancel;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _identity?.Dispose();
        base.OnExit(e);
    }
}
