using AmongUs.GameOptions;
using MiraAPI.GameOptions;
using MiraAPI.GameOptions.OptionTypes;
using MiraAPI.Utilities;

namespace DraftMode.Options;

public sealed class DraftOptions : AbstractOptionGroup
{
    internal static string[] OptionStrings =
    [
        MiscUtils.GetParsedRoleBucket("CrewInvestigative"),
        MiscUtils.GetParsedRoleBucket("CrewKilling"),
        MiscUtils.GetParsedRoleBucket("CrewProtective"),
        MiscUtils.GetParsedRoleBucket("CrewPower"),
        MiscUtils.GetParsedRoleBucket("CrewSupport"),

        MiscUtils.GetParsedRoleBucket("CommonCrew"),
        MiscUtils.GetParsedRoleBucket("SpecialCrew"),
        MiscUtils.GetParsedRoleBucket("RandomCrew"),

        MiscUtils.GetParsedRoleBucket("NeutralBenign"),
        MiscUtils.GetParsedRoleBucket("NeutralEvil"),
        MiscUtils.GetParsedRoleBucket("NeutralKilling"),
        MiscUtils.GetParsedRoleBucket("NeutralOutlier"),

        MiscUtils.GetParsedRoleBucket("CommonNeutral"),
        MiscUtils.GetParsedRoleBucket("SpecialNeutral"),
        MiscUtils.GetParsedRoleBucket("WildcardNeutral"),
        MiscUtils.GetParsedRoleBucket("RandomNeutral"),

        MiscUtils.GetParsedRoleBucket("ImpConcealing"),
        MiscUtils.GetParsedRoleBucket("ImpKilling"),
        MiscUtils.GetParsedRoleBucket("ImpPower"),
        MiscUtils.GetParsedRoleBucket("ImpSupport"),

        MiscUtils.GetParsedRoleBucket("CommonImp"),
        MiscUtils.GetParsedRoleBucket("SpecialImp"),
        MiscUtils.GetParsedRoleBucket("RandomImp"),

        MiscUtils.GetParsedRoleBucket("NonImp"),
        MiscUtils.GetParsedRoleBucket("Any")
    ];

    public override string GroupName => "Draft Settings";
    public override uint GroupPriority => 2;

    public ModdedToggleOption IsDraftMode { get; set; } = new("Enable Draft Mode", true)
    {
    };

    public ModdedEnumOption<DraftRecapMode> DraftRecap { get; } =
        new("Draft Recap Displays", DraftRecapMode.Faction)
        {
            Visible = () => OptionGroupSingleton<DraftOptions>.Instance.IsDraftMode.Value
        };

    public ModdedEnumOption<DraftRecapMode> DraftSidebarDisplay { get; } =
        new("Draft Sidebar Displays", DraftRecapMode.Faction)
        {
            Visible = () => OptionGroupSingleton<DraftOptions>.Instance.IsDraftMode.Value
        };

    public ModdedToggleOption UseRoleListForPool { get; set; } = new("Use Role List For Pool", false)
    {
        Visible = () => OptionGroupSingleton<DraftOptions>.Instance.IsDraftMode.Value
    };

    public ModdedNumberOption OfferedRolesCount { get; set; } = new("Offered Role Picks Per Turn", 3f, 1f, 9f, 1f, MiraNumberSuffixes.None, "0")
    {
        Visible = () => OptionGroupSingleton<DraftOptions>.Instance.IsDraftMode.Value
    };

    public ModdedToggleOption ShowRandomOption { get; set; } = new("Show Random Role Pick", true)
    {
        Visible = () => OptionGroupSingleton<DraftOptions>.Instance.IsDraftMode.Value
    };
    public ModdedNumberOption RerollsPerPlayer { get; set; } = new("Rerolls Per Player", 1f, 0f, 3f, 1f, MiraNumberSuffixes.None, "0")
    {
        Visible = () => OptionGroupSingleton<DraftOptions>.Instance.IsDraftMode.Value
    };

    public ModdedNumberOption TurnDurationSeconds { get; set; } = new("Turn Duration", 10f, 5f, 60f, 1f, MiraNumberSuffixes.Seconds, "0")
    {
        Visible = () => OptionGroupSingleton<DraftOptions>.Instance.IsDraftMode.Value
    };

    public ModdedNumberOption ConcurrentPicks { get; set; } = new("Concurrent Picks Per Turn", 1f, 1f, 2f, 1f, MiraNumberSuffixes.None, "0")
    {
        Visible = () => OptionGroupSingleton<DraftOptions>.Instance.IsDraftMode.Value
    };
}

public enum DraftRecapMode
{
    Nothing,
    Faction,
    Alignment,
    Role,
}

public enum RoleListOption
{
    CrewInvest,
    CrewKilling,
    CrewProtective,
    CrewPower,
    CrewSupport,

    CrewCommon, // Investigative / Protective / Support
    CrewSpecial, // Killing / Power
    // CrewUtility, // Investigative / Support
    // CrewBasic, // Vanilla Crewmate
    CrewRandom, // Any Crewmate role

    NeutBenign,
    NeutEvil,
    NeutKilling,
    NeutOutlier,

    NeutCommon, // Benign / Evil
    NeutSpecial, // Killing / Outlier
    NeutWildcard, // Benign / Evil / Outlier
    // NeutChaos, // Evil / Outlier
    // NeutPassive, // Benign / Outlier, this name sucks btw - Atony
    NeutRandom, // Any Neutral role

    ImpConceal,
    ImpKilling,
    ImpPower,
    ImpSupport,

    ImpCommon, // Concealing / Support
    ImpSpecial, // Killing / Power
    // ImpUtility, // Concealing / Killing / Support
    // ImpBasic, // Vanilla Impostor
    ImpRandom, // Any Impostor role

    NonImp, // Crewmate / Neutral
    // NonKilling, // Everything but Impostors, NKs, and CKs
    // AnyKilling, // Impostors, NKs, and CKs
    Any
}