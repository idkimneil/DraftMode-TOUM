using MiraAPI.Roles;
using TownOfUs.Utilities;
using System;
using System.Linq;

namespace DraftModeTOUM.Managers
{
    public static class RoleCategory
    {
        /// <summary>
        /// Determine the faction from a live RoleBehaviour instance.
        /// Uses MiraAPI's team system and alignment string — no hardcoded name lists.
        /// </summary>
        public static RoleFaction GetFactionFromRole(RoleBehaviour role)
        {
            if (role == null) return RoleFaction.Crewmate;
            if (role.IsImpostor) return RoleFaction.Impostor;

            if (role is ICustomRole customRole)
            {
                if (customRole.Team == ModdedRoleTeams.Crewmate) return RoleFaction.Crewmate;
                if (customRole.Team == ModdedRoleTeams.Impostor)  return RoleFaction.Impostor;

                // NK determination via alignment string rather than a hardcoded name list —
                // picks up new ToU:M NK roles automatically without any mod update.
                string alignment = string.Empty;
                try { alignment = MiscUtils.GetParsedRoleAlignment(role)?.ToLowerInvariant() ?? string.Empty; }
                catch { }

                if (alignment.Contains("killing") && !alignment.Contains("crew"))
                    return RoleFaction.NeutralKilling;

                return RoleFaction.Neutral;
            }

            return RoleFaction.Crewmate;
        }

        /// <summary>
        /// Determine the faction from a role name string.
        /// Resolves via RoleManager when possible; falls back to Crewmate.
        /// </summary>
        public static RoleFaction GetFaction(string roleName)
        {
            var normalized = Normalize(roleName);

            if (RoleManager.Instance != null)
            {
                foreach (var r in RoleManager.Instance.AllRoles.ToArray())
                {
                    if (r == null) continue;
                    if (Normalize(r.NiceName) != normalized) continue;
                    return GetFactionFromRole(r);
                }
            }

            return RoleFaction.Crewmate;
        }

        public static bool IsNeutralKilling(string roleName) =>
            GetFaction(roleName) == RoleFaction.NeutralKilling;

        public static bool IsNeutral(string roleName) =>
            GetFaction(roleName) == RoleFaction.Neutral;

        private static string Normalize(string s) =>
            (s ?? string.Empty).Replace(" ", "").Replace("-", "").ToLowerInvariant();
    }
}
