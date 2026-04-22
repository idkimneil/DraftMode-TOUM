using DraftModeTOUM.Managers;
using HarmonyLib;

namespace DraftModeTOUM.Patches
{

    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastDraftStart))]
    public static class ShowCancelButtonOnDraftStart
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftCancelButton.Show();
            DraftModePlugin.Logger.LogInfo("[DraftCancelButton] Shown after BroadcastDraftStart.");
        }
    }


    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastRecap))]
    public static class HideCancelButtonOnRecap
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftCancelButton.Hide();
        }
    }


    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastCancelDraft))]
    public static class HideCancelButtonOnCancelDraft
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftCancelButton.Hide();
        }
    }


    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastDraftEnd))]
    public static class HideCancelButtonOnDraftEnd
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftCancelButton.Hide();
        }
    }
}
