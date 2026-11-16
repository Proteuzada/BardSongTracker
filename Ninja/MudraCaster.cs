using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System;
using System.Collections.Generic;

namespace BardSongTracker.Ninja
{
    /// <summary>
    /// One key runs one ninjutsu. The mudras go out in the official order,
    /// and Ninjutsu is pressed only after each of them was accepted.
    /// </summary>
    public sealed unsafe class MudraCaster
    {
        private const uint NinjaJob = 30;
        private const int StepBudgetMs = 1200;
        private const int MinimumGapMs = 420;

        private readonly IKeyState keys;
        private readonly IObjectTable objects;
        private readonly IPluginLog log;
        private readonly Configuration configuration;
        private readonly HashSet<int> held = new();
        private readonly List<uint> steps = new();
        private readonly Hook<ExecuteSlotDelegate>? executeSlot;
        private readonly Hook<ExecuteSlotByIdDelegate>? executeSlotById;
        private readonly Hook<IsSlotUsableDelegate>? slotUsable;

        private int index = -1;
        private bool sawLock;
        private long deadline;
        private long nextGap;

        public MudraCaster(IKeyState keys, IObjectTable objects, IGameInteropProvider interop, IPluginLog log, Configuration configuration)
        {
            this.keys = keys;
            this.objects = objects;
            this.log = log;
            this.configuration = configuration;
            executeSlot = Arm<ExecuteSlotDelegate>(interop, RaptureHotbarModule.Addresses.ExecuteSlot.Value, ExecuteSlotDetour);
            executeSlotById = Arm<ExecuteSlotByIdDelegate>(interop, RaptureHotbarModule.Addresses.ExecuteSlotById.Value, ExecuteSlotByIdDetour);
            slotUsable = Arm<IsSlotUsableDelegate>(interop, RaptureHotbarModule.HotbarSlot.Addresses.IsSlotUsable.Value, IsSlotUsableDetour);
        }

        public void Dispose()
        {
            executeSlot?.Dispose();
            executeSlotById?.Dispose();
            slotUsable?.Dispose();
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

        public unsafe string? Place(int hotbar, int slot, uint actionId)
        {
            if (hotbar is < 1 or > 10 || slot is < 1 or > 12)
                return "Use hotbar 1–10 and slot 1–12.";

            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
                return "Hotbar is not ready yet.";
            if ((module->ActiveHotbarClassJobId & 0x7F) != NinjaJob)
                return "Switch to Ninja first. The icon is saved on the job you are playing.";

            module->SetAndSaveSlot((uint)(hotbar - 1), (uint)(slot - 1), RaptureHotbarModule.HotbarSlotType.Action, actionId, false, false);
            var written = module->GetSlotById((uint)(hotbar - 1), (uint)(slot - 1));
            if (written == null
                || written->CommandType != RaptureHotbarModule.HotbarSlotType.Action
                || written->CommandId != actionId)
                return "The hotbar did not take that icon.";

            written->LoadIconId();
            return null;
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

        private unsafe byte ExecuteSlotDetour(RaptureHotbarModule* module, RaptureHotbarModule.HotbarSlot* slot)
        {
            if (Accept(slot))
                return 1;

            return executeSlot!.Original(module, slot);
        }

        private unsafe byte ExecuteSlotByIdDetour(RaptureHotbarModule* module, uint hotbarId, uint slotId)
        {
            var slot = module == null ? null : module->GetSlotById(hotbarId, slotId);
            if (Accept(slot))
                return 1;

            return executeSlotById!.Original(module, hotbarId, slotId);
        }

        private unsafe byte IsSlotUsableDetour(RaptureHotbarModule.HotbarSlot* slot, RaptureHotbarModule.HotbarSlotType type, uint actionId)
        {
            if (type == RaptureHotbarModule.HotbarSlotType.Action && configuration.NinjaMudraKeys && MudraTable.IsJutsu(actionId))
            {
                var player = objects.LocalPlayer;
                if (player != null && player.ClassJob.RowId == NinjaJob)
                    return 1;
            }

            return slotUsable!.Original(slot, type, actionId);
        }

        private unsafe bool Accept(RaptureHotbarModule.HotbarSlot* slot)
        {
            if (slot == null || HotkeysSuspended || !configuration.NinjaMudraKeys)
                return false;
            if (slot->CommandType != RaptureHotbarModule.HotbarSlotType.Action)
                return false;
            if (!MudraTable.TryGet(slot->CommandId, out var jutsu))
                return false;

            var player = objects.LocalPlayer;
            if (player == null || player.ClassJob.RowId != NinjaJob || Typing())
                return false;
            if (index >= 0 || HasStatus(player, MudraTable.MudraStatus) || HasStatus(player, MudraTable.TenChiJinStatus))
                return true;

            Begin(player, jutsu);
            return true;
        }

        private Hook<T>? Arm<T>(IGameInteropProvider interop, IntPtr address, T detour) where T : Delegate
        {
            if (address == IntPtr.Zero)
            {
                log.Error("Ninja hotbar hook is missing.");
                return null;
            }

            Hook<T>? hook = null;
            try
            {
                hook = interop.HookFromAddress<T>(address, detour);
                hook.Enable();
                return hook;
            }
            catch (Exception ex)
            {
                log.Error("Ninja hotbar hook failed: " + ex.Message);
                hook?.Dispose();
                return null;
            }
        }

        private unsafe delegate byte ExecuteSlotDelegate(RaptureHotbarModule* module, RaptureHotbarModule.HotbarSlot* slot);

        private unsafe delegate byte ExecuteSlotByIdDelegate(RaptureHotbarModule* module, uint hotbarId, uint slotId);

        private unsafe delegate byte IsSlotUsableDelegate(RaptureHotbarModule.HotbarSlot* slot, RaptureHotbarModule.HotbarSlotType type, uint actionId);

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
