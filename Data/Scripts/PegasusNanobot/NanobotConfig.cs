namespace Pegasus.Nanobot
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public sealed class NanobotConfig
    {
        public const float DefaultRange = 250f;
        public const float MaxRange = 500f;
        public const int DefaultConnectorHops = 2;
        public const int MaxConnectorHops = 5;
        public const int DefaultGridsPerScan = 5;
        public const int DefaultMaxScanBuffer = 80;
        public const int DefaultProjectionScanBlocks = 8;
        public const int MaxScanBufferLimit = 200;
        public const float DefaultWeldSpeed = 1f;
        public const float MinWeldSpeed = 0.25f;
        public const float MaxWeldSpeed = 4f;
        public const string StatusMarker = "---status---";

        public float Range = DefaultRange;
        public float WeldSpeed = DefaultWeldSpeed;
        public bool ScanUnloadedGrids;
        public bool ScanOwnGridOnly;
        public bool FactionOnly;
        public bool RepairProjections = true;
        public bool UseConnectors;
        public bool LowPowerMode = true;
        public int ConnectorHops = DefaultConnectorHops;
        public int GridsPerScan = DefaultGridsPerScan;
        public int MaxScanBuffer = DefaultMaxScanBuffer;
        public int ProjectionScanBlocks = DefaultProjectionScanBlocks;
        public bool ForceReset;
        public string LcdMode = "detail";
        public int WelderId;
        public NanobotRepairMode RepairMode = NanobotRepairMode.FunctionalFirst;
        public string PriorityBlocks = string.Empty;
        public string IgnoreBlocks = string.Empty;

        public readonly List<string> PriorityBlockKeywords = new List<string>();
        public readonly List<string> IgnoreBlockKeywords = new List<string>();

        public string UserConfigSection { get; private set; } = DefaultConfigHeader();

        public static string DefaultConfigHeader()
        {
            return "# Pegasus Nanobot Config (OwnerOnly = same-owner on server)\n"
                + "Range=250\nFactionOnly=false\nScanOwnGridOnly=false\n"
                + "UseConnectors=true\nConnectorHops=2\nRepairProjections=true\nGridsPerScan=5\n"
                + "LowPowerMode=true\nMaxScanBuffer=80\nProjectionScanBlocks=8\n"
                + "WeldSpeed=1\nScanUnloadedGrids=false\n"
                + "RepairMode=FunctionalFirst\nPriorityBlocks=\nIgnoreBlocks=\n"
                + "LcdMode=detail\nWelderId=0";
        }

        public void Parse(string customData)
        {
            Range = DefaultRange;
            ScanOwnGridOnly = false;
            FactionOnly = false;
            RepairProjections = true;
            UseConnectors = true;
            LowPowerMode = true;
            ConnectorHops = DefaultConnectorHops;
            GridsPerScan = DefaultGridsPerScan;
            MaxScanBuffer = DefaultMaxScanBuffer;
            ProjectionScanBlocks = DefaultProjectionScanBlocks;
            WeldSpeed = DefaultWeldSpeed;
            ScanUnloadedGrids = false;
            ForceReset = false;
            LcdMode = "detail";
            WelderId = 0;
            RepairMode = NanobotRepairMode.FunctionalFirst;
            PriorityBlocks = string.Empty;
            IgnoreBlocks = string.Empty;

            if (string.IsNullOrWhiteSpace(customData))
            {
                UserConfigSection = DefaultConfigHeader();
                return;
            }

            var header = new StringBuilder(384);
            var lines = customData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line == StatusMarker) break;
                if (line.StartsWith("=== Pegasus Nanobot", StringComparison.Ordinal)) break;

                if (line.StartsWith("#", StringComparison.Ordinal) || line.IndexOf('=') > 0)
                {
                    header.AppendLine(line);
                    ApplyLine(line);
                }
            }

            UserConfigSection = header.Length > 0 ? header.ToString().TrimEnd() : DefaultConfigHeader();
            if (Range <= 0f || Range > MaxRange)
                Range = DefaultRange;
            if (ConnectorHops < 0)
                ConnectorHops = 0;
            if (ConnectorHops > MaxConnectorHops)
                ConnectorHops = MaxConnectorHops;
            if (GridsPerScan < 1)
                GridsPerScan = 1;
            if (GridsPerScan > 20)
                GridsPerScan = 20;
            if (MaxScanBuffer < 20)
                MaxScanBuffer = 20;
            if (MaxScanBuffer > MaxScanBufferLimit)
                MaxScanBuffer = MaxScanBufferLimit;
            if (ProjectionScanBlocks < 1)
                ProjectionScanBlocks = 1;
            if (ProjectionScanBlocks > NanobotProjectionHelper.MaxBlocksPerProjectorScan)
                ProjectionScanBlocks = NanobotProjectionHelper.MaxBlocksPerProjectorScan;
            if (WeldSpeed < MinWeldSpeed)
                WeldSpeed = MinWeldSpeed;
            if (WeldSpeed > MaxWeldSpeed)
                WeldSpeed = MaxWeldSpeed;

            NanobotRepairPriority.ParseKeywordList(PriorityBlocks, PriorityBlockKeywords);
            NanobotRepairPriority.ParseKeywordList(IgnoreBlocks, IgnoreBlockKeywords);
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

        public void ClearForceResetFlag()
        {
            if (!ForceReset) return;
            ForceReset = false;
            UserConfigSection = UserConfigSection
                .Replace("ForceReset=true", "ForceReset=false")
                .Replace("ForceReset=True", "ForceReset=false")
                .Replace("ForceReset=1", "ForceReset=false")
                .Replace("ForceReset=yes", "ForceReset=false")
                .Replace("ForceReset=Yes", "ForceReset=false");
        }

        private void ApplyLine(string line)
        {
            if (line.StartsWith("#", StringComparison.Ordinal)) return;

            var eq = line.IndexOf('=');
            if (eq <= 0) return;

            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim();

            if (key.Equals("Range", StringComparison.OrdinalIgnoreCase))
            {
                float parsed;
                if (float.TryParse(value, out parsed))
                    Range = parsed;
                return;
            }

            if (key.Equals("ScanOwnGridOnly", StringComparison.OrdinalIgnoreCase))
            {
                ScanOwnGridOnly = ParseBool(value);
                return;
            }

            if (key.Equals("FactionOnly", StringComparison.OrdinalIgnoreCase)
                || key.Equals("OwnerOnly", StringComparison.OrdinalIgnoreCase))
            {
                FactionOnly = ParseBool(value);
                return;
            }

            if (key.Equals("RepairProjections", StringComparison.OrdinalIgnoreCase))
            {
                RepairProjections = ParseBool(value);
                return;
            }

            if (key.Equals("UseConnectors", StringComparison.OrdinalIgnoreCase))
            {
                UseConnectors = ParseBool(value);
                return;
            }

            if (key.Equals("ConnectorHops", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, out parsed))
                    ConnectorHops = parsed;
                return;
            }

            if (key.Equals("GridsPerScan", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, out parsed))
                    GridsPerScan = parsed;
                return;
            }

            if (key.Equals("LowPowerMode", StringComparison.OrdinalIgnoreCase))
            {
                LowPowerMode = ParseBool(value);
                return;
            }

            if (key.Equals("MaxScanBuffer", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, out parsed))
                    MaxScanBuffer = parsed;
                return;
            }

            if (key.Equals("ProjectionScanBlocks", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, out parsed))
                    ProjectionScanBlocks = parsed;
                return;
            }

            if (key.Equals("WeldSpeed", StringComparison.OrdinalIgnoreCase))
            {
                float parsed;
                if (float.TryParse(value, out parsed))
                    WeldSpeed = parsed;
                return;
            }

            if (key.Equals("ScanUnloadedGrids", StringComparison.OrdinalIgnoreCase))
            {
                ScanUnloadedGrids = ParseBool(value);
                return;
            }

            if (key.Equals("ForceReset", StringComparison.OrdinalIgnoreCase))
            {
                ForceReset = ParseBool(value);
                return;
            }

            if (key.Equals("LcdMode", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(value))
                    LcdMode = value.ToLowerInvariant();
                return;
            }

            if (key.Equals("WelderId", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, out parsed) && parsed >= 0)
                    WelderId = parsed;
                return;
            }

            if (key.Equals("RepairMode", StringComparison.OrdinalIgnoreCase))
            {
                RepairMode = NanobotRepairPriority.ParseMode(value);
                return;
            }

            if (key.Equals("PriorityBlocks", StringComparison.OrdinalIgnoreCase))
            {
                PriorityBlocks = value ?? string.Empty;
                return;
            }

            if (key.Equals("IgnoreBlocks", StringComparison.OrdinalIgnoreCase))
            {
                IgnoreBlocks = value ?? string.Empty;
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
