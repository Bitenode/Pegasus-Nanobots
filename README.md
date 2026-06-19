# [Pegasus Nanobot](https://mod.io/g/spaceengineers/m/nanobot-generator#description)

Server-side Build and Repair System welders for [Space Engineers](https://store.steampowered.com/app/244850/Space_Engineers/), with shared caching, smart repair priority, fleet supply boards, and companion automation blocks for dedicated servers.

**Version:** 1.1.0  
**Download:** [NanoBot Generator on mod.io](https://mod.io/g/spaceengineers/m/nanobot-generator#description)

## Features

- **Large and small Build and Repair System welders** — vanilla-style nanobot blocks with Pegasus script logic
- **Smart repair priority** — fix thrusters, weapons, and reactors before cosmetic hull damage (`RepairMode=FunctionalFirst`)
- **Automatic repair and construction** — scans nearby grids, pulls components from connected inventories, and welds damaged or incomplete blocks
- **Projection support** — can repair blocks on active projectors (`RepairProjections`)
- **Connector-aware inventory** — optional multi-grid component sourcing through connectors (`UseConnectors`, `ConnectorHops`)
- **LCD fleet and supply panels** — bind text panels by `[NB…]` name tag for status, fleet counts, or aggregated missing parts
- **Pegasus Assembler Link** — auto-queues assembler jobs when welders are starved for components
- **Pegasus Dock Doctor** — carrier connector that boosts docked-grid repair (FunctionalFirst + fast scan)
- **Dedicated-server friendly** — logic runs on the server only; **LowPowerMode is ON by default**

## Installation

### mod.io

Subscribe on [mod.io — NanoBot Generator](https://mod.io/g/spaceengineers/m/nanobot-generator#description) and enable the mod in your world or server config.

### Manual / GitHub

1. Clone or download this repository.
2. Copy the mod folder contents (`metadata.mod`, `Data/`, `Models/`, `Sounds/`, `Textures/`) into your Space Engineers mods directory:
   - **Windows:** `%AppData%\SpaceEngineers\Mods\2811581\`
   - **Linux (Proton/Steam):** `~/.steam/steam/steamapps/compatdata/244850/pfx/drive/users/steamuser/AppData/Roaming/SpaceEngineers/Mods/2811581/`
3. Enable the mod in the world or dedicated server configuration.

For a dedicated server, add the mod to `SpaceEngineers-Dedicated.cfg` under `<Mods>`.

## Blocks

| Block | Subtype ID |
|-------|------------|
| Large Build and Repair System | `SELtdLargeNanobotBuildAndRepairSystem` |
| Small Build and Repair System | `SELtdSmallNanobotBuildAndRepairSystem` |
| Large Assembler Link | `PegasusAssemblerLinkLarge` |
| Small Assembler Link | `PegasusAssemblerLinkSmall` |
| Large Dock Doctor | `PegasusDockDoctorLarge` |
| Small Dock Doctor | `PegasusDockDoctorSmall` |

Find welders in the G menu under **Build and Repair System** or the **Pegasus Nanobot** tab. Assembler Link and Dock Doctor appear in the **Pegasus Nanobot** tab.

## Nanobot welder configuration

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
RepairMode=Nearest
PriorityBlocks=
IgnoreBlocks=
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
| `RepairMode` | `Nearest` | `Nearest` or `FunctionalFirst` (critical systems before hull) |
| `PriorityBlocks` | *(empty)* | Comma-separated keywords to repair first (e.g. `Thrust,Weapon,Gyro`) |
| `IgnoreBlocks` | *(empty)* | Comma-separated keywords to skip (e.g. `Armor,Light`) |
| `LcdMode` | `detail` | Default LCD display mode (see below) |
| `WelderId` | `0` | Numeric ID for LCD filtering and fleet grouping |
| `ForceReset` | `false` | Set to `true` once to clear internal state |

**Repair modes:** `Nearest` keeps the original deformed-first, nearest-block behavior. `FunctionalFirst` prioritizes reactors, batteries, thrusters, gyros, weapons, and logistics blocks before armor and lights.

Active welding still runs at full rate when a target is found. Set `LowPowerMode=false` if you prefer the previous always-on scan cadence on small builds.

## LCD panels

Name a **text panel** on the same grid so its **Custom Name** contains `[NB` (case-insensitive). Closing `]` is optional. Examples:

```text
[NB]
[NB:fleet]
[NB:supply]
[NB:alert:supply]
[NB:dock]
[NB:2:compact]
```

| Part | Meaning |
|------|---------|
| First number | `WelderId` filter (`0` = any welder on the grid) |
| Mode token | Display mode override |

**LCD modes:**

| Mode | Shows |
|------|-------|
| `detail` | Full welder status (default) |
| `compact` | Short status + progress bar |
| `stats` | Status + repaired count + queue size |
| `alert` | Text only when this welder is missing components |
| `fleet` | Count of welders by state on the grid (needs a nanobot, assembler link, or dock doctor on grid) |
| `supply` | Aggregated missing components across starved welders |
| `alert-supply` | Supply board only when any welder is starved |
| `dock` | Dock Doctor status (needs a Dock Doctor block on the grid) |

## Pegasus Assembler Link

Place on the same grid as nanobot welders and assemblers. Reads starved welders from the fleet registry and queues missing component blueprints on available assemblers.

Default Custom Data:

```ini
# Pegasus Assembler Link
AutoAssemble=true
MaxQueueItems=5
AllowedComponents=
ScanInterval=30
```

| Key | Default | Description |
|-----|---------|-------------|
| `AutoAssemble` | `true` | Enable automatic queueing |
| `MaxQueueItems` | `5` | Max jobs to queue per scan (1–20) |
| `AllowedComponents` | *(empty)* | Restrict to listed components; empty = all |
| `ScanInterval` | `30` | Updates between scans (10–600) |

## Pegasus Dock Doctor

Replace or supplement a carrier **connector** with a Dock Doctor block. When a ship docks, welders on both the carrier and docked grid receive a temporary repair boost.

Default Custom Data:

```ini
# Pegasus Dock Doctor
Enabled=true
BoostSeconds=120
RepairMode=FunctionalFirst
FastScan=true
```

| Key | Default | Description |
|-----|---------|-------------|
| `Enabled` | `true` | Activate dock boost on connect |
| `BoostSeconds` | `120` | Boost duration in seconds (10–600) |
| `RepairMode` | `FunctionalFirst` | Temporary repair priority for welders |
| `FastScan` | `true` | Faster scan interval during boost |

Add a text panel named `[NB:dock]` on the carrier grid for a live dock status display.

## Performance notes

Designed for ships and stations with multiple nanobots:

- **Shared per-grid caches** — one damaged-block scan and one topology/inventory refresh per grid per tick window, shared across all welders on that grid
- **LowPowerMode (default)** — welders update every 100th frame when idle; scan and LCD refresh intervals are doubled on the slow path
- **Adaptive update rate** — switches to every 10th frame while actively welding

On a dedicated server with many welders on one grid, leave `LowPowerMode=true` unless you need maximum idle responsiveness.

## 
## Repository layout

```text
├── metadata.mod
├── Data/
│   ├── CubeBlocks.sbc
│   └── Scr│       └── PegasusNanobot/
├── Models/
├── Sounds/
└── Textures/
```

## Credits

- Block models, icons, and sounds from the **Nanobot Build and Repair System** mod assets
- Pegasus script implementation, performance optimizations, and maintenance

## License

Add a `LICENSE` file in this repo for the Pegasus script source. Nanobot Build and Repair System models, textures, and sounds remain subject to their original terms.
