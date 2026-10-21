using AmongUs.GameOptions;
using TownOfUs.Extensions;
using TownOfUs.Interfaces;
using TownOfUs.Roles;
using TownOfUs.Utilities;

namespace DraftFix;

internal sealed record DraftCopy(ushort RoleId, RoleAlignment Alignment, int Chance, int Weight);

internal static class HostDraftPool
{
    internal static readonly RoleAlignment[] Crew =
    [
        RoleAlignment.CrewmateInvestigative, RoleAlignment.CrewmateKilling, RoleAlignment.CrewmateProtective,
        RoleAlignment.CrewmatePower, RoleAlignment.CrewmateSupport
    ];

    internal static readonly RoleAlignment[] Impostor =
    [
        RoleAlignment.ImpostorConcealing, RoleAlignment.ImpostorKilling, RoleAlignment.ImpostorPower,
        RoleAlignment.ImpostorSupport
    ];

    internal static readonly RoleAlignment[] Neutral =
    [
        RoleAlignment.NeutralBenign, RoleAlignment.NeutralEvil, RoleAlignment.NeutralKilling,
        RoleAlignment.NeutralOutlier
    ];

    internal static List<DraftCopy> Build()
    {
        var blocked = MiscUtils.SpawnableRoles
            .Where(x => x is ISpawnChange { NoSpawn: true })
            .Select(x => x.Role)
            .ToHashSet();

        bool Allowed(RoleBehaviour role) => !blocked.Contains(role.Role) && role is not ITownOfUsRole { IsDraftable: false };

        var pool = new List<DraftCopy>();
        foreach (var alignment in Crew.Concat(Impostor).Concat(Neutral))
        {
            foreach (var (_, roleType, chance) in MiscUtils.GetRolesToAssign(alignment, Allowed))
            {
                if (chance <= 0)
                {
                    continue;
                }

                var weight = MiscUtils.GetRegisteredRole((RoleTypes)roleType) is IDoubleDraftRole { IsDoubleDraftRole: true } ? 2 : 1;
                pool.Add(new DraftCopy(roleType, alignment, chance, weight));
            }
        }

        return pool;
    }
}
