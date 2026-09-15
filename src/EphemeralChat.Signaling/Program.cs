using EphemeralChat.Core.Signaling;
using EphemeralChat.Signaling;
using EphemeralChat.Signaling.Server;
using Microsoft.Extensions.Hosting;

int port = args.Length > 0 && int.TryParse(args[0], out int parsed)
    ? parsed
    : SignalingProtocol.DefaultPort;

using var app = SignalingServerHost.Create(
    new SignalingServerOptions(),
    $"http://+:{port}");

await app.StartAsync();
Console.WriteLine($"EphemeralChat Signaling Server listening on {string.Join(", ", app.Urls)}");
Console.WriteLine("In-memory only: presence TTL, request TTL, no message storage.");
await app.WaitForShutdownAsync();
