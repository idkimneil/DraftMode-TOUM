using BepInEx.Configuration;
using MiraAPI.LocalSettings;
using TownOfUs.Assets;
using MiraAPI.LocalSettings.Attributes;
using UnityEngine;

namespace DraftModeTOUM;

public enum AudioTiming
{
    NoSound    = 0,
    DraftStart = 1,
    TurnStart  = 2
}

public sealed class DraftModeLocalSettings(ConfigFile config) : LocalSettingsTab(config)
{
    public override string TabName => "Draft Mode";
    protected override bool ShouldCreateLabels => true;

    public override LocalSettingTabAppearance TabAppearance => new()
    {
        TabIcon = DraftAssets.DraftIcon,
        TabColor = Color.green,
        TabButtonHoverColor = Color.cyan,
        TabButtonActiveColor = Color.cyan,
    };

    [LocalEnumSetting]
    public ConfigEntry<AudioTiming> AudioCueTiming { get; private set; } =
        config.Bind("Audio", "Cue Timing", AudioTiming.DraftStart);

    [LocalToggleSetting]
    public ConfigEntry<bool> CustomChime { get; private set; } =
        config.Bind("Audio", "Custom Chime", false);

    [LocalToggleSetting]
    public ConfigEntry<bool> HideHudDuringDraft { get; private set; } =
        config.Bind("UI", "Hide HUD During Draft", true);
}