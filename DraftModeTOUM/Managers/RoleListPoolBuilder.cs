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
    /// <summary>
    /// A pool entry produced from a single Role List slot.
    /// Each slot resolves to either a specific role id or a faction bucket.
    /// </summary>
    public sealed class RoleListSlotEntry
    {
        /// <summary>Slot index (1-based, mirrors the role list UI).</summary>
        public int SlotIndex { get; }

        /// <summary>
        /// The concrete role id resolved for this slot, or null if the slot
        /// only constrains a faction (e.g. CrewRandom, ImpRandom, NonImp).
        /// </summary>
        public ushort? ConcreteRoleId { get; set; }

        /// <summary>
        /// Faction constraint implied by the slot value.
        /// For Any slots this is null (anything goes).
        /// </summary>
        public RoleFaction? FactionConstraint { get; set; }

        /// <summary>
        /// When true the slot allows any role — no faction restriction.
        /// </summary>
        public bool IsAny { get; set; }

        /// <summary>
        /// Sub-category hint carried by the slot (e.g. CrewInvest, CrewKilling).
        /// Used to bias weighted picking within the faction.
        /// </summary>
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

    /// <summary>
    /// Builds a <see cref="DraftRolePool"/> from the TOU Role List when
    /// <see cref="DraftModeOptions.UseRoleListForPool"/> is enabled.
    /// </summary>
    public static class RoleListPoolBuilder
    {
        // ── Public entry point ───────────────────────────────────────────────

        /// <summary>
        /// Builds and returns a pool derived from the active TOU Role List.
        /// Player count is used to cap the number of slots consumed.
        /// Slots beyond 15 receive a random non-impostor fallback entry.
        /// </summary>
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

                // Resolve the slots we have role-list data for (up to 15)
                for (int i = 0; i < slotsToUse && i < slotValues.Count; i++)
                {
                    var entry = ResolveSlot(i + 1, slotValues[i]);
                    entries.Add(entry);
                }

                // Players beyond slot 15 get a random non-impostor fallback
                for (int i = slotValues.Count; i < playerCount; i++)
                {
                    var fallback = new RoleListSlotEntry(i + 1)
                    {
                        FactionConstraint = null, // crewmate or neutral passive
                        IsAny = false,
                        Category = RoleListCategory.None,
                    };
                    entries.Add(fallback);
                    DraftModePlugin.Logger.LogInfo(
                        $"[RoleListPoolBuilder] Slot {i + 1} beyond role list cap — using non-impostor fallback.");
                }

                // Now build the draft pool from the resolved entries
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

        // ── Slot value extraction ────────────────────────────────────────────

        private static List<RoleListOption> GetAllSlotValues(RoleOptions roleOptions)
        {
            // Reads all 15 slots in order.
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

        // ── Slot resolution ──────────────────────────────────────────────────

        private static RoleListSlotEntry ResolveSlot(int slotIndex, RoleListOption option)
        {
            var entry = new RoleListSlotEntry(slotIndex);

            switch (option)
            {
                // ── Any ──────────────────────────────────────────────────────
                case RoleListOption.Any:
                    entry.IsAny = true;
                    entry.Category = RoleListCategory.None;
                    break;

                // ── Non-Impostor (crewmate or neutral) ───────────────────────
                case RoleListOption.NonImp:
                    entry.FactionConstraint = null; // crewmate OR neutral — handled in picker
                    entry.Category = RoleListCategory.None;
                    break;

                // ── Crewmate broad categories ─────────────────────────────────
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

                // ── Impostor ──────────────────────────────────────────────────
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
                    // Unknown / future option — treat as Any
                    entry.IsAny = true;
                    DraftModePlugin.Logger.LogWarning(
                        $"[RoleListPoolBuilder] Unknown RoleListOption '{option}' on slot {slotIndex} — treating as Any.");
                    break;
            }

            return entry;
        }

        // ── Pool population ──────────────────────────────────────────────────

        private static void PopulatePool(DraftRolePool pool, List<RoleListSlotEntry> entries, int playerCount)
        {
            // Build a lookup of all available enabled roles (same filter as RolePoolBuilder)
            var allRoles = GatherEnabledRoles(playerCount);

            // Count how many times each faction is needed by the slot list
            // so we can derive per-faction max counts for the pool.
            var factionNeeds = CountFactionNeeds(entries);

            foreach (var role in allRoles)
            {
                if (role == null) continue;

                var faction = GetFaction(role);
                var roleId  = (ushort)role.Role;

                // Determine max drafts for this role based on slot requirements
                int maxCount = ComputeMaxCount(role, faction, entries, factionNeeds, playerCount);
                if (maxCount <= 0) continue;

                int weight = GetWeight(role);
                AddOrUpdateRole(pool, roleId, maxCount, weight, faction);
            }

            // Always ensure at least plain Crewmate is available as a safety net
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

            // Count slots that explicitly match this role by name
            int explicitSlots = entries.Count(e =>
                e.ConcreteRoleId.HasValue && e.ConcreteRoleId.Value == roleId);

            // Count slots whose faction/category constraint this role satisfies
            int constraintSlots = entries.Count(e =>
                !e.ConcreteRoleId.HasValue && SlotAcceptsRole(e, role, faction));

            int total = explicitSlots + constraintSlots;

            // Hard cap: never exceed how many slots need this faction
            if (factionNeeds.TryGetValue(faction, out int factionCap))
                total = Mathf.Min(total, factionCap);

            // Also cap at playerCount (can never draft more than all players)
            return Mathf.Min(total, playerCount);
        }

        private static bool SlotAcceptsRole(RoleListSlotEntry entry, RoleBehaviour role, RoleFaction faction)
        {
            if (entry.IsAny) return true;

            // NonImp fallback entries: accept crewmate and neutral passive, not NK or imp
            if (!entry.FactionConstraint.HasValue)
                return faction == RoleFaction.Crewmate || faction == RoleFaction.Neutral;

            if (entry.FactionConstraint.Value != faction) return false;

            // No further sub-category filtering — any crewmate/imp role qualifies
            if (entry.Category == RoleListCategory.None) return true;

            // Sub-category filtering via TOU alignment string
            return CategoryMatchesRole(entry.Category, role);
        }

        // ── Category matching ────────────────────────────────────────────────

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
                // CrewCommon / CrewSpecial / ImpCommon / ImpSpecial — no alignment string in TOU,
                // fall back to accepting all roles in the faction.
                _ => true,
            };
        }

        // ── Faction need counting ────────────────────────────────────────────

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

        // ── Role gathering ───────────────────────────────────────────────────

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

        // ── Helpers ──────────────────────────────────────────────────────────

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

        private static void AddOrUpdateRole(DraftRolePool pool, ushort roleId, int maxCount, int weight, RoleFaction faction)
        {
            if (!pool.MaxCounts.ContainsKey(roleId))
            {
                pool.RoleIds.Add(roleId);
                pool.MaxCounts[roleId] = Math.Max(1, maxCount);
                pool.Weights[roleId]   = Math.Max(1, weight);
                pool.Factions[roleId]  = faction;
            }
            else
            {
                pool.MaxCounts[roleId] = Math.Max(pool.MaxCounts[roleId], maxCount);
                pool.Weights[roleId]   = Math.Max(pool.Weights[roleId], weight);
            }
        }
    }
}