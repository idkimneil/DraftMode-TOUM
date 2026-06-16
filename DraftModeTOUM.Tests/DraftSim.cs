using System.Collections.Generic;
using System.Linq;
using DraftModeTOUM.Managers;

namespace DraftModeTOUM.Tests
{
    // Full-draft runner that drives the SAME DraftDistribution the game uses,
    // mirroring DraftManager's round loop and FinalisePickForState re-checks.
    public static class DraftSim
    {
        public sealed class Result
        {
            public int Impostors;
            public int NeutralKillings;
            public int Neutrals;
            public int Crew;
            public int Evil => Impostors + NeutralKillings + Neutrals;
            public readonly List<ushort> ChosenRoles = new();
            public RoleFaction[] SlotFaction;
            public int OffersBuilt;
            public int OffersWithDupAlignment;
        }

        public static bool HasDupAlignment(List<ushort> offer, System.Func<ushort, string> alignOf)
        {
            var seen = new HashSet<string>();
            foreach (var id in offer)
            {
                var a = alignOf(id);
                if (string.IsNullOrEmpty(a)) continue;
                if (!seen.Add(a)) return true;
            }
            return false;
        }

        private static RoleFaction?[] AssignBuckets(int n, DraftConfig cfg, IRng rng)
        {
            var buckets = new RoleFaction?[n];
            var want = new List<RoleFaction>();
            for (int i = 0; i < cfg.MaxImpostors; i++) want.Add(RoleFaction.Impostor);
            for (int i = 0; i < cfg.MaxNeutralKillings; i++) want.Add(RoleFaction.NeutralKilling);
            for (int i = 0; i < cfg.MaxNeutralPassives; i++) want.Add(RoleFaction.Neutral);

            var positions = Enumerable.Range(0, n).OrderBy(_ => rng.NextInt(100000)).ToList();
            for (int i = 0; i < want.Count && i < n; i++)
                buckets[positions[i]] = want[i];
            return buckets;
        }

        public static Result Run(DraftRolePool pool, DraftConfig cfg, int n, int concurrent,
                                 double evilProb, double afkProb, IRng rng)
        {
            var engine = new DraftDistribution(pool, cfg, rng);
            var buckets = AssignBuckets(n, cfg, rng);

            var states = new List<PlayerDraftState>();
            for (int i = 0; i < n; i++)
                states.Add(new PlayerDraftState
                {
                    PlayerId = (byte)i,
                    SlotNumber = i + 1,
                    GuaranteedFaction = buckets[i]
                });

            var result = new Result { SlotFaction = new RoleFaction[n] };
            int turnIndex = 0;

            while (turnIndex < n)
            {
                var active = new List<PlayerDraftState>();
                for (int k = 0; k < concurrent && turnIndex + k < n; k++)
                    active.Add(states[turnIndex + k]);

                var reserved = new HashSet<ushort>();
                var budget = engine.CreateRoundBudget();

                int playersRemaining = n - turnIndex;
                var locks = engine.AssignFloorLocks(playersRemaining, active.Count, n);

                var lockShare = new int[active.Count];
                for (int ai = 0; ai < active.Count; ai++)
                {
                    lockShare[ai] = int.MaxValue;
                    if (!locks[ai].HasValue) continue;
                    int peers = 0;
                    for (int k = 0; k < active.Count; k++) if (locks[k] == locks[ai]) peers++;
                    if (peers > 1)
                    {
                        int avail = engine.GetAvailableForFaction(locks[ai].Value).Count(id => !reserved.Contains(id));
                        lockShare[ai] = System.Math.Max(1, (avail + peers - 1) / peers);
                    }
                }

                for (int ai = 0; ai < active.Count; ai++)
                {
                    var s = active[ai];
                    if (locks[ai].HasValue) s.GuaranteedFaction = locks[ai];
                    s.OfferedRoleIds = engine.BuildOffer(s, s.SlotNumber - 1, n, reserved, null, budget, locks[ai], lockShare[ai]);
                    result.OffersBuilt++;
                    if (HasDupAlignment(s.OfferedRoleIds, engine.GetAlignment)) result.OffersWithDupAlignment++;
                }

                var chosenThisRound = new HashSet<ushort>();
                foreach (var s in active)
                {
                    ushort chosen;
                    bool afk = rng.NextDouble() < afkProb;

                    if (afk)
                    {
                        chosen = engine.PickFullRandomForState(s, chosenThisRound, reserved);
                    }
                    else
                    {
                        var offer = s.OfferedRoleIds;
                        var evilIdx = new List<int>();
                        var crewIdx = new List<int>();
                        for (int i = 0; i < offer.Count; i++)
                        {
                            if (engine.GetFaction(offer[i]) == RoleFaction.Crewmate) crewIdx.Add(i);
                            else evilIdx.Add(i);
                        }

                        if (evilIdx.Count > 0 && rng.NextDouble() < evilProb)
                            chosen = offer[evilIdx[rng.NextInt(evilIdx.Count)]];
                        else if (crewIdx.Count > 0)
                            chosen = offer[crewIdx[rng.NextInt(crewIdx.Count)]];
                        else
                            chosen = offer[rng.NextInt(offer.Count)];
                    }

                    // Mirror DraftManager.FinalisePickForState re-checks.
                    if (engine.IsUniqueRole(chosen) && chosenThisRound.Contains(chosen))
                        chosen = engine.PickFullRandom(chosenThisRound);
                    if (!engine.IsRoleAvailable(chosen))
                        chosen = engine.PickFullRandom(chosenThisRound);
                    if (engine.IsUniqueRole(chosen))
                        chosenThisRound.Add(chosen);

                    var faction = engine.Commit(chosen);
                    result.ChosenRoles.Add(chosen);
                    result.SlotFaction[s.SlotNumber - 1] = faction;
                    if (faction == RoleFaction.Impostor) result.Impostors++;
                    else if (faction == RoleFaction.NeutralKilling) result.NeutralKillings++;
                    else if (faction == RoleFaction.Neutral) result.Neutrals++;
                    else result.Crew++;
                }

                turnIndex += active.Count;
            }

            return result;
        }
    }
}
