using BepInEx.Configuration;
using MiraAPI.LocalSettings;
using MiraAPI.Utilities;
using TownOfUs.Assets;
using UnityEngine;
using MiraAPI.LocalSettings.Attributes;

namespace DraftModeTOUM;

public enum AudioTiming
{
    NoSound = 0,
    DraftStart = 1,
    TurnStart = 2
}

public sealed class DraftModeLocalSettings(ConfigFile config) : LocalSettingsTab(config)
{
    public override string TabName => "Draft Mode";
    protected override bool ShouldCreateLabels => true;

    public override LocalSettingTabAppearance TabAppearance => new()
    {
        TabIcon = TouRoleIcons.Traitor
    };

    [LocalEnumSetting]
    public ConfigEntry<AudioTiming> AudioCueTiming { get; private set; } =
        config.Bind("Audio", "Cue Timing", AudioTiming.DraftStart, "When to play the draft audio cue sound");

    [LocalToggleSetting]
    public ConfigEntry<bool> CustomChime { get; private set; } =
        config.Bind("Audio", "Custom Chime", false,
            "If enabled, plays BepInEx/config/draftchime.wave or BepInEx/config/draftchime.mp3 instead of the default cue");
}
