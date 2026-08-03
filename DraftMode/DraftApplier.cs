using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;


namespace DraftMode
{
    public static class DraftApplier
    {
        public static Action<byte, string, ushort> OnAssignRoleRequested = DefaultAssignmentHandler;

        internal static List<DraftSlotState> PendingDraftStates = new();

        public static void StorePendingDraftStates(IReadOnlyList<DraftSlotState> slotStates)
        {
            if (slotStates == null) return;
            PendingDraftStates = slotStates.ToList();
        }

        public static void ApplyDraftResults(IReadOnlyList<DraftSlotState> slotStates)
        {
            if (slotStates == null) return;

            foreach (var s in slotStates)
            {
                var roleName = s.ForcedRoleName;
                if (string.IsNullOrEmpty(roleName))
                    roleName = DraftRolePool.GetRoleNameFromId(s.ChosenRoleId);

                try
                {
                    var playerId = s.PlayerId;
                    OnAssignRoleRequested?.Invoke(playerId, roleName, s.ChosenRoleId);
                }
                catch (Exception e)
                {
                    MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Warning, $"DraftApplier: assignment handler threw: {e}");
                }
            }
        }

        private static void DefaultAssignmentHandler(byte playerId, string roleName, ushort roleId)
        {
            if (!AmongUsClient.Instance.AmHost)
                return;

            var player = PlayerControl.AllPlayerControls.ToArray()
                .FirstOrDefault(p => p != null && p.PlayerId == playerId);

            if (player == null)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Warning,
                    $"DraftApplier: no PlayerControl found for player {playerId}, cannot assign role {roleName}");
                return;
            }

            if (roleId == 0)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Warning,
                    $"DraftApplier: no drafted role for player {playerId} ({roleName ?? "unknown"}), forcing Crewmate so no leftover role from the normal game start survives");
                player.RpcSetRole(RoleTypes.Crewmate);
                return;
            }

            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info,
                $"DraftApplier: assigning role {(!string.IsNullOrEmpty(roleName) ? roleName : $"role#{roleId}")} to player {playerId}");

            player.RpcSetRole((RoleTypes)roleId);
        }
    }
}