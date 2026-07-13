using System;
using MiraAPI.GameOptions;
using MiraAPI.GameOptions.OptionTypes;
using UnityEngine;
using TownOfUs;


namespace DraftMode.Options;

public sealed class RoleDraftRoleListOptions : AbstractOptionGroup
{
    public override Func<bool> GroupVisible => () =>
        OptionGroupSingleton<DraftOptions>.Instance.IsDraftMode.Value && OptionGroupSingleton<DraftOptions>.Instance.UseRoleListForPool.Value;

    public override string GroupName => "Role List Settings";
    public override uint GroupPriority => 3;
    public override Color GroupColor => TownOfUsColors.Jester;

    public ModdedEnumOption<RoleListOption> Slot1 { get; } =
        new("Slot 1", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot2 { get; } =
        new("Slot 2", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot3 { get; } =
        new("Slot 3", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot4 { get; } =
        new("Slot 4", RoleListOption.ImpCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot5 { get; } =
        new("Slot 5", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot6 { get; } =
        new("Slot 6", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot7 { get; } =
        new("Slot 7", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot8 { get; } =
        new("Slot 8", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot9 { get; } =
        new("Slot 9", RoleListOption.ImpCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot10 { get; } =
        new("Slot 10", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot11 { get; } =
        new("Slot 11", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot12 { get; } =
        new("Slot 12", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot13 { get; } =
        new("Slot 13", RoleListOption.CrewCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot14 { get; } =
        new("Slot 14", RoleListOption.ImpCommon, DraftOptions.OptionStrings);

    public ModdedEnumOption<RoleListOption> Slot15 { get; } =
        new("Slot 15", RoleListOption.CrewCommon, DraftOptions.OptionStrings);
}