using HarmonyLib;
using MiraAPI.LocalSettings;
using TownOfUs.Patches;
using UnityEngine;

namespace DraftModeTOUM.Patches
{
    /// <summary>
    /// Hides top-right HUD buttons and the role/neutral list during a draft,
    /// leaving ability buttons (bottom-left) untouched.
    ///
    /// TOU Mira consolidates all top-right buttons into two GridArrange containers:
    ///   HudManagerPatches.UiTopRight      — vanilla buttons (map, settings, chat, friends)
    ///   HudManagerPatches.ExtraUiTopRight — TOU extras (wiki, zoom, submerged floor)
    ///
    /// The role/neutral list is HudManagerPatches.RoleList (a PingTracker clone).
    ///
    /// We simply toggle SetActive on those three objects and let TOU Mira's own
    /// GridArrange + AspectPosition logic restore them when we turn them back on.
    /// </summary>
    public static class DraftHudVisibilityManager
    {
        private static bool _isHiding = false;

        // Track what was active before we hid it so we restore only what we actually touched.
        private static bool _uiTopRightWasActive;
        private static bool _extraUiTopRightWasActive;
        private static bool _roleListWasActive;

        // ── Public API ────────────────────────────────────────────────────────

        public static void ApplyForDraft()
        {
            if (_isHiding) return;

            var settings = LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
            if (settings == null || !settings.HideHudDuringDraft.Value) return;

            _isHiding = true;
            if (HudManagerPatches.UiTopRight != null)
            {
                _uiTopRightWasActive = HudManagerPatches.UiTopRight.activeSelf;
                HudManagerPatches.UiTopRight.SetActive(false);
            }
            if (HudManagerPatches.ExtraUiTopRight != null)
            {
                _extraUiTopRightWasActive = HudManagerPatches.ExtraUiTopRight.activeSelf;
                HudManagerPatches.ExtraUiTopRight.SetActive(false);
            }
            if (HudManagerPatches.RoleList != null)
            {
                _roleListWasActive = HudManagerPatches.RoleList.activeSelf;
                HudManagerPatches.RoleList.SetActive(false);
            }

            DraftModePlugin.Logger.LogInfo("[DraftHud] HUD elements hidden for draft.");
        }

        public static void Restore()
        {
            if (!_isHiding) return;
            _isHiding = false;

            if (HudManagerPatches.UiTopRight != null && _uiTopRightWasActive)
                HudManagerPatches.UiTopRight.SetActive(true);

            if (HudManagerPatches.ExtraUiTopRight != null && _extraUiTopRightWasActive)
                HudManagerPatches.ExtraUiTopRight.SetActive(true);

            if (HudManagerPatches.RoleList != null && _roleListWasActive)
                HudManagerPatches.RoleList.SetActive(true);

            DraftModePlugin.Logger.LogInfo("[DraftHud] HUD elements restored.");
        }
    }

    // ── Patches ───────────────────────────────────────────────────────────────

    /// Apply hiding whenever the overlay state transitions to anything visible (draft active),
    /// and restore when it goes back to Hidden (draft over).
    [HarmonyPatch(typeof(DraftStatusOverlay), nameof(DraftStatusOverlay.SetState))]
    public static class DraftHudHideOnSetState
    {
        [HarmonyPostfix]
        public static void Postfix(OverlayState state)
        {
            if (state == OverlayState.Hidden)
                DraftHudVisibilityManager.Restore();
            else
                DraftHudVisibilityManager.ApplyForDraft();
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    public static class DraftHudRestoreOnDisconnect
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftHudVisibilityManager.Restore();
    }

    /// Restore when the game starts (intro fires, lobby is over).
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    public static class DraftHudRestoreOnIntro
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftHudVisibilityManager.Restore();
    }
}
