namespace Pegasus.Nanobot
{
    using System;
    using System.Text;

    public sealed class DockDoctorConfig
    {
        public const int DefaultBoostSeconds = 120;
        public const int MaxBoostSeconds = 600;
        public const string StatusMarker = "---status---";

        public bool Enabled = true;
        public int BoostSeconds = DefaultBoostSeconds;
        public string RepairMode = "FunctionalFirst";
        public bool FastScan = true;

        public string UserConfigSection { get; private set; } = DefaultConfigHeader();

        public static string DefaultConfigHeader()
        {
            return "# Pegasus Dock Doctor\n"
                + "Enabled=true\nBoostSeconds=120\nRepairMode=FunctionalFirst\nFastScan=true";
        }

        public void Parse(string customData)
        {
            Enabled = true;
            BoostSeconds = DefaultBoostSeconds;
            RepairMode = "FunctionalFirst";
            FastScan = true;

            if (string.IsNullOrWhiteSpace(customData))
            {
                UserConfigSection = DefaultConfigHeader();
                return;
            }

            var header = new StringBuilder(256);
            var lines = customData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line == StatusMarker) break;
                if (line.StartsWith("=== Pegasus Dock Doctor", StringComparison.Ordinal)) break;

                if (line.StartsWith("#", StringComparison.Ordinal) || line.IndexOf('=') > 0)
                {
                    header.AppendLine(line);
                    ApplyLine(line);
                }
            }

            UserConfigSection = header.Length > 0 ? header.ToString().TrimEnd() : DefaultConfigHeader();
            if (BoostSeconds < 10) BoostSeconds = 10;
            if (BoostSeconds > MaxBoostSeconds) BoostSeconds = MaxBoostSeconds;
        }

        public string FormatCustomData(string statusBody)
        {
            var config = string.IsNullOrEmpty(UserConfigSection)
                ? DefaultConfigHeader()
                : UserConfigSection;

            var result = new StringBuilder(config.Length + statusBody.Length + 32);
            result.Append(config);
            result.Append('\n').Append(StatusMarker).Append('\n');
            result.Append(statusBody);
            return result.ToString();
        }

        public NanobotRepairMode GetRepairMode()
        {
            return NanobotRepairPriority.ParseMode(RepairMode);
        }

        private void ApplyLine(string line)
        {
            if (line.StartsWith("#", StringComparison.Ordinal)) return;

            var eq = line.IndexOf('=');
            if (eq <= 0) return;

            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim();

            if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
            {
                Enabled = ParseBool(value);
                return;
            }

            if (key.Equals("BoostSeconds", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, out parsed))
                    BoostSeconds = parsed;
                return;
            }

            if (key.Equals("RepairMode", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(value))
                    RepairMode = value;
                return;
            }

            if (key.Equals("FastScan", StringComparison.OrdinalIgnoreCase))
                FastScan = ParseBool(value);
        }

        private static bool ParseBool(string value)
        {
            return value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.Ordinal)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
