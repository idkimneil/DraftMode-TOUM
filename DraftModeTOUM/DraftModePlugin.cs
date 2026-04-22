using BepInEx;
using Reactor.Utilities.Attributes;
using TownOfUs.Networking;
using BepInEx.Logging;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using MiraAPI.PluginLoading;
using Reactor.Networking;
using Reactor.Networking.Attributes;
using UnityEngine;


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
            LoggingSystem.Initialize(Logger);
            LoggingSystem.Info($"DraftModeTOUM v{PluginInfo.PLUGIN_VERSION} loading...");

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            Logger.LogInfo("DraftModeTOUM loaded successfully!");
        }

        public override bool Unload()
        {
            _harmony?.UnpatchSelf();
            return base.Unload();
        }

        internal static class PluginInfo
        {
            public const string PLUGIN_GUID = "com.draftmodetoun.mod";
            public const string PLUGIN_NAME = "DraftModeTOUM";
            public const string PLUGIN_VERSION = "1.0.8";
        }

        [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
        public static class OnDisconnectPatch
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                DraftScreenController.Hide();
                DraftUiManager.CloseAll();
                DraftRecapOverlay.Hide();
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
