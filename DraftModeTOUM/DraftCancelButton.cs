using System;
using System.Reflection;
using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using MiraAPI.Utilities.Assets;
using MiraAPI.Utilities;
using MiraAPI.Hud;
using TownOfUs.Assets;
using TownOfUs.Buttons;
using TownOfUs.Utilities;
using UnityEngine;

// Kept in the root namespace so DraftModePlugin.cs and DraftCancelButtonPatch.cs
// can reference it without any extra using directive.
namespace DraftModeTOUM;

public sealed class DraftCancelButton : TownOfUsButton
{
    public static void Show()
    {
        CustomButtonSingleton<DraftCancelButton>.Instance.Disabled = false;
    }

    public static void Hide()
    {
        CustomButtonSingleton<DraftCancelButton>.Instance.Disabled = true;
    }

    public override string Name => "Cancel Draft";
    public override float InitialCooldown => 0.001f;
    public override float Cooldown => 0.001f;

    public override bool ZeroIsInfinite { get; set; } = true;
    public override ButtonLocation Location => ButtonLocation.BottomRight;


    public override Color TextOutlineColor => new Color32(198, 22, 22, 255);

    public override LoadableAsset<Sprite> Sprite => DraftAssets.QuitSprite;

    public override bool Disabled { get; set; } = true;

    public override bool Enabled(RoleBehaviour? role)
    {
        return AmongUsClient.Instance.AmHost && !Disabled;
    }

    public override bool CanUse()
    {
        return base.CanUse() && DraftManager.IsDraftActive && !Disabled;
    }

    protected override void OnClick()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!DraftManager.IsDraftActive) return;

        DraftModePlugin.Logger.LogInfo("[DraftCancelButton] Cancel clicked by host.");
        DraftNetworkHelper.BroadcastCancelDraft();
        DraftNetworkHelper.BroadcastCreateNotif(
            "<color=#FF0000>Draft Mode</color> has been cancelled by the <color=#FFBFCC><b>Host</b></color>!"); // ADD THIS
        DraftManager.Reset(cancelledBeforeCompletion: true);
        DraftNetworkHelper.BroadcastDraftEnd();
        Hide();
    }
}
