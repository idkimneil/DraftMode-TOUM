using System.Text;
using HarmonyLib;
using MiraAPI.LocalSettings;
using TMPro;
using TownOfUs.Patches;
using UnityEngine;
using DraftModeTOUM.Patches;

namespace DraftModeTOUM.Managers
{
    public static class DraftSidebarManager
    {
        private static bool _active = false;

        private static readonly string ColWaiting      = "#ffffffff";
        private static readonly string ColPicking      = "#ffffffff";
        private static readonly string ColCrewmate     = "#00FFFF";
        private static readonly string ColImpostor     = "#FF4444";
        private static readonly string ColNeutral      = "#7e7e7eff";
        private static readonly string ColDisconnected = "#ffffffff";
        private static readonly string ColHeader       = "#e7a6ffff";
        private static readonly string ColPlayerName   = "#ffdd00ff";
        private static readonly string ColLocalPlayer  = "#8bd5f9ff";

        public static void Activate()
        {
            if (!IsSettingEnabled()) return;
            _active = true;
            DraftModePlugin.Logger.LogInfo("[DraftSidebar] Activated.");
        }

        public static void Deactivate()
        {
            if (!_active) return; // guard against repeated/redundant calls
            _active = false;

            // Clear the text we wrote so TOU-Mira's UpdateRoleList reclaims the panel
            // on its next tick and shows the normal role/neutral list again.
            var tmp = HudManagerPatches.RoleListTextComp;
            if (tmp != null)
                tmp.text = string.Empty;

            // Also hide the panel so TOU-Mira can restore it properly on next Update tick.
            var roleList = HudManagerPatches.RoleList;
            if (roleList != null)
                roleList.SetActive(false);

            // Make sure IsHoveringRoleList is not stuck true — that would prevent
            // TOU-Mira's UpdateRoleList from writing new text (HudManagerPatches line ~926:
            // "if (!IsHoveringRoleList) RoleListTextComp.text = ...").
            HudManagerPatches.IsHoveringRoleList = false;

            DraftModePlugin.Logger.LogInfo("[DraftSidebar] Deactivated.");
        }

        public static bool IsActive => _active;

        public static void DrawSidebar()
        {
            var roleList = HudManagerPatches.RoleList;
            var tmp      = HudManagerPatches.RoleListTextComp;
            if (roleList == null || tmp == null) return;

            roleList.SetActive(true);
            tmp.fontSize    = 3f;
            tmp.fontSizeMin = 0.5f;
            tmp.fontSizeMax = 3f;
            tmp.text        = BuildText();
        }

        private static bool IsSettingEnabled()
        {
            var settings = LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
            return settings != null && settings.ShowDraftSidebar.Value;
        }

        private static string BuildText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<color={ColHeader}><b>── Draft Order ──</b></color>");
            sb.AppendLine();

            if (!DraftManager.IsDraftActive)
            {
                sb.Append($"<color={ColWaiting}>Waiting...</color>");
                return sb.ToString();
            }

            foreach (int slot in DraftManager.TurnOrder)
            {
                var state = DraftManager.GetStateForSlot(slot);
                if (state == null) continue;

                bool isMe = state.PlayerId == PlayerControl.LocalPlayer.PlayerId;
                string nameCol = isMe ? ColLocalPlayer : ColPlayerName;

                sb.AppendLine($"<color={nameCol}><b>Player {slot:D2}</b></color> " + BuildStatusLine(state));
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildStatusLine(PlayerDraftState state)
        {
            if (state.IsDisconnected)
                return $"<color={ColDisconnected}>DISCONNECTED</color>";

            if (state.IsPickingNow && !state.HasPicked)
                return $"<color={ColPicking}>is picking...</color>";

            if (state.HasPicked && state.ChosenRoleId.HasValue)
            {
                var faction = GetFactionForRole(state.ChosenRoleId.Value);
                switch (faction)
                {
                    case RoleFaction.Impostor:
                        return $"has picked <color={ColImpostor}><b>IMPOSTOR</b></color>";
                    case RoleFaction.NeutralKilling:
                    case RoleFaction.Neutral:
                        return $"has picked <color={ColNeutral}>NEUTRAL</color>";
                    default:
                        return $"has picked <color={ColCrewmate}>CREWMATE</color>";
                }
            }

            if (state.HasPicked)
                return $"has picked <color={ColCrewmate}>CREWMATE</color>";

            return $"<color={ColWaiting}>is waiting for turn</color>";
        }

        private static RoleFaction GetFactionForRole(ushort roleId)
        {
            try
            {
                var role = RoleManager.Instance?.GetRole((AmongUs.GameOptions.RoleTypes)roleId);
                if (role != null) return RoleCategory.GetFactionFromRole(role);
            }
            catch { }
            return RoleFaction.Crewmate;
        }
    }

    // ── Harmony hooks ─────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(HudManagerPatches), nameof(HudManagerPatches.UpdateRoleList))]
    public static class DraftSidebarUpdateRoleListPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!DraftSidebarManager.IsActive) return;
            DraftSidebarManager.DrawSidebar();
        }
    }

    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastDraftStart))]
    public static class DraftSidebarActivateOnStart
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftSidebarManager.Activate();
    }

    [HarmonyPatch(typeof(DraftManager), nameof(DraftManager.SetDraftStateFromHost))]
    public static class DraftSidebarActivateOnClient
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftSidebarManager.Activate();
    }

    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastRecap))]
    public static class DraftSidebarDeactivateOnRecap
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftSidebarManager.Deactivate();
    }

    [HarmonyPatch(typeof(DraftNetworkHelper), nameof(DraftNetworkHelper.BroadcastCancelDraft))]
    public static class DraftSidebarDeactivateOnCancel
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftSidebarManager.Deactivate();
    }

    /// <summary>
    /// Catches all draft-end paths on both host and client:
    /// - Normal completion → SetState(Hidden) via CoEndDraftSequence
    /// - Cancel → already caught above, but this is a universal safety net
    /// Every exit path transitions the overlay to Hidden, so this is reliable.
    /// </summary>
    [HarmonyPatch(typeof(DraftStatusOverlay), nameof(DraftStatusOverlay.SetState))]
    public static class DraftSidebarDeactivateOnOverlayHidden
    {
        [HarmonyPostfix]
        public static void Postfix(OverlayState state)
        {
            if (state == OverlayState.Hidden)
                DraftSidebarManager.Deactivate();
        }
    }

    /// <summary>
    /// Belt-and-suspenders: deactivate when the game actually starts
    /// so the sidebar never leaks into in-game.
    /// </summary>
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    public static class DraftSidebarDeactivateOnIntro
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftSidebarManager.Deactivate();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    public static class DraftSidebarDeactivateOnDisconnect
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftSidebarManager.Deactivate();
    }

    // ── RoleListHoverComponent suppression ────────────────────────────────────
    //
    // RoleListHoverComponent lives on HudManager.Instance.gameObject and drives
    // the "hover the role list panel to show bucket tooltips" feature.
    // It sets HudManagerPatches.IsHoveringRoleList = true while the mouse is
    // over the panel, which blocks UpdateRoleList from writing new text
    // (HudManagerPatches.UpdateRoleList: "if (!IsHoveringRoleList) text = ...").
    //
    // During draft we own the role list panel for the sidebar, so we patch
    // RoleListHoverComponent.Update: when a draft is active the original is
    // skipped and IsHoveringRoleList is forced to false, ensuring our
    // DrawSidebar() postfix is never blocked.
    //
    // OnEnable is NOT patched here — it is inherited from MonoBehaviour and
    // Harmony cannot patch inherited methods via the subtype.

    [HarmonyPatch(typeof(RoleListHoverComponent), nameof(RoleListHoverComponent.Update))]
    public static class RoleListHoverSuppressUpdate
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!DraftManager.IsDraftActive) return true; // run normally outside draft

            // Draft is active: prevent hover detection and clear any stale flag so
            // our DrawSidebar postfix is never blocked by UpdateRoleList's guard.
            HudManagerPatches.IsHoveringRoleList = false;
            return false; // skip original Update
        }
    }
}