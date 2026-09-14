using EphemeralChat.App.ViewModels;

namespace EphemeralChat.Tests.App;

public class RelayCommandTests
{
    [Fact]
    public void CanExecute_DefaultsToTrue_WhenNoPredicate()
    {
        var command = new RelayCommand(_ => { });

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void CanExecute_UsesProvidedPredicate()
    {
        var called = false;
        var command = new RelayCommand(_ => { }, _ => called);

        Assert.False(command.CanExecute(null));
        called = true;
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void Execute_InvokesAction()
    {
        var executed = false;
        var command = new RelayCommand(_ => executed = true);

        command.Execute(null);

        Assert.True(executed);
    }

    [Fact]
    public void RaiseCanExecuteChanged_RaisesEvent()
    {
        var raised = false;
        var command = new RelayCommand(_ => { });
        command.CanExecuteChanged += (_, _) => raised = true;

        command.RaiseCanExecuteChanged();

        Assert.True(raised);
    }
}
