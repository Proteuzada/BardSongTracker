namespace BardSongTracker.Parse
{
    /// <summary>
    /// Field order of an ACT network ability line (types 21 and 22).
    /// The fourteen unnamed slots after the first damage value are the other
    /// seven flag/amount pairs. FFLogs reads this same file.
    /// </summary>
    internal static class ActFields
    {
        public static readonly string[] Ability =
        {
            "type", "timestamp", "sourceId", "source", "id", "ability", "targetId", "target",
            "flags", "damage",
            "flags2", "damage2", "flags3", "damage3", "flags4", "damage4",
            "flags5", "damage5", "flags6", "damage6", "flags7", "damage7", "flags8", "damage8",
            "targetCurrentHp", "targetMaxHp", "targetCurrentMp", "targetMaxMp",
            "unknown1", "unknown2",
            "targetX", "targetY", "targetZ", "targetHeading",
            "currentHp", "maxHp", "currentMp", "maxMp",
            "unknown3", "unknown4",
            "x", "y", "z", "heading",
            "sequence", "targetIndex", "targetCount", "ownerId", "ownerName",
            "effectDisplayType", "actionId", "actionAnimationId", "animationLockTime", "rotationHex",
        };

        public static int AbilityIndex(string name)
        {
            for (var i = 0; i < Ability.Length; i++)
            {
                if (Ability[i] == name)
                    return i;
            }

            return -1;
        }
    }
}
