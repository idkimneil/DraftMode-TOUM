
using DraftMode.Options;
using MiraAPI.Utilities;
using MiraAPI.GameOptions;
using MiraAPI.Hud;
using TownOfUs.Buttons;
using UnityEngine;
using HarmonyLib;


namespace DraftMode;

public sealed class DraftRerollButton : TownOfUsButton
{
    public static void Show()
    {
        CustomButtonSingleton<DraftRerollButton>.Instance.Disabled = false;
    }

    public static void Hide()
    {
        CustomButtonSingleton<DraftRerollButton>.Instance.Disabled = true;
    }

    public static void ShowAndReset()
    {
        Show();
        CustomButtonSingleton<DraftRerollButton>.Instance.SetUses(
            (int)OptionGroupSingleton<DraftOptions>.Instance.RerollsPerPlayer.Value);
    }

    public static void HideAndReset()
    {
        Hide();
        CustomButtonSingleton<DraftRerollButton>.Instance.SetUses(
            (int)OptionGroupSingleton<DraftOptions>.Instance.RerollsPerPlayer.Value);
    }

    public override string Name => "Reroll";
    public override float InitialCooldown => 0.001f;
    public override float Cooldown => 0.001f;
    public override int MaxUses => (int)OptionGroupSingleton<DraftOptions>.Instance.RerollsPerPlayer.Value;
    public override bool ZeroIsInfinite { get; set; } = true;
    public override ButtonLocation Location => ButtonLocation.BottomRight;
    public override Color TextOutlineColor => new Color32(255, 221, 0, 255);

    public override LoadableAsset<Sprite> Sprite => DraftAssets.RerollSprite;

    public override bool Disabled { get; set; } = true;
    public override void CreateButton(Transform parent)
    {
        base.CreateButton(parent);
    }
    public override bool Enabled(RoleBehaviour role)
    {
        return DraftManager.IsDraftActive && !Disabled && MaxUses > 0 ;
    }

    public override bool CanUse()
    {
        return DraftManager.GetStateForPlayer(PlayerControl.LocalPlayer.PlayerId).IsPickingNow && DraftManager.IsDraftActive && !Disabled && MaxUses > 0 && UsesLeft>0;
    }

    protected override void OnClick()
    {
        if (!DraftManager.IsDraftActive) return;
        Helpers.CreateAndShowNotification("Your picks have been rerolled!", Color.white,
                new Vector3(0f, 1f, -80f), spr: DraftAssets.RerollSprite.LoadAsset());
        DraftNetworkHelper.RequestReroll();    
        }

    [HarmonyPatch(typeof(DraftRpcs), nameof(DraftRpcs.RpcStartDraft))]
    public static class ShowDraftRerollButtonOnDraftStart
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Show();
            CustomButtonSingleton<DraftRerollButton>.Instance.SetUses((int)OptionGroupSingleton<DraftOptions>.Instance.RerollsPerPlayer.Value);
        }
    }


    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastRecap))]
    public static class HideDraftReroll
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftRerollButton.HideAndReset();
        }
    }


    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastCancelDraft))]
    public static class HideDraftRerollOnCancelDraft
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftRerollButton.Hide();
            CustomButtonSingleton<DraftRerollButton>.Instance.SetUses((int)OptionGroupSingleton<DraftOptions>.Instance.RerollsPerPlayer.Value);
        }
    }


    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastDraftEnd))]
    public static class HideDraftRerollOnDraftEnd
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftRerollButton.Hide();
            CustomButtonSingleton<DraftRerollButton>.Instance.SetUses((int)OptionGroupSingleton<DraftOptions>.Instance.RerollsPerPlayer.Value);
        }
    }
    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastDraftEnd))]
    public static class HideDraftRerollAfterUsedUp
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftRerollButton.Hide();
            CustomButtonSingleton<DraftRerollButton>.Instance.SetUses((int)OptionGroupSingleton<DraftOptions>.Instance.RerollsPerPlayer.Value);
        }
    }
}