using System;
using System.Collections.Generic;
using System.Linq;

namespace DraftModeTOUM.Managers
{
    public sealed class FactionBudget
    {
        public int Impostor;
        public int NeutralKilling;
        public int Neutral;

        public FactionBudget(int impostor, int neutralKilling, int neutral)
        {
            Impostor = impostor;
            NeutralKilling = neutralKilling;
            Neutral = neutral;
        }

        public int Remaining(RoleFaction faction) => faction switch
        {
            RoleFaction.Impostor => Impostor,
            RoleFaction.NeutralKilling => NeutralKilling,
            RoleFaction.Neutral => Neutral,
            _ => int.MaxValue
        };

        public void Consume(RoleFaction faction)
        {
            if (faction == RoleFaction.Impostor && Impostor > 0) Impostor--;
            else if (faction == RoleFaction.NeutralKilling && NeutralKilling > 0) NeutralKilling--;
            else if (faction == RoleFaction.Neutral && Neutral > 0) Neutral--;
        }
    }

    public sealed class DraftDistribution
    {
        private readonly DraftRolePool _pool;
        private readonly DraftConfig _cfg;
        private readonly IRng _rng;
        private readonly Dictionary<ushort, int> _drafted = new();
        private readonly List<ushort> _tempLockPool       = new();
        private readonly List<ushort> _tempEvilCandidates = new();
        private readonly List<ushort> _tempNonCrew        = new();

        public int ImpostorsDrafted { get; private set; }
        public int NeutralKillingsDrafted { get; private set; }
        public int NeutralPassivesDrafted { get; private set; }

        public DraftDistribution(DraftRolePool pool, DraftConfig cfg, IRng rng)
        {
            _pool = pool ?? new DraftRolePool();
            _cfg = cfg ?? new DraftConfig();
            _rng = rng ?? new DeterministicRng(1u);
        }

        public DraftConfig Config => _cfg;

        public int GetDraftedCount(ushort id) => _drafted.TryGetValue(id, out var c) ? c : 0;
        public int GetMaxCount(ushort id) => _pool.MaxCounts.TryGetValue(id, out var c) ? c : 1;
        public RoleFaction GetFaction(ushort id) => _pool.Factions.TryGetValue(id, out var f) ? f : RoleFaction.Crewmate;
        public int GetWeight(ushort id) => _pool.Weights.TryGetValue(id, out var w) ? Math.Max(1, w) : 1;
        public string GetAlignment(ushort id) => _pool.Alignments.TryGetValue(id, out var a) ? (a ?? string.Empty) : string.Empty;
        public bool IsUniqueRole(ushort id) => id != _cfg.CrewmateRoleId;

        private int FactionDrafted(RoleFaction faction) => faction switch
        {
            RoleFaction.Impostor => ImpostorsDrafted,
            RoleFaction.NeutralKilling => NeutralKillingsDrafted,
            RoleFaction.Neutral => NeutralPassivesDrafted,
            _ => 0
        };

        private int FactionCap(RoleFaction faction) => faction switch
        {
            RoleFaction.Impostor => _cfg.MaxImpostors,
            RoleFaction.NeutralKilling => _cfg.MaxNeutralKillings,
            RoleFaction.Neutral => _cfg.MaxNeutralPassives,
            _ => int.MaxValue
        };

        public int FactionRemaining(RoleFaction faction) => Math.Max(0, FactionCap(faction) - FactionDrafted(faction));
        private int FloorDeficit(RoleFaction faction)
        {
            if (faction == RoleFaction.Crewmate) return 0;
            int raw = Math.Max(0, FactionCap(faction) - FactionDrafted(faction));
            if (raw == 0) return 0;
            return Math.Min(raw, GetAvailableForFaction(faction).Count);
        }

        private int PoolFactionCount(RoleFaction faction)
        {
            int c = 0;
            foreach (var id in _pool.RoleIds) if (GetFaction(id) == faction) c++;
            return c;
        }
        private int InitialFloorTotal() =>
              Math.Min(FactionCap(RoleFaction.Impostor),       PoolFactionCount(RoleFaction.Impostor))
            + Math.Min(FactionCap(RoleFaction.NeutralKilling),  PoolFactionCount(RoleFaction.NeutralKilling))
            + Math.Min(FactionCap(RoleFaction.Neutral),         PoolFactionCount(RoleFaction.Neutral));
        private double EffectiveSpread(int totalPlayers)
        {
            if (totalPlayers <= 0) return 0.0;
            double density = (double)InitialFloorTotal() / totalPlayers;
            double adaptive = Math.Max(0.0, Math.Min(0.5, 1.5 * (density - 0.6)));
            return _cfg.FloorSpreadBias * adaptive;
        }
        private double PositionTilt(int slotIndex, int totalSlots)
        {
            if (_cfg.PositionEdge <= 0 || totalSlots <= 1) return 1.0;
            // pos: 0.0 at first slot -> 1.0 at last slot. center at 0.5.
            double pos = (double)slotIndex / (totalSlots - 1);
            double tilt = 1.0 + _cfg.PositionEdge * (0.5 - pos) * 2.0; // +PositionEdge at slot0, -PositionEdge at last
            return Math.Max(0.35, Math.Min(1.65, tilt));
        }

        public List<RoleFaction?> AssignFloorLocks(int playersRemaining, int activeCount, int totalPlayers)
        {
            var locks = new List<RoleFaction?>();
            for (int i = 0; i < activeCount; i++) locks.Add(null);

            int dImp = FloorDeficit(RoleFaction.Impostor);
            int dNk  = FloorDeficit(RoleFaction.NeutralKilling);
            int dNeu = FloorDeficit(RoleFaction.Neutral);
            int rem  = playersRemaining;
            double spread = EffectiveSpread(totalPlayers);

            for (int i = 0; i < activeCount; i++)
            {
                int total = dImp + dNk + dNeu;
                bool doLock = total > 0 && rem > 0
                    && (total >= rem || _rng.NextDouble() < spread * (double)total / rem);
                if (doLock)
                {
                    if (dImp >= dNk && dImp >= dNeu && dImp > 0) { locks[i] = RoleFaction.Impostor;       dImp--; }
                    else if (dNk >= dNeu && dNk > 0)             { locks[i] = RoleFaction.NeutralKilling;  dNk--;  }
                    else if (dNeu > 0)                           { locks[i] = RoleFaction.Neutral;         dNeu--; }
                }
                rem--;
            }
            return locks;
        }

        public bool IsRoleAvailable(ushort id)
        {
            if (GetDraftedCount(id) >= GetMaxCount(id)) return false;
            var f = GetFaction(id);
            if (f != RoleFaction.Crewmate && FactionRemaining(f) <= 0) return false;
            return true;
        }

        public List<ushort> GetAvailableIds(ISet<ushort> exclude = null)
        {
            var result = new List<ushort>();
            foreach (var id in _pool.RoleIds)
            {
                if (exclude != null && exclude.Contains(id)) continue;
                if (GetDraftedCount(id) >= GetMaxCount(id)) continue;
                var f = GetFaction(id);
                if (f != RoleFaction.Crewmate && FactionRemaining(f) <= 0) continue;
                result.Add(id);
            }
            return result;
        }

        public List<ushort> GetAvailableForFaction(RoleFaction faction) =>
            GetAvailableIds().Where(id => GetFaction(id) == faction).ToList();

        public FactionBudget CreateRoundBudget() => new FactionBudget(
            FactionRemaining(RoleFaction.Impostor),
            FactionRemaining(RoleFaction.NeutralKilling),
            FactionRemaining(RoleFaction.Neutral));

        public ushort PickWeighted(IList<ushort> candidates)
        {
            if (candidates == null || candidates.Count == 0) return _cfg.CrewmateRoleId;
            if (!_cfg.UseRoleChances) return candidates[_rng.NextInt(candidates.Count)];

            int total = 0;
            for (int i = 0; i < candidates.Count; i++) total += GetWeight(candidates[i]);
            if (total <= 0) return candidates[_rng.NextInt(candidates.Count)];

            int roll = _rng.NextInt(1, total + 1);
            int acc = 0;
            foreach (var id in candidates)
            {
                acc += GetWeight(id);
                if (roll <= acc) return id;
            }
            return candidates[_rng.NextInt(candidates.Count)];
        }

        public List<ushort> PickWeightedUnique(IList<ushort> candidates, int count)
        {
            var results = new List<ushort>();
            var temp = new List<ushort>(candidates);
            while (results.Count < count && temp.Count > 0)
            {
                var pick = PickWeighted(temp);
                results.Add(pick);
                temp.Remove(pick);
            }
            return results;
        }
        private ushort PickDiverse(List<ushort> candidates, HashSet<string> usedAlignments)
        {
            List<ushort> poolToUse = candidates;
            if (_cfg.OfferDiversity)
            {
                var preferred = candidates.Where(id =>
                {
                    var a = GetAlignment(id);
                    return string.IsNullOrEmpty(a) || !usedAlignments.Contains(a);
                }).ToList();
                if (preferred.Count > 0) poolToUse = preferred;
            }

            var pick = PickWeighted(poolToUse);
            var align = GetAlignment(pick);
            if (!string.IsNullOrEmpty(align)) usedAlignments.Add(align);
            return pick;
        }
        public List<ushort> BuildOffer(PlayerDraftState state, int slotIndex, int totalSlots,
                                       ISet<ushort> reserveInto, ISet<ushort> extraExclude, FactionBudget budget,
                                       RoleFaction? lockFaction = null, int lockShare = int.MaxValue)
        {
            int target = _cfg.OfferedRolesCount;
            budget ??= new FactionBudget(int.MaxValue, int.MaxValue, int.MaxValue);

            var exclude = new HashSet<ushort>();
            if (reserveInto != null) foreach (var r in reserveInto) exclude.Add(r);
            if (extraExclude != null) foreach (var r in extraExclude) exclude.Add(r);

            var available = GetAvailableIds(exclude);
            var offered = new List<ushort>();
            var usedAlignments = new HashSet<string>();

            if (lockFaction.HasValue)
            {
                _tempLockPool.Clear();
                foreach (var id in available)
                    if (GetFaction(id) == lockFaction.Value) _tempLockPool.Add(id);
                var lockPool = _tempLockPool;
                if (lockPool.Count > 0)
                {
                    int take = Math.Min(target, Math.Max(1, lockShare));
                    for (int i = 0; i < take && lockPool.Count > 0; i++)
                    {
                        var pick = PickDiverse(lockPool, usedAlignments);
                        offered.Add(pick);
                        lockPool.Remove(pick);
                    }
                    Shuffle(offered);
                    if (reserveInto != null)
                        foreach (var id in offered)
                            if (IsUniqueRole(id)) reserveInto.Add(id);
                    return offered;
                }
            }

            List<ushort> EvilCandidatesOfFaction(RoleFaction faction)
            {
                _tempEvilCandidates.Clear();
                foreach (var id in available)
                    if (GetFaction(id) == faction && !offered.Contains(id) && budget.Remaining(faction) > 0)
                        _tempEvilCandidates.Add(id);
                return _tempEvilCandidates;
            }

            List<ushort> EvilCandidates()
            {
                _tempEvilCandidates.Clear();
                foreach (var id in available)
                {
                    var f = GetFaction(id);
                    if (f != RoleFaction.Crewmate && f != RoleFaction.Impostor
                        && !offered.Contains(id) && budget.Remaining(f) > 0)
                        _tempEvilCandidates.Add(id);
                }
                return _tempEvilCandidates;
            }

            bool AddEvilOfFaction(RoleFaction faction)
            {
                var pool = EvilCandidatesOfFaction(faction);
                if (pool.Count == 0) return false;
                var pick = PickDiverse(pool, usedAlignments);
                offered.Add(pick);
                budget.Consume(faction);
                return true;
            }

            if (available.Count > 0)
            {
                _tempNonCrew.Clear();
                foreach (var id in available)
                    if (GetFaction(id) != RoleFaction.Crewmate) _tempNonCrew.Add(id);
                var nonCrew = _tempNonCrew;
                double posTilt = PositionTilt(slotIndex, totalSlots);
                double evilChance = Math.Min(0.99, _cfg.EvilOfferChance * posTilt);
                int maxEvil = Math.Min(nonCrew.Count, target >= 4 ? 4 : target);
                int minEvil = _cfg.PositionEdge > 0 ? 0 : (nonCrew.Count > 0 ? 1 : 0);
                int evilCount = minEvil;
                int extraMax = Math.Max(0, maxEvil - minEvil);
                for (int i = 0; i < extraMax; i++)
                    if (_rng.NextDouble() < evilChance) evilCount++;
                evilCount = Math.Min(evilCount, maxEvil);

                if (_cfg.SoftImpostorNudge
                    && ImpostorsDrafted < _cfg.MaxImpostors
                    && budget.Remaining(RoleFaction.Impostor) > 0)
                {
                    int impDeficit   = _cfg.MaxImpostors - ImpostorsDrafted;
                    int playersLeft  = Math.Max(1, totalSlots - slotIndex);
                    double boost = 1.0;
                    if (_cfg.ImpostorSpreadPower > 0 && totalSlots > 0)
                    {
                        double density = (double)_cfg.MaxImpostors / totalSlots;
                        boost = Math.Pow(0.20 / Math.Max(0.04, density), _cfg.ImpostorSpreadPower);
                        boost = Math.Min(4.0, Math.Max(0.4, boost));
                    }
                    double nudgeProb = Math.Min(1.0, boost * impDeficit / playersLeft);
                    if (_cfg.PositionEdge > 0 && nudgeProb < 1.0)
                        nudgeProb = Math.Min(0.99, nudgeProb * PositionTilt(slotIndex, totalSlots));
                    if (_rng.NextDouble() < nudgeProb && AddEvilOfFaction(RoleFaction.Impostor))
                        evilCount = Math.Max(0, evilCount - 1);
                }

                if (state != null && state.GuaranteedFaction.HasValue
                    && budget.Remaining(state.GuaranteedFaction.Value) > 0)
                {
                    if (AddEvilOfFaction(state.GuaranteedFaction.Value))
                        evilCount = Math.Max(0, evilCount - 1);
                }
                for (int i = 0; i < evilCount; i++)
                {
                    var pool = EvilCandidates();
                    if (pool.Count == 0) break;
                    var pick = PickDiverse(pool, usedAlignments);
                    offered.Add(pick);
                    budget.Consume(GetFaction(pick));
                }

                int remaining = target - offered.Count;
                if (remaining > 0)
                {
                    var crewPool = available.Where(id => GetFaction(id) == RoleFaction.Crewmate && !offered.Contains(id)).ToList();
                    for (int i = 0; i < remaining && crewPool.Count > 0; i++)
                    {
                        var pick = PickDiverse(crewPool, usedAlignments);
                        offered.Add(pick);
                        crewPool.Remove(pick);
                    }
                }

                while (offered.Count < target)
                {
                    var topUp = available.Where(id => !offered.Contains(id)).ToList();
                    if (topUp.Count == 0) break;
                    var pick = PickDiverse(topUp, usedAlignments);
                    offered.Add(pick);
                }
            }

            while (offered.Count < target) offered.Add(_cfg.CrewmateRoleId);

            Shuffle(offered);

            if (reserveInto != null)
                foreach (var id in offered)
                    if (IsUniqueRole(id)) reserveInto.Add(id);

            return offered;
        }

        private void Shuffle(List<ushort> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.NextInt(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public ushort PickFullRandom(ISet<ushort> exclude = null)
        {
            var available = GetAvailableIds(exclude);
            if (available.Count == 0) return _cfg.CrewmateRoleId;
            return _cfg.UseRoleChances ? PickWeighted(available) : available[_rng.NextInt(available.Count)];
        }

        public ushort PickFullRandomForState(PlayerDraftState state, ISet<ushort> roundChosen, ISet<ushort> roundReserved)
        {
            if (state != null && state.GuaranteedFaction.HasValue)
            {
                var f = state.GuaranteedFaction.Value;
                if (f != RoleFaction.Crewmate && FactionRemaining(f) > 0)
                {
                    var facPool = GetAvailableForFaction(f)
                        .Where(id => (roundChosen == null || !roundChosen.Contains(id))
                                  && (roundReserved == null || !roundReserved.Contains(id)
                                      || (state != null && state.OfferedRoleIds.Contains(id))))
                        .ToList();
                    if (facPool.Count > 0)
                        return _cfg.UseRoleChances ? PickWeighted(facPool) : facPool[_rng.NextInt(facPool.Count)];
                }
            }

            var exclude = new HashSet<ushort>();
            if (roundChosen != null) foreach (var id in roundChosen) exclude.Add(id);
            if (roundReserved != null)
                foreach (var id in roundReserved)
                    if (state == null || !state.OfferedRoleIds.Contains(id)) exclude.Add(id);

            var pick = PickFullRandom(exclude);
            if (IsUniqueRole(pick) && exclude.Contains(pick))
                pick = PickFullRandom(roundChosen);
            return pick;
        }

        public RoleFaction Commit(ushort roleId)
        {
            _drafted[roleId] = GetDraftedCount(roleId) + 1;
            var f = GetFaction(roleId);
            if (f == RoleFaction.Impostor) ImpostorsDrafted++;
            else if (f == RoleFaction.NeutralKilling) NeutralKillingsDrafted++;
            else if (f == RoleFaction.Neutral) NeutralPassivesDrafted++;
            return f;
        }
    }
}
