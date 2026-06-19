namespace Pegasus.Nanobot
{
    using System.Collections.Generic;

    public static class NanobotDockDoctorRegistry
    {
        public const int DefaultBoostTicks = 3600;

        private struct BoostEntry
        {
            public long GridId;
            public int RemainingTicks;
            public NanobotRepairMode RepairMode;
            public bool FastScan;
        }

        private static readonly List<BoostEntry> Entries = new List<BoostEntry>();

        public static void ApplyBoost(long gridId, int durationTicks, NanobotRepairMode repairMode, bool fastScan)
        {
            if (gridId == 0) return;
            if (durationTicks <= 0)
                durationTicks = DefaultBoostTicks;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].GridId != gridId) continue;

                var entry = Entries[i];
                entry.RemainingTicks = durationTicks;
                entry.RepairMode = repairMode;
                entry.FastScan = fastScan;
                Entries[i] = entry;
                return;
            }

            Entries.Add(new BoostEntry
            {
                GridId = gridId,
                RemainingTicks = durationTicks,
                RepairMode = repairMode,
                FastScan = fastScan
            });
        }

        public static void RemoveBoost(long gridId)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].GridId == gridId)
                    Entries.RemoveAt(i);
            }
        }

        public static void Tick()
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                entry.RemainingTicks--;
                if (entry.RemainingTicks <= 0)
                    Entries.RemoveAt(i);
                else
                    Entries[i] = entry;
            }
        }

        public static bool TryGetBoost(long gridId, out NanobotRepairMode repairMode, out bool fastScan)
        {
            repairMode = NanobotRepairMode.Nearest;
            fastScan = false;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].GridId != gridId) continue;

                repairMode = Entries[i].RepairMode;
                fastScan = Entries[i].FastScan;
                return true;
            }

            return false;
        }
    }
}
