namespace Pegasus.Nanobot
{
    using System.Collections.Generic;

    using Sandbox.ModAPI;
    using VRage;
    using VRage.Game;
    using VRage.Game.ModAPI;
    using VRage.ObjectBuilders;

    public static class InventoryHelper
    {
        public static void RefreshSources(
            IMyShipWelder welder,
            List<IMyInventory> sources,
            bool includeConnected,
            int maxHops,
            long builderId,
            bool ownerOnly,
            List<IMyCubeGrid> gridScratch,
            List<IMySlimBlock> blockScratch,
            out int gridCount)
        {
            sources.Clear();
            gridCount = 0;
            if (welder == null || welder.Closed) return;

            var welderInventory = welder.GetInventory(0);
            if (welderInventory == null) return;

            sources.Add(welderInventory);

            var homeGrid = welder.CubeGrid;
            if (homeGrid == null) return;

            gridScratch.Clear();
            gridScratch.Add(homeGrid);

            if (includeConnected && maxHops > 0)
                CollectLinkedGrids(gridScratch, maxHops, blockScratch);

            gridCount = gridScratch.Count;

            for (int g = 0; g < gridScratch.Count; g++)
            {
                var grid = gridScratch[g];
                if (grid == null || grid.Closed || grid.MarkedForClose) continue;
                if (!IsGridOwnerAllowed(grid, homeGrid, builderId, ownerOnly)) continue;

                blockScratch.Clear();
                grid.GetBlocks(blockScratch, block => block.FatBlock != null && block.FatBlock.HasInventory);

                for (int i = 0; i < blockScratch.Count; i++)
                {
                    var fat = blockScratch[i].FatBlock as IMyTerminalBlock;
                    if (fat == null || fat == welder || fat.Closed) continue;

                    for (int invIndex = 0; invIndex < fat.InventoryCount; invIndex++)
                    {
                        var inventory = fat.GetInventory(invIndex);
                        if (inventory == null || inventory == welderInventory) continue;
                        if (!sources.Contains(inventory))
                            sources.Add(inventory);
                    }
                }
            }

            for (int i = sources.Count - 1; i >= 0; i--)
            {
                if (sources[i] == null)
                    sources.RemoveAt(i);
            }
        }

        public static void CollectConnectedGrids(
            List<IMyCubeGrid> destination,
            int maxHops,
            List<IMySlimBlock> blockScratch)
        {
            if (destination == null || destination.Count == 0 || maxHops <= 0) return;

            int expanded = 0;
            for (int hop = 0; hop < maxHops; hop++)
            {
                var scanEnd = destination.Count;
                if (expanded >= scanEnd) break;

                while (expanded < scanEnd)
                {
                    var grid = destination[expanded++];
                    if (grid == null || grid.Closed) continue;

                    blockScratch.Clear();
                    grid.GetBlocks(blockScratch, block => block.FatBlock is IMyShipConnector);

                    for (int i = 0; i < blockScratch.Count; i++)
                    {
                        var connector = blockScratch[i].FatBlock as IMyShipConnector;
                        if (connector == null || connector.Closed) continue;

                        var other = connector.OtherConnector;
                        if (other == null || other.Closed) continue;

                        var otherGrid = other.CubeGrid;
                        if (otherGrid == null || otherGrid.Closed || otherGrid.MarkedForClose) continue;
                        if (ContainsGrid(destination, otherGrid)) continue;

                        destination.Add(otherGrid);
                    }
                }
            }
        }

        public static void CollectLinkedGrids(
            List<IMyCubeGrid> destination,
            int maxHops,
            List<IMySlimBlock> blockScratch)
        {
            if (destination == null || destination.Count == 0 || maxHops <= 0) return;

            CollectConnectedGrids(destination, maxHops, blockScratch);
            CollectMechanicalGrids(destination, maxHops, blockScratch);
        }

        public static void CollectMechanicalGrids(
            List<IMyCubeGrid> destination,
            int maxHops,
            List<IMySlimBlock> blockScratch)
        {
            if (destination == null || destination.Count == 0 || maxHops <= 0) return;

            int expanded = 0;
            for (int hop = 0; hop < maxHops; hop++)
            {
                var scanEnd = destination.Count;
                if (expanded >= scanEnd) break;

                while (expanded < scanEnd)
                {
                    var grid = destination[expanded++];
                    if (grid == null || grid.Closed) continue;

                    blockScratch.Clear();
                    grid.GetBlocks(blockScratch, block => block.FatBlock is IMyMechanicalConnectionBlock);

                    for (int i = 0; i < blockScratch.Count; i++)
                    {
                        var mech = blockScratch[i].FatBlock as IMyMechanicalConnectionBlock;
                        if (mech == null || !mech.IsAttached) continue;

                        var top = mech.Top;
                        if (top == null || top.Closed) continue;

                        var topGrid = top.CubeGrid;
                        if (topGrid == null || topGrid.Closed || topGrid.MarkedForClose) continue;
                        if (ContainsGrid(destination, topGrid)) continue;

                        destination.Add(topGrid);
                    }
                }
            }
        }

        public static long ComputeConnectorGridHash(
            IMyShipWelder welder,
            int maxHops,
            List<IMyCubeGrid> gridScratch,
            List<IMySlimBlock> blockScratch)
        {
            if (welder?.CubeGrid == null) return 0;

            gridScratch.Clear();
            gridScratch.Add(welder.CubeGrid);

            if (maxHops > 0)
                CollectLinkedGrids(gridScratch, maxHops, blockScratch);

            long hash = 0;
            for (int i = 0; i < gridScratch.Count; i++)
            {
                var grid = gridScratch[i];
                if (grid == null) continue;
                hash ^= grid.EntityId;
            }

            return hash;
        }

        public static MyFixedPoint GetTotalItemAmount(List<IMyInventory> sources, MyDefinitionId componentId)
        {
            MyFixedPoint total = 0;
            if (sources == null) return total;

            for (int s = 0; s < sources.Count; s++)
            {
                var source = sources[s];
                if (source == null) continue;
                total += source.GetItemAmount(componentId);
            }

            return total;
        }

        public static MyFixedPoint GetConnectedItemAmount(
            IMyInventory welderInventory,
            List<IMyInventory> sources,
            MyDefinitionId componentId)
        {
            if (welderInventory == null || sources == null) return 0;

            MyFixedPoint total = welderInventory.GetItemAmount(componentId);
            for (int s = 0; s < sources.Count; s++)
            {
                var source = sources[s];
                if (source == null || source == welderInventory) continue;
                if (!welderInventory.IsConnectedTo(source)) continue;
                total += source.GetItemAmount(componentId);
            }

            return total;
        }

        public static bool TryPullToWelder(
            IMyInventory welderInventory,
            List<IMyInventory> sources,
            MyDefinitionId componentId,
            MyFixedPoint amountNeeded)
        {
            if (welderInventory == null || amountNeeded <= 0) return false;

            var remaining = amountNeeded;
            for (int s = 0; s < sources.Count && remaining > 0; s++)
            {
                var source = sources[s];
                if (source == null || source == welderInventory) continue;
                if (!welderInventory.IsConnectedTo(source)) continue;

                var items = source.GetItems();
                if (items == null) continue;

                for (int i = items.Count - 1; i >= 0 && remaining > 0; i--)
                {
                    var item = items[i];
                    if (item == null || item.Amount <= 0) continue;
                    if (!MatchesComponent(item, componentId)) continue;

                    var moveAmount = item.Amount < remaining ? item.Amount : remaining;
                    if (welderInventory.TransferItemFrom(source, item, moveAmount))
                    {
                        remaining -= moveAmount;
                        continue;
                    }

                    if (source.TransferItemTo(welderInventory, i, null, true, moveAmount))
                        remaining -= moveAmount;
                }
            }

            return remaining < amountNeeded;
        }

        public static void SupplyConstructionStockpile(
            IMySlimBlock target,
            IMyInventory welderInventory,
            List<IMyInventory> sources)
        {
            if (target == null || welderInventory == null) return;

            target.MoveItemsToConstructionStockpile(welderInventory);

            if (sources == null) return;

            for (int s = 0; s < sources.Count; s++)
            {
                var source = sources[s];
                if (source == null || source == welderInventory) continue;
                if (!welderInventory.IsConnectedTo(source)) continue;

                target.MoveItemsToConstructionStockpile(source);
            }
        }

        public static bool CanWeldTarget(
            IMySlimBlock target,
            IMyInventory welderInventory,
            bool creativeMode,
            Dictionary<string, int> missingScratch)
        {
            if (creativeMode) return true;
            if (target == null || welderInventory == null) return false;

            missingScratch.Clear();
            target.GetMissingComponents(missingScratch);

            var needsComponents = false;
            foreach (var entry in missingScratch)
            {
                if (entry.Value > 0)
                {
                    needsComponents = true;
                    break;
                }
            }

            if (!needsComponents)
                return target.Integrity < target.MaxIntegrity || target.HasDeformation;

            return target.CanContinueBuild(welderInventory);
        }

        public static bool HasComponentsForWelding(IMySlimBlock target, IMyInventory welderInventory, bool creativeMode)
        {
            if (creativeMode) return true;
            if (target == null || welderInventory == null) return false;
            return target.CanContinueBuild(welderInventory);
        }

        public static bool HasConnectedComponentsAvailable(
            IMySlimBlock target,
            IMyInventory welderInventory,
            List<IMyInventory> sources,
            Dictionary<string, int> missingScratch)
        {
            if (target == null || welderInventory == null || sources == null) return false;

            missingScratch.Clear();
            target.GetMissingComponents(missingScratch);
            if (missingScratch.Count == 0) return true;

            foreach (var entry in missingScratch)
            {
                if (entry.Value <= 0) continue;
                var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), entry.Key);
                var total = GetConnectedItemAmount(welderInventory, sources, componentId);
                if (total < (MyFixedPoint)entry.Value)
                    return false;
            }

            return true;
        }

        public static bool HasMissingComponentsAvailable(
            IMySlimBlock target,
            IMyInventory welderInventory,
            List<IMyInventory> sources,
            Dictionary<string, int> missingScratch)
        {
            if (target == null || welderInventory == null || sources == null) return false;

            missingScratch.Clear();
            target.GetMissingComponents(missingScratch);
            if (missingScratch.Count == 0) return true;

            foreach (var entry in missingScratch)
            {
                if (entry.Value <= 0) continue;
                var componentId = new MyDefinitionId(typeof(MyObjectBuilder_Component), entry.Key);
                var total = GetTotalItemAmount(sources, componentId);
                if (total < (MyFixedPoint)entry.Value)
                    return false;
            }

            return true;
        }

        private static bool IsGridOwnerAllowed(IMyCubeGrid grid, IMyCubeGrid homeGrid, long builderId, bool ownerOnly)
        {
            if (!ownerOnly) return true;
            if (grid == homeGrid) return true;
            if (builderId == 0) return grid == homeGrid;

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

        private static bool MatchesComponent(IMyInventoryItem item, MyDefinitionId componentId)
        {
            if (item?.Content == null) return false;
            var contentId = new MyDefinitionId(item.Content.TypeId, item.Content.SubtypeId);
            return contentId == componentId;
        }
    }
}
