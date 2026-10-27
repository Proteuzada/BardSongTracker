using Dalamud.Game.Command;
using Dalamud.Game.ClientState.JobGauge.Enums;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using BardSongTracker.Windows;
using System;

namespace BardSongTracker
{
    public sealed class Plugin : IDalamudPlugin
    {
        private const uint BardClassJobId = 23;

        private IDalamudPluginInterface PluginInterface { get; init; }
        public IClientState ClientState { get; init; }
        public IObjectTable ObjectTable { get; init; }
        public IJobGauges JobGauges { get; init; }
        public IFramework Framework { get; init; }
        public ICommandManager CommandManager { get; init; }
        public IPluginLog PluginLog { get; init; }
        public ITargetManager TargetManager { get; init; }

        public Configuration Configuration { get; init; }
        public WindowSystem WindowSystem { get; init; } = new("BardSongTracker");

        private ConfigWindow ConfigWindow { get; init; }
        private OverlayWindow OverlayWindow { get; init; }

        private SongTracker SongTracker { get; init; }
        private DotTracker DotTracker { get; init; }
        private SoundAlert SoundAlert { get; init; }

        public Plugin(
            IDalamudPluginInterface pluginInterface,
            IClientState clientState,
            IObjectTable objectTable,
            IJobGauges jobGauges,
            IFramework framework,
            ICommandManager commandManager,
            IPluginLog pluginLog,
            ITargetManager targetManager)
        {
            PluginInterface = pluginInterface;
            ClientState = clientState;
            ObjectTable = objectTable;
            JobGauges = jobGauges;
            Framework = framework;
            CommandManager = commandManager;
            PluginLog = pluginLog;
            TargetManager = targetManager;

            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(PluginInterface);

            SongTracker = new SongTracker();
            DotTracker = new DotTracker();
            SoundAlert = new SoundAlert();
            SoundAlert.SetVolume(Configuration.SoundVolume);

            ConfigWindow = new ConfigWindow(Configuration, SongTracker, DotTracker, SoundAlert, CurrentJobId);
            OverlayWindow = new OverlayWindow(Configuration, SongTracker, DotTracker);

            WindowSystem.AddWindow(ConfigWindow);
            WindowSystem.AddWindow(OverlayWindow);

            CommandManager.AddHandler("/bst", new CommandInfo(OnCommand)
            {
                HelpMessage = "Abre o menu do BardSongTracker (músicas e DoTs)."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUI;
            PluginInterface.UiBuilder.OpenMainUi += OnOpenConfigUI;
            Framework.Update += OnFrameworkUpdate;

            PluginLog.Info("BardSongTracker loaded successfully!");
        }

        private uint? CurrentJobId()
        {
            var localPlayer = ObjectTable.LocalPlayer;
            return localPlayer == null ? null : localPlayer.ClassJob.RowId;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            try
            {
                var localPlayer = ObjectTable.LocalPlayer;
                if (localPlayer == null)
                {
                    SongTracker.Update(null);
                    DotTracker.Clear();
                    return;
                }

                if (localPlayer.ClassJob.RowId == BardClassJobId)
                {
                    var brdGauge = JobGauges.Get<BRDGauge>();
                    SongTracker.Update(brdGauge);

                    var songThreshold = SongTracker.CurrentSong == Song.ArmysPaeon
                        ? Configuration.ArmysPaeonThreshold
                        : Configuration.AlertThreshold;

                    if (Configuration.PlaySound && SongTracker.IsExpiring(songThreshold))
                        SoundAlert.PlayAlert(SongTracker.RemainingTime);
                }
                else
                {
                    SongTracker.Update(null);
                }

                var target = TargetManager.Target as IBattleChara;
                DotTracker.Update(localPlayer, target, Configuration);

                if (Configuration.PlaySound && DotTracker.HasExpiring(Configuration.DotAlertThreshold))
                    SoundAlert.PlayAlert(0f);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error in framework update: {ex.Message}");
            }
        }

        private void OnCommand(string command, string args)
        {
            ConfigWindow.IsOpen = !ConfigWindow.IsOpen;
        }

        private void DrawUI()
        {
            WindowSystem.Draw();
        }

        private void OnOpenConfigUI()
        {
            ConfigWindow.IsOpen = true;
        }

        public void Dispose()
        {
            Framework.Update -= OnFrameworkUpdate;
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUI;
            PluginInterface.UiBuilder.OpenMainUi -= OnOpenConfigUI;

            CommandManager.RemoveHandler("/bst");

            WindowSystem.RemoveAllWindows();
            ConfigWindow.Dispose();
            OverlayWindow.Dispose();
            SoundAlert.Dispose();

            PluginLog.Info("BardSongTracker unloaded.");
        }
    }
}
