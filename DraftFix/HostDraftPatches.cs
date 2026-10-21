using HarmonyLib;
using TownOfUs.Modules.DraftMode;
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

[HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
internal static class VerifyDraftedRolesPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix()
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

        HostDraft.FinalPicks.Clear();
    }
}
