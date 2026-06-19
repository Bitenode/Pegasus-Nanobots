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
            public string MissingParts;
        }

        private static readonly List<Entry> Entries = new List<Entry>();
        private static int _globalTick;

        public static void Update(
            long welderEntityId,
            long gridId,
            int welderConfigId,
            int statusCode,
            string missingParts)
        {
            var tick = ++_globalTick;
            if (missingParts == null)
                missingParts = string.Empty;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].WelderEntityId != welderEntityId) continue;

                var entry = Entries[i];
                entry.GridId = gridId;
                entry.WelderConfigId = welderConfigId;
                entry.StatusCode = statusCode;
                entry.LastSeenTick = tick;
                entry.MissingParts = missingParts;
                Entries[i] = entry;
                return;
            }

            Entries.Add(new Entry
            {
                WelderEntityId = welderEntityId,
                GridId = gridId,
                WelderConfigId = welderConfigId,
                StatusCode = statusCode,
                LastSeenTick = tick,
                MissingParts = missingParts
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

        public static string BuildSupplySummary(long gridId)
        {
            var totals = new Dictionary<string, int>();
            var starvedWelders = 0;

            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (entry.GridId != gridId) continue;
                if (entry.StatusCode != 2) continue;

                starvedWelders++;
                if (string.IsNullOrEmpty(entry.MissingParts)) continue;

                var parts = entry.MissingParts.Split(';');
                for (int p = 0; p < parts.Length; p++)
                {
                    var part = parts[p];
                    if (part.Length == 0) continue;

                    var colon = part.IndexOf(':');
                    if (colon <= 0) continue;

                    var name = part.Substring(0, colon);
                    int amount;
                    if (!int.TryParse(part.Substring(colon + 1), out amount) || amount <= 0)
                        continue;

                    int existing;
                    if (totals.TryGetValue(name, out existing))
                        totals[name] = existing + amount;
                    else
                        totals[name] = amount;
                }
            }

            if (starvedWelders == 0)
                return "Nanobot Supply\nAll welders stocked";

            var sb = new StringBuilder(256);
            sb.Append("Nanobot Supply (").Append(starvedWelders).Append(" starved)\n");

            foreach (var pair in totals)
                sb.Append("  ").Append(pair.Key).Append(": ").Append(pair.Value).Append('\n');

            if (totals.Count == 0)
                sb.Append("  (waiting for parts)\n");

            return sb.ToString().TrimEnd();
        }

        public static bool HasStarvedWelders(long gridId)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (entry.GridId == gridId && entry.StatusCode == 2)
                    return true;
            }

            return false;
        }

        public static void CollectMissingParts(long gridId, Dictionary<string, int> totals)
        {
            totals.Clear();

            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (entry.GridId != gridId) continue;
                if (entry.StatusCode != 2) continue;
                if (string.IsNullOrEmpty(entry.MissingParts)) continue;

                var parts = entry.MissingParts.Split(';');
                for (int p = 0; p < parts.Length; p++)
                {
                    var part = parts[p];
                    if (part.Length == 0) continue;

                    var colon = part.IndexOf(':');
                    if (colon <= 0) continue;

                    var name = part.Substring(0, colon);
                    int amount;
                    if (!int.TryParse(part.Substring(colon + 1), out amount) || amount <= 0)
                        continue;

                    int existing;
                    if (totals.TryGetValue(name, out existing))
                        totals[name] = existing + amount;
                    else
                        totals[name] = amount;
                }
            }
        }
    }
}
