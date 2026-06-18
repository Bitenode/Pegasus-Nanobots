namespace Pegasus.Nanobot
{
    using System.Collections.Generic;

    using Sandbox.ModAPI;
    using VRage.Game.ModAPI;
    using VRageMath;

    public struct WeldTarget
    {
        public IMySlimBlock Block;
        public IMyProjector Projector;
        public IMySlimBlock Projected;

        public bool IsProjection => Projector != null && Projected != null;

        public static WeldTarget FromBlock(IMySlimBlock block)
        {
            return new WeldTarget { Block = block };
        }

        public static WeldTarget FromProjection(IMyProjector projector, IMySlimBlock projected, IMySlimBlock block = null)
        {
            return new WeldTarget { Projector = projector, Projected = projected, Block = block };
        }
    }

    public static class NanobotProjectionHelper
    {
        public const int DefaultBlocksPerProjectorScan = 8;
        public const int MaxBlocksPerProjectorScan = 32;

        private const double MatchRangeSq = 9.0;
        private static readonly List<IMySlimBlock> RebuildScratch = new List<IMySlimBlock>();

        public static void RebuildProjectedCache(
            IMyProjector projector,
            Vector3D welderPos,
            double rangeSq,
            List<IMySlimBlock> cache)
        {
            cache.Clear();
            if (projector == null) return;

            var projectedGrid = projector.ProjectedGrid;
            if (projectedGrid == null || projectedGrid.Closed) return;

            RebuildScratch.Clear();
            projectedGrid.GetBlocks(RebuildScratch, block => block != null && !block.IsDestroyed);

            for (int i = 0; i < RebuildScratch.Count; i++)
            {
                var projected = RebuildScratch[i];
                Vector3D worldPos;
                projected.ComputeWorldCenter(out worldPos);
                if (Vector3D.DistanceSquared(worldPos, welderPos) > rangeSq) continue;
                cache.Add(projected);
            }
        }

        public static void ScanProjectorsOnGrid(
            IMyCubeGrid grid,
            Vector3D welderPos,
            double rangeSq,
            List<WeldTarget> output,
            List<IMySlimBlock> blockScratch,
            List<IMySlimBlock> projectedCache,
            ref long cacheProjectorId,
            ref long cacheGridId,
            ref int cacheIndex,
            int maxBuffer,
            int blocksPerProjectorScan)
        {
            if (grid == null || grid.Closed || output == null) return;
            if (output.Count >= maxBuffer) return;
            if (blocksPerProjectorScan <= 0) return;

            blockScratch.Clear();
            grid.GetBlocks(blockScratch, block => block.FatBlock is IMyProjector);

            for (int i = 0; i < blockScratch.Count; i++)
            {
                if (output.Count >= maxBuffer) return;

                var projector = blockScratch[i].FatBlock as IMyProjector;
                if (projector == null || projector.Closed) continue;
                if (!projector.Enabled || !projector.IsFunctional) continue;
                if (!projector.IsProjecting) continue;

                var projectedGrid = projector.ProjectedGrid;
                if (projectedGrid == null || projectedGrid.Closed) continue;

                var projectorId = projector.EntityId;
                var gridId = projectedGrid.EntityId;
                if (projectorId != cacheProjectorId || gridId != cacheGridId || projectedCache.Count == 0)
                {
                    cacheProjectorId = projectorId;
                    cacheGridId = gridId;
                    cacheIndex = 0;
                    RebuildProjectedCache(projector, welderPos, rangeSq, projectedCache);
                }

                ScanProjectorFromCache(
                    projector,
                    projectedCache,
                    ref cacheIndex,
                    blocksPerProjectorScan,
                    output,
                    maxBuffer);
            }
        }

        public static void ScanProjectorFromCache(
            IMyProjector projector,
            List<IMySlimBlock> cache,
            ref int index,
            int maxPerTick,
            List<WeldTarget> output,
            int maxBuffer)
        {
            if (projector == null || cache == null || output == null || maxPerTick <= 0) return;

            var processed = 0;
            while (index < cache.Count && processed < maxPerTick && output.Count < maxBuffer)
            {
                var projected = cache[index++];

                var check = projector.CanBuild(projected, false);
                if (check == BuildCheckResult.OK)
                {
                    output.Add(WeldTarget.FromProjection(projector, projected, null));
                    processed++;
                    continue;
                }

                if (check != BuildCheckResult.AlreadyBuilt)
                    continue;

                var physical = FindPhysicalBlock(projector, projected);
                if (physical == null || !NeedsWork(physical)) continue;

                output.Add(WeldTarget.FromProjection(projector, projected, physical));
                processed++;
            }
        }

        public static bool IsProjectedCandidate(IMyProjector projector, IMySlimBlock projected)
        {
            if (projector == null || projected == null) return false;

            var check = projector.CanBuild(projected, false);
            if (check == BuildCheckResult.OK)
                return true;

            if (check != BuildCheckResult.AlreadyBuilt)
                return false;

            var physical = FindPhysicalBlock(projector, projected);
            return physical != null && NeedsWork(physical);
        }

        public static IMySlimBlock EnsurePhysicalBlock(
            IMyProjector projector,
            IMySlimBlock projected,
            IMySlimBlock cachedPhysical,
            long ownerId,
            long builderId)
        {
            if (projector == null || projected == null || projector.Closed) return null;
            if (!projector.Enabled || !projector.IsFunctional) return null;

            if (cachedPhysical != null && !cachedPhysical.IsDestroyed && NeedsWork(cachedPhysical))
                return cachedPhysical;

            var check = projector.CanBuild(projected, false);
            if (check == BuildCheckResult.NotFound)
                return null;

            if (check == BuildCheckResult.OK)
            {
                if (ownerId == 0)
                    ownerId = projector.OwnerId;
                if (builderId == 0)
                    builderId = ownerId;
                if (ownerId == 0)
                    return null;

                projector.Build(projected, ownerId, builderId, false);
                return FindPhysicalBlock(projector, projected);
            }

            if (check == BuildCheckResult.AlreadyBuilt)
                return FindPhysicalBlock(projector, projected);

            return null;
        }

        public static IMySlimBlock FindPhysicalBlock(IMyProjector projector, IMySlimBlock projected)
        {
            if (projector?.CubeGrid == null || projected == null) return null;

            var hostGrid = projector.CubeGrid;
            Vector3D worldCenter;
            projected.ComputeWorldCenter(out worldCenter);

            var localPos = Vector3D.Transform(worldCenter, MatrixD.Invert(hostGrid.WorldMatrix));
            var gridPos = new Vector3I(
                (int)System.Math.Floor(localPos.X),
                (int)System.Math.Floor(localPos.Y),
                (int)System.Math.Floor(localPos.Z));

            var block = hostGrid.GetCubeBlock(gridPos);
            if (IsMatchingBlock(block, projected, worldCenter))
                return block;

            IMySlimBlock best = null;
            var bestDist = double.MaxValue;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;

                        var probe = hostGrid.GetCubeBlock(gridPos + new Vector3I(dx, dy, dz));
                        if (!IsMatchingBlock(probe, projected, worldCenter)) continue;

                        Vector3D center;
                        probe.ComputeWorldCenter(out center);
                        var distSq = Vector3D.DistanceSquared(center, worldCenter);
                        if (distSq >= bestDist) continue;

                        bestDist = distSq;
                        best = probe;
                    }
                }
            }

            return best;
        }

        private static bool IsMatchingBlock(IMySlimBlock block, IMySlimBlock projected, Vector3D worldCenter)
        {
            if (block == null || block.IsDestroyed || projected == null) return false;

            var projectedDef = projected.BlockDefinition;
            if (projectedDef != null && block.BlockDefinition != projectedDef) return false;

            Vector3D center;
            block.ComputeWorldCenter(out center);
            return Vector3D.DistanceSquared(center, worldCenter) <= MatchRangeSq;
        }

        public static bool NeedsWork(IMySlimBlock block)
        {
            if (block == null || block.IsDestroyed) return false;
            if (block.FatBlock != null && block.FatBlock.Closed) return false;
            if (block.Integrity < block.MaxIntegrity) return true;
            if (block.HasDeformation) return true;
            return block.BuildLevelRatio < 1f;
        }

        public static void GetWorldPosition(WeldTarget target, out Vector3D position)
        {
            if (target.IsProjection)
            {
                target.Projected.ComputeWorldCenter(out position);
                return;
            }

            if (target.Block?.CubeGrid != null)
                position = target.Block.CubeGrid.GridIntegerToWorld(target.Block.Position);
            else
                position = Vector3D.Zero;
        }

        public static long GetTargetKey(WeldTarget target)
        {
            if (target.IsProjection)
            {
                var pos = target.Projected.Position;
                return target.Projector.EntityId
                    ^ ((long)pos.X << 20)
                    ^ ((long)pos.Y << 10)
                    ^ pos.Z;
            }

            if (target.Block?.CubeGrid == null) return 0;
            var blockPos = target.Block.Position;
            return target.Block.CubeGrid.EntityId
                ^ ((long)blockPos.X << 16)
                ^ blockPos.Y
                ^ blockPos.Z;
        }

        public static string GetDisplayName(WeldTarget target)
        {
            var block = target.Block ?? target.Projected;
            if (block == null) return "Unknown";

            var def = block.BlockDefinition;
            if (def == null) return "Unknown";
            if (def.DisplayNameText != null && def.DisplayNameText.Length > 0)
                return def.DisplayNameText.ToString();
            return def.Id.SubtypeId.ToString();
        }

        public static int GetIntegrityPercent(WeldTarget target)
        {
            var block = target.Block ?? target.Projected;
            if (block == null || block.MaxIntegrity <= 0f) return 0;
            return (int)(block.Integrity / block.MaxIntegrity * 100f);
        }
    }
}
