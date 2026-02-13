<p align="center">
  <img src="PoshNodeGraph/Assets/poshblox-banner.svg" alt="PoSHBlox" width="720" />
</p>

<p align="center">
  A visual node-graph editor for building PowerShell scripts. Drag, connect, run.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Avalonia-11.3-7B2BF9?style=flat-square" alt="Avalonia" />
  <img src="https://img.shields.io/badge/FluentAvalonia-2.5-0078D4?style=flat-square" alt="FluentAvalonia" />
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-333?style=flat-square" alt="Platform" />
</p>

---

## What is PoSHBlox?

PoSHBlox lets you visually compose PowerShell scripts by wiring together nodes on a canvas. Each node represents a cmdlet, control-flow block, or custom script fragment. Connections between nodes define the data pipeline. When you're done, export a clean `.ps1` file.

### Features

- **Node palette** with 30+ built-in cmdlet templates across categories (File/Folder, Process/Service, Registry, Network, String/Data, Output)
- **Control flow containers** -- If/Else, ForEach, Try/Catch, While loops, and Functions
- **Live script preview** and one-click Run in a PowerShell window
- **Pipeline-aware code generation** -- chains piped cmdlets, assigns variables at branch points, and detects cycles
- **Dark theme** with a custom node-graph renderer (pan, zoom, Bezier wires)

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build & Run

```bash
git clone https://github.com/yourname/PoSHBlox.git
cd PoSHBlox/PoshNodeGraph
dotnet run
```

## How It Works

1. **Add nodes** from the palette or toolbar
2. **Connect** output ports to input ports by dragging wires
3. **Configure** parameters in the right-side properties panel
4. **Preview** the generated script, then **Run** or **Save** as `.ps1`

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI Framework | [Avalonia UI](https://avaloniaui.net/) 11.3 |
| Theme | [FluentAvalonia](https://github.com/amwx/FluentAvalonia) 2.5 |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) |
| Runtime | .NET 10 |

## License

MIT
