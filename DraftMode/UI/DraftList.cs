using System.Text;
using System.Linq;
using HarmonyLib;
using MiraAPI.GameOptions;
using TMPro;
using UnityEngine;
using TownOfUs;
using TownOfUs.Patches;
using DraftMode.Options;

namespace DraftMode
{
    [HarmonyPatch(typeof(HudManagerPatches), nameof(HudManagerPatches.UpdateRoleList))]
    public static class DraftLobbyRoleListPatch
    {
        [HarmonyPostfix]
        public static void Postfix(HudManager instance)
        {
            if (!LobbyBehaviour.Instance) return;
            if (DraftSidebarManager.IsActive) return;

            var draftOpts = OptionGroupSingleton<DraftOptions>.Instance;
            if (draftOpts == null || !draftOpts.IsDraftMode.Value) return;
            var roleList = HudManagerPatches.RoleList;
            var tmp = HudManagerPatches.RoleListTextComp;
            if (roleList == null || tmp == null) return;

            EnsureHoverComponent(instance, tmp);

            if (HudManagerPatches.IsHoveringRoleList) return;

            tmp.text = BuildDraftSettingsText();
            roleList.SetActive(true);
        }

        private static DraftRoleListHoverComponent _hoverComp;
        private static void EnsureHoverComponent(HudManager instance, TextMeshPro tmp)
        {
            if (_hoverComp == null)
            {
                _hoverComp = instance.gameObject.GetComponent<DraftRoleListHoverComponent>()
                             ?? instance.gameObject.AddComponent<DraftRoleListHoverComponent>();
            }
            _hoverComp.TextTarget = tmp;
        }

        public static bool IsShowingDraftList()
        {
            if (!LobbyBehaviour.Instance || DraftSidebarManager.IsActive) return false;
            var draftOpts = OptionGroupSingleton<DraftOptions>.Instance;
            return draftOpts != null && draftOpts.IsDraftMode.Value;
        }

        public static bool IsShowingRoleListPool()
        {
            if (!IsShowingDraftList()) return false;
            return OptionGroupSingleton<DraftOptions>.Instance.UseRoleListForPool.Value;
        }

        public static TownOfUs.Options.RoleListOption? BucketForLine(int line)
        {
            var slotIndex = line - 2;
            if (slotIndex < 0) return null;

            var list = OptionGroupSingleton<RoleDraftRoleListOptions>.Instance;
            var slotValue = slotIndex switch
            {
                0 => list.Slot1.Value,
                1 => list.Slot2.Value,
                2 => list.Slot3.Value,
                3 => list.Slot4.Value,
                4 => list.Slot5.Value,
                5 => list.Slot6.Value,
                6 => list.Slot7.Value,
                7 => list.Slot8.Value,
                8 => list.Slot9.Value,
                9 => list.Slot10.Value,
                10 => list.Slot11.Value,
                11 => list.Slot12.Value,
                12 => list.Slot13.Value,
                13 => list.Slot14.Value,
                14 => list.Slot15.Value,
                _ => (RoleListOption)(-1)
            };

            if ((int)slotValue < 0) return null;
            return (TownOfUs.Options.RoleListOption)(int)slotValue;
        }

        private static string AnimatedTitle()
        {
            float t = Time.time;
            var sb = new StringBuilder();
            sb.Append("<size=105%><b>");
            sb.Append(Shimmer("DRAFT", new Color(0.36f, 0.84f, 0.89f), t, 0));
            sb.Append(' ');
            sb.Append(Shimmer("MODE", new Color(1f, 0.31f, 0.31f), t, 6));
            sb.Append(':');
            sb.Append("</b></size>");
            return sb.ToString();
        }

        private static string Shimmer(string word, Color baseCol, float t, int startIdx)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < word.Length; i++)
            {
                float w = (Mathf.Sin(t * 2.2f - (startIdx + i) * 0.6f) + 1f) * 0.5f;
                w *= w;
                Color c = Color.Lerp(baseCol, Color.white, w * 0.8f);
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{word[i]}</color>");
            }
            return sb.ToString();
        }

        private static string BuildDraftSettingsText()
        {
            var sb = new StringBuilder($"{AnimatedTitle()}\n");
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"<size=64%><color=#6B7178>WAITING TO START ...</color></size>\n");

            var draftOpts = OptionGroupSingleton<DraftOptions>.Instance;

            if (draftOpts.UseRoleListForPool.Value)
            {
                AppendRoleListPool(sb);
            }
            else
            {
                AppendCapPool(sb);
            }

            return sb.ToString();
        }

        private static void AppendRoleListPool(StringBuilder sb)
        {
            var list = OptionGroupSingleton<RoleDraftRoleListOptions>.Instance;
            const int hardCap = 15;
            int playerCount = PlayerControl.AllPlayerControls?.ToArray()
                .Count(p => p != null && !p.Data.Disconnected) ?? 0;
            int maxSlots = Mathf.Clamp(playerCount, 0, hardCap);

            for (var i = 0; i < maxSlots; i++)
            {
                var slotValue = i switch
                {
                    0 => list.Slot1.Value,
                    1 => list.Slot2.Value,
                    2 => list.Slot3.Value,
                    3 => list.Slot4.Value,
                    4 => list.Slot5.Value,
                    5 => list.Slot6.Value,
                    6 => list.Slot7.Value,
                    7 => list.Slot8.Value,
                    8 => list.Slot9.Value,
                    9 => list.Slot10.Value,
                    10 => list.Slot11.Value,
                    11 => list.Slot12.Value,
                    12 => list.Slot13.Value,
                    13 => list.Slot14.Value,
                    14 => list.Slot15.Value,
                    _ => (RoleListOption)(-1)
                };

                sb.AppendLine(GetDraftRoleForSlot(slotValue));
            }
        }

        private static void AppendCapPool(StringBuilder sb)
        {
            var crew = OptionGroupSingleton<RoleDraftCrewOptions>.Instance;
            var imp  = OptionGroupSingleton<RoleDraftImpOptions>.Instance;
            var neut = OptionGroupSingleton<RoleDraftNeutOptions>.Instance;

            sb.AppendLine($"{Palette.CrewmateBlue.ToTextColor()}Crew</color> Investigatives: {crew.MaxCrewInvestigative.Value} Max");
            sb.AppendLine($"{Palette.CrewmateBlue.ToTextColor()}Crew</color> Killings: {crew.MaxCrewKilling.Value} Max");
            sb.AppendLine($"{Palette.CrewmateBlue.ToTextColor()}Crew</color> Powers: {crew.MaxCrewPower.Value} Max");
            sb.AppendLine($"{Palette.CrewmateBlue.ToTextColor()}Crew</color> Protectives: {crew.MaxCrewProtective.Value} Max");
            sb.AppendLine($"{Palette.CrewmateBlue.ToTextColor()}Crew</color> Supports: {crew.MaxCrewSupport.Value} Max");

            sb.AppendLine($"{TownOfUsColors.ImpSoft.ToTextColor()}Impostors</color>: {imp.MaxImpostors.Value} Max");
            sb.AppendLine($"{TownOfUsColors.ImpSoft.ToTextColor()}Imp</color> Killings: {imp.MaxImpKilling.Value} Max");
            sb.AppendLine($"{TownOfUsColors.ImpSoft.ToTextColor()}Imp</color> Powers: {imp.MaxImpPower.Value} Max");
            sb.AppendLine($"{TownOfUsColors.ImpSoft.ToTextColor()}Imp</color> Supports: {imp.MaxImpSupport.Value} Max");
            sb.AppendLine($"{TownOfUsColors.ImpSoft.ToTextColor()}Imp</color> Concealing: {imp.MaxImpConcealing.Value} Max");

            sb.AppendLine($"{TownOfUsColors.Neutral.ToTextColor()}Neutrals</color>: {neut.MaxNeutrals.Value} Max");
            sb.AppendLine($"{TownOfUsColors.Neutral.ToTextColor()}Neutral</color> Benigns: {neut.MaxNeutBenign.Value} Max");
            sb.AppendLine($"{TownOfUsColors.Neutral.ToTextColor()}Neutral</color> Killings: {neut.MaxNeutKilling.Value} Max");
            sb.AppendLine($"{TownOfUsColors.Neutral.ToTextColor()}Neutral</color> Evils: {neut.MaxNeutEvil.Value} Max");
        }

        private static string GetDraftRoleForSlot(RoleListOption slotValue)
        {
            var idx = (int)slotValue;
            var strings = DraftOptions.OptionStrings;
            if (idx >= 0 && idx < strings.Length)
            {
                return strings[idx];
            }

            return "<color=#696969>???</color>";
        }
    }
}