using EphemeralChat.Core.Signaling;
using EphemeralChat.Signaling.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace EphemeralChat.Signaling;

/// <summary>
/// Assembles the signaling server. Used both by Program.cs and by integration
/// tests, so production and tests always run the exact same wiring.
/// </summary>
public static class SignalingServerHost
{
    public static WebApplication Create(SignalingServerOptions options, string urls)
    {
        var builder = WebApplication.CreateBuilder();

        // Privacy: no request logging of any kind; payloads are never logged.
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(urls);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<PeerRegistry>();
        builder.Services.AddSingleton<PendingRequestRegistry>();
        builder.Services.AddSingleton<SignalingSessions>();
        builder.Services.AddSingleton<SignalingServer>();
        builder.Services.AddHostedService<SignalingSweeperService>();

        var app = builder.Build();
        app.UseWebSockets();
        app.Map(SignalingProtocol.WebSocketPath, HandleWebSocketAsync);
        return app;
    }

    private static async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        await context.RequestServices
            .GetRequiredService<SignalingServer>()
            .HandleConnectionAsync(socket, context.RequestAborted);
    }

    /// <summary>Actual bound port after starting with a port-0 URL.</summary>
    public static int GetBoundPort(WebApplication app)
    {
        string? address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        return address is null ? 0 : new Uri(address).Port;
    }
}
