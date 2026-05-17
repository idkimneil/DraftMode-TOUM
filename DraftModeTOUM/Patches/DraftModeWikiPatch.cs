using System.Collections.Generic;
using System.Linq;
using System.Text;
using DraftModeTOUM.Managers;
using HarmonyLib;
using MiraAPI.GameOptions;
using TMPro;
using TownOfUs.Assets;
using TownOfUs.Modules.Wiki;
using UnityEngine;

namespace DraftModeTOUM.Patches
{

    [HarmonyPatch(typeof(IngameWikiMinigame), nameof(IngameWikiMinigame.AddNewSettings))]
    public static class DraftModeWikiSettingsPatch
    {
        internal const string PageTitle = "Draft Mode";

        [HarmonyPostfix]
        public static void Postfix(IngameWikiMinigame instance)
        {
            instance._activeSettings.Add(new OptionWikiInfo(
                PageTitle,
                new List<AbstractOptionGroup>
                {
                    OptionGroupSingleton<DraftModeOptions>.Instance
                },
                TouRoleIcons.Traitor
            ));
        }
    }

    [HarmonyPatch(typeof(IngameWikiMinigame), "SelectSettingsPage")]
    public static class DraftModeWikiContentPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IngameWikiMinigame __instance)
        {
            var sectionName = __instance.SettingsScreenSectionName.Value;
            if (sectionName == null) return;

            string currentTitle = sectionName.text ?? string.Empty;
            bool isDraftPage = currentTitle.Contains("Draft Mode");

            if (!isDraftPage) return;

            sectionName.text = "Draft Mode Settings";
            var description = __instance.SettingsDescription.Value;
            if (description == null) return;
            description.text = BuildBody();
            description.ForceMeshUpdate();
            description.pageToDisplay = 1;
            var counter = __instance.SettingsScreenTabCount.Value;

            if (counter != null)
            {
                int total = description.textInfo.pageCount;
                counter.text = counter.text
                    .Replace($"<po>{description.pageToDisplay}", $"<po>1")
                    .Replace($"{description.pageToDisplay}", "1");
                var activeSettings = GetActiveSettings(__instance);
                int sectionIndex  = activeSettings.FindIndex(
                    s => s.Title == DraftModeWikiSettingsPatch.PageTitle) + 1;
                int sectionTotal  = activeSettings.Count;
                counter.text = $"{1}/{total}  ({sectionIndex}/{sectionTotal})";
            }
        }

        private static string BuildBody()
        {
            var opts = OptionGroupSingleton<DraftModeOptions>.Instance;
            var sb   = new StringBuilder();

            Append(sb, opts.EnableDraft
                ? "Draft Mode is <color=#88FF88><b>Enabled</b></color>"
                : "Draft Mode is <color=#FF6666><b>Disabled</b></color>");

            Append(sb, opts.ShowRecap.Value
                ? "Draft Recap is <color=#88FF88><b>Enabled</b></color>"
                : "Draft Recap is <color=#FF6666><b>Disabled</b></color>");

            Append(sb, opts.UseRoleListForPool.Value
                ? "Roles are pulled from the <color=#88FF88><b>Role List</b></color>"
                : "Roles are pulled from the <color=#88FF88><b>Manual Role Amounts</b></color>");

            if (!opts.UseRoleListForPool.Value)
            {

                Append(sb, $"<color=#88DDFF><b>{(int)opts.MaxImpostors.Value}</b></color> Impostor Role{P(opts.MaxImpostors.Value)} are available");
                Append(sb, $"<color=#88DDFF><b>{(int)opts.MaxNeutralKillings.Value}</b></color> Neutral Killing role{P(opts.MaxNeutralKillings.Value)} are available!");
                Append(sb, $"<color=#88DDFF><b>{(int)opts.MaxNeutralPassives.Value}</b></color> Neutral Other role{P(opts.MaxNeutralPassives.Value)} are available!");
            }

            Append(sb, $"You can choose from <color=#88DDFF><b>{(int)opts.OfferedRolesCount.Value}</b></color> possible role{P(opts.OfferedRolesCount.Value)}");
            Append(sb, $"You have <color=#88DDFF><b>{(int)opts.TurnDurationSeconds.Value} second{P(opts.TurnDurationSeconds.Value)}</b></color> to pick your role!");

            Append(sb, (int)opts.ConcurrentPicks.Value > 1
                ? $"<color=#88DDFF><b>{(int)opts.ConcurrentPicks.Value}</b></color> players pick at the same time each round"
                : "Players pick <color=#88DDFF><b>one at a time</b></color> in order");

            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string line) => sb.AppendLine(line);
        private static string P(float v) => (int)v == 1 ? "" : "s";

        private static List<OptionWikiInfo> GetActiveSettings(IngameWikiMinigame instance)
        {
            return instance._activeSettings;
        }
    }
}