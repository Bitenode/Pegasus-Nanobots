# [Pegasus Nanobot](https://mod.io/g/spaceengineers/m/nanobot-generator#description)

Server-side Build and Repair System welders for [Space Engineers](https://store.steampowered.com/app/244850/Space_Engineers/), with shared caching and low-power defaults for large grids and dedicated servers.

**Version:** 1.0.10  
**Download:** [NanoBot Generator on mod.io](https://mod.io/g/spaceengineers/m/nanobot-generator#description)

## Features

- **Large and small Build and Repair System welders** — vanilla-style nanobot blocks with Pegasus script logic
- **Automatic repair and construction** — scans nearby grids, pulls components from connected inventories, and welds damaged or incomplete blocks
- **Projection support** — can repair blocks on active projectors (`RepairProjections`)
- **Connector-aware inventory** — optional multi-grid component sourcing through connectors (`UseConnectors`, `ConnectorHops`)
- **LCD fleet panels** — bind text panels by name tag for status, stats, or fleet summaries
- **Dedicated-server friendly** — logic runs on the server only; **LowPowerMode is ON by default** to reduce sim cost when many welders share one grid

## Installation

### mod.io

Subscribe on [mod.io — NanoBot Generator](https://mod.io/g/spaceengineers/m/nanobot-generator#description) and enable the mod in your world or server config.

### Manual / GitHub

1. Clone or download this repository.
2. Copy the mod folder contents (`metadata.mod`, `Data/`, `Models/`, `Sounds/`, `Textures/`) into your Space Engineers mods directory:
   - **Windows:** `%AppData%\SpaceEngineers\Mods\2811581\`
   - **Linux (Proton/Steam):** `~/.steam/steam/steamapps/compatdata/244850/pfx/drive/users/steamuser/AppData/Roaming/SpaceEngineers/Mods/2811581/`
3. Enable mod **2811581** in the world or dedicated server configuration.

For a dedicated server, add the mod to `SpaceEngineers-Dedicated.cfg` under `<Mods>`.

## Blocks

| Block | Subtype ID |
|-------|------------|
| Large Build and Repair System | `SELtdLargeNanobotBuildAndRepairSystem` |
| Small Build and Repair System | `SELtdSmallNanobotBuildAndRepairSystem |

Find them in the G menu under **Build and Repair System** (large/small welder categories).

## Configuration

Edit the welder block's **Custom Data** terminal panel. Settings above the `---status---` marker are preserved; everything below is updated automatically by the script.

Default header (inserted when Custom Data is empty):

```ini
# Pegasus Nanobot Config (OwnerOnly = same-owner on server)
Range=250
FactionOnly=false
ScanOwnGridOnly=false
UseConnectors=true
ConnectorHops=2
RepairProjections=true
GridsPerScan=5
LowPowerMode=true
MaxScanBuffer=80
ProjectionScanBlocks=8
LcdMode=detail
WelderId=0
```

### Custom Data reference

| Key | Default | Description |
|-----|---------|-------------|
| `Range` | `250` | Scan and weld radius in meters (max 500) |
| `FactionOnly` / `OwnerOnly` | `false` | Restrict targets to same faction or owner |
| `ScanOwnGridOnly` | `false` | Only scan the welder's own grid |
| `UseConnectors` | `true` | Pull components from grids linked via connectors |
| `ConnectorHops` | `2` | Connector traversal depth (0–5) |
| `RepairProjections` | `true` | Repair projected blocks on active projectors |
| `GridsPerScan` | `5` | Grids processed per scan pass (1–20) |
| `LowPowerMode` | `true` | Slower idle scanning and updates when not actively welding |
| `MaxScanBuffer` | `80` | Max damaged blocks queued per scan (20–200) |
| `ProjectionScanBlocks` | `8` | Projector blocks checked per projection scan |
| `LcdMode` | `detail` | Default LCD display mode (see below) |
| `WelderId` | `0` | Numeric ID for LCD filtering and fleet grouping |
| `ForceReset` | `false` | Set to `true` once to clear internal state |

Active welding still runs at full rate when a target is found. Set `LowPowerMode=false` if you prefer the previous always-on scan cadence on small builds.

## LCD panels

Name a **text panel** on the same grid so its **Custom Name** contains `[NB` (case-insensitive). Optional tag format:

```text
[NB]
[NB:fleet]
[NB:2:compact]
[NB:2:stats]
```

| Part | Meaning |
|------|---------|
| First number | `WelderId` filter (`0` = any welder on the grid) |
| Second token | Display mode override |

**LCD modes:** `detail` (default status), `compact`, `stats`, `alert` (shows text only when components are missing), `fleet` (summary of all welders on the grid).

## Performance notes

Designed for ships and stations with multiple nanobots:

- **Shared per-grid caches** — one damaged-block scan and one topology/inventory refresh per grid per tick window, shared across all welders on that grid
- **LowPowerMode (default)** — welders update every 100th frame when idle; scan and LCD refresh intervals are doubled on the slow path
- **Adaptive update rate** — switches to every 10th frame while actively welding

On a dedicated server with many welders on one grid, leave `LowPowerMode=true` unless you need maximum idle responsiveness.

## Repository layout

```text
2811581/
├── metadata.mod          # Mod version
├── Data/
│   ├── CubeBlocks.sbc    # Block definitions
│   └── Scripts/
│       └── PegasusNanobot/   # C# mod scripts (server-side)
├── Models/
├── Sounds/
└── Textures/
```

## Credits

- Block models, icons, and sounds from the **Nanobot Build and Repair System** mod assets
- Pegasus script implementation, performance optimizations, and maintenance

## License

Add a `LICENSE` file in this repo for the Pegasus script source. Nanobot Build and Repair System models, textures, and sounds remain subject to their original terms.
