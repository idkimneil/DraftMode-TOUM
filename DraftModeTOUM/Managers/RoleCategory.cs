using MiraAPI.Roles;
using TownOfUs.Utilities;
using System;
using System.Linq;

namespace DraftModeTOUM.Managers
{
    public static class RoleCategory
    {

        public static RoleFaction GetFactionFromRole(RoleBehaviour role)
        {
            if (role == null) return RoleFaction.Crewmate;
            if (role.IsImpostor) return RoleFaction.Impostor;

            if (role is ICustomRole customRole)
            {
                if (customRole.Team == ModdedRoleTeams.Crewmate) return RoleFaction.Crewmate;
                if (customRole.Team == ModdedRoleTeams.Impostor)  return RoleFaction.Impostor;
                string alignment = string.Empty;
                try { alignment = MiscUtils.GetParsedRoleAlignment(role)?.ToLowerInvariant() ?? string.Empty; }
                catch { }

                if (alignment.Contains("killing") && !alignment.Contains("crew"))
                    return RoleFaction.NeutralKilling;

                return RoleFaction.Neutral;
            }

            return RoleFaction.Crewmate;
        }

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
