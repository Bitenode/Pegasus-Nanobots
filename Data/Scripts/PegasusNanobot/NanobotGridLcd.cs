namespace Pegasus.Nanobot
{
    using System.Collections.Generic;
    using System.Text;

    using Sandbox.ModAPI;
    using VRage.Game.GUI.TextPanel;
    using VRage.Game.ModAPI;

    public static class NanobotGridLcd
    {
        private const int RefreshIntervalTicks = 180;

        private static readonly List<IMySlimBlock> BlockBuffer = new List<IMySlimBlock>();
        private static readonly Dictionary<long, int> LastRefreshTick = new Dictionary<long, int>();
        private static int _globalTick;

        public static void UpdateSharedPanels(IMyCubeGrid grid, int tick)
        {
            if (grid == null || grid.Closed) return;

            var gridId = grid.EntityId;
            int lastTick;
            if (LastRefreshTick.TryGetValue(gridId, out lastTick))
            {
                if (tick - lastTick < RefreshIntervalTicks)
                    return;
            }

            LastRefreshTick[gridId] = tick;

            BlockBuffer.Clear();
            grid.GetBlocks(BlockBuffer, block => block.FatBlock is IMyTextPanel);

            for (int i = 0; i < BlockBuffer.Count; i++)
            {
                var panel = BlockBuffer[i].FatBlock as IMyTextPanel;
                if (panel == null || panel.Closed) continue;

                var name = panel.CustomName;
                if (string.IsNullOrEmpty(name))
                    name = panel.DisplayNameText ?? string.Empty;

                if (!NanobotLcdTagParser.ContainsTag(name)) continue;

                int welderId;
                string mode;
                if (!NanobotLcdTagParser.TryParse(name, out welderId, out mode)) continue;
                if (string.IsNullOrEmpty(mode)) continue;

                var text = BuildSharedModeText(gridId, mode);
                if (text == null) continue;

                if (panel.ContentType != ContentType.TEXT_AND_IMAGE)
                    panel.ContentType = ContentType.TEXT_AND_IMAGE;

                panel.WriteText(text, append: false);
            }
        }

        public static int NextTick()
        {
            return ++_globalTick;
        }

        private static string BuildSharedModeText(long gridId, string mode)
        {
            if (mode == "fleet")
                return NanobotFleetRegistry.BuildFleetSummary(gridId);

            if (mode == "supply")
                return NanobotFleetRegistry.BuildSupplySummary(gridId);

            if (mode == "alert-supply")
            {
                if (!NanobotFleetRegistry.HasStarvedWelders(gridId))
                    return string.Empty;

                return NanobotFleetRegistry.BuildSupplySummary(gridId);
            }

            return null;
        }
    }
}
