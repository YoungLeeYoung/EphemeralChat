using System.Collections.ObjectModel;
using System.Windows.Input;
using EphemeralChat.Core.Models;
using EphemeralChat.Security.Identity;

namespace EphemeralChat.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private string _draftMessage = string.Empty;
    private readonly RelayCommand _sendCommand;
    private LocalIdentity? _localIdentity;

    public MainViewModel()
    {
        _sendCommand = new RelayCommand(Send, _ => !string.IsNullOrWhiteSpace(DraftMessage));
    }

    public string Title => "EphemeralChat — Milestone 0 Skeleton";

    public string ConnectionStatus => "Offline — networking arrives in Milestone 3";

    public string LocalPeerDescription => _localIdentity is null
        ? "No local identity loaded"
        : $"Peer ID: {_localIdentity.PeerId}";

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

    public void SetLocalIdentity(LocalIdentity? identity)
    {
        _localIdentity = identity;
        OnPropertyChanged(nameof(LocalPeerDescription));
    }

    private void Send(object? parameter)
    {
        Messages.Add(new ChatMessage(
            Sender: "Local",
            Content: DraftMessage.Trim(),
            SentAt: DateTimeOffset.Now));

        DraftMessage = string.Empty;
    }
}
