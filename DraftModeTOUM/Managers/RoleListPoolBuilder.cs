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
        CrewSpecial,
        ImpCommon,
        ImpSpecial,
        NeutralKilling,
        NeutralPassive,
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
                    return RolePoolBuilder.BuildPool(); // fallback
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
                    var fallback = new RoleListSlotEntry(i + 1)
                    {
                        FactionConstraint = null,
                        IsAny = false,
                        Category = RoleListCategory.None,
                    };
                    entries.Add(fallback);
                    DraftModePlugin.Logger.LogInfo(
                        $"[RoleListPoolBuilder] Slot {i + 1} beyond role list cap — using non-impostor fallback.");
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
            var entry = new RoleListSlotEntry(slotIndex);

            switch (option)
            {
                case RoleListOption.Any:
                    entry.IsAny = true;
                    entry.Category = RoleListCategory.None;
                    break;
                case RoleListOption.NonImp:
                    entry.FactionConstraint = null;
                    entry.Category = RoleListCategory.None;
                    break;
                case RoleListOption.CrewRandom:
                    entry.FactionConstraint = RoleFaction.Crewmate;
                    entry.Category = RoleListCategory.None;
                    break;
                case RoleListOption.CrewCommon:
                    entry.FactionConstraint = RoleFaction.Crewmate;
                    entry.Category = RoleListCategory.CrewCommon;
                    break;
                case RoleListOption.CrewInvest:
                    entry.FactionConstraint = RoleFaction.Crewmate;
                    entry.Category = RoleListCategory.CrewInvest;
                    break;
                case RoleListOption.CrewProtective:
                    entry.FactionConstraint = RoleFaction.Crewmate;
                    entry.Category = RoleListCategory.CrewProtective;
                    break;
                case RoleListOption.CrewSupport:
                    entry.FactionConstraint = RoleFaction.Crewmate;
                    entry.Category = RoleListCategory.CrewSupport;
                    break;
                case RoleListOption.CrewKilling:
                    entry.FactionConstraint = RoleFaction.Crewmate;
                    entry.Category = RoleListCategory.CrewKilling;
                    break;
                case RoleListOption.CrewPower:
                    entry.FactionConstraint = RoleFaction.Crewmate;
                    entry.Category = RoleListCategory.CrewPower;
                    break;
                case RoleListOption.CrewSpecial:
                    entry.FactionConstraint = RoleFaction.Crewmate;
                    entry.Category = RoleListCategory.CrewSpecial;
                    break;
                case RoleListOption.ImpRandom:
                    entry.FactionConstraint = RoleFaction.Impostor;
                    entry.Category = RoleListCategory.None;
                    break;
                case RoleListOption.ImpCommon:
                    entry.FactionConstraint = RoleFaction.Impostor;
                    entry.Category = RoleListCategory.ImpCommon;
                    break;
                case RoleListOption.ImpSpecial:
                    entry.FactionConstraint = RoleFaction.Impostor;
                    entry.Category = RoleListCategory.ImpSpecial;
                    break;

                default:
                    entry.IsAny = true;
                    DraftModePlugin.Logger.LogWarning(
                        $"[RoleListPoolBuilder] Unknown RoleListOption '{option}' on slot {slotIndex} — treating as Any.");
                    break;
            }

            return entry;
        }
        private static void PopulatePool(DraftRolePool pool, List<RoleListSlotEntry> entries, int playerCount)
        {
            var allRoles = GatherEnabledRoles(playerCount);
            var factionNeeds = CountFactionNeeds(entries);

            foreach (var role in allRoles)
            {
                if (role == null) continue;

                var faction = GetFaction(role);
                var roleId  = (ushort)role.Role;
                int maxCount = ComputeMaxCount(role, faction, entries, factionNeeds, playerCount);
                if (maxCount <= 0) continue;
                int weight = GetWeight(role);
                AddOrUpdateRole(pool, roleId, maxCount, weight, faction, GetAlignment(role));
            }
            if (!pool.RoleIds.Contains((ushort)RoleTypes.Crewmate))
            {
                AddOrUpdateRole(pool, (ushort)RoleTypes.Crewmate, playerCount, 1, RoleFaction.Crewmate);
            }
        }

        private static int ComputeMaxCount(
            RoleBehaviour role,
            RoleFaction faction,
            List<RoleListSlotEntry> entries,
            Dictionary<RoleFaction?, int> factionNeeds,
            int playerCount)
        {
            var roleId = (ushort)role.Role;
            int explicitSlots = entries.Count(e =>
                e.ConcreteRoleId.HasValue && e.ConcreteRoleId.Value == roleId);
            int constraintSlots = entries.Count(e =>
                !e.ConcreteRoleId.HasValue && SlotAcceptsRole(e, role, faction));
            int total = explicitSlots + constraintSlots;
            if (factionNeeds.TryGetValue(faction, out int factionCap))
                total = Mathf.Min(total, factionCap);
            return Mathf.Min(total, playerCount);
        }

        private static bool SlotAcceptsRole(RoleListSlotEntry entry, RoleBehaviour role, RoleFaction faction)
        {
            if (entry.IsAny) return true;
            if (!entry.FactionConstraint.HasValue)
                return faction == RoleFaction.Crewmate || faction == RoleFaction.Neutral;

            if (entry.FactionConstraint.Value != faction) return false;
            if (entry.Category == RoleListCategory.None) return true;

            return CategoryMatchesRole(entry.Category, role);
        }

        private static bool CategoryMatchesRole(RoleListCategory category, RoleBehaviour role)
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
                _ => true,
            };
        }

        private static Dictionary<RoleFaction?, int> CountFactionNeeds(List<RoleListSlotEntry> entries)
        {
            var dict = new Dictionary<RoleFaction?, int>();
            foreach (var e in entries)
            {
                var key = e.IsAny ? (RoleFaction?)null : e.FactionConstraint;
                dict[key] = dict.TryGetValue(key, out int c) ? c + 1 : 1;
            }
            return dict;
        }

        private static List<RoleBehaviour> GatherEnabledRoles(int playerCount)
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

            // Always include plain Crewmate
            if (RoleManager.Instance != null)
            {
                var crew = RoleManager.Instance.GetRole(RoleTypes.Crewmate);
                if (crew != null && !result.Any(r => r.Role == RoleTypes.Crewmate))
                    result.Add(crew);
            }

            return result;
        }

        private static RoleFaction GetFaction(RoleBehaviour role) =>
            RoleCategory.GetFactionFromRole(role);

        private static int GetWeight(RoleBehaviour role)
        {
            try
            {
                var roleOptions = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions;
                if (roleOptions != null)
                {
                    int chance = roleOptions.GetChancePerGame(role.Role);
                    return Mathf.Max(1, chance);
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

        private static void AddOrUpdateRole(DraftRolePool pool, ushort roleId, int maxCount, int weight, RoleFaction faction, string alignment = "")
        {
            if (!pool.MaxCounts.ContainsKey(roleId))
            {
                pool.RoleIds.Add(roleId);
                pool.MaxCounts[roleId] = Math.Max(1, maxCount);
                pool.Weights[roleId]   = Math.Max(1, weight);
                pool.Factions[roleId]  = faction;
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