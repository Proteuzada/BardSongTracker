using System;
using System.Collections.Generic;

namespace BardSongTracker
{
    public readonly record struct StatusSnapshot(uint StatusId, uint SourceId, float Remaining);

    public readonly record struct ActiveDot(string SkillId, string Label, float Remaining, DotAnchor Anchor, CombatRole Role);

    /// <summary>
    /// Picks the local player's enabled DoTs out of a status list.
    /// A hit requires the current job, an enabled skill, and the player as the source.
    /// </summary>
    public static class DotMatcher
    {
        public static void Collect(
            List<ActiveDot> destination,
            uint playerEntityId,
            uint playerJobId,
            IReadOnlyList<StatusSnapshot> selfStatuses,
            IReadOnlyList<StatusSnapshot> targetStatuses,
            Func<string, bool> isEnabled)
        {
            destination.Clear();

            foreach (var skill in DotCatalog.ForJob(playerJobId))
            {
                if (!isEnabled(skill.Id))
                    continue;

                var statuses = skill.Anchor == DotAnchor.Self ? selfStatuses : targetStatuses;
                float bestRemaining = 0f;
                string? label = null;

                foreach (var status in statuses)
                {
                    if (status.Remaining <= 0f || !skill.Contains(status.StatusId))
                        continue;

                    if (!IsOwnedByPlayer(status.SourceId, playerEntityId, skill.Anchor))
                        continue;

                    if (label == null || status.Remaining > bestRemaining)
                    {
                        bestRemaining = status.Remaining;
                        label = skill.LabelFor(status.StatusId);
                    }
                }

                if (label != null)
                {
                    destination.Add(new ActiveDot(skill.Id, label, bestRemaining, skill.Anchor, DotCatalog.RoleOf(skill.JobId)));
                }
            }

            destination.Sort(static (a, b) => a.Remaining.CompareTo(b.Remaining));
        }

        public static bool IsOwnedByPlayer(uint sourceId, uint playerEntityId, DotAnchor anchor)
        {
            if (sourceId == playerEntityId)
                return true;

            // Ground effects on the player are sometimes stamped with the "self" id.
            return anchor == DotAnchor.Self && sourceId == DotCatalog.SelfSourceId;
        }
    }
}
