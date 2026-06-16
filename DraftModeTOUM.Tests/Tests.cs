using System.Collections.Generic;
using System.Linq;
using DraftModeTOUM.Managers;

namespace DraftModeTOUM.Tests
{
    public static class Tests
    {
        public static void OfferAlwaysHasTargetSize()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config(offered: 3);
            var eng = new DraftDistribution(pool, cfg, new DeterministicRng(123));
            for (int t = 0; t < 300; t++)
            {
                var st = new PlayerDraftState { PlayerId = 1, SlotNumber = 1 };
                var offer = eng.BuildOffer(st, 0, 10, new HashSet<ushort>(), null, eng.CreateRoundBudget());
                Check.Equal(3, offer.Count, "offer size equals OfferedRolesCount");
            }
        }

        // Fix #1: concurrent offers must never deal more evil cards of a faction than its remaining headroom.
        public static void ConcurrentOffersRespectFactionBudget()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config(offered: 3, maxImp: 1, maxNk: 1, maxNeu: 1);
            for (int seed = 1; seed <= 400; seed++)
            {
                var eng = new DraftDistribution(pool, cfg, new DeterministicRng((uint)seed));
                var reserved = new HashSet<ushort>();
                var budget = eng.CreateRoundBudget();

                var o1 = eng.BuildOffer(new PlayerDraftState { PlayerId = 0, SlotNumber = 1 }, 0, 2, reserved, null, budget);
                var o2 = eng.BuildOffer(new PlayerDraftState { PlayerId = 1, SlotNumber = 2 }, 1, 2, reserved, null, budget);

                int imp = o1.Concat(o2).Count(id => eng.GetFaction(id) == RoleFaction.Impostor);
                int nk = o1.Concat(o2).Count(id => eng.GetFaction(id) == RoleFaction.NeutralKilling);
                int neu = o1.Concat(o2).Count(id => eng.GetFaction(id) == RoleFaction.Neutral);
                Check.True(imp <= 1 && nk <= 1 && neu <= 1,
                    $"seed {seed}: evil cards across concurrent offers within headroom (imp={imp}, nk={nk}, neu={neu})");
            }
        }

        // Reservation: a unique role must never be offered to two concurrent pickers in the same round.
        public static void NoDuplicateUniqueAcrossConcurrentOffers()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config(offered: 3);
            for (int seed = 1; seed <= 400; seed++)
            {
                var eng = new DraftDistribution(pool, cfg, new DeterministicRng((uint)seed));
                var reserved = new HashSet<ushort>();
                var budget = eng.CreateRoundBudget();

                var offers = new List<List<ushort>>();
                for (int s = 0; s < 2; s++)
                    offers.Add(eng.BuildOffer(new PlayerDraftState { PlayerId = (byte)s, SlotNumber = s + 1 }, s, 2, reserved, null, budget));

                var uniques = offers.SelectMany(o => o).Where(eng.IsUniqueRole).ToList();
                Check.Equal(uniques.Count, uniques.Distinct().Count(),
                    $"seed {seed}: no duplicate unique role across concurrent offers");
            }
        }

        // Soft impostor nudge: at the last-chance positions (players left <= impostor deficit) the nudge
        // probability saturates to 1, so every such offer still contains an impostor card.
        public static void SoftImpostorNudgeOffersImpostor()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config(offered: 3);
            for (int seed = 1; seed <= 400; seed++)
            {
                var eng = new DraftDistribution(pool, cfg, new DeterministicRng((uint)seed));
                // slotIndex 9 of 10 => 1 player left, deficit 2 => nudge prob clamps to 1 (guaranteed).
                var offer = eng.BuildOffer(new PlayerDraftState { PlayerId = 0, SlotNumber = 10 }, 9, 10, new HashSet<ushort>(), null, eng.CreateRoundBudget());
                bool hasImp = offer.Any(id => eng.GetFaction(id) == RoleFaction.Impostor);
                Check.True(hasImp, $"seed {seed}: nudge guarantees an impostor card at the last-chance position");
            }
        }

        // Caps are hard: no completed draft ever exceeds the configured maximums.
        public static void CapsNeverExceeded()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config(maxImp: 2, maxNk: 2, maxNeu: 3);
            foreach (int c in new[] { 1, 2, 3 })
            {
                for (int seed = 1; seed <= 600; seed++)
                {
                    var r = DraftSim.Run(pool, cfg, 10, c, 1.0, 0.0, new DeterministicRng((uint)(seed * 13 + c)));
                    Check.True(r.Impostors <= 2 && r.NeutralKillings <= 2 && r.Neutrals <= 3,
                        $"C={c} seed {seed}: caps respected (imp={r.Impostors}, nk={r.NeutralKillings}, neu={r.Neutrals})");
                }
            }
        }

        // Same seed must reproduce the exact same offer (enables reproducible drafts + stable tests).
        public static void DeterministicOffers()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config();
            var a = new DraftDistribution(pool, cfg, new DeterministicRng(42));
            var b = new DraftDistribution(pool, cfg, new DeterministicRng(42));
            var oa = a.BuildOffer(new PlayerDraftState { SlotNumber = 1 }, 0, 10, new HashSet<ushort>(), null, a.CreateRoundBudget());
            var ob = b.BuildOffer(new PlayerDraftState { SlotNumber = 1 }, 0, 10, new HashSet<ushort>(), null, b.CreateRoundBudget());
            Check.True(oa.SequenceEqual(ob), "same seed yields identical offer");
        }

        // Fix #2: a bucket-impostor that goes Random/AFK gets an impostor while capacity remains.
        public static void BucketHonoredOnAfk()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config();
            int trials = 400, got = 0;
            for (int seed = 1; seed <= trials; seed++)
            {
                var eng = new DraftDistribution(pool, cfg, new DeterministicRng((uint)seed));
                var st = new PlayerDraftState { PlayerId = 0, SlotNumber = 1, GuaranteedFaction = RoleFaction.Impostor };
                var pick = eng.PickFullRandomForState(st, new HashSet<ushort>(), new HashSet<ushort>());
                if (eng.GetFaction(pick) == RoleFaction.Impostor) got++;
            }
            Check.Equal(trials, got, "bucket-impostor on Random/AFK always resolves to an impostor when capacity remains");
        }

        // Regression (review finding): a bucket auto-pick must NOT steal a unique role reserved for another
        // concurrent picker (i.e. reserved and not part of this player's own offer).
        public static void BucketAutoPickRespectsReservation()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config();
            for (int seed = 1; seed <= 400; seed++)
            {
                var eng = new DraftDistribution(pool, cfg, new DeterministicRng((uint)seed));
                var reserved = new HashSet<ushort>(pool.RoleIds.Where(id => eng.GetFaction(id) == RoleFaction.Impostor));
                var st = new PlayerDraftState
                {
                    PlayerId = 0,
                    SlotNumber = 1,
                    GuaranteedFaction = RoleFaction.Impostor,
                    OfferedRoleIds = new List<ushort>()
                };
                var pick = eng.PickFullRandomForState(st, new HashSet<ushort>(), reserved);
                Check.True(!reserved.Contains(pick), $"seed {seed}: bucket auto-pick must not steal a reserved role (picked {pick})");
            }
        }

        // Fix #3: draft slot no longer changes the evil-offer rate (flattened bias).
        public static void PositionDoesNotChangeEvilRate()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config();
            double EvilRateAtSlot(int slotIndex)
            {
                long evil = 0; int trials = 4000;
                for (int seed = 1; seed <= trials; seed++)
                {
                    var eng = new DraftDistribution(pool, cfg, new DeterministicRng((uint)seed));
                    var offer = eng.BuildOffer(new PlayerDraftState { SlotNumber = slotIndex + 1 }, slotIndex, 10, new HashSet<ushort>(), null, eng.CreateRoundBudget());
                    evil += offer.Count(id => eng.GetFaction(id) != RoleFaction.Crewmate);
                }
                return (double)evil / trials;
            }
            double first = EvilRateAtSlot(0);
            double last = EvilRateAtSlot(9);
            Check.True(System.Math.Abs(first - last) < 0.05,
                $"evil-offer rate is flat across draft position (first={first:F3}, last={last:F3})");
        }

        private static double ZeroImpostorPct(DraftRolePool pool, DraftConfig cfg, int concurrent, int trials = 5000)
        {
            int zero = 0;
            for (int seed = 1; seed <= trials; seed++)
            {
                var r = DraftSim.Run(pool, cfg, 10, concurrent, 0.5, 0.1, new DeterministicRng((uint)(seed * 7 + concurrent)));
                if (r.Impostors == 0) zero++;
            }
            return 100.0 * zero / trials;
        }

        // Headline guarantee for the SUPPORTED range (Concurrent Picks max = 2):
        // turning concurrency on no longer starves impostors.
        public static void ConcurrencyDoesNotStarveImpostorsInSupportedRange()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config();
            foreach (int c in new[] { 1, 2 })
            {
                double p0 = ZeroImpostorPct(pool, cfg, c);
                Check.True(p0 < 2.0, $"C={c}: P(0 impostors) = {p0:F2}% (must stay <2% for typical play)");
            }
        }

        // Beyond the supported range, soft-floor + high concurrency inherently weakens the impostor
        // guarantee (too few rounds to re-offer, and we never override a human crew pick). It must still
        // be dramatically better than the ORIGINAL "one-evil-per-round" throttle. Original baselines
        // (measured): C=3 -> 22.4%, C=5 -> 40.8%, C=10 -> 54.7%.
        public static void HighConcurrencyBeatsOriginalThrottle()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config();
            var originalBaseline = new Dictionary<int, double> { { 3, 22.4 }, { 5, 40.8 }, { 10, 54.7 } };
            foreach (var kv in originalBaseline)
            {
                double p0 = ZeroImpostorPct(pool, cfg, kv.Key);
                Check.True(p0 < kv.Value, $"C={kv.Key}: P(0 impostors) = {p0:F2}% must beat original {kv.Value:F1}%");
            }
        }

        // Floor guarantee: the configured faction counts are GUARANTEED (not just capped), even when
        // every human dodges evil. Enforcement is purely via constrained offers, never a pick override.
        public static void FloorTargetsAlwaysMet()
        {
            var pool = TestPool.Make();                                  // plenty of every faction
            var cfg = TestPool.Config(maxImp: 2, maxNk: 2, maxNeu: 3);   // floors 2 / 2 / 3
            foreach (int c in new[] { 1, 2 })
            foreach (int n in new[] { 7, 10, 15 })
            {
                for (int seed = 1; seed <= 400; seed++)
                {
                    // evilProb=0 → every player picks crew whenever a crew card is offered; afk=0.
                    var r = DraftSim.Run(pool, cfg, n, c, 0.0, 0.0, new DeterministicRng((uint)(seed * 13 + c)));
                    Check.True(r.Impostors == 2 && r.NeutralKillings == 2 && r.Neutrals == 3,
                        $"C={c} n={n} seed {seed}: floors met exactly (imp={r.Impostors}, nk={r.NeutralKillings}, neu={r.Neutrals})");
                }
            }
        }

        // Scarce-pool floor guarantee: when a faction has exactly as many cards as its floor and several
        // concurrent pickers lock to it, the early offers must not reserve every card and starve their
        // peers. Parametrized over different host caps + scarce factions to prove the fix scales with
        // whatever the host configures (impostors / neutral-killers / neutrals), not a hard-coded count.
        public static void FloorMetWhenFactionPoolScarceUnderConcurrency()
        {
            var scenarios = new (int imp, int nk, int neu, int maxImp, int maxNk, int maxNeu, int n)[]
            {
                (2, 0, 0, 2, 0, 0, 2),   // 2 impostors, pool holds exactly 2
                (3, 0, 0, 3, 0, 0, 3),   // host raised impostors to 3
                (0, 2, 0, 0, 2, 0, 2),   // 2 neutral-killers
                (0, 0, 3, 0, 0, 3, 3),   // 3 neutral-passives
                (2, 2, 0, 2, 2, 0, 4),   // mixed scarce factions
            };
            foreach (var sc in scenarios)
            {
                var pool = TestPool.Make(crew: 30, imp: sc.imp, nk: sc.nk, neu: sc.neu);
                var cfg = TestPool.Config(maxImp: sc.maxImp, maxNk: sc.maxNk, maxNeu: sc.maxNeu);
                for (int seed = 1; seed <= 200; seed++)
                {
                    var r = DraftSim.Run(pool, cfg, sc.n, 2, 0.0, 0.0, new DeterministicRng((uint)seed));
                    Check.True(r.Impostors == sc.maxImp && r.NeutralKillings == sc.maxNk && r.Neutrals == sc.maxNeu,
                        $"caps {sc.maxImp}/{sc.maxNk}/{sc.maxNeu} n={sc.n} seed {seed}: scarce floors met " +
                        $"(got imp={r.Impostors}, nk={r.NeutralKillings}, neu={r.Neutrals})");
                }
            }
        }

        // Impostor specifically (not just "evil") must be roughly equally likely at every draft position —
        // no front-loading the impostor role onto early picks.
        public static void ImpostorChanceIsPositionFair()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config();
            const int trials = 8000;
            foreach (int n in new[] { 10, 12, 15 })
            {
                long firstImp = 0, lastImp = 0;
                for (int seed = 1; seed <= trials; seed++)
                {
                    var r = DraftSim.Run(pool, cfg, n, 1, 0.5, 0.10, new DeterministicRng((uint)(seed * 7 + 1)));
                    if (r.SlotFaction[0] == RoleFaction.Impostor) firstImp++;
                    if (r.SlotFaction[n - 1] == RoleFaction.Impostor) lastImp++;
                }
                double pf = 100.0 * firstImp / trials, pl = 100.0 * lastImp / trials;
                Check.True(System.Math.Abs(pf - pl) < 8.0,
                    $"n={n}: impostor chance similar for first vs last pick (first={pf:F1}%, last={pl:F1}%)");
            }
        }

        // Diversity: offers avoid duplicate alignments when enough distinct alignments are available.
        public static void OffersPreferDistinctAlignments()
        {
            // crew pool with 9 distinct-ish alignments (Crew_0/1/2), evil small so most cards are crew.
            var pool = TestPool.Make(crew: 30, imp: 2, nk: 2, neu: 2);
            var cfg = TestPool.Config(offered: 3);
            int trials = 3000, allSame = 0;
            for (int seed = 1; seed <= trials; seed++)
            {
                var eng = new DraftDistribution(pool, cfg, new DeterministicRng((uint)seed));
                var offer = eng.BuildOffer(new PlayerDraftState { SlotNumber = 1 }, 0, 10, new HashSet<ushort>(), null, eng.CreateRoundBudget());
                var aligns = offer.Select(eng.GetAlignment).Distinct().Count();
                if (aligns <= 1) allSame++;
            }
            // With diversity on and many alignments available, "all three identical alignment" should be very rare.
            double pct = 100.0 * allSame / trials;
            Check.True(pct < 1.0, $"offers rarely repeat one alignment 3x (all-same = {pct:F2}%)");
        }

        // Reroll safety: rebuilding a player's offer (the manager's reroll path) must not draft anything,
        // must not move faction tallies, must keep reservations consistent (old cards returned, new cards
        // reserved), and must still honour a floor lock. Mirrors DraftManager.RequestReroll using engine
        // primitives, since the manager itself is Unity-coupled and not in this project.
        public static void RerollRebuildsOfferWithoutCorruptingState()
        {
            var pool = TestPool.MakeCategorized();
            var cfg = TestPool.Config(offered: 3, maxImp: 2, maxNk: 2, maxNeu: 3);
            for (int seed = 1; seed <= 500; seed++)
            {
                var eng = new DraftDistribution(pool, cfg, new DeterministicRng((uint)seed));
                var reserved = new HashSet<ushort>();
                var budget = eng.CreateRoundBudget();
                var state = new PlayerDraftState { SlotNumber = 1 };

                // Initial deal.
                var first = eng.BuildOffer(state, 0, 10, reserved, null, budget, null, int.MaxValue);
                state.OfferedRoleIds = first;

                int impBefore = eng.ImpostorsDrafted;
                int nkBefore = eng.NeutralKillingsDrafted;
                int neuBefore = eng.NeutralPassivesDrafted;

                // Reroll: return this player's unique cards to the pool (exactly as RequestReroll does),
                // then rebuild the offer. No Commit() is called, so nothing is drafted.
                var prev = new HashSet<ushort>();
                foreach (var id in first)
                    if (eng.IsUniqueRole(id)) { reserved.Remove(id); prev.Add(id); }

                var second = eng.BuildOffer(state, 0, 10, reserved, prev, budget, null, int.MaxValue);
                state.OfferedRoleIds = second;

                // 1. No faction tally moved (reroll never drafts).
                Check.True(eng.ImpostorsDrafted == impBefore && eng.NeutralKillingsDrafted == nkBefore
                           && eng.NeutralPassivesDrafted == neuBefore,
                    $"seed {seed}: reroll must not draft anything "
                    + $"(imp {impBefore}->{eng.ImpostorsDrafted}, nk {nkBefore}->{eng.NeutralKillingsDrafted}, neu {neuBefore}->{eng.NeutralPassivesDrafted})");

                // 2. Offer is still a full, valid hand.
                Check.True(second.Count == cfg.OfferedRolesCount, $"seed {seed}: reroll offer is full ({second.Count})");

                // 3. Reservation set is consistent: every unique card in the NEW offer is reserved,
                //    and no card from the OLD offer is left stranded as reserved unless it was redrawn.
                foreach (var id in second)
                    if (eng.IsUniqueRole(id))
                        Check.True(reserved.Contains(id), $"seed {seed}: new offer card {id} is reserved");
                foreach (var id in prev)
                    if (!second.Contains(id))
                        Check.True(!reserved.Contains(id), $"seed {seed}: discarded card {id} returned to pool");
            }
        }

        // Reroll under a floor lock: the rebuilt offer must stay locked to the same faction.
        public static void RerollHonoursFloorLock()
        {
            var pool = TestPool.MakeCategorized();
            var cfg = TestPool.Config(offered: 3, maxImp: 2, maxNk: 2, maxNeu: 3);
            for (int seed = 1; seed <= 500; seed++)
            {
                var eng = new DraftDistribution(pool, cfg, new DeterministicRng((uint)seed));
                var reserved = new HashSet<ushort>();
                var budget = eng.CreateRoundBudget();
                var state = new PlayerDraftState { SlotNumber = 1, FloorLock = RoleFaction.Impostor };

                var first = eng.BuildOffer(state, 0, 10, reserved, null, budget, RoleFaction.Impostor, int.MaxValue);
                state.OfferedRoleIds = first;

                var prev = new HashSet<ushort>();
                foreach (var id in first)
                    if (eng.IsUniqueRole(id)) { reserved.Remove(id); prev.Add(id); }

                var second = eng.BuildOffer(state, 0, 10, reserved, prev, budget, RoleFaction.Impostor, int.MaxValue);

                foreach (var id in second)
                    Check.True(eng.GetFaction(id) == RoleFaction.Impostor,
                        $"seed {seed}: floor-locked reroll only offers the locked faction (got {eng.GetFaction(id)})");
            }
        }
    }
}
