namespace Pegasus.Nanobot
{
    using System;
    using System.Text;

    public sealed class AssemblerLinkConfig
    {
        public const int DefaultMaxQueueItems = 5;
        public const int MaxQueueItemsLimit = 20;
        public const string StatusMarker = "---status---";

        public bool AutoAssemble = true;
        public int MaxQueueItems = DefaultMaxQueueItems;
        public string AllowedComponents = string.Empty;
        public int ScanInterval = 30;

        public string UserConfigSection { get; private set; } = DefaultConfigHeader();

        public static string DefaultConfigHeader()
        {
            return "# Pegasus Assembler Link\n"
                + "AutoAssemble=true\nMaxQueueItems=5\nAllowedComponents=\nScanInterval=30";
        }

        public void Parse(string customData)
        {
            AutoAssemble = true;
            MaxQueueItems = DefaultMaxQueueItems;
            AllowedComponents = string.Empty;
            ScanInterval = 30;

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
                if (line.StartsWith("=== Pegasus Assembler Link", StringComparison.Ordinal)) break;

                if (line.StartsWith("#", StringComparison.Ordinal) || line.IndexOf('=') > 0)
                {
                    header.AppendLine(line);
                    ApplyLine(line);
                }
            }

            UserConfigSection = header.Length > 0 ? header.ToString().TrimEnd() : DefaultConfigHeader();
            if (MaxQueueItems < 1) MaxQueueItems = 1;
            if (MaxQueueItems > MaxQueueItemsLimit) MaxQueueItems = MaxQueueItemsLimit;
            if (ScanInterval < 10) ScanInterval = 10;
            if (ScanInterval > 600) ScanInterval = 600;
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

        public bool IsComponentAllowed(string componentName)
        {
            if (string.IsNullOrWhiteSpace(AllowedComponents))
                return true;

            var parts = AllowedComponents.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (componentName.Equals(parts[i].Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void ApplyLine(string line)
        {
            if (line.StartsWith("#", StringComparison.Ordinal)) return;

            var eq = line.IndexOf('=');
            if (eq <= 0) return;

            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim();

            if (key.Equals("AutoAssemble", StringComparison.OrdinalIgnoreCase))
            {
                AutoAssemble = ParseBool(value);
                return;
            }

            if (key.Equals("MaxQueueItems", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, out parsed))
                    MaxQueueItems = parsed;
                return;
            }

            if (key.Equals("AllowedComponents", StringComparison.OrdinalIgnoreCase))
            {
                AllowedComponents = value ?? string.Empty;
                return;
            }

            if (key.Equals("ScanInterval", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, out parsed))
                    ScanInterval = parsed;
            }
        }

        private static bool ParseBool(string value)
        {
            return value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.Ordinal)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
