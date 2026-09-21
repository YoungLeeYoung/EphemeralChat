# EphemeralChat

[![English](https://img.shields.io/badge/📖-English-blue?style=for-the-badge)](#english) [![中文](https://img.shields.io/badge/📖-中文-red?style=for-the-badge)](#中文)

> Privacy-first, peer-to-peer chat for Windows. No accounts, no cloud, no stored messages.

---

## English

**EphemeralChat** is a minimal Windows-to-Windows P2P communication app built with .NET 9, WPF, and WebRTC. Two users must be online at the same time — when the session ends, nothing remains.

### Current Status

| Feature | Status |
|---------|--------|
| WPF UI & MVVM skeleton | ✅ Done |
| Local identity (key pair + Peer ID) | ✅ Done |
| Signaling Server | ✅ Done |
| WebRTC PeerConnection | ✅ Done |
| Text chat via DataChannel | ✅ Done |
| File transfer | 🔜 Planned |
| Voice calls | 🔜 Planned |
| Strict P2P mode | 🔜 Planned |

**problems faced**

- There are still some problems with connecting the server which has a domain name since it is ok to di it with the ipv4 address directly.
- WebRTC section is not fully tested to find the problems caused failed p2p connection in wifi which is not that pure.
- P2P is still under development because this is the key idea of this program.

### Architecture

```
EphemeralChat/
├── src/
│   ├── EphemeralChat.App/          # WPF client (MVVM)
│   ├── EphemeralChat.Core/         # Shared models
│   ├── EphemeralChat.Network/      # P2P protocols
│   ├── EphemeralChat.Security/     # Identity & key management
│   ├── EphemeralChat.Signaling/    # Lightweight signaling server
│   └── EphemeralChat.WebRTC/       # WebRTC abstraction (SIPSorcery)
├── tests/                          # Unit & integration tests
└── docs/                           # Milestone documentation
```

### Getting Started

**Prerequisites:** [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) · Windows 10+

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run the signaling server
dotnet run --project src/EphemeralChat.Signaling

# Run the WPF client
dotnet run --project src/EphemeralChat.App
```

### Privacy Principles

- Messages exist only in memory during an active session
- The signaling server never sees chat content
- Private keys never leave your machine
- No databases, no logs of message content, no offline queues

### Key Dependencies

| Library | Purpose | License |
|---------|---------|---------|
| [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) | WebRTC for .NET | BSD-3-Clause |
| [websocket-sharp](https://github.com/sta/websocket-sharp) | WebSocket client/server | MIT |

---

<div id="中文"></div>

## 中文

**EphemeralChat** 是一个基于 .NET 9 + WPF + WebRTC 的 Windows 点对点隐私通讯应用。双方必须同时在线——会话结束后，不留下任何痕迹。

### 当前进度

| 功能 | 状态 |
|------|------|
| WPF 界面 & MVVM 骨架 | ✅ 已完成 |
| 本地身份（密钥对 + Peer ID） | ✅ 已完成 |
| Signaling 服务器 | ✅ 已完成 |
| WebRTC PeerConnection | ✅ 已完成 |
| DataChannel 文字聊天 | ✅ 已完成 |
| 文件传输 | 🔜 计划中 |
| 语音通话 | 🔜 计划中 |
| 严格 P2P 模式 | 🔜 计划中 |

### 项目结构

```
EphemeralChat/
├── src/
│   ├── EphemeralChat.App/          # WPF 客户端（MVVM）
│   ├── EphemeralChat.Core/         # 共享模型
│   ├── EphemeralChat.Network/      # P2P 协议
│   ├── EphemeralChat.Security/     # 身份与密钥管理
│   ├── EphemeralChat.Signaling/    # 轻量信令服务器
│   └── EphemeralChat.WebRTC/       # WebRTC 封装（SIPSorcery）
├── tests/                          # 单元与集成测试
└── docs/                           # 里程碑文档
```

### 快速开始

**环境要求：** [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) · Windows 10 或更高版本

```bash
# 构建
dotnet build

# 运行测试
dotnet test

# 启动信令服务器
dotnet run --project src/EphemeralChat.Signaling

# 启动 WPF 客户端
dotnet run --project src/EphemeralChat.App
```

### 隐私原则

- 消息仅在会话期间存在于内存中
- 信令服务器永远看不到聊天内容
- 私钥永远不离开本机
- 无数据库、无消息内容日志、无离线队列

### 核心依赖

| 库 | 用途 | 许可证 |
|----|------|--------|
| [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) | .NET WebRTC | BSD-3-Clause |
| [websocket-sharp](https://github.com/sta/websocket-sharp) | WebSocket 通信 | MIT |

---

*EphemeralChat is not affiliated with or endorsed by any third-party library authors. This project is provided as-is, without warranty of any kind.*
