using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using MiraAPI.GameOptions;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using TownOfUs.Options;
using TownOfUs.Patches;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public sealed class RoleListSlotEntry
    {
        public int SlotIndex { get; }
        public ushort? ConcreteRoleId { get; set; }
        public RoleFaction? FactionConstraint { get; set; }
        public bool IsAny { get; set; }
        public bool IsNonImp { get; set; }
        public RoleListCategory Category { get; set; } = RoleListCategory.None;

        public RoleListSlotEntry(int slotIndex)
        {
            SlotIndex = slotIndex;
        }
    }

    public enum RoleListCategory
    {
        None,
        CrewCommon,
        CrewInvest,
        CrewProtective,
        CrewSupport,
        CrewKilling,
        CrewPower,
        CrewSpecial,       // Killing + Power
        ImpCommon,         // Concealing + Support
        ImpSpecial,        // Killing + Power
        ImpConceal,
        ImpKilling,
        ImpPower,
        ImpSupport,
        NeutBenign,
        NeutEvil,
        NeutKilling,
        NeutOutlier,
        NeutCommon,        // Benign + Evil
        NeutSpecial,       // Killing + Outlier
        NeutWildcard,      // Benign + Evil + Outlier
    }

    public static class RoleListPoolBuilder
    {
        public static DraftRolePool BuildFromRoleList(int playerCount)
        {
            var pool = new DraftRolePool();

            try
            {
                var roleOptions = OptionGroupSingleton<RoleOptions>.Instance;
                if (roleOptions == null)
                {
                    DraftModePlugin.Logger.LogWarning("[RoleListPoolBuilder] RoleOptions not available.");
                    return RolePoolBuilder.BuildPool();
                }

                var slotValues = GetAllSlotValues(roleOptions);
                int slotsToUse = Mathf.Min(playerCount, slotValues.Count);

                DraftModePlugin.Logger.LogInfo(
                    $"[RoleListPoolBuilder] Using {slotsToUse} role list slot(s) for {playerCount} players.");

                var entries = new List<RoleListSlotEntry>();

                for (int i = 0; i < slotsToUse && i < slotValues.Count; i++)
                {
                    var entry = ResolveSlot(i + 1, slotValues[i]);
                    entries.Add(entry);
                }

                for (int i = slotValues.Count; i < playerCount; i++)
                {
                    entries.Add(new RoleListSlotEntry(i + 1)
                    {
                        FactionConstraint = RoleFaction.Crewmate,
                        IsAny  = false,
                        IsNonImp = false,
                        Category = RoleListCategory.None,
                    });
                    DraftModePlugin.Logger.LogInfo(
                        $"[RoleListPoolBuilder] Slot {i + 1} beyond role list cap — Crewmate fallback.");
                }

                PopulatePool(pool, entries, playerCount);
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning(
                    $"[RoleListPoolBuilder] Failed building from role list: {ex.Message}\n{ex.StackTrace}");
                return RolePoolBuilder.BuildPool();
            }

            if (pool.RoleIds.Count == 0)
            {
                DraftModePlugin.Logger.LogWarning("[RoleListPoolBuilder] Pool empty after role list — using fallback.");
                return RolePoolBuilder.BuildPool();
            }

            DraftModePlugin.Logger.LogInfo(
                $"[RoleListPoolBuilder] Role list pool built: {pool.RoleIds.Count} distinct role(s).");
            return pool;
        }

        private static List<RoleListOption> GetAllSlotValues(RoleOptions roleOptions)
        {
            return new List<RoleListOption>
            {
                roleOptions.Slot1.Value,
                roleOptions.Slot2.Value,
                roleOptions.Slot3.Value,
                roleOptions.Slot4.Value,
                roleOptions.Slot5.Value,
                roleOptions.Slot6.Value,
                roleOptions.Slot7.Value,
                roleOptions.Slot8.Value,
                roleOptions.Slot9.Value,
                roleOptions.Slot10.Value,
                roleOptions.Slot11.Value,
                roleOptions.Slot12.Value,
                roleOptions.Slot13.Value,
                roleOptions.Slot14.Value,
                roleOptions.Slot15.Value,
            };
        }

        private static RoleListSlotEntry ResolveSlot(int slotIndex, RoleListOption option)
        {
            var e = new RoleListSlotEntry(slotIndex);

            switch (option)
            {
                case RoleListOption.Any:
                    e.IsAny = true;
                    break;

                case RoleListOption.NonImp:
                    e.IsNonImp = true;
                    break;

                case RoleListOption.CrewRandom:
                    e.FactionConstraint = RoleFaction.Crewmate;
                    break;
                case RoleListOption.CrewCommon:
                    e.FactionConstraint = RoleFaction.Crewmate;
                    e.Category = RoleListCategory.CrewCommon;
                    break;
                case RoleListOption.CrewInvest:
                    e.FactionConstraint = RoleFaction.Crewmate;
                    e.Category = RoleListCategory.CrewInvest;
                    break;
                case RoleListOption.CrewProtective:
                    e.FactionConstraint = RoleFaction.Crewmate;
                    e.Category = RoleListCategory.CrewProtective;
                    break;
                case RoleListOption.CrewSupport:
                    e.FactionConstraint = RoleFaction.Crewmate;
                    e.Category = RoleListCategory.CrewSupport;
                    break;
                case RoleListOption.CrewKilling:
                    e.FactionConstraint = RoleFaction.Crewmate;
                    e.Category = RoleListCategory.CrewKilling;
                    break;
                case RoleListOption.CrewPower:
                    e.FactionConstraint = RoleFaction.Crewmate;
                    e.Category = RoleListCategory.CrewPower;
                    break;
                case RoleListOption.CrewSpecial:
                    e.FactionConstraint = RoleFaction.Crewmate;
                    e.Category = RoleListCategory.CrewSpecial;
                    break;

                case RoleListOption.ImpRandom:
                    e.FactionConstraint = RoleFaction.Impostor;
                    break;
                case RoleListOption.ImpCommon:
                    e.FactionConstraint = RoleFaction.Impostor;
                    e.Category = RoleListCategory.ImpCommon;
                    break;
                case RoleListOption.ImpSpecial:
                    e.FactionConstraint = RoleFaction.Impostor;
                    e.Category = RoleListCategory.ImpSpecial;
                    break;
                case RoleListOption.ImpConceal:
                    e.FactionConstraint = RoleFaction.Impostor;
                    e.Category = RoleListCategory.ImpConceal;
                    break;
                case RoleListOption.ImpKilling:
                    e.FactionConstraint = RoleFaction.Impostor;
                    e.Category = RoleListCategory.ImpKilling;
                    break;
                case RoleListOption.ImpPower:
                    e.FactionConstraint = RoleFaction.Impostor;
                    e.Category = RoleListCategory.ImpPower;
                    break;
                case RoleListOption.ImpSupport:
                    e.FactionConstraint = RoleFaction.Impostor;
                    e.Category = RoleListCategory.ImpSupport;
                    break;
                case RoleListOption.NeutRandom:
                    break;
                case RoleListOption.NeutBenign:
                    e.Category = RoleListCategory.NeutBenign;
                    break;
                case RoleListOption.NeutEvil:
                    e.Category = RoleListCategory.NeutEvil;
                    break;
                case RoleListOption.NeutKilling:
                    e.FactionConstraint = RoleFaction.NeutralKilling;
                    e.Category = RoleListCategory.NeutKilling;
                    break;
                case RoleListOption.NeutOutlier:
                    e.Category = RoleListCategory.NeutOutlier;
                    break;
                case RoleListOption.NeutCommon:
                    e.Category = RoleListCategory.NeutCommon;
                    break;
                case RoleListOption.NeutSpecial:
                    e.Category = RoleListCategory.NeutSpecial;
                    break;
                case RoleListOption.NeutWildcard:
                    e.Category = RoleListCategory.NeutWildcard;
                    break;

                default:
                    e.IsAny = true;
                    DraftModePlugin.Logger.LogWarning(
                        $"[RoleListPoolBuilder] Unknown RoleListOption '{option}' on slot {slotIndex} — treating as Any.");
                    break;
            }

            return e;
        }

        private static List<RoleBehaviour> GatherAllEligibleRoles()
        {
            var result = new List<RoleBehaviour>();

            IEnumerable<RoleBehaviour> candidates;
            try   { candidates = MiscUtils.GetPotentialRoles().ToArray(); }
            catch { candidates = MiscUtils.AllRegisteredRoles.ToArray(); }

            foreach (var role in candidates)
            {
                if (role == null) continue;
                if (!CustomRoleUtils.CanSpawnOnCurrentMode(role)) continue;
                if (role.Role is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost or RoleTypes.GuardianAngel) continue;
                if (role is ICustomRole cr && (cr.Configuration.HideSettings || !cr.VisibleInSettings())) continue;
                if (RolePoolBuilder.IsBannedRole(role.NiceName)) continue;
                result.Add(role);
            }

            if (RoleManager.Instance != null)
            {
                var crew = RoleManager.Instance.GetRole(RoleTypes.Crewmate);
                if (crew != null && result.All(r => r.Role != RoleTypes.Crewmate))
                    result.Add(crew);
            }

            return result;
        }

        private static void PopulatePool(DraftRolePool pool, List<RoleListSlotEntry> entries, int playerCount)
        {
            var allRoles = GatherAllEligibleRoles();

            foreach (var role in allRoles)
            {
                if (role == null) continue;

                var   faction = GetFaction(role);
                var   roleId  = (ushort)role.Role;
                int maxCount = ComputeMaxCount(role, faction, entries, playerCount);
                if (maxCount <= 0) continue;
                int weight = GetWeightHint(role);

                AddOrUpdateRole(pool, roleId, maxCount, weight, faction, GetAlignment(role));
            }

            if (!pool.RoleIds.Contains((ushort)RoleTypes.Crewmate))
                AddOrUpdateRole(pool, (ushort)RoleTypes.Crewmate, playerCount, 1, RoleFaction.Crewmate);
        }
        private static int ComputeMaxCount(
            RoleBehaviour role,
            RoleFaction faction,
            List<RoleListSlotEntry> entries,
            int playerCount)
        {
            int count = 0;
            foreach (var e in entries)
                if (SlotAcceptsRole(e, role, faction))
                    count++;
            return Mathf.Min(count, playerCount);
        }

        private static bool SlotAcceptsRole(RoleListSlotEntry entry, RoleBehaviour role, RoleFaction faction)
        {
            if (entry.IsAny) return true;
            if (entry.IsNonImp)
                return faction == RoleFaction.Crewmate
                    || faction == RoleFaction.Neutral
                    || faction == RoleFaction.NeutralKilling;
            if (!entry.FactionConstraint.HasValue && entry.Category == RoleListCategory.None)
                return faction == RoleFaction.Neutral || faction == RoleFaction.NeutralKilling;

            if (entry.FactionConstraint.HasValue)
            {
                if (entry.FactionConstraint.Value != faction) return false;
                return entry.Category == RoleListCategory.None
                    || CategoryMatchesRole(entry.Category, role, faction);
            }
            if (IsNeutralCategory(entry.Category))
            {
                if (faction != RoleFaction.Neutral && faction != RoleFaction.NeutralKilling)
                    return false;
                return CategoryMatchesRole(entry.Category, role, faction);
            }

            return false;
        }

        private static bool IsNeutralCategory(RoleListCategory cat) => cat is
            RoleListCategory.NeutBenign  or RoleListCategory.NeutEvil    or
            RoleListCategory.NeutKilling or RoleListCategory.NeutOutlier or
            RoleListCategory.NeutCommon  or RoleListCategory.NeutSpecial  or
            RoleListCategory.NeutWildcard;

        private static bool CategoryMatchesRole(RoleListCategory category, RoleBehaviour role, RoleFaction faction)
        {
            string alignment = string.Empty;
            try { alignment = MiscUtils.GetParsedRoleAlignment(role) ?? string.Empty; }
            catch { }
            string lower = alignment.ToLowerInvariant();

            return category switch
            {
                RoleListCategory.CrewInvest     => lower.Contains("invest"),
                RoleListCategory.CrewProtective => lower.Contains("protect"),
                RoleListCategory.CrewSupport    => lower.Contains("support"),
                RoleListCategory.CrewKilling    => lower.Contains("killing"),
                RoleListCategory.CrewPower      => lower.Contains("power"),
                RoleListCategory.CrewCommon     => lower.Contains("invest") || lower.Contains("protect") || lower.Contains("support"),
                RoleListCategory.CrewSpecial    => lower.Contains("killing") || lower.Contains("power"),

                RoleListCategory.ImpConceal  => lower.Contains("conceal"),
                RoleListCategory.ImpKilling  => lower.Contains("killing"),
                RoleListCategory.ImpPower    => lower.Contains("power"),
                RoleListCategory.ImpSupport  => lower.Contains("support"),
                RoleListCategory.ImpCommon   => lower.Contains("conceal") || lower.Contains("support"),
                RoleListCategory.ImpSpecial  => lower.Contains("killing") || lower.Contains("power"),

                RoleListCategory.NeutKilling  => faction == RoleFaction.NeutralKilling,
                RoleListCategory.NeutBenign   => faction == RoleFaction.Neutral && (lower.Contains("benign") || lower.Contains("passive")),
                RoleListCategory.NeutEvil     => faction == RoleFaction.Neutral && lower.Contains("evil"),
                RoleListCategory.NeutOutlier  => faction == RoleFaction.Neutral && lower.Contains("outlier"),
                RoleListCategory.NeutCommon   => faction == RoleFaction.Neutral
                                                 && (lower.Contains("benign") || lower.Contains("passive") || lower.Contains("evil")),
                RoleListCategory.NeutSpecial  => faction == RoleFaction.NeutralKilling
                                                 || (faction == RoleFaction.Neutral && lower.Contains("outlier")),
                RoleListCategory.NeutWildcard => faction == RoleFaction.Neutral
                                                 && (lower.Contains("benign") || lower.Contains("passive")
                                                     || lower.Contains("evil") || lower.Contains("outlier")),

                _ => true,
            };
        }


        private static RoleFaction GetFaction(RoleBehaviour role) =>
            RoleCategory.GetFactionFromRole(role);

        private static int GetWeightHint(RoleBehaviour role)
        {
            try
            {
                var roleOptions = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions;
                if (roleOptions != null)
                {
                    int chance = roleOptions.GetChancePerGame(role.Role);
                    if (chance > 0) return chance;
                }
            }
            catch { }
            return 100;
        }

        private static string GetAlignment(RoleBehaviour role)
        {
            try { return MiscUtils.GetParsedRoleAlignment(role) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static void AddOrUpdateRole(DraftRolePool pool, ushort roleId, int maxCount, int weight,
                                            RoleFaction faction, string alignment = "")
        {
            if (!pool.MaxCounts.ContainsKey(roleId))
            {
                pool.RoleIds.Add(roleId);
                pool.MaxCounts[roleId]  = Math.Max(1, maxCount);
                pool.Weights[roleId]    = Math.Max(1, weight);
                pool.Factions[roleId]   = faction;
                pool.Alignments[roleId] = alignment ?? string.Empty;
            }
            else
            {
                pool.MaxCounts[roleId] = Math.Max(pool.MaxCounts[roleId], maxCount);
                pool.Weights[roleId]   = Math.Max(pool.Weights[roleId], weight);
            }
        }
    }
}