using MiraAPI.Roles;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public static class RoleColors
    {
        public static readonly Color CrewFallback     = new Color32(75,  215, 228, 255);
        public static readonly Color ImpostorFallback = new Color32(255, 70,  70,  255);
        public static readonly Color NeutralFallback  = new Color32(171, 70,  255, 255);
        public static readonly Color RandomColour     = new Color32(59,  204, 59,  255);

        public static Color GetColor(string roleName)
        {
            var normalized = roleName.Replace(" ", "").ToLowerInvariant();
            if (RoleManager.Instance != null)
            {
                foreach (var role in RoleManager.Instance.AllRoles.ToArray())
                {
                    if (role == null) continue;
                    if (role.NiceName.Replace(" ", "").ToLowerInvariant() != normalized) continue;

                    if (role is ICustomRole cr) return cr.RoleColor;
                    return role.TeamColor;
                }
            }

            // 3. Faction-based fallback
            return RoleCategory.GetFaction(roleName) switch
            {
                RoleFaction.Impostor                   => ImpostorFallback,
                RoleFaction.Neutral or
                RoleFaction.NeutralKilling             => NeutralFallback,
                _                                      => CrewFallback
            };
        }
    }
}
