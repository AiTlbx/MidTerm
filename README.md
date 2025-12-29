# MiddleManager

[![License: MPL 2.0](https://img.shields.io/badge/License-MPL%202.0-brightgreen.svg)](https://opensource.org/licenses/MPL-2.0)
[![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white)](#installation)
[![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black)](#installation)
[![macOS](https://img.shields.io/badge/macOS-000000?logo=apple&logoColor=white)](#installation)

**A web-based terminal multiplexer.** Access your terminal sessions from any browser.

<!-- TODO: Add screenshot or GIF demo here -->
![MiddleManager Screenshot](docs/screenshot.png)

## Why MiddleManager?

Ever needed to access a terminal on a remote machine but SSH wasn't an option? Or wanted to share a terminal session with a colleague without screen sharing? MiddleManager solves this by serving your terminal through a web interface.

- **Zero dependencies** — Single ~15MB executable, no runtime required
- **Cross-platform** — Native binaries for Windows, Linux, and macOS
- **Instant startup** — Built with .NET Native AOT for sub-second launch
- **Multi-session** — Run multiple terminals, all multiplexed over one WebSocket
- **Any shell** — PowerShell, CMD, Bash, Zsh — whatever's on your system

## Installation

### Download Binary

<!-- TODO: Add actual release links when available -->
Download the latest release for your platform:

| Platform | Download |
|----------|----------|
| Windows x64 | [mm-win-x64.zip](https://github.com/AiTlbx/MiddleManager/releases/latest) |
| Linux x64 | [mm-linux-x64.tar.gz](https://github.com/AiTlbx/MiddleManager/releases/latest) |
| macOS ARM64 | [mm-osx-arm64.tar.gz](https://github.com/AiTlbx/MiddleManager/releases/latest) |
| macOS x64 | [mm-osx-x64.tar.gz](https://github.com/AiTlbx/MiddleManager/releases/latest) |

### Quick Start

```bash
# Windows
mm.exe

# Linux/macOS
chmod +x mm
./mm
```

Open `http://localhost:2000` in your browser.

### Options

```
mm [options]

  --port 2000     Port to listen on (default: 2000)
  --bind 0.0.0.0  Address to bind to (default: 0.0.0.0)
```

## Building from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Clone
git clone https://github.com/AiTlbx/MiddleManager.git
cd MiddleManager

# Build (debug)
dotnet build

# Build AOT binary
cd Ai.Tlbx.MiddleManager.Aot
./build-aot.cmd          # Windows
./build-aot-linux.sh     # Linux
./build-aot-macos.sh     # macOS
```

Output binary will be in `publish/`.

## Configuration

Settings are stored in `~/.middlemanager/settings.json`:

```json
{
  "defaultShell": "Pwsh",
  "defaultCols": 120,
  "defaultRows": 30
}
```

## License

This project is licensed under the [Mozilla Public License 2.0](LICENSE) — you can use it freely, but modifications to MPL-covered files must be shared under the same license.

---

Created by [Johannes Schmidt](https://github.com/AiTlbx)
