using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using MiraAPI.PluginLoading;
using Reactor.Networking;
using Reactor.Networking.Attributes;
using Reactor.Utilities;
using DraftMode;

namespace DraftModeTOUM
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("gg.reactor.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("mira.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("auavengers.tou.mira", BepInDependency.DependencyFlags.HardDependency)]
    [ReactorModFlags(ModFlags.RequireOnAllClients)]
    public class DraftModePlugin : BasePlugin, IMiraPlugin
    {
        public static ManualLogSource Logger;
        private Harmony _harmony;

        public string OptionsTitleText => "Draft Mode";

        public ConfigFile GetConfigFile() => Config;

        public override void Load()
        {
            Logger = Log;

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            Logger.LogInfo("Draft Mode loaded successfully!");
            ReactorCredits.Register<DraftModePlugin>(ReactorCredits.AlwaysShow);
        }

        public override bool Unload()
        {
            _harmony?.UnpatchSelf();
            return base.Unload();
        }

        internal static class PluginInfo
        {
            public const string PLUGIN_GUID = "com.draftmodetoum.mod";
            public const string PLUGIN_NAME = "Draft Mode";
            public const string PLUGIN_VERSION = "2.0.0";
        }

        [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
        public static class OnDisconnectPatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                DraftScreenController.Hide();
                DraftCancelButton.Hide();
                bool draftStillInProgress = DraftManager.IsDraftActive;
                DraftManager.Reset(cancelledBeforeCompletion: draftStillInProgress);

                DraftStatusOverlay.ClearHudReferences();
                Logger.LogInfo($"[DraftModePlugin] Session cleared on disconnect.");
            }
        }

        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
        public static class MainMenuManagerStartPatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                DraftStatusOverlay.ClearHudReferences();
                Logger.LogInfo("[DraftModePlugin] MainMenu initialized.");
            }
        }
    }
}