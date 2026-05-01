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
        public static void ShowPicker(List<ushort> roleIds)
        {
            if (HudManager.Instance == null || roleIds == null || roleIds.Count == 0) return;
            DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);
            DraftScreenController.Show(roleIds.ToArray());
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

        public static List<DraftRoleCard> BuildCards(List<ushort> roleIds)
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
                string description = GetRoleDescription(role);

                cards.Add(new DraftRoleCard(displayName, team, icon, color, i, id, description));
            }

            if (DraftManager.ShowRandomOption)
                cards.Add(new DraftRoleCard(
                    "Random", "Random",
                    TouRoleIcons.RandomAny.LoadAsset(),
                    Color.white,
                    roleIds.Count,
                    null,
                    "Locks in a random eligible role from the draft offer."));

            return cards;
        }

        public static RoleBehaviour? ResolveRole(ushort roleId)
        {
            try { return RoleManager.Instance?.GetRole((RoleTypes)roleId); }
            catch { return null; }
        }

        public static string GetTeamLabel(RoleBehaviour? role)
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

        public static Sprite GetRoleIcon(RoleBehaviour? role)
        {
            if (role is ICustomRole cr && cr.Configuration.Icon != null)
            {
                try { return cr.Configuration.Icon.LoadAsset(); } catch { }
            }
            if (role?.RoleIconSolid != null) return role.RoleIconSolid;
            return TouRoleIcons.RandomAny.LoadAsset();
        }

        public static Color GetRoleColor(RoleBehaviour? role)
        {
            if (role is ICustomRole cr) return cr.RoleColor;
            if (role != null)           return role.TeamColor;
            return Color.white;
        }

        public static string GetRoleDescription(RoleBehaviour? role)
        {
            string? text = null;
            try { text = role?.Blurb; } catch { }
            if (string.IsNullOrWhiteSpace(text))
            {
                try { text = role?.BlurbLong; } catch { }
            }

            return CleanDescription(text);
        }

        private static string CleanDescription(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "No role preview text available; use the card name and team to choose.";

            string cleaned = text.Replace("\r", " ").Replace("\n", " ").Trim();
            while (cleaned.Contains("  ", StringComparison.Ordinal))
                cleaned = cleaned.Replace("  ", " ");

            const int max = 150;
            return cleaned.Length <= max ? cleaned : cleaned.Substring(0, max - 3).TrimEnd() + "...";
        }

        public static string Normalize(string s) =>
            (s ?? string.Empty).Replace(" ", "").Replace("-", "");
    }
}
