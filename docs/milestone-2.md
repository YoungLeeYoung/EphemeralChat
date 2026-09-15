# Milestone 2 — Signaling Server

## Scope

Minimal in-memory signaling server plus its client-side transport. Pure
signaling plane only: registration, presence, connect-request / accept /
reject, and opaque offer / answer / ICE forwarding. No chat, file, voice,
or WebRTC functionality.

## Components

| Component | Layer | Responsibility |
| --- | --- | --- |
| `Core.Signaling` (`SignalingMessage`, `SignalingProtocol`, `SignalingJson`) | Core | Shared protocol envelope, message types, and limits |
| `Signaling.Server.*` (`PeerRegistry`, `PendingRequestRegistry`, `SignalingSessions`, `SignalingServer`, `SignalingSweeperService`, `MessageValidator`) | Signaling | Kestrel + WebSocket server, in-memory state, TTL sweeps |
| `SignalingServerHost` | Signaling | Single wiring used by `Program.cs` and tests |
| `Network.Signaling.SignalingClient` | Network | Client transport (used by tests now, by WebRTC in M3) |

## Protocol

One JSON envelope `{ type, from, to, payload }` per WebSocket text frame.
`from` on forwarded messages is always set by the server (clients cannot
spoof it); clients only claim an identity during `register`. Payloads for
`offer` / `answer` / `ice` are opaque JSON, relayed untouched, never stored.

Rules enforced server-side:

- `register` must be the first message (10 s timeout) and carry a syntactically
  valid Peer ID (26 uppercase Base32) plus a public key (base64, 64–1024 bytes).
- Duplicate peer IDs are rejected — a second registration cannot hijack an
  online identity.
- `connect-request` creates exactly one pending request per requester (TTL 30 s,
  expires unanswered, requester gets `connect-timeout`).
- `offer` / `answer` / `ice` are only relayed between peers with an accepted
  session; unknown peers, self-targeting, and unlinked peers are rejected with
  `error` messages.
- 64 KB per-message cap; presence requires a heartbeat (client sends one every
  10 s; server evicts peers silent for 30 s and broadcasts offline).

## TTL / retention policy

- Presence: in-memory, evicted after 30 s of silence.
- Pending connect-requests: in-memory, expired after 30 s.
- Session links: in-memory, removed when either peer disconnects or expires.
- Message payloads: forwarded immediately, zero retention.
- No database, no files, no logs (logging providers are removed entirely).

## Dependencies

None added. The server uses the built-in ASP.NET Core / WebSocket stack via a
`FrameworkReference` (part of the .NET runtime, no NuGet packages).

## Known limitations (tracked for M8)

- Server validates Peer ID syntax only; verifying that a Peer ID is the hash of
  the presented public key, and challenge-response proof of key possession,
  are client-side authentication tasks planned for M3.
- No transport security: plain `ws://`. Production deployments should expose
  `wss://` via TLS termination.
- No rate limiting yet.
