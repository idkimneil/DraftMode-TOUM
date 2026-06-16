using System;
using System.Collections.Generic;
using System.Linq;
using DraftModeTOUM.Managers;

namespace DraftModeTOUM.Tests
{
    // Deep per-slot offer simulation across lobby sizes 2..25.
    // Reports, for each pick position, the average composition of the cards SHOWN
    // in that player's draft (Imp / NeutralKilling / Neutral / Crew) plus the
    // probability at least one card of each evil faction appears, averaged over
    // many randomized full drafts that mirror DraftManager's round loop.
    public static class LobbySim
    {
        const int Trials = 20000;

        public static void Run()
        {
            var pool = TestPool.MakeCategorized();
            // Real game DEFAULTS (DraftManager static defaults).
            int offered = 8, maxImp = 2, maxNk = 1, maxNp = 0, concurrent = 1;
            double evilProb = 0.6; // players favour evil when shown it
            double afkProb = 0.0;

            RunInner(pool, offered, maxImp, maxNk, maxNp, concurrent, evilProb, afkProb);
        }

        // Fairness lens: realistic 2/2/3 pool, 3 cards. Sweeps PositionEdge so you can see the
        // early-slot gradient. Only reports a few representative lobby sizes to stay readable.
        public static void Fairness()
        {
            var pool = TestPool.MakeCategorized();
            int offered = 3, maxImp = 2, maxNk = 2, maxNp = 3, concurrent = 1;
            double evilProb = 0.6, afkProb = 0.0;
            double[] edges = { 0.0, 0.25, 0.5 };
            int[] sizes = { 8, 12, 16, 25 };

            foreach (var edge in edges)
            {
            Console.WriteLine($"\n############ PositionEdge = {edge} ############");
            foreach (var n in sizes)
            {
                var cfg = TestPool.Config(offered, maxImp, maxNk, maxNp, edge);
                double[] seenImp = new double[n], seenNk = new double[n], seenNeu = new double[n];
                double[] pickImp = new double[n], pickNk = new double[n], pickNeu = new double[n];
                double[] distinctFac = new double[n], distinctAlign = new double[n];
                double dupAlignOffers = 0, totalOffers = 0;

                for (int t = 0; t < Trials; t++)
                {
                    var rng = new DeterministicRng((uint)(n * 1_000_003u + t * 2_654_435_761u + 7));
                    var (offers, picked) = RunCapturing(pool, cfg, n, concurrent, evilProb, afkProb, rng);
                    for (int s = 0; s < n; s++)
                    {
                        var facs = new HashSet<RoleFaction>();
                        var aligns = new HashSet<string>();
                        bool dup = false; var seenA = new HashSet<string>();
                        bool si = false, snk = false, sne = false;
                        foreach (var id in offers[s])
                        {
                            var f = pool.Factions[id]; facs.Add(f);
                            var a = pool.Alignments[id]; aligns.Add(a);
                            if (!seenA.Add(a)) dup = true;
                            if (f == RoleFaction.Impostor) si = true;
                            else if (f == RoleFaction.NeutralKilling) snk = true;
                            else if (f == RoleFaction.Neutral) sne = true;
                        }
                        if (si) seenImp[s]++; if (snk) seenNk[s]++; if (sne) seenNeu[s]++;
                        distinctFac[s] += facs.Count; distinctAlign[s] += aligns.Count;
                        if (dup) dupAlignOffers++; totalOffers++;
                        switch (picked[s])
                        {
                            case RoleFaction.Impostor: pickImp[s]++; break;
                            case RoleFaction.NeutralKilling: pickNk[s]++; break;
                            case RoleFaction.Neutral: pickNeu[s]++; break;
                        }
                    }
                }

                Console.Write($"  {n}p anyEvil-PICKED by slot: ");
                var parts = new List<string>();
                for (int s = 0; s < n; s++)
                {
                    double anyEvil = (pickImp[s] + pickNk[s] + pickNeu[s]) / Trials;
                    parts.Add($"{anyEvil,4:0%}");
                }
                Console.WriteLine(string.Join(" ", parts) + $"   [dup {dupAlignOffers / totalOffers:0%}]");
            }
            }
        }

        static void RunInner(DraftRolePool pool, int offered, int maxImp, int maxNk, int maxNp,
                             int concurrent, double evilProb, double afkProb)
        {
            for (int n = 2; n <= 25; n++)
            {
                // caps are min(cap, players, poolFaction) just like AssignFactionBuckets/engine
                var cfg = TestPool.Config(offered, maxImp, maxNk, maxNp);
                // engine already clamps faction remaining to pool; players clamp happens via floor deficit.

                // accumulators: [slot][faction]
                double[,] sumCards = new double[n, 4]; // 0 imp 1 nk 2 neu 3 crew
                double[] anyImp = new double[n], anyNk = new double[n], anyNeu = new double[n];
                // final picked faction tally per slot
                double[,] picked = new double[n, 4];

                for (int t = 0; t < Trials; t++)
                {
                    var rng = new DeterministicRng((uint)(n * 1_000_003u + t * 2_654_435_761u + 1));
                    var (offers, factionsPicked) = RunCapturing(pool, cfg, n, concurrent, evilProb, afkProb, rng);

                    for (int s = 0; s < n; s++)
                    {
                        int ci = 0, cnk = 0, cneu = 0, ccrew = 0;
                        foreach (var id in offers[s])
                        {
                            switch (pool.Factions[id])
                            {
                                case RoleFaction.Impostor: ci++; break;
                                case RoleFaction.NeutralKilling: cnk++; break;
                                case RoleFaction.Neutral: cneu++; break;
                                default: ccrew++; break;
                            }
                        }
                        sumCards[s, 0] += ci; sumCards[s, 1] += cnk; sumCards[s, 2] += cneu; sumCards[s, 3] += ccrew;
                        if (ci > 0) anyImp[s]++;
                        if (cnk > 0) anyNk[s]++;
                        if (cneu > 0) anyNeu[s]++;

                        switch (factionsPicked[s])
                        {
                            case RoleFaction.Impostor: picked[s, 0]++; break;
                            case RoleFaction.NeutralKilling: picked[s, 1]++; break;
                            case RoleFaction.Neutral: picked[s, 2]++; break;
                            default: picked[s, 3]++; break;
                        }
                    }
                }

                PrintLobby(n, offered, cfg, sumCards, anyImp, anyNk, anyNeu, picked, Trials);
            }
        }

        // Returns per-slot the OFFER (list of role ids shown) and the faction the slot ended up picking.
        static (List<ushort>[] offers, RoleFaction[] picked) RunCapturing(
            DraftRolePool pool, DraftConfig cfg, int n, int concurrent,
            double evilProb, double afkProb, IRng rng)
        {
            var engine = new DraftDistribution(pool, cfg, rng);

            // bucket assignment (same shape as DraftSim)
            var buckets = new RoleFaction?[n];
            var want = new List<RoleFaction>();
            int impB = Math.Min(cfg.MaxImpostors, n);
            int nkB = Math.Min(cfg.MaxNeutralKillings, n);
            int npB = Math.Min(cfg.MaxNeutralPassives, n);
            int nonCrew = impB + nkB + npB;
            if (nonCrew > n)
            {
                double scale = (double)n / nonCrew;
                impB = (int)Math.Floor(impB * scale);
                nkB = (int)Math.Floor(nkB * scale);
                npB = (int)Math.Floor(npB * scale);
            }
            for (int i = 0; i < impB; i++) want.Add(RoleFaction.Impostor);
            for (int i = 0; i < nkB; i++) want.Add(RoleFaction.NeutralKilling);
            for (int i = 0; i < npB; i++) want.Add(RoleFaction.Neutral);
            var positions = Enumerable.Range(0, n).OrderBy(_ => rng.NextInt(1_000_000)).ToList();
            for (int i = 0; i < want.Count && i < n; i++) buckets[positions[i]] = want[i];

            var states = new List<PlayerDraftState>();
            for (int i = 0; i < n; i++)
                states.Add(new PlayerDraftState { PlayerId = (byte)i, SlotNumber = i + 1, GuaranteedFaction = buckets[i] });

            var offers = new List<ushort>[n];
            var pickedFaction = new RoleFaction[n];
            int turnIndex = 0;

            while (turnIndex < n)
            {
                var active = new List<PlayerDraftState>();
                for (int k = 0; k < concurrent && turnIndex + k < n; k++) active.Add(states[turnIndex + k]);

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
                        lockShare[ai] = Math.Max(1, (avail + peers - 1) / peers);
                    }
                }

                for (int ai = 0; ai < active.Count; ai++)
                {
                    var s = active[ai];
                    if (locks[ai].HasValue) s.GuaranteedFaction = locks[ai];
                    s.OfferedRoleIds = engine.BuildOffer(s, s.SlotNumber - 1, n, reserved, null, budget, locks[ai], lockShare[ai]);
                    offers[s.SlotNumber - 1] = new List<ushort>(s.OfferedRoleIds);
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
                        var evilIdx = new List<int>(); var crewIdx = new List<int>();
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

                    if (engine.IsUniqueRole(chosen) && chosenThisRound.Contains(chosen))
                        chosen = engine.PickFullRandom(chosenThisRound);
                    if (!engine.IsRoleAvailable(chosen))
                        chosen = engine.PickFullRandom(chosenThisRound);
                    if (engine.IsUniqueRole(chosen)) chosenThisRound.Add(chosen);

                    pickedFaction[s.SlotNumber - 1] = engine.Commit(chosen);
                }

                turnIndex += active.Count;
            }

            return (offers, pickedFaction);
        }

        static void PrintLobby(int n, int offered, DraftConfig cfg,
            double[,] sumCards, double[] anyImp, double[] anyNk, double[] anyNeu,
            double[,] picked, int trials)
        {
            int effImp = Math.Min(cfg.MaxImpostors, n);
            int effNk = Math.Min(cfg.MaxNeutralKillings, n);
            int effNp = Math.Min(cfg.MaxNeutralPassives, n);
            int nonCrew = effImp + effNk + effNp;
            if (nonCrew > n)
            {
                double scale = (double)n / nonCrew;
                effImp = (int)Math.Floor(effImp * scale);
                effNk = (int)Math.Floor(effNk * scale);
                effNp = (int)Math.Floor(effNp * scale);
            }

            Console.WriteLine($"================ LOBBY {n} PLAYERS ================");
            Console.WriteLine($"  Cards per draft: {offered}   Evil floor this size -> Imp {effImp}, NK {effNk}, Neu {effNp}  (crew slots {n - effImp - effNk - effNp})");
            Console.WriteLine($"  Per-pick AVERAGE cards shown  |  chance an evil card of each type appears  |  what they ended up picking");
            Console.WriteLine($"  Pick | Imp  NK   Neu  Crew |  P(imp) P(nk) P(neu) |  picked%: Imp/NK/Neu/Crew");
            for (int s = 0; s < n; s++)
            {
                double im = sumCards[s, 0] / trials, nk = sumCards[s, 1] / trials, ne = sumCards[s, 2] / trials, cr = sumCards[s, 3] / trials;
                double pi = anyImp[s] / trials, pn = anyNk[s] / trials, pne = anyNeu[s] / trials;
                double ki = picked[s, 0] / trials, knk = picked[s, 1] / trials, kne = picked[s, 2] / trials, kcr = picked[s, 3] / trials;
                Console.WriteLine($"  {s + 1,4} | {im,4:0.00} {nk,4:0.00} {ne,4:0.00} {cr,4:0.00} |  {pi,5:0.0%} {pn,5:0.0%} {pne,5:0.0%} |  {ki,4:0.0%}/{knk,4:0.0%}/{kne,4:0.0%}/{kcr,4:0.0%}");
            }
            Console.WriteLine();
        }
    }
}
