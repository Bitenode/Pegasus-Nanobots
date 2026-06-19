namespace Pegasus.Nanobot
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    using Sandbox.Common.ObjectBuilders;
    using Sandbox.Definitions;
    using Sandbox.ModAPI;
    using VRage.Game;
    using VRage.Game.Components;
    using VRage.Game.GUI.TextPanel;
    using VRage.Game.ModAPI;
    using VRage.ModAPI;
    using VRage.ObjectBuilders;

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_TextPanel), false,
        "PegasusAssemblerLinkLarge", "PegasusAssemblerLinkSmall")]
    public class AssemblerLinkBlock : MyGameLogicComponent
    {
        public const string Version = "1.1.0";

        private IMyTextPanel _panel;
        private IMyTerminalBlock _terminal;
        private readonly AssemblerLinkConfig _config = new AssemblerLinkConfig();
        private readonly Dictionary<string, int> _missingTotals = new Dictionary<string, int>();
        private readonly List<IMySlimBlock> _blockBuffer = new List<IMySlimBlock>();
        private readonly StringBuilder _text = new StringBuilder(512);

        private string _lastPublishedText = string.Empty;
        private string _lastLcdText = string.Empty;
        private string _lastParsedConfigSection = string.Empty;
        private int _scanCounter;
        private int _queuedThisCycle;
        private string _lastStatus = "Initializing";
        private readonly List<Sandbox.ModAPI.Ingame.MyProductionItem> _queueScratch =
            new List<Sandbox.ModAPI.Ingame.MyProductionItem>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            _panel = Entity as IMyTextPanel;
            _terminal = Entity as IMyTerminalBlock;
            if (_panel == null || _terminal == null)
            {
                NeedsUpdate = MyEntityUpdateEnum.NONE;
                return;
            }

            _scanCounter = (int)(Entity.EntityId % 30);
            ReloadConfig();
            NeedsUpdate = MyEntityUpdateEnum.EACH_100TH_FRAME;
            PublishStatus(force: true);
        }

        public override void UpdateBeforeSimulation100()
        {
            if (_terminal == null || _terminal.Closed) return;
            if (MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer) return;

            ReloadConfigIfChanged();

            _scanCounter++;
            if (_scanCounter >= _config.ScanInterval)
            {
                _scanCounter = 0;
                try
                {
                    TickAssemble();
                }
                catch (Exception ex)
                {
                    _lastStatus = "Error: " + (ex.Message ?? "unknown");
                }
            }

            PublishStatus();
        }

        private void TickAssemble()
        {
            _queuedThisCycle = 0;
            _lastStatus = "Idle";

            if (!_config.AutoAssemble)
            {
                _lastStatus = "AutoAssemble disabled";
                return;
            }

            var grid = _panel.CubeGrid;
            if (grid == null)
            {
                _lastStatus = "No grid";
                return;
            }

            NanobotFleetRegistry.CollectMissingParts(grid.EntityId, _missingTotals);
            if (_missingTotals.Count == 0)
            {
                _lastStatus = "No starved welders";
                return;
            }

            _blockBuffer.Clear();
            grid.GetBlocks(_blockBuffer, block => block.FatBlock is IMyAssembler);

            if (_blockBuffer.Count == 0)
            {
                _lastStatus = "No assemblers on grid";
                return;
            }

            foreach (var pair in _missingTotals)
            {
                if (_queuedThisCycle >= _config.MaxQueueItems)
                    break;

                if (!_config.IsComponentAllowed(pair.Key))
                    continue;

                if (!TryQueueComponent(_blockBuffer, pair.Key, pair.Value))
                    continue;

                _queuedThisCycle++;
            }

            _lastStatus = _queuedThisCycle > 0
                ? "Queued " + _queuedThisCycle + " job(s)"
                : "No queue slots available";
        }

        private bool TryQueueComponent(List<IMySlimBlock> assemblers, string componentName, int amount)
        {
            if (amount <= 0) return false;

            var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), componentName);
            MyBlueprintDefinitionBase blueprint;
            if (!MyDefinitionManager.Static.TryGetBlueprintDefinitionByResultId(componentId, out blueprint))
                return false;

            var queueAmount = (MyFixedPoint)Math.Min(amount, 100);

            for (int i = 0; i < assemblers.Count; i++)
            {
                var assembler = assemblers[i].FatBlock as IMyAssembler;
                if (assembler == null || assembler.Closed || !assembler.IsFunctional) continue;
                if (!assembler.Enabled) continue;

                var production = assembler as IMyProductionBlock;
                if (production == null) continue;

                _queueScratch.Clear();
                production.GetQueue(_queueScratch);
                if (_queueScratch.Count >= _config.MaxQueueItems)
                    continue;

                assembler.AddQueueItem(blueprint.Id, queueAmount);
                return true;
            }

            return false;
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

            var idx = data.IndexOf(AssemblerLinkConfig.StatusMarker, StringComparison.Ordinal);
            if (idx > 0) return data.Substring(0, idx).TrimEnd();

            idx = data.IndexOf("=== Pegasus Assembler Link", StringComparison.Ordinal);
            if (idx > 0) return data.Substring(0, idx).TrimEnd();

            return data;
        }

        private void PublishStatus(bool force = false)
        {
            var grid = _panel.CubeGrid;
            if (grid != null)
                NanobotFleetRegistry.CollectMissingParts(grid.EntityId, _missingTotals);

            _text.Clear();
            _text.Append("=== Pegasus Assembler Link v").Append(Version).Append(" ===\n");
            _text.Append("Status: ").Append(_lastStatus).Append('\n');

            if (_missingTotals.Count > 0)
            {
                _text.Append("Missing parts:\n");
                foreach (var pair in _missingTotals)
                    _text.Append("  ").Append(pair.Key).Append(": ").Append(pair.Value).Append('\n');
            }

            var body = _text.ToString();
            var output = _config.FormatCustomData(body);
            if (force || output != _lastPublishedText)
            {
                _lastPublishedText = output;
                _terminal.CustomData = output;
            }

            WritePanelText(body, force);

            if (grid != null)
                NanobotGridLcd.UpdateSharedPanels(grid, NanobotGridLcd.NextTick());
        }

        private void WritePanelText(string text, bool force)
        {
            if (_panel == null || _panel.Closed) return;
            if (!force && text == _lastLcdText) return;

            _lastLcdText = text;

            if (_panel.ContentType != ContentType.TEXT_AND_IMAGE)
                _panel.ContentType = ContentType.TEXT_AND_IMAGE;

            if (_panel.FontSize != 0.8f)
                _panel.FontSize = 0.8f;

            _panel.WriteText(text, append: false);
        }
    }
}
