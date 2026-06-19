namespace Pegasus.Nanobot
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    using Sandbox.Common.ObjectBuilders;
    using Sandbox.ModAPI;
    using VRage.Game;
    using VRage.Game.Components;
    using VRage.Game.GUI.TextPanel;
    using VRage.Game.ModAPI;
    using VRage.ModAPI;
    using VRage.ObjectBuilders;
    using VRageMath;

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipWelder), false,
        "SELtdLargeNanobotBuildAndRepairSystem", "SELtdSmallNanobotBuildAndRepairSystem")]
    public class NanobotWelderBlock : MyGameLogicComponent
    {
        public const string Version = "1.1.0";

        private const float WeldIntegrityFractionPerTick = 0.016f;
        private const float MaxBoneMovement = 0.04f;
        private const string LcdNameTag = "[NB";
        private const int MaxQueueSize = 10;
        private const int DamagedCountCap = 50;
        private const int LcdRefreshIntervalUpdates = 180;
        private const int ScanIntervalActive = 3;
        private const int ScanIntervalIdle = 12;
        private const int ScanIntervalIdleEmpty = 24;
        private const int ErrorRecoveryUpdates = 120;
        private const int HealthResetUpdates = 3600;
        private const int ScanIntervalIdleLowPower = 24;
        private const int ScanIntervalIdleEmptyLowPower = 48;
        private const int LcdRefreshIntervalLowPower = 360;
        private const int CustomDataWriteInterval = 6;
        private const int CustomDataWriteIntervalLowPower = 12;
        private const int StuckWarningUpdates = 1800;
        private const int ScanCacheRefreshScans = 6;
        private const int InventoryRefreshScanInterval = 4;
        private const int SupplyRetryInterval = 24;
        private const int StarvedPublishInterval = 18;
        private const int ProjectionScanInterval = 4;
        private const int UpdatesPerSecond = 6;

        private IMyShipWelder _welder;
        private IMyTerminalBlock _terminal;
        private IMyInventory _inventory;
        private readonly NanobotConfig _config = new NanobotConfig();
        private readonly List<IMyInventory> _sources = new List<IMyInventory>();
        private readonly List<LcdBinding> _lcdPanels = new List<LcdBinding>();
        private readonly List<WeldTarget> _queue = new List<WeldTarget>();
        private readonly List<WeldTarget> _scanBuffer = new List<WeldTarget>();
        private readonly List<IMySlimBlock> _gridBlocksBuffer = new List<IMySlimBlock>();
        private readonly List<WeldTarget> _queuePickBuffer = new List<WeldTarget>();
        private readonly List<IMyCubeGrid> _nearbyGrids = new List<IMyCubeGrid>();
        private readonly List<IMyCubeGrid> _sourceGrids = new List<IMyCubeGrid>();
        private readonly List<IMyCubeGrid> _spatialCacheScratch = new List<IMyCubeGrid>();
        private readonly List<IMyCubeGrid> _topologyGrids = new List<IMyCubeGrid>();
        private readonly Dictionary<string, int> _missing = new Dictionary<string, int>();
        private readonly List<IMySlimBlock> _projectedBlocksCache = new List<IMySlimBlock>();
        private readonly StringBuilder _text = new StringBuilder(768);

        private WeldTarget _target;
        private Status _status = Status.Initializing;
        private bool _creative;
        private int _scanCounter;
        private int _scanPhaseOffset;
        private int _lcdRefreshCounter;
        private int _damagedInRange;
        private int _blocksRepaired;
        private int _sourceGridCount;
        private string _lastPublishedText = string.Empty;
        private string _lastParsedConfigSection = string.Empty;
        private string _lastStatusBody = string.Empty;
        private string _lastError = string.Empty;
        private long _homeGridId;
        private int _errorRecoveryCounter;
        private int _healthResetCounter;
        private int _customDataWriteCounter;
        private int _stuckStatusCounter;
        private int _scanGridCursor;
        private int _scanCacheTicks;
        private int _tickStaggerPhase;
        private int _tickStaggerCounter;
        private int _inventoryRefreshCounter;
        private long _lastSourceGridHash;
        private bool _forceInventoryRefresh;
        private bool _partsStarved;
        private int _supplyRetryCounter;
        private int _projectionScanCounter;
        private long _projectionCacheProjectorId;
        private long _projectionCacheGridId;
        private int _projectionCacheIndex;
        private Status _lastPublishedStatus = Status.Initializing;
        private long _lastPublishedTargetKey;
        private bool _lowPowerIdleThrottle;

        private struct LcdBinding
        {
            public IMyTextPanel Panel;
            public string Mode;
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            _welder = Entity as IMyShipWelder;
            _terminal = Entity as IMyTerminalBlock;
            if (_welder == null || _terminal == null)
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            _inventory = _welder.GetInventory(0);
            _scanPhaseOffset = (int)(Entity.EntityId % ScanIntervalIdle);
            _scanCounter = int.MaxValue / 2;
            _healthResetCounter = (int)(Entity.EntityId % HealthResetUpdates);
            _tickStaggerPhase = (int)(Entity.EntityId % 3);
            _homeGridId = _welder.CubeGrid?.EntityId ?? 0;
            _terminal.AppendingCustomInfo += AppendTerminalInfo;

            ReloadConfig();
            NeedsUpdate = _config.LowPowerMode
                ? MyEntityUpdateEnum.EACH_100TH_FRAME
                : MyEntityUpdateEnum.EACH_10TH_FRAME;
            _lastParsedConfigSection = ExtractConfigSection(_terminal.CustomData ?? string.Empty);
            RefreshInventorySourcesIfNeeded(force: true);
            _status = Status.Idle;
            RefreshLcdPanels(force: true);
            PublishStatus(force: true);
            var grid = _welder.CubeGrid;
            if (grid != null)
                NanobotGridLcd.UpdateSharedPanels(grid, NanobotGridLcd.NextTick());
        }

        public override void Close()
        {
            NanobotFleetRegistry.Remove(Entity.EntityId);
            if (_terminal != null)
                _terminal.AppendingCustomInfo -= AppendTerminalInfo;
            base.Close();
        }

        public override void UpdateBeforeSimulation10()
        {
            if (NeedsUpdate != MyEntityUpdateEnum.EACH_10TH_FRAME) return;
            RunUpdateTick();
        }

        public override void UpdateBeforeSimulation100()
        {
            if (NeedsUpdate != MyEntityUpdateEnum.EACH_100TH_FRAME) return;
            RunUpdateTick();
        }

        private void RunUpdateTick()
        {
            if (NeedsUpdate == MyEntityUpdateEnum.NONE)
                NeedsUpdate = MyEntityUpdateEnum.EACH_10TH_FRAME;

            _tickStaggerCounter++;
            var skipUiTick = ShouldSkipStaggeredTick();

            EnsureBlockReferences();
            if (_welder == null || _welder.Closed || _welder.CubeGrid == null) return;
            if (MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer) return;

            UpdateLowPowerIdleThrottle();

            if (_inventory == null)
                _inventory = _welder.GetInventory(0);
            if (_inventory == null) return;

            CheckGridChanged();
            TickErrorRecovery();
            TickHealthReset();
            TickStuckCounter();

            ReloadConfigIfChanged();

            if (!skipUiTick)
            {
                _lcdRefreshCounter++;
                if (_lcdRefreshCounter >= GetEffectiveLcdRefreshInterval())
                {
                    _lcdRefreshCounter = 0;
                    RefreshLcdPanels(force: true);
                    var grid = _welder.CubeGrid;
                    if (grid != null)
                        NanobotGridLcd.UpdateSharedPanels(grid, NanobotGridLcd.NextTick());
                }
            }

            var scanInterval = GetScanInterval();
            _scanCounter++;
            if (_scanCounter >= scanInterval)
            {
                _scanCounter = _scanPhaseOffset;
                try
                {
                    TickScan();
                }
                catch (Exception ex)
                {
                    SetError(ex);
                }
            }

            try
            {
                TickWeld();
            }
            catch (Exception ex)
            {
                SetError(ex);
            }

            ApplyAdaptiveUpdateRate();
        }

        private bool ShouldSkipStaggeredTick()
        {
            if (NeedsUpdate != MyEntityUpdateEnum.EACH_100TH_FRAME) return false;
            if (_status == Status.Welding || _status == Status.MissingComponents) return false;
            if (HasTarget(_target) || _queue.Count > 0 || _damagedInRange > 0) return false;
            return (_tickStaggerCounter % 3) != _tickStaggerPhase;
        }

        private void UpdateLowPowerIdleThrottle()
        {
            if (!_config.LowPowerMode)
            {
                _lowPowerIdleThrottle = false;
                return;
            }

            if (_partsStarved)
            {
                _lowPowerIdleThrottle = true;
                return;
            }

            var wantSlow = !CanOperate()
                || (_status == Status.Idle && _damagedInRange == 0 && !HasTarget(_target));

            if (_status == Status.Welding || HasTarget(_target))
                wantSlow = false;
            if (_status == Status.MissingComponents)
                wantSlow = true;
            if (_damagedInRange > 0 && _status != Status.Off && _status != Status.Damaged && _status != Status.MissingComponents)
                wantSlow = false;

            _lowPowerIdleThrottle = wantSlow;
        }

        private void ApplyAdaptiveUpdateRate()
        {
            if (_partsStarved)
            {
                if (NeedsUpdate != MyEntityUpdateEnum.EACH_100TH_FRAME)
                    NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
                _lowPowerIdleThrottle = true;
                return;
            }

            if (!_config.LowPowerMode)
            {
                if (NeedsUpdate != MyEntityUpdateEnum.EACH_10TH_FRAME)
                    NeedsUpdate = MyEntityUpdateEnum.EACH_10TH_FRAME;
                _lowPowerIdleThrottle = false;
                return;
            }

            var wantSlow = _lowPowerIdleThrottle;
            var desired = wantSlow
                ? MyEntityUpdateEnum.EACH_100TH_FRAME
                : MyEntityUpdateEnum.EACH_10TH_FRAME;

            if (NeedsUpdate != desired)
                NeedsUpdate = desired;
        }

        private int GetEffectiveCustomDataWriteInterval()
        {
            return _lowPowerIdleThrottle ? CustomDataWriteIntervalLowPower : CustomDataWriteInterval;
        }

        private int GetEffectiveLcdRefreshInterval()
        {
            return _lowPowerIdleThrottle ? LcdRefreshIntervalLowPower : LcdRefreshIntervalUpdates;
        }

        private int GetScanInterval()
        {
            NanobotRepairMode boostMode;
            bool fastScan;
            var gridId = _welder.CubeGrid?.EntityId ?? 0;
            if (gridId != 0
                && NanobotDockDoctorRegistry.TryGetBoost(gridId, out boostMode, out fastScan)
                && fastScan)
            {
                if (_partsStarved)
                    return ScanIntervalIdleEmpty;
                if (HasTarget(_target) && _status != Status.MissingComponents) return ScanIntervalActive;
                return ScanIntervalIdle;
            }

            if (_partsStarved)
                return _lowPowerIdleThrottle ? ScanIntervalIdleEmptyLowPower : ScanIntervalIdleEmpty;
            if (HasTarget(_target) && _status != Status.MissingComponents) return ScanIntervalActive;
            if (_damagedInRange == 0 && _status == Status.Idle)
                return _lowPowerIdleThrottle ? ScanIntervalIdleEmptyLowPower : ScanIntervalIdleEmpty;
            return _lowPowerIdleThrottle ? ScanIntervalIdleLowPower : ScanIntervalIdle;
        }

        private void SetError(Exception ex)
        {
            _status = Status.Error;
            _lastError = ex.Message ?? "Unknown error";
            if (_lastError.Length > 120)
                _lastError = _lastError.Substring(0, 120);
            _target = default(WeldTarget);
            _queue.Clear();
            _errorRecoveryCounter = ErrorRecoveryUpdates;
            _stuckStatusCounter = 0;
            PublishStatus(force: true);
        }

        private void EnsureBlockReferences()
        {
            if (_welder == null || _welder.Closed)
                _welder = Entity as IMyShipWelder;
            if (_terminal == null || _terminal.Closed)
                _terminal = Entity as IMyTerminalBlock;
        }

        private void CheckGridChanged()
        {
            var grid = _welder.CubeGrid;
            var gridId = grid?.EntityId ?? 0;
            if (gridId == _homeGridId) return;

            _homeGridId = gridId;
            PerformSoftReset(clearError: true);
        }

        private void PerformSoftReset(bool clearError)
        {
            _inventory = _welder.GetInventory(0);
            _queue.Clear();
            _target = default(WeldTarget);
            _scanBuffer.Clear();
            _nearbyGrids.Clear();
            _scanGridCursor = 0;
            _scanCacheTicks = 0;
            _forceInventoryRefresh = true;
            _partsStarved = false;
            _supplyRetryCounter = 0;
            _projectionScanCounter = 0;
            _projectionCacheProjectorId = 0;
            _projectionCacheGridId = 0;
            _projectionCacheIndex = 0;
            _projectedBlocksCache.Clear();
            RefreshInventorySourcesIfNeeded(force: true);
            RefreshLcdPanels(force: true);

            if (clearError || _status == Status.Error || _status == Status.NoPower)
            {
                _status = Status.Idle;
                _lastError = string.Empty;
                _errorRecoveryCounter = 0;
                _stuckStatusCounter = 0;
            }
        }

        private void TickErrorRecovery()
        {
            if (_status != Status.Error) return;

            _errorRecoveryCounter--;
            if (_errorRecoveryCounter > 0) return;

            PerformSoftReset(clearError: true);
            PublishStatus(force: true);
        }

        private void TickHealthReset()
        {
            _healthResetCounter++;
            if (_healthResetCounter < HealthResetUpdates) return;

            _healthResetCounter = 0;
            NanobotFleetRegistry.PruneStale();
            NanobotDockDoctorRegistry.Tick();
            _forceInventoryRefresh = true;
            RefreshInventorySourcesIfNeeded(force: true);
            PurgeInvalidQueueEntries();
            RefreshLcdPanels(force: true);

            if (_status == Status.Error || _status == Status.NoPower)
            {
                _status = Status.Idle;
                _lastError = string.Empty;
                _errorRecoveryCounter = 0;
                _stuckStatusCounter = 0;
            }
        }

        private void TickStuckCounter()
        {
            if (_status == Status.Error || _status == Status.NoPower)
                _stuckStatusCounter++;
            else
                _stuckStatusCounter = 0;
        }

        private void PurgeInvalidQueueEntries()
        {
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (!IsValidTarget(_queue[i]))
                    _queue.RemoveAt(i);
            }

            if (HasTarget(_target) && !IsValidTarget(_target))
                _target = default(WeldTarget);

            if (!HasTarget(_target) && _queue.Count > 0)
                _target = _queue[0];
        }

        private bool IsWelderPowered()
        {
            if (_creative) return true;
            // Custom script welds for nanobots; vanilla IsWorking stays false when HelpOthers
            // is off and SensorRadius is near zero, so rely on enabled + functional instead.
            return _welder.Enabled && _welder.IsFunctional;
        }

        private void ReloadConfigIfChanged()
        {
            var data = _terminal.CustomData ?? string.Empty;
            var configSection = ExtractConfigSection(data);
            if (configSection == _lastParsedConfigSection) return;

            _lastParsedConfigSection = configSection;
            _config.Parse(data);

            if (_config.ForceReset)
            {
                PerformSoftReset(clearError: true);
                _config.ClearForceResetFlag();
                _lastParsedConfigSection = _config.UserConfigSection;
                PublishStatus(force: true);
            }
        }

        private static string ExtractConfigSection(string data)
        {
            if (string.IsNullOrEmpty(data)) return string.Empty;

            var idx = data.IndexOf(NanobotConfig.StatusMarker, StringComparison.Ordinal);
            if (idx > 0) return data.Substring(0, idx).TrimEnd();

            idx = data.IndexOf("=== Pegasus Nanobot", StringComparison.Ordinal);
            if (idx > 0) return data.Substring(0, idx).TrimEnd();

            return data;
        }

        private void ReloadConfig()
        {
            _config.Parse(_terminal.CustomData ?? string.Empty);
        }

        private void RefreshInventorySourcesIfNeeded(bool force = false)
        {
            _inventoryRefreshCounter++;

            if (!force && !_forceInventoryRefresh)
            {
                if (_inventoryRefreshCounter < InventoryRefreshScanInterval
                    && _status != Status.MissingComponents)
                {
                    var builderId = GetBuilderId();
                    var topologyHash = InventoryHelper.ComputeConnectorGridHash(
                        _welder,
                        _config.UseConnectors ? _config.ConnectorHops : 0,
                        _sourceGrids,
                        _gridBlocksBuffer);
                    if (topologyHash == _lastSourceGridHash
                        && NanobotTopologyCache.TryGetInventorySources(
                            _welder,
                            _config.ConnectorHops,
                            _config.UseConnectors,
                            _config.FactionOnly,
                            builderId,
                            topologyHash,
                            _sources,
                            out _sourceGridCount))
                    {
                        return;
                    }
                }
            }

            RefreshInventorySources();
            _lastSourceGridHash = ComputeSourceGridHash(_sourceGrids);
            _inventoryRefreshCounter = 0;
            _forceInventoryRefresh = false;
        }

        private static long ComputeSourceGridHash(List<IMyCubeGrid> grids)
        {
            long hash = 0;
            if (grids == null) return hash;

            for (int i = 0; i < grids.Count; i++)
            {
                var grid = grids[i];
                if (grid == null) continue;
                hash ^= grid.EntityId;
            }

            return hash;
        }

        private void RefreshInventorySources()
        {
            var builderId = GetBuilderId();
            var topologyHash = InventoryHelper.ComputeConnectorGridHash(
                _welder,
                _config.UseConnectors ? _config.ConnectorHops : 0,
                _sourceGrids,
                _gridBlocksBuffer);

            if (!_forceInventoryRefresh
                && topologyHash == _lastSourceGridHash
                && NanobotTopologyCache.TryGetInventorySources(
                    _welder,
                    _config.ConnectorHops,
                    _config.UseConnectors,
                    _config.FactionOnly,
                    builderId,
                    topologyHash,
                    _sources,
                    out _sourceGridCount))
            {
                return;
            }

            InventoryHelper.RefreshSources(
                _welder,
                _sources,
                _config.UseConnectors,
                _config.ConnectorHops,
                builderId,
                _config.FactionOnly,
                _sourceGrids,
                _gridBlocksBuffer,
                out _sourceGridCount);

            NanobotTopologyCache.StoreInventorySources(
                _welder,
                _config.ConnectorHops,
                _config.UseConnectors,
                _config.FactionOnly,
                builderId,
                topologyHash,
                _sources,
                _sourceGridCount);
        }

        private void TickScan()
        {
            if (_partsStarved)
                return;

            RefreshInventorySourcesIfNeeded();

            if (!CanOperate())
            {
                _queue.Clear();
                _target = default(WeldTarget);
                PublishStatus(force: true);
                return;
            }

            _creative = MyAPIGateway.Session.CreativeMode;
            if (!_creative && !IsWelderPowered())
            {
                _status = Status.NoPower;
                _queue.Clear();
                _target = default(WeldTarget);
                PublishStatus(force: true);
                return;
            }

            if (HasTarget(_target) && IsValidTarget(_target) && TargetNeedsRepair(_target))
            {
                if (!_target.IsProjection)
                    RunAreaScan(fillQueue: false);
                PublishStatus(force: true);
                return;
            }

            RunAreaScan(fillQueue: true);
            _target = _queue.Count > 0 ? _queue[0] : default(WeldTarget);
            _status = HasTarget(_target) ? Status.Welding : Status.Idle;
            PublishStatus(force: true);
        }

        private void TickWeld()
        {
            _creative = MyAPIGateway.Session.CreativeMode;
            UpdateStatus();

            if (!CanOperate())
            {
                PublishStatus();
                return;
            }

            if (!_creative && !IsWelderPowered())
            {
                _status = Status.NoPower;
                PublishStatus();
                return;
            }

            if (!HasTarget(_target) || !IsValidTarget(_target))
            {
                _partsStarved = false;
                _supplyRetryCounter = 0;
                AdvanceQueue();
                if (!HasTarget(_target))
                {
                    if (_status == Status.Welding || _status == Status.MissingComponents)
                        _status = Status.Idle;
                    PublishStatus();
                    return;
                }
            }

            IMySlimBlock weldBlock;
            if (!ResolveWeldBlock(out weldBlock))
            {
                AdvanceQueue();
                _status = HasTarget(_target) ? Status.MissingComponents : Status.Idle;
                PublishStatus();
                return;
            }

            var targetGrid = weldBlock.CubeGrid;
            if (targetGrid == null || targetGrid.Closed
                || (!_config.ScanUnloadedGrids && targetGrid.Physics == null))
            {
                AdvanceQueue();
                _status = HasTarget(_target) ? Status.Welding : Status.Idle;
                PublishStatus();
                return;
            }

            TrySupplyComponents(weldBlock, updateStockpile: true);

            if (!_creative && !InventoryHelper.CanWeldTarget(weldBlock, _inventory, _creative, _missing))
            {
                var partsOnLine = InventoryHelper.HasConnectedComponentsAvailable(
                    weldBlock, _inventory, _sources, _missing);
                _partsStarved = !partsOnLine;

                if (_partsStarved)
                {
                    _supplyRetryCounter++;
                    if (_supplyRetryCounter < SupplyRetryInterval)
                    {
                        _status = Status.MissingComponents;
                        PublishStatus(throttle: true);
                        return;
                    }

                    _supplyRetryCounter = 0;
                    RefreshInventorySourcesIfNeeded(force: true);
                    TrySupplyComponents(weldBlock, updateStockpile: false);
                }
                else
                {
                    RefreshInventorySourcesIfNeeded(force: true);
                    TrySupplyComponents(weldBlock, updateStockpile: true);
                }

                if (!InventoryHelper.CanWeldTarget(weldBlock, _inventory, _creative, _missing))
                {
                    _status = Status.MissingComponents;
                    PublishStatus(throttle: _partsStarved);
                    return;
                }
            }

            _partsStarved = false;
            _supplyRetryCounter = 0;

            var speed = MyAPIGateway.Session.WelderSpeedMultiplier * _config.WeldSpeed;
            var weldAmount = weldBlock.MaxIntegrity * WeldIntegrityFractionPerTick * speed;
            var remaining = weldBlock.MaxIntegrity - weldBlock.Integrity;
            if (remaining > 0f && weldAmount > remaining)
                weldAmount = remaining;

            if (weldAmount > 0f && _inventory != null)
            {
                weldBlock.IncreaseMountLevel(
                    weldAmount,
                    GetBuilderId(),
                    _inventory,
                    speed * MaxBoneMovement,
                    false);
            }

            if (!TargetNeedsRepair(_target))
            {
                _blocksRepaired++;
                AdvanceQueue();
            }

            _status = HasTarget(_target) ? Status.Welding : Status.Idle;
            PublishStatus();
        }

        private void AdvanceQueue()
        {
            if (_queue.Count > 0)
                _queue.RemoveAt(0);
            _target = _queue.Count > 0 ? _queue[0] : default(WeldTarget);
        }

        private void RebuildNearbyGridsCache()
        {
            _nearbyGrids.Clear();

            var welderPos = _welder.GetPosition();
            NanobotSpatialCache.GetNearbyGrids(welderPos, _config.Range, _spatialCacheScratch);

            for (int i = 0; i < _spatialCacheScratch.Count; i++)
            {
                var grid = _spatialCacheScratch[i];
                if (!IsGridAllowed(grid)) continue;
                if (ContainsGrid(_nearbyGrids, grid)) continue;
                _nearbyGrids.Add(grid);
            }

            AddLinkedGridsToScan();
        }

        private void AddLinkedGridsToScan()
        {
            var homeGrid = _welder.CubeGrid;
            if (homeGrid == null) return;

            var hops = _config.ConnectorHops > 0 ? _config.ConnectorHops : NanobotConfig.DefaultConnectorHops;
            var builderId = GetBuilderId();

            if (NanobotTopologyCache.TryGetLinkedGrids(
                _welder,
                hops,
                true,
                _config.FactionOnly,
                builderId,
                _topologyGrids))
            {
                for (int i = 0; i < _topologyGrids.Count; i++)
                {
                    var grid = _topologyGrids[i];
                    if (grid == null || grid.Closed) continue;
                    if (!IsGridAllowed(grid)) continue;
                    if (ContainsGrid(_nearbyGrids, grid)) continue;
                    _nearbyGrids.Add(grid);
                }

                return;
            }

            _topologyGrids.Clear();
            _topologyGrids.Add(homeGrid);
            InventoryHelper.CollectLinkedGrids(_topologyGrids, hops, _gridBlocksBuffer);

            NanobotTopologyCache.StoreLinkedGrids(
                _welder,
                hops,
                true,
                _config.FactionOnly,
                builderId,
                _topologyGrids);

            for (int i = 0; i < _topologyGrids.Count; i++)
            {
                var grid = _topologyGrids[i];
                if (grid == null || grid.Closed) continue;
                if (!IsGridAllowed(grid)) continue;
                if (ContainsGrid(_nearbyGrids, grid)) continue;
                _nearbyGrids.Add(grid);
            }
        }

        private void RunAreaScan(bool fillQueue)
        {
            if (_scanCacheTicks <= 0 || _nearbyGrids.Count == 0)
            {
                RebuildNearbyGridsCache();
                _scanGridCursor = 0;
                _scanCacheTicks = ScanCacheRefreshScans;
                _scanBuffer.Clear();
                _damagedInRange = 0;
            }

            _scanCacheTicks--;

            if (_nearbyGrids.Count == 0)
            {
                if (fillQueue)
                    _queue.Clear();
                return;
            }

            var welderPos = _welder.GetPosition();
            var rangeSq = (double)_config.Range * _config.Range;
            var gridsThisTick = _config.GridsPerScan;
            var processed = 0;

            while (processed < gridsThisTick && _nearbyGrids.Count > 0)
            {
                if (_scanGridCursor >= _nearbyGrids.Count)
                    _scanGridCursor = 0;

                ScanGridBlocks(_nearbyGrids[_scanGridCursor], welderPos, rangeSq);
                _scanGridCursor++;
                processed++;
            }

            if (fillQueue && _scanBuffer.Count > 0)
                FinalizeScanQueue();
        }

        private void ScanGridBlocks(IMyCubeGrid grid, Vector3D welderPos, double rangeSq)
        {
            if (grid == null || grid.Closed) return;

            NanobotGridBlockCache.CollectDamagedInRange(
                grid,
                welderPos,
                rangeSq,
                _welder,
                _config.MaxScanBuffer,
                DamagedCountCap,
                ref _damagedInRange,
                _scanBuffer,
                _config.LowPowerMode,
                ShouldIncludeBlock);

            if (_config.RepairProjections && ShouldScanProjections())
            {
                NanobotProjectionHelper.ScanProjectorsOnGrid(
                    grid,
                    welderPos,
                    rangeSq,
                    _scanBuffer,
                    _gridBlocksBuffer,
                    _projectedBlocksCache,
                    ref _projectionCacheProjectorId,
                    ref _projectionCacheGridId,
                    ref _projectionCacheIndex,
                    _config.MaxScanBuffer,
                    _config.ProjectionScanBlocks);
            }
        }

        private bool ShouldScanProjections()
        {
            if (HasTarget(_target) && _target.IsProjection)
                return false;

            _projectionScanCounter++;
            return (_projectionScanCounter % ProjectionScanInterval) == 0;
        }

        private void FinalizeScanQueue()
        {
            _queuePickBuffer.Clear();
            for (int i = 0; i < _scanBuffer.Count; i++)
                _queuePickBuffer.Add(_scanBuffer[i]);

            _queue.Clear();

            for (int n = 0; n < MaxQueueSize && _queuePickBuffer.Count > 0; n++)
            {
                var bestIdx = 0;
                for (int i = 1; i < _queuePickBuffer.Count; i++)
                {
                    if (CompareRepairPriority(_queuePickBuffer[i], _queuePickBuffer[bestIdx]) < 0)
                        bestIdx = i;
                }

                _queue.Add(_queuePickBuffer[bestIdx]);
                _queuePickBuffer.RemoveAt(bestIdx);
            }
        }

        private bool ShouldIncludeBlock(IMySlimBlock block)
        {
            if (block == null) return false;
            return !NanobotRepairPriority.IsIgnored(block, _config.IgnoreBlockKeywords);
        }

        private NanobotRepairMode GetEffectiveRepairMode()
        {
            var gridId = _welder.CubeGrid?.EntityId ?? 0;
            NanobotRepairMode boostMode;
            bool fastScan;
            if (gridId != 0 && NanobotDockDoctorRegistry.TryGetBoost(gridId, out boostMode, out fastScan))
                return boostMode;

            return _config.RepairMode;
        }

        private int CompareRepairPriority(WeldTarget a, WeldTarget b)
        {
            if (!HasTarget(a) && !HasTarget(b)) return 0;
            if (!HasTarget(a)) return 1;
            if (!HasTarget(b)) return -1;

            var aBlock = a.Block ?? a.Projected;
            var bBlock = b.Block ?? b.Projected;

            var repairMode = GetEffectiveRepairMode();
            var aScore = NanobotRepairPriority.GetPriorityScore(aBlock, repairMode, _config.PriorityBlockKeywords);
            var bScore = NanobotRepairPriority.GetPriorityScore(bBlock, repairMode, _config.PriorityBlockKeywords);
            if (aScore != bScore) return aScore.CompareTo(bScore);

            var aDef = aBlock != null && aBlock.HasDeformation ? 0 : 1;
            var bDef = bBlock != null && bBlock.HasDeformation ? 0 : 1;
            if (aDef != bDef) return aDef.CompareTo(bDef);

            var welderPos = _welder.GetPosition();
            Vector3D aPos;
            Vector3D bPos;
            NanobotProjectionHelper.GetWorldPosition(a, out aPos);
            NanobotProjectionHelper.GetWorldPosition(b, out bPos);
            return Vector3D.DistanceSquared(aPos, welderPos).CompareTo(Vector3D.DistanceSquared(bPos, welderPos));
        }

        private bool IsGridAllowed(IMyCubeGrid grid)
        {
            if (grid == null || grid.Closed || grid.MarkedForClose) return false;
            if (!_config.ScanUnloadedGrids && grid.Physics == null) return false;
            if (_config.ScanOwnGridOnly && grid != _welder.CubeGrid) return false;

            if (!_config.FactionOnly)
                return true;

            var builderId = GetBuilderId();
            if (builderId == 0) return grid == _welder.CubeGrid;
            if (grid == _welder.CubeGrid) return true;

            var owners = grid.BigOwners;
            if (owners == null || owners.Count == 0) return false;

            for (int i = 0; i < owners.Count; i++)
            {
                if (owners[i] == builderId) return true;
            }

            return false;
        }

        private static bool ContainsGrid(List<IMyCubeGrid> grids, IMyCubeGrid grid)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                if (grids[i] == grid) return true;
            }

            return false;
        }

        private void PublishStatus(bool force = false, bool throttle = false)
        {
            if (throttle && !force)
            {
                _customDataWriteCounter++;
                if (_customDataWriteCounter < StarvedPublishInterval)
                    return;
            }

            BuildStatusText();
            var statusBody = _text.ToString();
            _lastStatusBody = statusBody;

            var targetKey = NanobotProjectionHelper.GetTargetKey(_target);
            var statusChanged = _status != _lastPublishedStatus || targetKey != _lastPublishedTargetKey;
            _lastPublishedStatus = _status;
            _lastPublishedTargetKey = targetKey;

            UpdateFleetRegistry();

            _customDataWriteCounter++;
            var writeCustomData = force || statusChanged || _customDataWriteCounter >= GetEffectiveCustomDataWriteInterval();
            if (writeCustomData)
            {
                _customDataWriteCounter = 0;
                var output = _config.FormatCustomData(statusBody);

                if (force || output != _lastPublishedText)
                {
                    _lastPublishedText = output;
                    _terminal.CustomData = output;
                    _terminal.RefreshCustomInfo();
                }
            }

            WriteLcdPanels(statusBody, force || statusChanged);
        }

        private void UpdateFleetRegistry()
        {
            var gridId = _welder.CubeGrid?.EntityId ?? 0;
            NanobotFleetRegistry.Update(
                Entity.EntityId,
                gridId,
                _config.WelderId,
                ToFleetStatusCode(_status),
                BuildMissingPartsSummary());
        }

        private string BuildMissingPartsSummary()
        {
            if (_status != Status.MissingComponents)
                return string.Empty;

            _missing.Clear();
            if (HasTarget(_target))
            {
                var weldBlock = _target.Block ?? _target.Projected;
                if (weldBlock != null)
                    weldBlock.GetMissingComponents(_missing);
            }

            if (_missing.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(128);
            var first = true;
            foreach (var entry in _missing)
            {
                if (entry.Value <= 0) continue;
                if (!first) sb.Append(';');
                sb.Append(entry.Key).Append(':').Append(entry.Value);
                first = false;
            }

            return sb.ToString();
        }

        private static int ToFleetStatusCode(Status status)
        {
            switch (status)
            {
                case Status.Welding: return 1;
                case Status.MissingComponents: return 2;
                case Status.Error: return 3;
                case Status.Off: return 4;
                case Status.NoPower: return 5;
                case Status.Damaged: return 6;
                default: return 0;
            }
        }

        private void WriteLcdPanels(string statusBody, bool forceWrite)
        {
            for (int i = 0; i < _lcdPanels.Count; i++)
            {
                var binding = _lcdPanels[i];
                var panel = binding.Panel;
                if (panel == null || panel.Closed) continue;

                var lcdText = BuildLcdText(binding.Mode, statusBody);

                if (panel.ContentType != ContentType.TEXT_AND_IMAGE)
                    panel.ContentType = ContentType.TEXT_AND_IMAGE;

                panel.WriteText(lcdText, append: false);
            }
        }

        private string BuildLcdText(string panelMode, string statusBody)
        {
            var mode = string.IsNullOrEmpty(panelMode) ? _config.LcdMode : panelMode;
            if (mode == "compact")
                return BuildCompactStatus();
            if (mode == "stats")
                return statusBody + "\nRepaired: " + _blocksRepaired + "\nQueue: " + _queue.Count;
            if (mode == "alert")
                return _status == Status.MissingComponents ? statusBody : string.Empty;

            return statusBody;
        }

        private string BuildCompactStatus()
        {
            var sb = new StringBuilder(256);
            sb.Append("Nanobot v").Append(Version).Append('\n');
            sb.Append(GetStatusLabel()).Append('\n');
            AppendRecoveryCountdown(sb);
            if (HasTarget(_target))
            {
                sb.Append(NanobotProjectionHelper.GetDisplayName(_target)).Append('\n');
                var pct = NanobotProjectionHelper.GetIntegrityPercent(_target);
                sb.Append(pct).Append("% ");
                AppendProgressBar(sb, pct);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private void AppendTerminalInfo(IMyTerminalBlock block, StringBuilder details)
        {
            var marker = NanobotConfig.StatusMarker;
            var published = _lastPublishedText;
            var idx = published.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var start = idx + marker.Length;
                while (start < published.Length && (published[start] == '\r' || published[start] == '\n'))
                    start++;
                if (start < published.Length)
                {
                    details.Append(published, start, published.Length - start);
                    return;
                }
            }

            BuildStatusText();
            details.Append(_text);
        }

        private void BuildStatusText()
        {
            _text.Clear();
            _text.Append("=== Pegasus Nanobot v").Append(Version).Append(" ===\n");
            _text.Append("Mod: ACTIVE\n");

            switch (_status)
            {
                case Status.Initializing:
                    _text.Append("Status: Initializing\n");
                    break;
                case Status.Off:
                    _text.Append("Status: Switched off\n");
                    break;
                case Status.Damaged:
                    _text.Append("Status: Block damaged\n");
                    break;
                case Status.NoPower:
                    _text.Append("Status: No power\n");
                    break;
                case Status.Error:
                    _text.Append("Status: Error\n");
                    _text.Append("Error: ").Append(_lastError).Append('\n');
                    AppendRecoveryCountdown(_text);
                    break;
                case Status.Idle:
                    _text.Append("Status: Idle (scanning ");
                    _text.Append((int)_config.Range).Append("m)\n");
                    _text.Append("Damaged in range: ");
                    if (_damagedInRange >= DamagedCountCap)
                        _text.Append(_damagedInRange).Append("+\n");
                    else
                        _text.Append(_damagedInRange).Append('\n');
                    break;
                case Status.MissingComponents:
                    _text.Append("Status: Missing components");
                    if (_partsStarved)
                        _text.Append(" (waiting, low tick)");
                    _text.Append('\n');
                    AppendTargetDetails(includeMissing: true);
                    break;
                case Status.Welding:
                    _text.Append("Status: Welding\n");
                    AppendTargetDetails(includeMissing: _config.LcdMode != "compact");
                    break;
            }

            AppendStuckWarning(_text);

            if (CanOperate())
            {
                _text.Append("Sources: ").Append(_sources.Count);
                _text.Append(" inv / ").Append(_sourceGridCount).Append(" grids");
                _text.Append(" | Queue: ").Append(_queue.Count).Append('\n');
                _text.Append("Range: ").Append((int)_config.Range).Append("m");
                if (GetEffectiveRepairMode() != NanobotRepairMode.Nearest)
                    _text.Append(" | Repair: ").Append(_config.RepairMode);
                _text.Append('\n');
            }
        }

        private void AppendRecoveryCountdown(StringBuilder sb)
        {
            if (_status != Status.Error || _errorRecoveryCounter <= 0) return;
            var seconds = (_errorRecoveryCounter + UpdatesPerSecond - 1) / UpdatesPerSecond;
            sb.Append("Recovery in: ").Append(seconds).Append("s\n");
        }

        private void AppendStuckWarning(StringBuilder sb)
        {
            if (_stuckStatusCounter < StuckWarningUpdates) return;
            if (_status != Status.Error && _status != Status.NoPower) return;
            sb.Append("STUCK? Toggle off/on or set ForceReset=true\n");
        }

        private void AppendTargetDetails(bool includeMissing)
        {
            if (!HasTarget(_target)) return;

            _text.Append("Target: ").Append(NanobotProjectionHelper.GetDisplayName(_target));
            if (_target.IsProjection)
                _text.Append(" [projection]");
            _text.Append('\n');
            var pct = NanobotProjectionHelper.GetIntegrityPercent(_target);
            _text.Append("Progress: ");
            AppendProgressBar(_text, pct);
            _text.Append(' ').Append(pct).Append("%\n");

            var dist = GetTargetDistanceM(_target);
            _text.Append("Dist: ").Append((int)dist).Append(" m\n");

            if (includeMissing)
                AppendMissingComponents();
        }

        private void AppendProgressBar(StringBuilder sb, int pct)
        {
            var filled = pct * 8 / 100;
            if (filled > 8) filled = 8;
            if (filled < 0) filled = 0;
            for (int i = 0; i < 8; i++)
                sb.Append(i < filled ? '\u2588' : '\u2591');
        }

        private void AppendMissingComponents()
        {
            if (!HasTarget(_target) || _inventory == null) return;

            var weldBlock = _target.Block ?? _target.Projected;
            if (weldBlock == null) return;

            _missing.Clear();
            weldBlock.GetMissingComponents(_missing);
            if (_missing.Count == 0) return;

            _text.Append("Need:\n");
            foreach (var entry in _missing)
            {
                if (entry.Value <= 0) continue;
                var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), entry.Key);
                var inWelder = (int)_inventory.GetItemAmount(componentId);
                var onLine = (int)InventoryHelper.GetConnectedItemAmount(_inventory, _sources, componentId);
                var onGrid = (int)InventoryHelper.GetTotalItemAmount(_sources, componentId);
                _text.Append("  ").Append(entry.Key).Append(": welder ")
                    .Append(inWelder).Append('/').Append(entry.Value);
                _text.Append(" (connected ").Append(onLine);
                if (onGrid != onLine)
                    _text.Append(", grid ").Append(onGrid);
                _text.Append(")\n");
            }
        }

        private string GetStatusLabel()
        {
            switch (_status)
            {
                case Status.Welding: return "Welding";
                case Status.MissingComponents: return "Missing parts";
                case Status.NoPower: return "No power";
                case Status.Off: return "Off";
                case Status.Damaged: return "Damaged";
                case Status.Error: return "Error";
                default: return "Idle";
            }
        }

        private static int GetIntegrityPercent(IMySlimBlock block)
        {
            if (block == null || block.MaxIntegrity <= 0f) return 0;
            return (int)(block.Integrity / block.MaxIntegrity * 100f);
        }

        private static string GetBlockDisplayName(IMySlimBlock block)
        {
            if (block == null) return "Unknown";
            var def = block.BlockDefinition;
            if (def == null) return "Unknown";
            if (def.DisplayNameText != null && def.DisplayNameText.Length > 0)
                return def.DisplayNameText.ToString();
            return def.Id.SubtypeId.ToString();
        }

        private double GetTargetDistanceM(WeldTarget target)
        {
            if (!HasTarget(target)) return 0;
            Vector3D pos;
            NanobotProjectionHelper.GetWorldPosition(target, out pos);
            return Vector3D.Distance(pos, _welder.GetPosition());
        }

        private static bool HasTarget(WeldTarget target)
        {
            return target.Block != null || target.IsProjection;
        }

        private bool ResolveWeldBlock(out IMySlimBlock weldBlock)
        {
            weldBlock = _target.Block;

            if (_target.IsProjection)
            {
                if (_target.Block != null && !_target.Block.IsDestroyed && NeedsRepair(_target.Block))
                {
                    weldBlock = _target.Block;
                    return true;
                }

                weldBlock = NanobotProjectionHelper.EnsurePhysicalBlock(
                    _target.Projector,
                    _target.Projected,
                    _target.Block,
                    GetBuilderId(),
                    GetBuilderId());
                if (weldBlock == null) return false;

                var target = _target;
                target.Block = weldBlock;
                _target = target;
            }

            return weldBlock != null;
        }

        private static bool TargetNeedsRepair(WeldTarget target)
        {
            if (target.IsProjection)
            {
                if (target.Block != null && !target.Block.IsDestroyed)
                    return NeedsRepair(target.Block);
                return NanobotProjectionHelper.IsProjectedCandidate(target.Projector, target.Projected);
            }

            return NeedsRepair(target.Block);
        }

        private bool IsValidTarget(WeldTarget target)
        {
            if (!HasTarget(target)) return false;

            if (target.IsProjection)
            {
                if (target.Projector == null || target.Projector.Closed || !target.Projector.Enabled) return false;
                if (!target.Projector.IsFunctional) return false;
                if (target.Projected == null || target.Projector.ProjectedGrid == null) return false;
                if (!IsGridAllowed(target.Projector.CubeGrid)) return false;

                Vector3D pos;
                NanobotProjectionHelper.GetWorldPosition(target, out pos);
                var rangeSq = (double)_config.Range * _config.Range;
                if (Vector3D.DistanceSquared(pos, _welder.GetPosition()) > rangeSq) return false;

                if (target.Block != null && !target.Block.IsDestroyed)
                    return NeedsRepair(target.Block);

                return NanobotProjectionHelper.IsProjectedCandidate(target.Projector, target.Projected);
            }

            return IsValidBlockTarget(target.Block);
        }

        private bool IsValidBlockTarget(IMySlimBlock block)
        {
            if (!NeedsRepair(block)) return false;
            if (block.CubeGrid == null) return false;
            if (!IsGridAllowed(block.CubeGrid)) return false;

            var distSq = Vector3D.DistanceSquared(
                block.CubeGrid.GridIntegerToWorld(block.Position),
                _welder.GetPosition());
            var rangeSq = (double)_config.Range * _config.Range;
            return distSq <= rangeSq;
        }

        private bool CanOperate()
        {
            return _welder.Enabled && _welder.IsFunctional;
        }

        private void UpdateStatus()
        {
            if (!_welder.Enabled)
            {
                _status = Status.Off;
                _target = default(WeldTarget);
                _queue.Clear();
                return;
            }

            if (!_welder.IsFunctional)
            {
                _status = Status.Damaged;
                _target = default(WeldTarget);
                _queue.Clear();
                return;
            }

            if (_status == Status.Off || _status == Status.Damaged || _status == Status.Initializing)
                _status = Status.Idle;
        }

        private long GetBuilderId()
        {
            if (_welder.OwnerId != 0) return _welder.OwnerId;

            var grid = _welder.CubeGrid;
            if (grid != null)
            {
                var owners = grid.BigOwners;
                if (owners != null && owners.Count > 0)
                    return owners[0];
            }

            return 0;
        }

        private void RefreshLcdPanels(bool force)
        {
            if (!force && _lcdRefreshCounter != 0) return;

            _lcdPanels.Clear();
            if (_welder.CubeGrid == null) return;

            _gridBlocksBuffer.Clear();
            _welder.CubeGrid.GetBlocks(_gridBlocksBuffer, block => block.FatBlock is IMyTextPanel);

            for (int i = 0; i < _gridBlocksBuffer.Count; i++)
            {
                var panel = _gridBlocksBuffer[i].FatBlock as IMyTextPanel;
                if (panel == null || panel.Closed) continue;

                var name = panel.CustomName;
                if (string.IsNullOrEmpty(name))
                    name = panel.DisplayNameText ?? string.Empty;

                if (name.IndexOf(NanobotLcdTagParser.LcdNameTag, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                int panelWelderId;
                string panelMode;
                if (!NanobotLcdTagParser.TryParse(name, out panelWelderId, out panelMode))
                    continue;

                if (panelWelderId != 0 && panelWelderId != _config.WelderId)
                    continue;

                if (NanobotLcdTagParser.IsSharedGridMode(panelMode))
                    continue;

                _lcdPanels.Add(new LcdBinding { Panel = panel, Mode = panelMode });
            }
        }

        private void TrySupplyComponents(IMySlimBlock target, bool updateStockpile)
        {
            if (_creative || target == null || _inventory == null) return;

            _missing.Clear();
            target.GetMissingComponents(_missing);

            var pulled = false;
            foreach (var entry in _missing)
            {
                if (entry.Value <= 0) continue;

                var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), entry.Key);
                var inWelder = _inventory.GetItemAmount(componentId);
                if (inWelder >= (MyFixedPoint)entry.Value) continue;

                var needed = (MyFixedPoint)entry.Value - inWelder;
                if (InventoryHelper.TryPullToWelder(_inventory, _sources, componentId, needed))
                    pulled = true;
            }

            if (updateStockpile && (pulled || _missing.Count > 0))
                InventoryHelper.SupplyConstructionStockpile(target, _inventory, _sources);
        }

        private static bool NeedsRepair(IMySlimBlock block)
        {
            if (block == null || block.IsDestroyed) return false;
            if (block.FatBlock != null && block.FatBlock.Closed) return false;
            if (block.Integrity < block.MaxIntegrity) return true;
            if (block.HasDeformation) return true;
            return block.BuildLevelRatio < 1f;
        }

        private enum Status
        {
            Initializing,
            Idle,
            Welding,
            MissingComponents,
            NoPower,
            Off,
            Damaged,
            Error
        }
    }
}
