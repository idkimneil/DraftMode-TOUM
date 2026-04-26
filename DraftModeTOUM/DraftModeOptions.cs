using MiraAPI.GameOptions;
using MiraAPI.GameOptions.Attributes;
using MiraAPI.GameOptions.OptionTypes;
using MiraAPI.Utilities;

namespace DraftModeTOUM;

public sealed class DraftModeOptions : AbstractOptionGroup
{
    public override string GroupName => "Draft Mode";
    public override uint GroupPriority => 100;

    [ModdedToggleOption("Enable Draft Mode")]
    public bool EnableDraft { get; set; } = true;

    public ModdedToggleOption LockLobbyOnDraftStart { get; set; } = new("Lock Lobby On Draft Start", true)
    {
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
    };

    public ModdedToggleOption AutoStartAfterDraft { get; set; } = new("Auto-Start After Draft", true)
    {
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
    };

    public ModdedToggleOption ShowRecap { get; set; } = new("Show Draft Recap", true)
    {
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
    };

    /// <summary>
    /// When enabled, the draft pool is built from the active TOU Role List
    /// (slots 1-15 scaled to player count) instead of the role chance/count
    /// options.  Slots beyond 15 fall back to a random non-impostor role.
    /// When disabled the original UseRoleChances weighting is used instead.
    /// </summary>
    public ModdedToggleOption UseRoleListForPool { get; set; } = new("Use Role List For Pool", false)
    {
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
    };

    public ModdedToggleOption UseRoleChances { get; set; } = new("Use Role Chances For Weighting", true)
    {
        // Only visible when NOT using the role list, since the role list
        // already carries its own chance weighting via GetChancePerGame.
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
                     && !OptionGroupSingleton<DraftModeOptions>.Instance.UseRoleListForPool
    };

    public ModdedToggleOption ShowRandomOption { get; set; } = new("Show Random Option", true)
    {
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
    };

    public ModdedNumberOption OfferedRolesCount { get; set; } = new("Offered Roles Per Turn", 3f, 1f, 9f, 1f, MiraNumberSuffixes.None, "0")
    {
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
    };

    public ModdedNumberOption TurnDurationSeconds { get; set; } = new("Turn Duration", 10f, 5f, 60f, 1f, MiraNumberSuffixes.Seconds, "0")
    {
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
    };

    public ModdedNumberOption ConcurrentPicks { get; set; } = new("Concurrent Picks Per Turn", 1f, 1f, 2f, 1f, MiraNumberSuffixes.None, "0")
    {
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
    };

    public ModdedNumberOption MaxImpostors { get; set; } = new("Max Impostors", 2f, 1f, 5f, 1f, MiraNumberSuffixes.None, "0")
    {
        // Impostor cap is still respected even in role list mode — it acts as
        // a safety ceiling on top of what the role list specifies.
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
        && !OptionGroupSingleton<DraftModeOptions>.Instance.UseRoleListForPool
    };

    public ModdedNumberOption MaxNeutralKillings { get; set; } = new("Max Neutral Killings", 2f, 1f, 10f, 1f, MiraNumberSuffixes.None, "0")
    {
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
        && !OptionGroupSingleton<DraftModeOptions>.Instance.UseRoleListForPool
    };

    public ModdedNumberOption MaxNeutralPassives { get; set; } = new("Max Neutral Other", 3f, 1f, 10f, 1f, MiraNumberSuffixes.None, "0")
    {
        Visible = () => OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft
        && !OptionGroupSingleton<DraftModeOptions>.Instance.UseRoleListForPool
    };
}