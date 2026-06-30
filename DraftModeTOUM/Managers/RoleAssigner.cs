using AmongUs.GameOptions;
using MiraAPI.Roles;
using TownOfUs.Utilities;
using System;
using System.Linq;

namespace DraftModeTOUM.Managers
{
    public static class RoleAssigner
    {
        public static void AssignRole(PlayerControl player, string roleName)
        {
            if (player == null || string.IsNullOrWhiteSpace(roleName)) return;

            var normalized = Normalize(roleName);

            try
            {
                if (normalized == "crewmate") { player.RpcSetRole(RoleTypes.Crewmate); return; }
                if (normalized == "impostor") { player.RpcSetRole(RoleTypes.Impostor); return; }
                var role = MiscUtils.AllRegisteredRoles
                    .FirstOrDefault(r => r != null &&
                        string.Equals(Normalize(r.NiceName), normalized, StringComparison.OrdinalIgnoreCase));

                if (role == null)
                {
                    DraftModePlugin.Logger.LogError($"[RoleAssigner] Unknown role '{roleName}'. No role assigned.");
                    return;
                }

                var roleId = (RoleTypes)RoleId.Get(role.GetType());
                player.RpcSetRole(roleId, false);
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogError($"[RoleAssigner] Failed to assign '{roleName}': {ex}");
            }
        }

        private static string Normalize(string role) =>
            role.ToLowerInvariant().Replace(" ", "").Replace("-", "");
    }
}
