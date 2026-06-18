namespace Pegasus.Nanobot
{
    using System.Collections.Generic;
    using System.Text;

    public static class NanobotFleetRegistry
    {
        public const int StaleEntryTicks = 6000;

        private struct Entry
        {
            public long WelderEntityId;
            public long GridId;
            public int WelderConfigId;
            public int StatusCode;
            public int LastSeenTick;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static int _globalTick;

        public static void Update(long welderEntityId, long gridId, int welderConfigId, int statusCode)
        {
            var tick = ++_globalTick;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].WelderEntityId != welderEntityId) continue;

                var entry = Entries[i];
                entry.GridId = gridId;
                entry.WelderConfigId = welderConfigId;
                entry.StatusCode = statusCode;
                entry.LastSeenTick = tick;
                Entries[i] = entry;
                return;
            }

            Entries.Add(new Entry
            {
                WelderEntityId = welderEntityId,
                GridId = gridId,
                WelderConfigId = welderConfigId,
                StatusCode = statusCode,
                LastSeenTick = tick
            });
        }

        public static void Remove(long welderEntityId)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].WelderEntityId == welderEntityId)
                    Entries.RemoveAt(i);
            }
        }

        public static void PruneStale()
        {
            var tick = _globalTick;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (tick - Entries[i].LastSeenTick > StaleEntryTicks)
                    Entries.RemoveAt(i);
            }
        }

        public static string BuildFleetSummary(long gridId)
        {
            var idle = 0;
            var welding = 0;
            var missing = 0;
            var error = 0;
            var off = 0;
            var other = 0;

            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (entry.GridId != gridId) continue;

                switch (entry.StatusCode)
                {
                    case 0: idle++; break;
                    case 1: welding++; break;
                    case 2: missing++; break;
                    case 3: error++; break;
                    case 4: off++; break;
                    case 5: off++; break;
                    case 6: other++; break;
                    default: other++; break;
                }
            }

            var total = idle + welding + missing + error + off + other;
            if (total == 0)
                return "Nanobot Fleet\nNo welders on grid";

            var sb = new StringBuilder(128);
            sb.Append("Nanobot Fleet (").Append(total).Append(")\n");
            if (welding > 0) sb.Append("Welding: ").Append(welding).Append('\n');
            if (idle > 0) sb.Append("Idle: ").Append(idle).Append('\n');
            if (missing > 0) sb.Append("Missing: ").Append(missing).Append('\n');
            if (error > 0) sb.Append("Error: ").Append(error).Append('\n');
            if (off > 0) sb.Append("Off: ").Append(off).Append('\n');
            if (other > 0) sb.Append("Other: ").Append(other).Append('\n');
            return sb.ToString().TrimEnd();
        }
    }
}
