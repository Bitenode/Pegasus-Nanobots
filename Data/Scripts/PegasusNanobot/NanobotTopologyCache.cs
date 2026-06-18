namespace Pegasus.Nanobot
{
    using System.Collections.Generic;

    using Sandbox.ModAPI;
    using VRage.Game.ModAPI;

    public static class NanobotTopologyCache
    {
        private const int TopologyTtlTicks = 90;
        private const int InventoryTtlTicks = 45;
        private const int MaxTopologyEntries = 12;
        private const int MaxInventoryEntries = 12;

        private struct TopologyKey
        {
            public long HomeGridId;
            public int ConnectorHops;
            public bool UseConnectors;
            public bool FactionOnly;
            public long BuilderId;

            public bool Equals(TopologyKey other)
            {
                return HomeGridId == other.HomeGridId
                    && ConnectorHops == other.ConnectorHops
                    && UseConnectors == other.UseConnectors
                    && FactionOnly == other.FactionOnly
                    && BuilderId == other.BuilderId;
            }
        }

        private struct TopologyEntry
        {
            public TopologyKey Key;
            public int Tick;
            public int LastUsed;
            public List<IMyCubeGrid> Grids;
        }

        private struct InventoryEntry
        {
            public TopologyKey Key;
            public long TopologyHash;
            public int Tick;
            public int LastUsed;
            public List<IMyInventory> Sources;
            public int GridCount;
        }

        private static readonly List<TopologyEntry> TopologyEntries = new List<TopologyEntry>();
        private static readonly List<InventoryEntry> InventoryEntries = new List<InventoryEntry>();
        private static int _globalTick;

        public static bool TryGetLinkedGrids(
            IMyShipWelder welder,
            int connectorHops,
            bool useConnectors,
            bool factionOnly,
            long builderId,
            List<IMyCubeGrid> destination)
        {
            destination.Clear();
            if (welder?.CubeGrid == null) return false;

            var key = BuildKey(welder.CubeGrid.EntityId, connectorHops, useConnectors, factionOnly, builderId);
            var tick = ++_globalTick;
            var entryIndex = FindTopologyIndex(key);

            if (entryIndex >= 0)
            {
                var entry = TopologyEntries[entryIndex];
                if (tick - entry.Tick < TopologyTtlTicks && entry.Grids != null)
                {
                    entry.LastUsed = tick;
                    TopologyEntries[entryIndex] = entry;
                    CopyGrids(entry.Grids, destination);
                    return true;
                }

                TopologyEntries.RemoveAt(entryIndex);
            }

            return false;
        }

        public static void StoreLinkedGrids(
            IMyShipWelder welder,
            int connectorHops,
            bool useConnectors,
            bool factionOnly,
            long builderId,
            List<IMyCubeGrid> grids)
        {
            if (welder?.CubeGrid == null || grids == null) return;

            var key = BuildKey(welder.CubeGrid.EntityId, connectorHops, useConnectors, factionOnly, builderId);
            var tick = _globalTick;

            if (TopologyEntries.Count >= MaxTopologyEntries)
                EvictOldestTopology();

            var copy = new List<IMyCubeGrid>(grids.Count);
            CopyGrids(grids, copy);

            TopologyEntries.Add(new TopologyEntry
            {
                Key = key,
                Tick = tick,
                LastUsed = tick,
                Grids = copy
            });
        }

        public static bool TryGetInventorySources(
            IMyShipWelder welder,
            int connectorHops,
            bool useConnectors,
            bool factionOnly,
            long builderId,
            long topologyHash,
            List<IMyInventory> destination,
            out int gridCount)
        {
            gridCount = 0;
            destination.Clear();
            if (welder == null) return false;

            var key = BuildKey(welder.CubeGrid?.EntityId ?? 0, connectorHops, useConnectors, factionOnly, builderId);
            var tick = _globalTick;
            var entryIndex = FindInventoryIndex(key);

            if (entryIndex < 0) return false;

            var entry = InventoryEntries[entryIndex];
            if (tick - entry.Tick >= InventoryTtlTicks) return false;
            if (entry.TopologyHash != topologyHash) return false;
            if (entry.Sources == null) return false;

            entry.LastUsed = tick;
            InventoryEntries[entryIndex] = entry;
            CopySources(entry.Sources, destination);
            gridCount = entry.GridCount;
            return true;
        }

        public static void StoreInventorySources(
            IMyShipWelder welder,
            int connectorHops,
            bool useConnectors,
            bool factionOnly,
            long builderId,
            long topologyHash,
            List<IMyInventory> sources,
            int gridCount)
        {
            if (welder == null || sources == null) return;

            var key = BuildKey(welder.CubeGrid?.EntityId ?? 0, connectorHops, useConnectors, factionOnly, builderId);
            var tick = _globalTick;
            var entryIndex = FindInventoryIndex(key);

            if (entryIndex >= 0)
                InventoryEntries.RemoveAt(entryIndex);

            if (InventoryEntries.Count >= MaxInventoryEntries)
                EvictOldestInventory();

            var copy = new List<IMyInventory>(sources.Count);
            CopySources(sources, copy);

            InventoryEntries.Add(new InventoryEntry
            {
                Key = key,
                TopologyHash = topologyHash,
                Tick = tick,
                LastUsed = tick,
                Sources = copy,
                GridCount = gridCount
            });
        }

        private static TopologyKey BuildKey(
            long homeGridId,
            int connectorHops,
            bool useConnectors,
            bool factionOnly,
            long builderId)
        {
            return new TopologyKey
            {
                HomeGridId = homeGridId,
                ConnectorHops = connectorHops,
                UseConnectors = useConnectors,
                FactionOnly = factionOnly,
                BuilderId = builderId
            };
        }

        private static int FindTopologyIndex(TopologyKey key)
        {
            for (int i = 0; i < TopologyEntries.Count; i++)
            {
                if (TopologyEntries[i].Key.Equals(key))
                    return i;
            }

            return -1;
        }

        private static int FindInventoryIndex(TopologyKey key)
        {
            for (int i = 0; i < InventoryEntries.Count; i++)
            {
                if (InventoryEntries[i].Key.Equals(key))
                    return i;
            }

            return -1;
        }

        private static void CopyGrids(List<IMyCubeGrid> source, List<IMyCubeGrid> destination)
        {
            destination.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                var grid = source[i];
                if (grid == null || grid.Closed || grid.MarkedForClose) continue;
                destination.Add(grid);
            }
        }

        private static void CopySources(List<IMyInventory> source, List<IMyInventory> destination)
        {
            destination.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                var inventory = source[i];
                if (inventory == null) continue;
                destination.Add(inventory);
            }
        }

        private static void EvictOldestTopology()
        {
            var oldestIdx = 0;
            var oldestUsed = TopologyEntries[0].LastUsed;

            for (int i = 1; i < TopologyEntries.Count; i++)
            {
                if (TopologyEntries[i].LastUsed >= oldestUsed) continue;
                oldestUsed = TopologyEntries[i].LastUsed;
                oldestIdx = i;
            }

            TopologyEntries.RemoveAt(oldestIdx);
        }

        private static void EvictOldestInventory()
        {
            var oldestIdx = 0;
            var oldestUsed = InventoryEntries[0].LastUsed;

            for (int i = 1; i < InventoryEntries.Count; i++)
            {
                if (InventoryEntries[i].LastUsed >= oldestUsed) continue;
                oldestUsed = InventoryEntries[i].LastUsed;
                oldestIdx = i;
            }

            InventoryEntries.RemoveAt(oldestIdx);
        }
    }
}
