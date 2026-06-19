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

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_ShipConnector), false,
        "PegasusDockDoctorLarge", "PegasusDockDoctorSmall")]
    public class DockDoctorBlock : MyGameLogicComponent
    {
        public const string Version = "1.1.0";
        private const int UpdatesPerSecond = 6;
        private const int LcdRefreshInterval = 30;

        private IMyShipConnector _connector;
        private IMyTerminalBlock _terminal;
        private readonly DockDoctorConfig _config = new DockDoctorConfig();
        private readonly List<IMySlimBlock> _blockBuffer = new List<IMySlimBlock>();
        private readonly List<IMyTextPanel> _dockPanels = new List<IMyTextPanel>();
        private readonly StringBuilder _text = new StringBuilder(384);
        private readonly StringBuilder _lcdText = new StringBuilder(256);

        private string _lastPublishedText = string.Empty;
        private string _lastLcdText = string.Empty;
        private string _lastParsedConfigSection = string.Empty;
        private bool _wasConnected;
        private long _lastDockedGridId;
        private string _lastDockedGridName = string.Empty;
        private int _boostRemainingTicks;
        private int _lcdRefreshCounter;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            _connector = Entity as IMyShipConnector;
            _terminal = Entity as IMyTerminalBlock;
            if (_connector == null || _terminal == null)
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            ReloadConfig();
            RefreshDockPanels();
            NeedsUpdate = MyEntityUpdateEnum.EACH_10TH_FRAME;
            PublishStatus(force: true);
            WriteDockLcdPanels(force: true);

            var grid = _connector.CubeGrid;
            if (grid != null)
                NanobotGridLcd.UpdateSharedPanels(grid, NanobotGridLcd.NextTick());
        }

        public override void UpdateBeforeSimulation10()
        {
            if (_terminal == null || _terminal.Closed) return;
            if (MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer) return;

            ReloadConfigIfChanged();

            _lcdRefreshCounter++;
            if (_lcdRefreshCounter >= LcdRefreshInterval)
            {
                _lcdRefreshCounter = 0;
                RefreshDockPanels();
            }

            try
            {
                TickDock();
            }
            catch (Exception ex)
            {
                _text.Clear();
                _text.Append("Error: ").Append(ex.Message ?? "unknown");
            }

            if (_boostRemainingTicks > 0)
                _boostRemainingTicks--;

            PublishStatus();
            WriteDockLcdPanels(force: false);

            var grid = _connector.CubeGrid;
            if (grid != null)
                NanobotGridLcd.UpdateSharedPanels(grid, NanobotGridLcd.NextTick());
        }

        private void TickDock()
        {
            var connected = _connector.IsConnected && _connector.OtherConnector != null;

            if (connected && !_wasConnected && _config.Enabled)
                OnDockConnected();

            if (!connected && _wasConnected)
                OnDockDisconnected();

            _wasConnected = connected;
        }

        private void OnDockConnected()
        {
            var other = _connector.OtherConnector;
            if (other == null) return;

            var dockedGrid = other.CubeGrid;
            var carrierGrid = _connector.CubeGrid;
            if (dockedGrid == null || carrierGrid == null) return;

            _lastDockedGridId = dockedGrid.EntityId;
            _lastDockedGridName = dockedGrid.DisplayName ?? "Docked grid";

            var boostTicks = _config.BoostSeconds * UpdatesPerSecond;
            var repairMode = _config.GetRepairMode();

            NanobotDockDoctorRegistry.ApplyBoost(dockedGrid.EntityId, boostTicks, repairMode, _config.FastScan);
            NanobotDockDoctorRegistry.ApplyBoost(carrierGrid.EntityId, boostTicks, repairMode, _config.FastScan);

            _boostRemainingTicks = boostTicks;
        }

        private void OnDockDisconnected()
        {
            _boostRemainingTicks = 0;
        }

        private void RefreshDockPanels()
        {
            _dockPanels.Clear();
            var grid = _connector.CubeGrid;
            if (grid == null) return;

            _blockBuffer.Clear();
            grid.GetBlocks(_blockBuffer, block => block.FatBlock is IMyTextPanel);

            for (int i = 0; i < _blockBuffer.Count; i++)
            {
                var panel = _blockBuffer[i].FatBlock as IMyTextPanel;
                if (panel == null || panel.Closed) continue;

                var name = panel.CustomName;
                if (string.IsNullOrEmpty(name))
                    name = panel.DisplayNameText ?? string.Empty;

                if (!NanobotLcdTagParser.ContainsTag(name)) continue;

                int welderId;
                string mode;
                if (!NanobotLcdTagParser.TryParse(name, out welderId, out mode)) continue;

                if (!string.IsNullOrEmpty(mode) && mode != "dock")
                    continue;

                _dockPanels.Add(panel);
            }
        }

        private void WriteDockLcdPanels(bool force)
        {
            BuildDockLcdText();
            var text = _lcdText.ToString();
            if (!force && text == _lastLcdText) return;

            _lastLcdText = text;

            for (int i = 0; i < _dockPanels.Count; i++)
            {
                var panel = _dockPanels[i];
                if (panel == null || panel.Closed) continue;

                if (panel.ContentType != ContentType.TEXT_AND_IMAGE)
                    panel.ContentType = ContentType.TEXT_AND_IMAGE;

                panel.WriteText(text, append: false);
            }
        }

        private void BuildDockLcdText()
        {
            _lcdText.Clear();
            _lcdText.Append("Dock Doctor v").Append(Version).Append('\n');

            if (_connector.IsConnected && _connector.OtherConnector != null)
            {
                _lcdText.Append("Connected: ").Append(_lastDockedGridName).Append('\n');
                if (_boostRemainingTicks > 0)
                {
                    var seconds = (_boostRemainingTicks + UpdatesPerSecond - 1) / UpdatesPerSecond;
                    _lcdText.Append("Boost: ").Append(seconds).Append("s\n");
                    _lcdText.Append("Mode: ").Append(_config.RepairMode).Append('\n');
                }
                else
                {
                    _lcdText.Append("Boost: idle\n");
                }
            }
            else
            {
                _lcdText.Append("Awaiting dock\n");
            }
        }

        private void ReloadConfigIfChanged()
        {
            var data = _terminal.CustomData ?? string.Empty;
            var configSection = ExtractConfigSection(data);
            if (configSection == _lastParsedConfigSection) return;

            _lastParsedConfigSection = configSection;
            _config.Parse(data);
        }

        private void ReloadConfig()
        {
            _config.Parse(_terminal.CustomData ?? string.Empty);
            _lastParsedConfigSection = _config.UserConfigSection;
        }

        private static string ExtractConfigSection(string data)
        {
            if (string.IsNullOrEmpty(data)) return string.Empty;

            var idx = data.IndexOf(DockDoctorConfig.StatusMarker, StringComparison.Ordinal);
            if (idx > 0) return data.Substring(0, idx).TrimEnd();

            idx = data.IndexOf("=== Pegasus Dock Doctor", StringComparison.Ordinal);
            if (idx > 0) return data.Substring(0, idx).TrimEnd();

            return data;
        }

        private void PublishStatus(bool force = false)
        {
            _text.Clear();
            _text.Append("=== Pegasus Dock Doctor v").Append(Version).Append(" ===\n");
            _text.Append("Enabled: ").Append(_config.Enabled ? "yes" : "no").Append('\n');

            if (_connector.IsConnected)
            {
                _text.Append("Docked: ").Append(_lastDockedGridName).Append('\n');
                if (_boostRemainingTicks > 0)
                {
                    var seconds = (_boostRemainingTicks + UpdatesPerSecond - 1) / UpdatesPerSecond;
                    _text.Append("Boost active: ").Append(seconds).Append("s\n");
                }
            }
            else
            {
                _text.Append("Status: Ready\n");
            }

            var body = _text.ToString();
            var output = _config.FormatCustomData(body);
            if (force || output != _lastPublishedText)
            {
                _lastPublishedText = output;
                _terminal.CustomData = output;
            }
        }
    }
}
