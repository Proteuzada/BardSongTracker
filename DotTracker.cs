using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Statuses;
using System.Collections.Generic;

namespace BardSongTracker
{
    /// <summary>
    /// Reads the player's own statuses and the current target's statuses once per frame.
    /// </summary>
    public sealed class DotTracker
    {
        private readonly List<ActiveDot> active = new();
        private readonly List<StatusSnapshot> selfBuffer = new();
        private readonly List<StatusSnapshot> targetBuffer = new();

        public IReadOnlyList<ActiveDot> Active => active;

        public void Clear() => active.Clear();

        public bool HasExpiring(float threshold)
        {
            foreach (var dot in active)
            {
                if (dot.Remaining > 0f && dot.Remaining <= threshold)
                    return true;
            }

            return false;
        }

        public void Update(IBattleChara player, IBattleChara? target, Configuration configuration)
        {
            if (!configuration.TrackDots)
            {
                active.Clear();
                return;
            }

            Fill(selfBuffer, player.StatusList);
            targetBuffer.Clear();

            if (target != null && target.EntityId != player.EntityId)
                Fill(targetBuffer, target.StatusList);

            DotMatcher.Collect(
                active,
                player.EntityId,
                player.ClassJob.RowId,
                selfBuffer,
                targetBuffer,
                configuration.IsDotEnabled);
        }

        private static void Fill(List<StatusSnapshot> buffer, StatusList statuses)
        {
            buffer.Clear();
            foreach (var status in statuses)
            {
                if (status.StatusId == 0 || status.RemainingTime <= 0f)
                    continue;

                buffer.Add(new StatusSnapshot(status.StatusId, status.SourceId, status.RemainingTime));
            }
        }
    }
}
