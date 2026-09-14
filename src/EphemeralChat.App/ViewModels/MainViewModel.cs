using System.Collections.ObjectModel;
using System.Windows.Input;
using EphemeralChat.Core.Models;

namespace EphemeralChat.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private string _draftMessage = string.Empty;
    private readonly RelayCommand _sendCommand;

    public MainViewModel()
    {
        _sendCommand = new RelayCommand(Send, _ => !string.IsNullOrWhiteSpace(DraftMessage));
    }

    public string Title => "EphemeralChat — Milestone 0 Skeleton";

    public string ConnectionStatus => "Offline — networking arrives in Milestone 3";

    public string LocalPeerDescription => "Local identity will be generated in Milestone 1";

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public string DraftMessage
    {
        get => _draftMessage;
        set
        {
            if (_draftMessage == value)
            {
                return;
            }

            _draftMessage = value;
            OnPropertyChanged();
            _sendCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand SendCommand => _sendCommand;

    private void Send(object? parameter)
    {
        Messages.Add(new ChatMessage(
            Sender: "Local",
            Content: DraftMessage.Trim(),
            SentAt: DateTimeOffset.Now));

        DraftMessage = string.Empty;
    }
}
