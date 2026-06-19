namespace Pegasus.Nanobot
{
    using System.Collections.Generic;

    using Sandbox.ModAPI;
    using VRage.Game.ModAPI;
    using VRageMath;

    public static class NanobotGridBlockCache
    {
        private const int MaxEntries = 16;
        private const int TtlTicks = 30;
        private const int TtlTicksLowPower = 60;

        private struct CachedBlock
        {
            public IMySlimBlock Block;
            public Vector3D WorldCenter;
        }

        private struct CacheEntry
        {
            public long GridId;
            public int Tick;
            public int LastUsed;
            public List<CachedBlock> Blocks;
        }

        private static readonly List<CacheEntry> Entries = new List<CacheEntry>();
        private static readonly List<IMySlimBlock> BuildScratch = new List<IMySlimBlock>();
        private static int _globalTick;

        public static void CollectDamagedInRange(
            IMyCubeGrid grid,
            Vector3D welderPos,
            double rangeSq,
            IMyShipWelder welder,
            int maxBuffer,
            int damagedCountCap,
            ref int damagedInRange,
            List<WeldTarget> scanBuffer,
            bool lowPowerMode,
            System.Func<IMySlimBlock, bool> shouldInclude = null)
        {
            if (grid == null || grid.Closed || scanBuffer == null) return;

            var tick = ++_globalTick;
            var ttl = lowPowerMode ? TtlTicksLowPower : TtlTicks;
            var gridId = grid.EntityId;
            var entryIndex = FindEntryIndex(gridId);
            List<CachedBlock> blocks;

            if (entryIndex >= 0)
            {
                var entry = Entries[entryIndex];
                var cacheValid = entry.Blocks != null && tick - entry.Tick < ttl;
                if (cacheValid)
                {
                    entry.LastUsed = tick;
                    Entries[entryIndex] = entry;
                    blocks = entry.Blocks;
                }
                else
                {
                    blocks = RefreshEntry(entryIndex, grid, tick);
                }
            }
            else
            {
                blocks = CreateEntry(grid, gridId, tick);
            }

            if (blocks == null) return;

            for (int i = 0; i < blocks.Count; i++)
            {
                var cached = blocks[i];
                var block = cached.Block;
                if (block == null || block.IsDestroyed) continue;
                if (!NeedsRepair(block)) continue;
                if (block.FatBlock != null && block.FatBlock == welder) continue;
                if (shouldInclude != null && !shouldInclude(block)) continue;

                if (Vector3D.DistanceSquared(cached.WorldCenter, welderPos) > rangeSq) continue;

                if (damagedInRange < damagedCountCap)
                    damagedInRange++;

                if (scanBuffer.Count < maxBuffer)
                    scanBuffer.Add(WeldTarget.FromBlock(block));
            }
        }

        private static List<CachedBlock> RefreshEntry(int entryIndex, IMyCubeGrid grid, int tick)
        {
            var blocks = BuildBlockList(grid);
            var entry = Entries[entryIndex];
            entry.Tick = tick;
            entry.LastUsed = tick;
            entry.Blocks = blocks;
            Entries[entryIndex] = entry;
            return blocks;
        }

        private static List<CachedBlock> CreateEntry(IMyCubeGrid grid, long gridId, int tick)
        {
            if (Entries.Count >= MaxEntries)
                EvictOldest();

            var blocks = BuildBlockList(grid);
            Entries.Add(new CacheEntry
            {
                GridId = gridId,
                Tick = tick,
                LastUsed = tick,
                Blocks = blocks
            });
            return blocks;
        }

        private static List<CachedBlock> BuildBlockList(IMyCubeGrid grid)
        {
            var result = new List<CachedBlock>();
            if (grid == null || grid.Closed) return result;

            BuildScratch.Clear();
            grid.GetBlocks(BuildScratch, NeedsRepair);

            for (int i = 0; i < BuildScratch.Count; i++)
            {
                var block = BuildScratch[i];
                if (block == null || block.IsDestroyed) continue;

                Vector3D worldCenter;
                block.ComputeWorldCenter(out worldCenter);
                result.Add(new CachedBlock { Block = block, WorldCenter = worldCenter });
            }

            return result;
        }

        private static int FindEntryIndex(long gridId)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].GridId == gridId)
                    return i;
            }

            return -1;
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

        private static bool NeedsRepair(IMySlimBlock block)
        {
            if (block == null || block.IsDestroyed) return false;
            if (block.FatBlock != null && block.FatBlock.Closed) return false;
            if (block.Integrity < block.MaxIntegrity) return true;
            if (block.HasDeformation) return true;
            return block.BuildLevelRatio < 1f;
        }
    }
}
