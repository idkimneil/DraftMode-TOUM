using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using DraftModeTOUM.Patches;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public static class DraftUiManager
    {
        public static void ShowPicker(List<ushort> roleIds, int rerollsLeft = 0)
        {
            if (HudManager.Instance == null || roleIds == null || roleIds.Count == 0) return;
            DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);
            DraftScreenController.Show(roleIds.ToArray(), rerollsLeft);
        }

        public static void RefreshTurnList()
        {
            DraftStatusOverlay.Refresh();
        }

        public static void CloseAll()
        {
            DraftScreenController.Hide();

            if (DraftManager.IsDraftActive)
                DraftStatusOverlay.SetState(OverlayState.Waiting);
        }

        public static List<DraftRoleCard> BuildCards(List<ushort> roleIds, int rerollsLeft = 0)
        {
            var cards = new List<DraftRoleCard>();
            for (int i = 0; i < roleIds.Count; i++)
            {
                ushort id   = roleIds[i];
                var    role = ResolveRole(id);

                string displayName = role?.NiceName          ?? $"Role {id}";
                string team        = GetTeamLabel(role)       ?? "Unknown";
                Sprite icon        = GetRoleIcon(role);
                Color  color       = GetRoleColor(role);

                cards.Add(new DraftRoleCard(displayName, team, icon, color, i, id, GetRoleDescription(role)));
            }

            if (DraftManager.ShowRandomOption)
                cards.Add(new DraftRoleCard(
                    "Random", "Random",
                    TouRoleIcons.RandomAny.LoadAsset(),
                    Color.white,
                    roleIds.Count, 0, "Locks in a completely random role for you."));

            if (rerollsLeft > 0)
                cards.Add(new DraftRoleCard(
                    "Reroll", $"{rerollsLeft} left",
                    TouRoleIcons.RandomAny.LoadAsset(),
                    new Color(1f, 0.82f, 0.12f),
                    DraftScreenController.RerollCardIndex, 0,
                    "Discard these cards and draw a fresh set. Limited uses."));

            return cards;
        }

        public static string GetRoleDescription(RoleBehaviour role)
        {
            if (role == null) return string.Empty;
            try
            {
                string s = role.BlurbLong;
                if (string.IsNullOrWhiteSpace(s)) s = role.Blurb;
                return s ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        public static RoleBehaviour ResolveRole(ushort roleId)  // removed ?
        {
            try { return RoleManager.Instance?.GetRole((RoleTypes)roleId); }
            catch { return null; }
        }

        public static string GetTeamLabel(RoleBehaviour role)  // removed ?
        {
            if (role == null) return "Unknown";
            try { return MiscUtils.GetParsedRoleAlignment(role); } catch { }
            return RoleCategory.GetFactionFromRole(role) switch
            {
                RoleFaction.Impostor       => "Impostor",
                RoleFaction.NeutralKilling => "Neutral Killing",
                RoleFaction.Neutral        => "Neutral",
                _                          => "Crewmate"
            };
        }

        public static Sprite GetRoleIcon(RoleBehaviour role)  // removed ?
        {
            if (role is ICustomRole cr && cr.Configuration.Icon != null)
            {
                try { return cr.Configuration.Icon.LoadAsset(); } catch { }
            }
            if (role?.RoleIconSolid != null) return role.RoleIconSolid;
            return TouRoleIcons.RandomAny.LoadAsset();
        }

        public static Color GetRoleColor(RoleBehaviour role)  // removed ?
        {
            if (role is ICustomRole cr) return cr.RoleColor;
            if (role != null)           return role.TeamColor;
            return Color.white;
        }

        public static string Normalize(string s) =>
            (s ?? string.Empty).Replace(" ", "").Replace("-", "");
    }
}
