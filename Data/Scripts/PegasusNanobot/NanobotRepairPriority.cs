namespace Pegasus.Nanobot
{
    using System;
    using System.Collections.Generic;

    using VRage.Game.ModAPI;

    public enum NanobotRepairMode
    {
        Nearest,
        FunctionalFirst
    }

    public static class NanobotRepairPriority
    {
        private static readonly string[] DefaultFunctionalKeywords =
        {
            "Reactor", "Battery", "Thrust", "Gyro", "RemoteControl", "Cockpit", "Controller",
            "Weapon", "Turret", "Gatling", "Missile", "FixedGun",
            "Conveyor", "Connector", "Assembler", "Refinery", "Oxygen", "Medical", "Survival",
            "Antenna", "Beacon", "Sensor", "Camera", "Drill", "Collector", "Tank", "Farm"
        };

        public static NanobotRepairMode ParseMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return NanobotRepairMode.Nearest;

            if (value.Equals("FunctionalFirst", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Functional", StringComparison.OrdinalIgnoreCase)
                || value.Equals("CriticalFirst", StringComparison.OrdinalIgnoreCase))
            {
                return NanobotRepairMode.FunctionalFirst;
            }

            return NanobotRepairMode.Nearest;
        }

        public static void ParseKeywordList(string value, List<string> output)
        {
            output.Clear();
            if (string.IsNullOrWhiteSpace(value)) return;

            var parts = value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                if (part.Length > 0)
                    output.Add(part);
            }
        }

        public static bool IsIgnored(IMySlimBlock block, List<string> ignoreKeywords)
        {
            if (block == null || ignoreKeywords == null || ignoreKeywords.Count == 0)
                return false;

            return MatchesAnyKeyword(block, ignoreKeywords);
        }

        public static int GetPriorityScore(
            IMySlimBlock block,
            NanobotRepairMode mode,
            List<string> priorityKeywords)
        {
            if (block == null) return 1000;

            if (priorityKeywords != null && priorityKeywords.Count > 0
                && MatchesAnyKeyword(block, priorityKeywords))
            {
                return 0;
            }

            if (mode != NanobotRepairMode.FunctionalFirst)
                return 100;

            return GetFunctionalTier(block);
        }

        private static int GetFunctionalTier(IMySlimBlock block)
        {
            var typeName = GetBlockTypeName(block);

            if (ContainsAny(typeName, "Reactor", "Battery", "Thrust", "Gyro", "RemoteControl", "Cockpit", "Controller"))
                return 10;

            if (ContainsAny(typeName, "Weapon", "Turret", "Gatling", "Missile", "FixedGun", "Warhead"))
                return 20;

            if (ContainsAny(typeName, "Conveyor", "Connector", "Assembler", "Refinery", "Oxygen", "Medical", "Survival", "Drill", "Collector"))
                return 30;

            if (ContainsAny(typeName, "Antenna", "Beacon", "Sensor", "Camera", "Laser", "Radio"))
                return 40;

            if (ContainsAny(typeName, "Light", "Armor", "Corner", "Slope", "Panel"))
                return 200;

            for (int i = 0; i < DefaultFunctionalKeywords.Length; i++)
            {
                if (typeName.IndexOf(DefaultFunctionalKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return 50;
            }

            return 100;
        }

        private static bool MatchesAnyKeyword(IMySlimBlock block, List<string> keywords)
        {
            var typeName = GetBlockTypeName(block);
            for (int i = 0; i < keywords.Count; i++)
            {
                if (typeName.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string GetBlockTypeName(IMySlimBlock block)
        {
            var def = block.BlockDefinition;
            if (def == null) return string.Empty;

            var typeId = def.Id.TypeId.ToString();
            var subtypeId = def.Id.SubtypeId.ToString();
            if (subtypeId.Length == 0)
                return typeId;

            return typeId + "/" + subtypeId;
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
            {
                if (haystack.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
