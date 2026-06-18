namespace Pegasus.Nanobot
{
    using System.Collections.Generic;

    using Sandbox.ModAPI;
    using VRage.Game.ModAPI;
    using VRage.ModAPI;
    using VRageMath;

    public static class NanobotSpatialCache
    {
        private const int CellSize = 128;
        private const int MaxEntries = 32;
        private const int TtlTicks = 45;

        private struct CacheEntry
        {
            public long CellKey;
            public int Tick;
            public int LastUsed;
            public List<long> GridIds;
        }

        private static readonly List<CacheEntry> Entries = new List<CacheEntry>();
        private static readonly List<long> BuildScratch = new List<long>();
        private static int _globalTick;

        public static void GetNearbyGrids(Vector3D position, float range, List<IMyCubeGrid> destination)
        {
            destination.Clear();
            if (MyAPIGateway.Entities == null) return;

            var tick = ++_globalTick;
            var cellKey = ComputeCellKey(position, range);
            var entryIndex = FindEntryIndex(cellKey);

            if (entryIndex >= 0)
            {
                var entry = Entries[entryIndex];
                if (tick - entry.Tick < TtlTicks)
                {
                    entry.LastUsed = tick;
                    Entries[entryIndex] = entry;
                    ResolveGrids(entry.GridIds, destination);
                    return;
                }

                Entries.RemoveAt(entryIndex);
            }

            BuildGridIds(position, range, BuildScratch);
            StoreEntry(cellKey, tick, BuildScratch);
            ResolveGrids(BuildScratch, destination);
        }

        private static long ComputeCellKey(Vector3D position, float range)
        {
            var cx = (int)(position.X / CellSize);
            var cy = (int)(position.Y / CellSize);
            var cz = (int)(position.Z / CellSize);
            var rangeBucket = (int)(range / 32f);

            unchecked
            {
                long key = cx;
                key = (key * 397) ^ cy;
                key = (key * 397) ^ cz;
                key = (key * 397) ^ rangeBucket;
                return key;
            }
        }

        private static int FindEntryIndex(long cellKey)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].CellKey == cellKey)
                    return i;
            }

            return -1;
        }

        private static void BuildGridIds(Vector3D position, float range, List<long> gridIds)
        {
            gridIds.Clear();

            var sphere = new BoundingSphereD(position, range);
            var entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
            if (entities == null || entities.Count == 0) return;

            for (int e = 0; e < entities.Count; e++)
            {
                var grid = entities[e] as IMyCubeGrid;
                if (grid == null || grid.Closed || grid.MarkedForClose) continue;

                var id = grid.EntityId;
                if (ContainsId(gridIds, id)) continue;
                gridIds.Add(id);
            }
        }

        private static void StoreEntry(long cellKey, int tick, List<long> gridIds)
        {
            if (Entries.Count >= MaxEntries)
                EvictOldest();

            var copy = new List<long>(gridIds.Count);
            for (int i = 0; i < gridIds.Count; i++)
                copy.Add(gridIds[i]);

            Entries.Add(new CacheEntry
            {
                CellKey = cellKey,
                Tick = tick,
                LastUsed = tick,
                GridIds = copy
            });
        }

        private static void EvictOldest()
        {
            var oldestIdx = 0;
            var oldestUsed = Entries[0].LastUsed;

            for (int i = 1; i < Entries.Count; i++)
            {
                if (Entries[i].LastUsed >= oldestUsed) continue;
                oldestUsed = Entries[i].LastUsed;
                oldestIdx = i;
            }

            Entries.RemoveAt(oldestIdx);
        }

        private static void ResolveGrids(List<long> gridIds, List<IMyCubeGrid> destination)
        {
            if (gridIds == null) return;

            for (int i = 0; i < gridIds.Count; i++)
            {
                var entity = MyAPIGateway.Entities.GetEntityById(gridIds[i]) as IMyCubeGrid;
                if (entity == null || entity.Closed || entity.MarkedForClose) continue;
                if (ContainsGrid(destination, entity)) continue;
                destination.Add(entity);
            }
        }

        private static bool ContainsId(List<long> ids, long id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id) return true;
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
    }
}
