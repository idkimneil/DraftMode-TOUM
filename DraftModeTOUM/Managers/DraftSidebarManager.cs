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
        private static readonly string ColPlayerName  = "#ffdd00ff";
        private static readonly string ColLocalPlayer = "#8bd5f9ff";
        private static GameObject    _bannerGo;
        private static SpriteRenderer _bannerSr;

        public static void Activate()
        {
            if (!IsSettingEnabled()) return;
            _active = true;
            EnsureBanner();
            if (_bannerGo != null) _bannerGo.SetActive(true);
            DraftModePlugin.Logger.LogInfo("[DraftSidebar] Activated.");
        }

        public static void Deactivate()
        {
            if (!_active) return;
            _active = false;

            if (_bannerGo != null) _bannerGo.SetActive(false);

            var tmp = HudManagerPatches.RoleListTextComp;
            if (tmp != null)
                tmp.text = string.Empty;

            var roleList = HudManagerPatches.RoleList;
            if (roleList != null)
                roleList.SetActive(false);

            HudManagerPatches.IsHoveringRoleList = false;

            DraftModePlugin.Logger.LogInfo("[DraftSidebar] Deactivated.");
        }
        public static void ClearBannerRef()
        {
            _bannerGo = null;
            _bannerSr = null;
        }

        public static bool IsActive => _active;
        public static void DrawSidebar()
        {
            var roleList = HudManagerPatches.RoleList;
            var tmp      = HudManagerPatches.RoleListTextComp;
            if (roleList == null || tmp == null) return;
            EnsureBanner();
            if (_bannerGo != null && !_bannerGo.activeSelf)
                _bannerGo.SetActive(true);

            roleList.SetActive(true);
            tmp.fontSize    = 3f;
            tmp.fontSizeMin = 0.5f;
            tmp.fontSizeMax = 3f;
            tmp.text        = BuildText();
        }
        private static void EnsureBanner()
        {
            if (_bannerGo != null) return;

            var roleList = HudManagerPatches.RoleList;
            if (roleList == null) return;

            _bannerGo = new GameObject("DraftSidebarBanner");
            _bannerGo.transform.SetParent(roleList.transform, false);

            _bannerSr                  = _bannerGo.AddComponent<SpriteRenderer>();
            _bannerSr.sortingLayerName = "UI";
            _bannerSr.sortingOrder     = 51;

            var sprite = DraftAssets.DraftBanner.LoadAsset();
            if (sprite != null)
            {
                _bannerSr.sprite = sprite;
                _bannerGo.transform.localScale    = Vector3.one * 0.38f;
                _bannerGo.transform.localPosition = new Vector3(1.55001f, -1.0001f, -1f);
            }
            else
            {
                DraftModePlugin.Logger.LogWarning("[DraftSidebar] DraftBanner sprite failed to load.");
            }
            _bannerGo.SetActive(false);
        }

        private static bool IsSettingEnabled()
        {
            var settings = LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
            return settings != null && settings.ShowDraftSidebar.Value;
        }

        private static string BuildText()
        {
            var sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();


            if (!DraftManager.IsDraftActive)
            {
                sb.Append($"<color=#ffffffff>Waiting...</color>");
                return sb.ToString();
            }

            foreach (int slot in DraftManager.TurnOrder)
            {
                var state = DraftManager.GetStateForSlot(slot);
                if (state == null) continue;

                bool   isMe     = state.PlayerId == PlayerControl.LocalPlayer.PlayerId;
                string nameCol  = isMe ? ColLocalPlayer : ColPlayerName;

                sb.AppendLine(
                    $"<color={nameCol}><b>Player {slot:D2}</b></color> " +
                    BuildStatusLine(state));
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildStatusLine(PlayerDraftState state)
        {
            if (state.IsDisconnected)
                return $"<color=#ffffffff>DISCONNECTED</color>";

            if (state.IsPickingNow && !state.HasPicked)
                return $"<color=#ffffffff>is picking...</color>";

            if (state.HasPicked && state.ChosenRoleId.HasValue)
            {
                var faction = GetFactionForRole(state.ChosenRoleId.Value);
                switch (faction)
                {
                    case RoleFaction.Impostor:
                        return $"has picked <color=#FF4444><b>IMPOSTOR</b></color>";
                    case RoleFaction.NeutralKilling:
                    case RoleFaction.Neutral:
                        return $"has picked <color=#7e7e7eff>NEUTRAL</color>";
                    default:
                        return $"has picked <color=#00FFFF>CREWMATE</color>";
                }
            }

            if (state.HasPicked)
                return $"has picked <color=#00FFFF>CREWMATE</color>";

            return $"<color=#ffffffff>is waiting for turn</color>";
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

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    public static class DraftSidebarDeactivateOnIntro
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftSidebarManager.Deactivate();
            DraftSidebarManager.ClearBannerRef();
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    public static class DraftSidebarDeactivateOnDisconnect
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftSidebarManager.Deactivate();
            DraftSidebarManager.ClearBannerRef();
        }
    }

    [HarmonyPatch(typeof(RoleListHoverComponent), nameof(RoleListHoverComponent.Update))]
    public static class RoleListHoverSuppressUpdate
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!DraftManager.IsDraftActive) return true;

            HudManagerPatches.IsHoveringRoleList = false;
            return false;
        }
    }
}