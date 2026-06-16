using MiraAPI.GameOptions;
using MiraAPI.Hud;
using UnityEngine;
using HarmonyLib;

namespace DraftModeTOUM.Patches
{

    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastDraftStart))]
    public static class ShowDraftRerollButtonOnDraftStart
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftRerollButton.Show();
            DraftModePlugin.Logger.LogInfo("[DraftRerollButton] Shown after BroadcastDraftStart.");
            CustomButtonSingleton<DraftRerollButton>.Instance.SetUses((int)OptionGroupSingleton<DraftModeOptions>.Instance.RerollsPerPlayer.Value);
        }
    }


    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastRecap))]
    public static class HideDraftReroll
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            CustomButtonSingleton<DraftRerollButton>.Instance.CreateButton(PlayerControl.LocalPlayer.GetComponent<HudManager>().transform);
        }
    }


    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastCancelDraft))]
    public static class HideDraftRerollOnCancelDraft
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftRerollButton.Hide();
            CustomButtonSingleton<DraftRerollButton>.Instance.SetUses((int)OptionGroupSingleton<DraftModeOptions>.Instance.RerollsPerPlayer.Value);
        }
    }


    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastDraftEnd))]
    public static class HideDraftRerollOnDraftEnd
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftRerollButton.Hide();
            CustomButtonSingleton<DraftRerollButton>.Instance.SetUses((int)OptionGroupSingleton<DraftModeOptions>.Instance.RerollsPerPlayer.Value);
        }
    }
    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastDraftEnd))]
    public static class HideDraftRerollAfterUsedUp
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftRerollButton.Hide();
            CustomButtonSingleton<DraftRerollButton>.Instance.SetUses((int)OptionGroupSingleton<DraftModeOptions>.Instance.RerollsPerPlayer.Value);
        }
    }
}
