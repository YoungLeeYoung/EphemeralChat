using EphemeralChat.Core.Signaling;
using Microsoft.Extensions.Hosting;

namespace EphemeralChat.Signaling.Server;

/// <summary>
/// Periodic TTL enforcement: evicts silent peers (presence TTL) and expires
/// unanswered connect-requests. Also the only place that closes sockets from
/// outside the peer's own handler.
/// </summary>
public sealed class SignalingSweeperService : BackgroundService
{
    private readonly PeerRegistry _peers;
    private readonly PendingRequestRegistry _pending;
    private readonly SignalingServer _server;
    private readonly SignalingServerOptions _options;
    private readonly TimeProvider _clock;

    public SignalingSweeperService(
        PeerRegistry peers,
        PendingRequestRegistry pending,
        SignalingServer server,
        SignalingServerOptions options,
        TimeProvider clock)
    {
        _peers = peers;
        _pending = pending;
        _server = server;
        _options = options;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_options.SweepInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (PendingRequest request in _pending.RemoveExpired(_clock.GetUtcNow()))
                {
                    await _server.HandleExpiredRequestAsync(request, stoppingToken);
                }

                foreach (PeerConnection peer in _peers.RemoveIdle(_options.PeerIdleTimeout))
                {
                    await _server.HandleIdlePeerAsync(peer, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }
}
