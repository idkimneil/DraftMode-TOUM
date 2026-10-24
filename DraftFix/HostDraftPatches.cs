using HarmonyLib;
using MiraAPI.GameOptions;
using Reactor.Networking.Rpc;
using TownOfUs.Modules.DraftMode;
using TownOfUs.Options;
using TownOfUs.Utilities;

namespace DraftFix;

[HarmonyPatch(typeof(DraftEngineBehaviour), nameof(DraftEngineBehaviour.StartHostDraft))]
internal static class StartHostDraftPatch
{
    public static bool Prefix(Dictionary<byte, int> pidToSlot)
    {
        return !AmongUsClient.Instance.AmHost || !HostDraft.Start(pidToSlot);
    }
}

[HarmonyPatch(typeof(DraftEngineBehaviour), nameof(DraftEngineBehaviour.TryApplySubmittedPick))]
internal static class TryApplySubmittedPickPatch
{
    public static bool Prefix(byte playerId, byte index, ushort roleId)
    {
        if (!AmongUsClient.Instance.AmHost) return true;

        HostDraft.OnPick(playerId, index, roleId);
        return false;
    }
}

[HarmonyPatch(typeof(DraftEngineBehaviour), nameof(DraftEngineBehaviour.RequestShuffle))]
internal static class RequestShuffleDraftPatch
{
    public static bool Prefix(byte playerId)
    {
        if (!AmongUsClient.Instance.AmHost) return true;

        HostDraft.RequestShuffle(playerId);
        return false;
    }
}

[HarmonyPatch(typeof(DraftEngineBehaviour), nameof(DraftEngineBehaviour.CancelDraft))]
internal static class CancelDraftPatch
{
    public static void Prefix()
    {
        HostDraft.Stop();
    }
}

[HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastPickConfirmed))]
internal static class BroadcastPickConfirmedPatch
{
    public static bool Prefix(int slot, ushort roleId, bool timedOut)
    {
        if (!AmongUsClient.Instance.AmHost) return true;

        var pickerId = DraftManager.GetStateForSlot(slot)?.PlayerId;
        var hide = OptionGroupSingleton<RoleOptions>.Instance.DraftSidebarDisplay.Value == DraftRecapMode.Nothing;
        var rpc = Rpc<DraftPickConfirmedRpc>.Instance;

        foreach (var client in AmongUsClient.Instance.allClients.ToArray())
        {
            if (client == null || client.Id == AmongUsClient.Instance.ClientId) continue;

            var isPicker = pickerId.HasValue && client.Character != null && client.Character.PlayerId == pickerId.Value;
            var publicRoleId = hide && !isPicker ? (ushort)0 : roleId;
            rpc.SendTo(PlayerControl.LocalPlayer, client.Id,
                new DraftPickConfirmedData { Slot = slot, RoleId = publicRoleId, TimedOut = timedOut });
        }

        var localSlot = DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
        if (slot == localSlot && roleId != 0)
        {
            DraftScreenController.Hide();
            DraftScreenController.ShowFinalPickNotification(roleId);
        }

        DraftSidebarManager.InvalidateCache();
        DraftStatusOverlay.Refresh();
        return false;
    }
}

[HarmonyPatch(typeof(DraftScreenController), nameof(DraftScreenController.ShowFinalPickNotification))]
internal static class FinalPickNotificationPatch
{
    public static bool Prefix()
    {
        return !AmongUsClient.Instance.AmHost || HostDraft.TryMarkFinalPickNotificationShown();
    }
}

[HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
internal static class VerifyDraftedRolesPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix()
    {
        try
        {
            if (!AmongUsClient.Instance.AmHost || HostDraft.FinalPicks.Count == 0) return;

            foreach (var (playerId, roleId) in HostDraft.FinalPicks)
            {
                var player = MiscUtils.PlayerById(playerId);
                if (player == null || player.Data?.Role == null || roleId == 0) continue;
                if ((ushort)player.Data.Role.Role == roleId) continue;

                DraftHostFixPlugin.Logger.LogWarning(
                    $"Player {playerId} drafted role {roleId} but ended with {(ushort)player.Data.Role.Role} ({player.Data.Role.NiceName})");

                if (DraftHostFixPlugin.ReassertPicks.Value)
                {
                    player.RpcSetRole((AmongUs.GameOptions.RoleTypes)roleId);
                }
            }
        }
        catch (Exception e)
        {
            DraftHostFixPlugin.Logger.LogError($"Draft role verification failed: {e}");
        }
        finally
        {
            HostDraft.FinalPicks.Clear();
        }
    }
}