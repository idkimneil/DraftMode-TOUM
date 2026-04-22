using DraftModeTOUM.Managers;
using HarmonyLib;
using MiraAPI.GameOptions;
using System.Linq;
using UnityEngine;
using TownOfUs.Utilities;


namespace DraftModeTOUM.Patches
{
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    public static class ChatPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First + 100)]
        public static bool Prefix(ChatController __instance)
        {
            string msg = __instance.freeChatField.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(msg)) return true;

            if (msg.StartsWith("/draft", System.StringComparison.OrdinalIgnoreCase)
                && !msg.StartsWith("/draftend", System.StringComparison.OrdinalIgnoreCase))
            {
                MiscUtils.AddFakeChat(PlayerControl.LocalPlayer.Data, "<color=#8BFDFD>System</color>", "/draft no longer exists, make sure you have Draft Mode enabled in the Settings and click the Start Button to start the Draft");
                ClearChat(__instance);
                return false;
            }

            if (msg.StartsWith("/draftend", System.StringComparison.OrdinalIgnoreCase))
            {
                MiscUtils.AddFakeChat(PlayerControl.LocalPlayer.Data, "<color=#8BFDFD>System</color>", "/draftend no longer exists, use the cancel button in the bottom left to end Draft Mode");
                ClearChat(__instance);
                return false;
            }

            return true;
        }

        private static void ClearChat(ChatController chat)
        {
            chat.freeChatField.Clear();
            chat.quickChatMenu.Clear();
            chat.quickChatField.Clear();
            chat.UpdateChatMode();
        }
    }
}
