using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows;
using EphemeralChat.Core.Models;
using EphemeralChat.Network.P2P;
using EphemeralChat.Network.Signaling;
using EphemeralChat.Security.Identity;

namespace EphemeralChat.App.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private string _draftMessage = string.Empty;
    private string _profileName = "Default";
    private string _connectionStatus = "Signaling: Offline";
    private string _p2pStatus = "Direct P2P: Idle";
    private string _requestStatus = string.Empty;
    private string _targetPeerId = string.Empty;
    private bool _isConnecting;
    private bool _isResponding;
    private readonly RelayCommand _sendCommand;
    private readonly RelayCommand _connectSignalingCommand;
    private readonly RelayCommand _copyPeerIdCommand;
    private readonly RelayCommand _sendConnectRequestCommand;
    private readonly RelayCommand _acceptRequestCommand;
    private readonly RelayCommand _rejectRequestCommand;
    private readonly Uri _signalingUri;
    private readonly Func<SignalingClient, IP2PConnectionManager>? _p2pManagerFactory;
    private readonly SynchronizationContext? _uiContext;
    private LocalIdentity? _localIdentity;
    private SignalingClient? _signalingClient;
    private IP2PConnectionManager? _p2pManager;
    private string? _incomingRequestPeerId;
    private string? _connectedPeerId;

    public MainViewModel(
        Uri? signalingUri = null,
        Func<SignalingClient, IP2PConnectionManager>? p2pManagerFactory = null)
    {
        _uiContext = SynchronizationContext.Current;
        _signalingUri = signalingUri ?? new Uri("ws://localhost:8080/ws");
        _p2pManagerFactory = p2pManagerFactory;
        _sendCommand = new RelayCommand(
            _ => _ = SendTextAsync(),
            _ => CanSendMessage());
        _copyPeerIdCommand = new RelayCommand(
            _ => CopyPeerId(),
            _ => _localIdentity is not null);
        _connectSignalingCommand = new RelayCommand(
            _ => _ = ConnectSignalingAsync(),
            _ => _localIdentity is not null && !_isConnecting && _signalingClient is null);
        _sendConnectRequestCommand = new RelayCommand(
            _ => _ = SendConnectRequestAsync(),
            _ => _signalingClient is not null
                && !_isResponding
                && !string.IsNullOrWhiteSpace(_targetPeerId)
                && _targetPeerId.Trim() != _connectedPeerId
                && _targetPeerId.Trim() != _localIdentity?.PeerId);
        _acceptRequestCommand = new RelayCommand(
            _ => _ = AcceptRequestAsync(),
            _ => CanRespondToIncomingRequest);
        _rejectRequestCommand = new RelayCommand(
            _ => _ = RejectRequestAsync(),
            _ => CanRespondToIncomingRequest);
    }

    public string Title => "EphemeralChat — Milestone 0 Skeleton";

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set
        {
            if (_connectionStatus == value)
            {
                return;
            }

            _connectionStatus = value;
            OnPropertyChanged();
        }
    }

    public string P2pStatus
    {
        get => _p2pStatus;
        private set
        {
            if (_p2pStatus == value)
            {
                return;
            }

            _p2pStatus = value;
            OnPropertyChanged();
        }
    }

    public string LocalPeerDescription => _localIdentity is null
        ? "No local identity loaded"
        : $"Peer ID: {_localIdentity.PeerId}";

    public string ProfileDescription => $"Profile: {_profileName}";

    public string RequestStatus
    {
        get => _requestStatus;
        private set
        {
            if (_requestStatus == value)
            {
                return;
            }

            _requestStatus = value;
            OnPropertyChanged();
        }
    }

    public string TargetPeerId
    {
        get => _targetPeerId;
        set
        {
            if (_targetPeerId == value)
            {
                return;
            }

            _targetPeerId = value;
            OnPropertyChanged();
            _sendConnectRequestCommand.RaiseCanExecuteChanged();
        }
    }

    public string IncomingRequestDescription => string.IsNullOrEmpty(_incomingRequestPeerId)
        ? "No incoming connection request"
        : $"Connection request from {_incomingRequestPeerId}";

    public bool HasIncomingRequest => !string.IsNullOrEmpty(_incomingRequestPeerId);

    public string ConnectedPeerDescription => string.IsNullOrEmpty(_connectedPeerId)
        ? "Not connected to any peer"
        : $"Connected to {_connectedPeerId}";

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

    public ICommand ConnectSignalingCommand => _connectSignalingCommand;

    public ICommand CopyPeerIdCommand => _copyPeerIdCommand;

    public ICommand SendConnectRequestCommand => _sendConnectRequestCommand;

    public ICommand AcceptRequestCommand => _acceptRequestCommand;

    public ICommand RejectRequestCommand => _rejectRequestCommand;

    public void SetLocalIdentity(LocalIdentity? identity, string? profileName = null)
    {
        _localIdentity = identity;
        _profileName = profileName ?? "Default";
        OnPropertyChanged(nameof(ProfileDescription));
        OnPropertyChanged(nameof(LocalPeerDescription));
        _copyPeerIdCommand.RaiseCanExecuteChanged();
        _connectSignalingCommand.RaiseCanExecuteChanged();
    }

    private bool CanRespondToIncomingRequest =>
        _signalingClient is not null && !_isResponding && HasIncomingRequest;

    private async Task ConnectSignalingAsync()
    {
        if (_localIdentity is null || _isConnecting || _signalingClient is not null)
        {
            return;
        }

        _isConnecting = true;
        _connectSignalingCommand.RaiseCanExecuteChanged();
        ConnectionStatus = "Signaling: Connecting…";

        // The signaling client receives only the Peer ID and public key.
        // The private key remains inside the LocalIdentity object.
        var client = new SignalingClient(
            _localIdentity.PeerId,
            Convert.ToBase64String(_localIdentity.ExportPublicKey()));
        _signalingClient = client;
        client.ConnectionRequested += OnConnectionRequested;
        client.RequestResponded += OnRequestResponded;
        client.ErrorReceived += OnErrorReceived;
        client.Disconnected += OnDisconnected;

        try
        {
            await client.ConnectAndRegisterAsync(_signalingUri);
            ConnectionStatus = "Signaling: Connected";

            // The manager owns WebRTC and signaling-plane negotiation. The
            // view model only consumes lifecycle events.
            Func<SignalingClient, IP2PConnectionManager>? managerFactory =
                _p2pManagerFactory;
            if (managerFactory is null)
            {
                managerFactory = WebRtcConnectionManager.Create;
            }

            IP2PConnectionManager manager = managerFactory(client);
            manager.ConnectionStateChanged += OnP2pStateChanged;
            manager.DataChannelOpened += OnP2pDataChannelOpened;
            manager.TextMessageReceived += OnP2pTextMessageReceived;
            manager.OperationFailed += OnP2pOperationFailed;
            _p2pManager = manager;
            _sendCommand.RaiseCanExecuteChanged();
            _sendConnectRequestCommand.RaiseCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            ConnectionStatus = "Signaling: Offline";
        }
        catch (Exception)
        {
            ConnectionStatus = "Signaling: Offline — connection failed";
            await _signalingClient.DisposeAsync();
            _signalingClient = null;
        }
        finally
        {
            _isConnecting = false;
            _connectSignalingCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task SendConnectRequestAsync()
    {
        string? targetId = _targetPeerId?.Trim();
        if (_signalingClient is null || _isResponding || string.IsNullOrEmpty(targetId))
        {
            return;
        }

        if (targetId == _connectedPeerId)
        {
            RequestStatus = "Already connected";
            return;
        }

        if (targetId == _localIdentity?.PeerId)
        {
            RequestStatus = "Cannot connect to yourself";
            return;
        }

        _isResponding = true;
        _sendConnectRequestCommand.RaiseCanExecuteChanged();
        RequestStatus = $"Request sent to {targetId}";

        try
        {
            await _signalingClient.SendConnectRequestAsync(targetId);
        }
        catch (Exception)
        {
            RequestStatus = "Request failed";
        }
        finally
        {
            _isResponding = false;
            _sendConnectRequestCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task AcceptRequestAsync()
    {
        if (_signalingClient is null || !HasIncomingRequest)
        {
            return;
        }

        string requesterId = _incomingRequestPeerId!;
        _isResponding = true;
        RaiseResponseCommandStates();

        try
        {
            if (_p2pManager is { } manager)
            {
                // Prepare before telling the server so an immediately forwarded
                // offer cannot race manager creation.
                await manager.PrepareIncomingAsync(requesterId);
            }

            await _signalingClient.SendAcceptAsync(requesterId);
            RequestStatus = $"Accepted request from {requesterId}";
            _connectedPeerId = requesterId;
            OnPropertyChanged(nameof(ConnectedPeerDescription));
            _sendConnectRequestCommand.RaiseCanExecuteChanged();
            ClearIncomingRequest();
        }
        catch (Exception)
        {
            RequestStatus = "Accept failed";
        }
        finally
        {
            _isResponding = false;
            RaiseResponseCommandStates();
        }
    }

    private async Task RejectRequestAsync()
    {
        if (_signalingClient is null || !HasIncomingRequest)
        {
            return;
        }

        string requesterId = _incomingRequestPeerId!;
        _isResponding = true;
        RaiseResponseCommandStates();

        try
        {
            await _signalingClient.SendRejectAsync(requesterId);
            RequestStatus = $"Rejected request from {requesterId}";
            ClearIncomingRequest();
        }
        catch (Exception)
        {
            RequestStatus = "Reject failed";
        }
        finally
        {
            _isResponding = false;
            RaiseResponseCommandStates();
        }
    }

    private void OnConnectionRequested(string requesterId) => RunOnUiThread(() =>
    {
        _incomingRequestPeerId = requesterId;
        OnPropertyChanged(nameof(IncomingRequestDescription));
        OnPropertyChanged(nameof(HasIncomingRequest));
        RequestStatus = $"Incoming request from {requesterId}";
        RaiseResponseCommandStates();
    });

    private void OnRequestResponded(string fromPeerId, ConnectionRequestOutcome outcome) =>
        RunOnUiThread(() =>
        {
            RequestStatus = outcome switch
            {
                ConnectionRequestOutcome.Accepted => $"Accepted by {fromPeerId}",
                ConnectionRequestOutcome.Rejected => $"Rejected by {fromPeerId}",
                ConnectionRequestOutcome.TimedOut => $"Request to {fromPeerId} timed out",
                _ => "Unknown response"
            };

            if (outcome == ConnectionRequestOutcome.Accepted)
            {
                _connectedPeerId = fromPeerId;
                OnPropertyChanged(nameof(ConnectedPeerDescription));
                _sendConnectRequestCommand.RaiseCanExecuteChanged();
                _ = StartP2POutgoingAsync(fromPeerId);
            }
        });

    private async Task StartP2POutgoingAsync(string remotePeerId)
    {
        if (_p2pManager is not { } manager)
        {
            return;
        }

        try
        {
            await manager.StartOutgoingAsync(remotePeerId);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // WebRtcConnectionManager has already mapped failures to P2pStatus.
        }
    }

    private void OnP2pStateChanged(P2PConnectionState state)
    {
        RunOnUiThread(() =>
        {
            P2pStatus = state switch
            {
                P2PConnectionState.Idle => "Direct P2P: Idle",
                P2PConnectionState.Connecting => "Direct P2P: Connecting…",
                P2PConnectionState.Connected => "Direct P2P: Connected",
                P2PConnectionState.Disconnected => "Direct P2P: Disconnected",
                P2PConnectionState.Failed => "Direct P2P: Failed",
                P2PConnectionState.Closed => "Direct P2P: Closed",
                _ => "Direct P2P: Unknown"
            };

            if (state is P2PConnectionState.Disconnected or
                P2PConnectionState.Failed or
                P2PConnectionState.Closed)
            {
                // Session data is ephemeral. When the P2P session ends, its
                // UI-only message objects are released immediately.
                Messages.Clear();
            }

            _sendCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnP2pDataChannelOpened()
    {
        RunOnUiThread(() =>
        {
            P2pStatus = "Direct P2P: Data channel ready";
            _sendCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnP2pTextMessageReceived(string fromPeerId, P2PTextMessage message)
    {
        RunOnUiThread(() =>
        {
            Messages.Add(new ChatMessage(
                Sender: fromPeerId,
                Content: message.Content,
                SentAt: message.SentAt));
        });
    }

    private void OnP2pOperationFailed(string description)
    {
        RunOnUiThread(() =>
        {
            P2pStatus = $"Direct P2P: {description}";
        });
    }

    private void OnErrorReceived(string error) => RunOnUiThread(() =>
    {
        // Server error codes are translated to stable UI states without
        // exposing message payloads or private identity material.
        RequestStatus = error.Contains("peer-not-online", StringComparison.Ordinal)
            ? "PeerOffline"
            : error.Contains("already-connected", StringComparison.Ordinal)
                ? "Already connected"
                : "Signaling error";
    });

    private void OnDisconnected()
    {
        IP2PConnectionManager? existingManager = _p2pManager;
        bool preserveP2p = existingManager?.State == P2PConnectionState.Connected;

        RunOnUiThread(() =>
        {
            ClearIncomingRequest();
            RequestStatus = string.Empty;
            ConnectionStatus = "Signaling: Offline";
            if (!preserveP2p)
            {
                _connectedPeerId = null;
                OnPropertyChanged(nameof(ConnectedPeerDescription));
                P2pStatus = "Direct P2P: Idle";
            }
        });

        SignalingClient? client = Interlocked.Exchange(ref _signalingClient, null);
        if (client is not null)
        {
            _ = client.DisposeAsync();
        }

        // A completed WebRTC session does not depend on the signaling server.
        // Keep it alive so peers can continue communicating after signaling
        // goes away. Sessions still connecting cannot finish negotiation and
        // are released with the old signaling transport.
        if (!preserveP2p)
        {
            IP2PConnectionManager? manager = Interlocked.Exchange(ref _p2pManager, null);
            if (manager is not null)
            {
                _ = manager.DisposeAsync();
            }
        }

        RunOnUiThread(() =>
        {
            _connectSignalingCommand.RaiseCanExecuteChanged();
            _sendConnectRequestCommand.RaiseCanExecuteChanged();
            RaiseResponseCommandStates();
        });
    }

    private void CopyPeerId()
    {
        if (_localIdentity is not null)
        {
            Clipboard.SetText(_localIdentity.PeerId);
        }
    }

    private void ClearIncomingRequest()
    {
        _incomingRequestPeerId = null;
        OnPropertyChanged(nameof(IncomingRequestDescription));
        OnPropertyChanged(nameof(HasIncomingRequest));
        RaiseResponseCommandStates();
    }

    private void RaiseResponseCommandStates()
    {
        _sendConnectRequestCommand.RaiseCanExecuteChanged();
        _acceptRequestCommand.RaiseCanExecuteChanged();
        _rejectRequestCommand.RaiseCanExecuteChanged();
    }

    private void RunOnUiThread(Action action)
    {
        if (_uiContext is null)
        {
            action();
        }
        else
        {
            _uiContext.Post(_ => action(), null);
        }
    }

    public void Dispose()
    {
        _signalingClient?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _signalingClient = null;
        _p2pManager?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _p2pManager = null;
        Messages.Clear();
    }

    private bool CanSendMessage() =>
        !string.IsNullOrWhiteSpace(DraftMessage) &&
        _p2pManager?.State == P2PConnectionState.Connected;

    private async Task SendTextAsync()
    {
        string content = DraftMessage.Trim();
        IP2PConnectionManager? manager = _p2pManager;
        if (content.Length == 0 || manager is null || manager.State != P2PConnectionState.Connected)
        {
            return;
        }

        try
        {
            P2PTextMessage sent = await manager.SendTextAsync(content);
            RunOnUiThread(() =>
            {
                Messages.Add(new ChatMessage(
                    Sender: "Local",
                    Content: sent.Content,
                    SentAt: sent.SentAt));
                DraftMessage = string.Empty;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            P2pStatus = "Direct P2P: Message not sent";
        }
        finally
        {
            _sendCommand.RaiseCanExecuteChanged();
        }
    }
}
