using System.Windows;
using EphemeralChat.App.ViewModels;

namespace EphemeralChat.App;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var viewModel = new MainViewModel();
        var mainWindow = new Views.MainWindow
        {
            DataContext = viewModel
        };

        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
