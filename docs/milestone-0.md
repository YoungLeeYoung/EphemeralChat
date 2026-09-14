# Milestone 0 — Project Skeleton

## Scope

- WPF solution with five projects and one test project.
- Basic MVVM infrastructure without third-party packages.
- A minimal main window that proves the View -> ViewModel -> Model data flow.
- No WebRTC, networking, signaling, identity, file transfer, or voice.

## Projects and responsibilities

| Project | Type | Responsibility |
| --- | --- | --- |
| `EphemeralChat.App` | WPF (net9.0-windows) | UI layer: WPF views, view models, and commands. References Core, Security, and Network. |
| `EphemeralChat.Core` | Class library (net9.0) | UI-agnostic domain models (e.g. `ChatMessage`). No networking or OS dependencies. |
| `EphemeralChat.Security` | Class library (net9.0) | Future local identity: key generation, storage, Peer ID derivation. Depends only on Core. Empty in Milestone 0. |
| `EphemeralChat.Network` | Class library (net9.0) | Future transport and session orchestration. Depends only on Core. Empty in Milestone 0. |
| `EphemeralChat.Signaling` | Console app (net9.0) | Future standalone signaling server. Signaling plane only; never carries chat data. |
| `EphemeralChat.Tests` | xUnit (net9.0-windows) | Unit tests for Core models and App MVVM primitives. |

## Intended dependency direction

```text
EphemeralChat.App ──> Core, Security, Network
EphemeralChat.Security ──> Core
EphemeralChat.Network ──> Core
EphemeralChat.Signaling (standalone server process)
```

## MVVM notes

- `ViewModelBase` implements `INotifyPropertyChanged`.
- `RelayCommand` implements `ICommand`.
- No MVVM framework is used yet. When the app grows (Milestone 1+), we can
  introduce `CommunityToolkit.Mvvm` if codegen benefits outweigh the dependency.
