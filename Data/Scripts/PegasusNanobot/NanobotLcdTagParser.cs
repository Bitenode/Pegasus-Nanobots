namespace Pegasus.Nanobot
{
    using System;
    using System.Collections.Generic;

    public static class NanobotLcdTagParser
    {
        public const string LcdNameTag = "[NB";

        public static bool ContainsTag(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf(LcdNameTag, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TryParse(string name, out int welderId, out string mode)
        {
            welderId = 0;
            mode = string.Empty;

            if (string.IsNullOrEmpty(name)) return false;

            var start = name.IndexOf(LcdNameTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return false;

            var innerStart = start + LcdNameTag.Length;
            if (innerStart >= name.Length) return true;

            string inner;
            var end = name.IndexOf(']', innerStart);
            if (end >= innerStart)
                inner = name.Substring(innerStart, end - innerStart);
            else
                inner = name.Substring(innerStart).Trim();

            if (inner.StartsWith(":", StringComparison.Ordinal))
                inner = inner.Substring(1);

            inner = inner.Trim();
            if (inner.Length == 0) return true;

            var parts = inner.Split(':');
            var modeParts = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                if (part.Length == 0) continue;

                int id;
                if (int.TryParse(part, out id))
                {
                    welderId = id;
                    continue;
                }

                if (part.Equals("all", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("*", StringComparison.Ordinal))
                {
                    welderId = 0;
                    continue;
                }

                modeParts.Add(part.ToLowerInvariant());
            }

            if (modeParts.Count == 1)
                mode = modeParts[0];
            else if (modeParts.Count > 1)
                mode = string.Join("-", modeParts);

            return true;
        }

        public static bool IsSharedGridMode(string mode)
        {
            return mode == "fleet"
                || mode == "supply"
                || mode == "alert-supply";
        }
    }
}
