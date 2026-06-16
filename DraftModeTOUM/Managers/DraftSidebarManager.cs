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
        private static GameObject    _bannerGo;
        private static SpriteRenderer _bannerSr;

        public static void Activate()
        {
            if (!IsSettingEnabled()) return;
            _active = true;
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

            roleList.SetActive(true);
            tmp.fontSize           = 3f;
            tmp.fontSizeMin        = 0.5f;
            tmp.fontSizeMax        = 3f;
            tmp.enableWordWrapping = false;
            tmp.text               = BuildText();
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

            sb.AppendLine(AnimatedTitle());

            if (!DraftManager.IsDraftActive)
            {
                sb.AppendLine();
                sb.Append("<color=#7A8089><i>Waiting to start...</i></color>");
                return sb.ToString();
            }

            int total = 0, picked = 0;
            foreach (int slot in DraftManager.TurnOrder)
            {
                var s = DraftManager.GetStateForSlot(slot);
                if (s == null) continue;
                total++;
                if (s.HasPicked) picked++;
            }

            sb.AppendLine($"<size=64%><color=#6B7178>{picked} / {total}  LOCKED IN</color></size>");
            sb.AppendLine();

            foreach (int slot in DraftManager.TurnOrder)
            {
                var state = DraftManager.GetStateForSlot(slot);
                if (state == null) continue;
                bool isMe = state.PlayerId == PlayerControl.LocalPlayer.PlayerId;
                sb.AppendLine(BuildRow(slot, state, isMe));
            }

            return sb.ToString().TrimEnd();
        }

        // Soft light sweeping across the wordmark every frame, for a smooth, premium feel.
        private static string AnimatedTitle()
        {
            float t = Time.time;
            var sb = new StringBuilder();
            sb.Append("<size=105%><b>");
            sb.Append(Shimmer("DRAFT", new Color(0.36f, 0.84f, 0.89f), t, 0));
            sb.Append(" ");
            sb.Append(Shimmer("MODE", new Color(1f, 0.31f, 0.31f), t, 6));
            sb.Append("</b></size>");
            return sb.ToString();
        }

        private static string Shimmer(string word, Color baseCol, float t, int startIdx)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < word.Length; i++)
            {
                float w = (Mathf.Sin(t * 2.2f - (startIdx + i) * 0.6f) + 1f) * 0.5f;
                w *= w; // sharpen the moving glint
                Color c = Color.Lerp(baseCol, Color.white, w * 0.8f);
                sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{word[i]}</color>");
            }
            return sb.ToString();
        }

        private static string BuildRow(int slot, PlayerDraftState state, bool isMe)
        {
            string you    = isMe ? "  <color=#8BD5F9><b>(you)</b></color>" : string.Empty;
            string numCol = isMe ? "#8BD5F9" : "#9AA0A6";

            if (state.IsPickingNow && !state.HasPicked && !state.IsDisconnected)
            {
                float p = (Mathf.Sin(Time.time * 3.0f) + 1f) * 0.5f;                 // smooth breathing
                Color c = Color.Lerp(new Color(1f, 0.80f, 0.28f), new Color(1f, 0.97f, 0.74f), p);
                string hex = ColorUtility.ToHtmlStringRGB(c);
                return $"<color={numCol}><b>#{slot:D2}</b></color>   <b><color=#{hex}>picking...</color></b>{you}";
            }

            string statusCol, statusTxt;
            if (state.IsDisconnected)
            {
                statusCol = "#6E6E6E"; statusTxt = "disconnected";
            }
            else if (state.HasPicked)
            {
                switch (state.ChosenRoleId.HasValue ? GetFactionForRole(state.ChosenRoleId.Value) : RoleFaction.Crewmate)
                {
                    case RoleFaction.Impostor:       statusCol = "#FF5050"; statusTxt = "Impostor"; break;
                    case RoleFaction.NeutralKilling:
                    case RoleFaction.Neutral:        statusCol = "#B06CFF"; statusTxt = "Neutral";  break;
                    default:                          statusCol = "#5BD7E4"; statusTxt = "Crewmate"; break;
                }
            }
            else
            {
                statusCol = "#555B61"; statusTxt = "waiting";
            }

            string row = $"<color={numCol}><b>#{slot:D2}</b></color>   <color={statusCol}>{statusTxt}</color>{you}";
            if (isMe)
                return $"<mark=#8BD5F910>{row}</mark>";
            return row;
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