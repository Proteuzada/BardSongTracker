using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;

namespace BardSongTracker.Ninja
{
    /// <summary>
    /// One key runs one ninjutsu. The mudras go out in the official order,
    /// and Ninjutsu is pressed only after each of them was accepted.
    /// </summary>
    public sealed class MudraCaster
    {
        private const uint NinjaJob = 30;
        private const int StepBudgetMs = 1200;
        private const int MinimumGapMs = 420;

        private readonly IKeyState keys;
        private readonly HashSet<int> held = new();
        private readonly List<uint> steps = new();

        private int index = -1;
        private bool sawLock;
        private long deadline;
        private long nextGap;

        public MudraCaster(IKeyState keys)
        {
            this.keys = keys;
        }

        public bool HotkeysSuspended { get; set; }

        public string? ActiveName { get; private set; }

        public void Tick(IPlayerCharacter? player, Configuration configuration)
        {
            Advance();
            if (index >= 0 || HotkeysSuspended || configuration == null || !configuration.NinjaMudraKeys)
                return;
            if (player == null || player.ClassJob.RowId != NinjaJob)
                return;
            if (Typing() || HasStatus(player, MudraTable.MudraStatus) || HasStatus(player, MudraTable.TenChiJinStatus))
                return;

            var pressed = Pressed();
            if (pressed == 0)
                return;

            foreach (var jutsu in MudraTable.All)
            {
                if (configuration.MudraKey(jutsu.Id) != pressed)
                    continue;

                Begin(player, jutsu);
                return;
            }
        }

        private void Begin(IPlayerCharacter player, MudraTable.Jutsu jutsu)
        {
            steps.Clear();
            if (jutsu.NeedsKassatsu && !HasStatus(player, MudraTable.KassatsuStatus))
                steps.Add(MudraTable.Kassatsu);
            steps.AddRange(jutsu.Mudras);
            steps.Add(MudraTable.Ninjutsu);
            index = 0;
            sawLock = false;
            nextGap = 0;
            deadline = Environment.TickCount64 + 2500;
            ActiveName = jutsu.Name;
        }

        public int CaptureKey()
        {
            return HotkeysSuspended ? Pressed() : 0;
        }

        private unsafe void Advance()
        {
            if (index < 0)
                return;

            var now = Environment.TickCount64;
            if (now > deadline)
            {
                Stop();
                return;
            }

            var manager = ActionManager.Instance();
            if (manager == null)
            {
                Stop();
                return;
            }

            var locked = manager->AnimationLock > 0.05f;
            if (!sawLock && index == 0 && locked)
                return;
            if (sawLock)
            {
                if (locked || now < nextGap)
                    return;
                sawLock = false;
            }
            else if (now < nextGap)
            {
                return;
            }

            if (index >= steps.Count)
            {
                Stop();
                return;
            }

            var accepted = manager->UseAction(ActionType.Action, steps[index], 0xE0000000, 0, ActionManager.UseActionMode.None, 0, null);
            if (!accepted)
            {
                Stop();
                return;
            }

            index++;
            if (index >= steps.Count)
            {
                Stop();
                return;
            }

            sawLock = true;
            nextGap = now + MinimumGapMs;
            deadline = now + StepBudgetMs;
        }

        private void Stop()
        {
            index = -1;
            steps.Clear();
            ActiveName = null;
            sawLock = false;
        }

        private int Pressed()
        {
            var found = 0;
            foreach (var key in keys.GetValidVirtualKeys())
            {
                var code = (int)key;
                if (code < 8 || IsModifier(key))
                    continue;

                var down = keys[key];
                var was = held.Contains(code);
                if (down && !was && found == 0)
                    found = code;
                if (down)
                    held.Add(code);
                else
                    held.Remove(code);
            }

            return found;
        }

        private static bool IsModifier(VirtualKey key)
        {
            return key is VirtualKey.SHIFT or VirtualKey.CONTROL or VirtualKey.MENU
                or VirtualKey.LSHIFT or VirtualKey.RSHIFT
                or VirtualKey.LCONTROL or VirtualKey.RCONTROL
                or VirtualKey.LMENU or VirtualKey.RMENU;
        }

        private static bool HasStatus(IPlayerCharacter player, uint statusId)
        {
            foreach (var status in player.StatusList)
            {
                if (status.StatusId == statusId && status.RemainingTime > 0f)
                    return true;
            }

            return false;
        }

        private static unsafe bool Typing()
        {
            var ui = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->GetUIModule();
            if (ui == null)
                return true;

            var atk = ui->GetRaptureAtkModule();
            return atk == null || atk->IsTextInputActive();
        }
    }
}
