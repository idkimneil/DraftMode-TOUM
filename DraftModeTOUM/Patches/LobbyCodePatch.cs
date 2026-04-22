using HarmonyLib;
using InnerNet;

namespace DraftModeTOUM.Patches
{
    [HarmonyPatch]
    public static class LobbyCodePatch
    {
        public static int GameId;

        [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.JoinGame))]
        [HarmonyPostfix]
        public static void Postfix(InnerNetClient __instance)
        {
            GameId = __instance.GameId;
            DraftModePlugin.Logger.LogInfo($"[DraftModePlugin] Lobby code {GameCode.IntToGameName(GameId)} from GameId: {GameId}");
        }
    }
}
