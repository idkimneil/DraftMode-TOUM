using System;
using System.Collections.Generic;
using DraftModeTOUM.Managers;

namespace DraftModeTOUM.Tests
{
    public static class Compare
    {
        private sealed class M
        {
            public double meanImp, meanEvil, sdEvil, p0imp, pHealthy, fairnessSd, pEvilFirst, pEvilLast, meanDistinct, clumpPct;
        }

        public static void Sweep()
        {
            var pool = TestPool.MakeCategorized();
            var cfg = TestPool.Config();
            const int trials = 8000;
            const int c = 1;
            const double evil = 0.5, afk = 0.10;

            Console.WriteLine($"Player-count sweep n=2..25 — C={c} (default), TYPICAL play, {trials} drafts/cell.");
            Console.WriteLine("(caps imp2/nk2/neu3; small lobbies are capacity-limited)\n");
            Console.WriteLine("        mean impostors     mean total evil     P(0 impostors)%     fairness stddev");
            Console.WriteLine("   n     orig    new        orig    new         orig     new        orig    new");
            for (int n = 2; n <= 25; n++)
            {
                var o = Agg(false, pool, cfg, n, c, evil, afk, trials);
                var w = Agg(true, pool, cfg, n, c, evil, afk, trials);
                Console.WriteLine(
                    $"  {n,2}    {o.meanImp,5:F2}  {w.meanImp,5:F2}      {o.meanEvil,5:F2}  {w.meanEvil,5:F2}       " +
                    $"{o.p0imp,5:F1}  {w.p0imp,5:F1}       {o.fairnessSd,5:F2}  {w.fairnessSd,5:F2}");
            }
        }

        public static void FloorBias()
        {
            var pool = TestPool.MakeCategorized();
            const int trials = 12000;
            const double evil = 0.5, afk = 0.10;
            Console.WriteLine($"Floor-spread-bias grid — C=1 fairness stddev (lower=fairer), {trials} drafts/cell.");
            var biases = new[] { 0.0, 0.1, 0.15, 0.2, 0.3, 0.4, 0.5, 0.7, 1.0 };
            Console.Write("   n  ");
            foreach (var b in biases) Console.Write($"{b,6:F2}");
            Console.WriteLine();
            foreach (int n in new[] { 8, 10, 12, 13, 14, 15 })
            {
                Console.Write($"  {n,2}  ");
                foreach (var bias in biases)
                {
                    var cfg = TestPool.Config(); cfg.FloorSpreadBias = bias;
                    var w = Agg(true, pool, cfg, n, 1, evil, afk, trials);
                    Console.Write($"{w.fairnessSd,6:F1}");
                }
                Console.WriteLine();
            }
        }

        public static void ImpostorBias()
        {
            var pool = TestPool.MakeCategorized();
            const int trials = 12000;
            const double evil = 0.5, afk = 0.10;
            var powers = new[] { 0.0, 1.0, 1.5, 2.0, 2.5, 3.0 };
            Console.WriteLine($"Impostor-nudge density-power sweep — first% / last% / gap of impostor by seat, {trials} drafts/cell.");
            Console.Write("   n  ");
            foreach (var p in powers) Console.Write($"   pow{p:F1}        ");
            Console.WriteLine();
            foreach (int n in new[] { 8, 9, 10, 11, 12, 13, 14, 15 })
            {
                Console.Write($"  {n,2}  ");
                foreach (var p in powers)
                {
                    var cfg = TestPool.Config(); cfg.ImpostorSpreadPower = p;
                    long fi = 0, li = 0;
                    for (int s = 1; s <= trials; s++)
                    {
                        var r = DraftSim.Run(pool, cfg, n, 1, evil, afk, new DeterministicRng((uint)(s * 7 + 1)));
                        if (r.SlotFaction[0] == RoleFaction.Impostor) fi++;
                        if (r.SlotFaction[n - 1] == RoleFaction.Impostor) li++;
                    }
                    double f = 100.0 * fi / trials, l = 100.0 * li / trials;
                    Console.Write($" {f,4:F1}/{l,4:F1}/{f - l,5:F1}");
                }
                Console.WriteLine();
            }
        }

        public static void FullSim()
        {
            var pool = TestPool.MakeCategorized();
            var cfg = TestPool.Config();
            const int trials = 12000;
            const double evil = 0.5, afk = 0.10;
            const int c = 1;

            int poolImp = 0, poolNk = 0, poolNeu = 0, poolCrew = 0;
            foreach (var id in pool.RoleIds)
            {
                switch (pool.Factions.TryGetValue(id, out var f) ? f : RoleFaction.Crewmate)
                {
                    case RoleFaction.Impostor: poolImp++; break;
                    case RoleFaction.NeutralKilling: poolNk++; break;
                    case RoleFaction.Neutral: poolNeu++; break;
                    default: poolCrew++; break;
                }
            }

            Console.WriteLine($"FULL SIMULATION  —  n = 1..25,  C={c} (default),  TYPICAL play (0.5 evil, 10% AFK),  {trials} drafts per lobby size.");
            Console.WriteLine($"Pool: {poolImp} impostor, {poolNk} neutral-killing, {poolNeu} neutral-other, {poolCrew} crew roles.  Caps: imp {cfg.MaxImpostors} / nk {cfg.MaxNeutralKillings} / neu {cfg.MaxNeutralPassives}.\n");

            int CREW = (int)RoleFaction.Crewmate, IMP = (int)RoleFaction.Impostor,
                NEU = (int)RoleFaction.Neutral,  NK  = (int)RoleFaction.NeutralKilling;

            var firstAll = new double[26][];
            var lastAll  = new double[26][];

            Console.WriteLine("=== LOBBY COMPOSITION  (mean roles drafted per game) ===");
            Console.WriteLine("   n     imp     nk    neu    crew    P(0 imp)%   P(0 NK)%   P(0 neu)%");
            for (int n = 1; n <= 25; n++)
            {
                var slot = new long[n, 4];
                double sImp = 0, sNk = 0, sNeu = 0, sCrew = 0;
                long z0imp = 0, z0nk = 0, z0neu = 0;
                for (int s = 1; s <= trials; s++)
                {
                    var r = DraftSim.Run(pool, cfg, n, c, evil, afk, new DeterministicRng((uint)(s * 7 + c)));
                    sImp += r.Impostors; sNk += r.NeutralKillings; sNeu += r.Neutrals; sCrew += r.Crew;
                    if (r.Impostors == 0) z0imp++;
                    if (r.NeutralKillings == 0) z0nk++;
                    if (r.Neutrals == 0) z0neu++;
                    for (int i = 0; i < n; i++) slot[i, (int)r.SlotFaction[i]]++;
                }
                Console.WriteLine(
                    $"  {n,2}   {sImp / trials,5:F2}  {sNk / trials,5:F2}  {sNeu / trials,5:F2}  {sCrew / trials,6:F2}     " +
                    $"{100.0 * z0imp / trials,5:F1}      {100.0 * z0nk / trials,5:F1}      {100.0 * z0neu / trials,5:F1}");

                double[] Pct(int idx) => new[]
                {
                    100.0 * slot[idx, CREW] / trials, 100.0 * slot[idx, IMP] / trials,
                    100.0 * slot[idx, NK]   / trials, 100.0 * slot[idx, NEU] / trials,
                };
                firstAll[n] = Pct(0);
                lastAll[n]  = Pct(n - 1);
            }

            Console.WriteLine("\n=== ROLE-GROUP CHANCE BY DRAFT POSITION  (player 1 = first pick, player N = last pick) ===");
            Console.WriteLine("         ----------- PLAYER 1 ------------    ----------- PLAYER N ------------");
            Console.WriteLine("   n      Crew   Imp    NK    Neu             Crew   Imp    NK    Neu");
            for (int n = 1; n <= 25; n++)
            {
                var a = firstAll[n]; var b = lastAll[n];
                Console.WriteLine(
                    $"  {n,2}     {a[0],5:F1} {a[1],5:F1} {a[2],5:F1} {a[3],5:F1}           " +
                    $"{b[0],5:F1} {b[1],5:F1} {b[2],5:F1} {b[3],5:F1}");
            }
        }

        public static void Report()
        {
            var pool = TestPool.MakeCategorized();
            var cfg = TestPool.Config();
            const int trials = 20000, n = 10;

            Console.WriteLine($"Comprehensive comparison — {n} players, TYPICAL play (0.5 evil, 10% AFK), {trials} drafts/cell.\n");

            foreach (int c in new[] { 1, 2 })
            {
                var o = Agg(false, pool, cfg, n, c, 0.5, 0.10, trials);
                var w = Agg(true, pool, cfg, n, c, 0.5, 0.10, trials);
                Console.WriteLine($"### Concurrent picks = {c}                         orig  ->  new");
                Row("BALANCE  mean total evil roles (cap 7)", o.meanEvil, w.meanEvil, "F2");
                Row("         evil-count stddev (lower=steadier)", o.sdEvil, w.sdEvil, "F2");
                Row("         P(0 impostors) % (lower=better)", o.p0imp, w.p0imp, "F1");
                Row("         P(healthy 4-7 evil) % (higher=better)", o.pHealthy, w.pHealthy, "F1");
                Row("FAIRNESS stddev of P(evil|slot) across slots (lower=fairer)", o.fairnessSd, w.fairnessSd, "F2");
                Row("         P(evil | FIRST slot) %", o.pEvilFirst, w.pEvilFirst, "F1");
                Row("         P(evil | LAST slot) %", o.pEvilLast, w.pEvilLast, "F1");
                Row("VARIETY  mean distinct sub-categories / game", o.meanDistinct, w.meanDistinct, "F2");
                Row("         offer clumping % (repeated alignment, lower=fresher)", o.clumpPct, w.clumpPct, "F1");
                Console.WriteLine();
            }

            Console.WriteLine("### ROBUSTNESS — P(0 impostors) % vs player behavior (C=2)");
            Console.WriteLine("  behavior     orig     new");
            foreach (var (lbl, e, a) in new[] { ("eager", 0.8, 0.0), ("typical", 0.5, 0.10), ("shy", 0.2, 0.10) })
            {
                var o = Agg(false, pool, cfg, n, 2, e, a, trials);
                var w = Agg(true, pool, cfg, n, 2, e, a, trials);
                Console.WriteLine($"  {lbl,-10} {o.p0imp,5:F1}%   {w.p0imp,5:F1}%");
            }
        }

        private static void Row(string label, double oldv, double newv, string fmt)
            => Console.WriteLine($"  {label,-58} {oldv.ToString(fmt),7}  ->  {newv.ToString(fmt),7}");

        private static M Agg(bool useNew, DraftRolePool pool, DraftConfig cfg, int n, int c, double evil, double afk, int trials)
        {
            var slotEvil = new long[n];
            long offersBuilt = 0, offersDup = 0, zero = 0, healthy = 0;
            double sumImp = 0, sumEvil = 0, sumEvil2 = 0, sumDistinct = 0;
            for (int s = 1; s <= trials; s++)
            {
                var rng = new DeterministicRng((uint)(s * 7 + c));
                var r = useNew ? DraftSim.Run(pool, cfg, n, c, evil, afk, rng) : new OriginalSim(pool, cfg, rng).Run(n, c, evil, afk);
                int e = r.Evil;
                sumImp += r.Impostors;
                sumEvil += e; sumEvil2 += (double)e * e;
                if (r.Impostors == 0) zero++;
                if (e >= 4 && e <= 7) healthy++;
                for (int i = 0; i < n; i++) if (r.SlotFaction[i] != RoleFaction.Crewmate) slotEvil[i]++;
                offersBuilt += r.OffersBuilt; offersDup += r.OffersWithDupAlignment;
                var distinct = new HashSet<string>();
                foreach (var id in r.ChosenRoles)
                {
                    var a = pool.Alignments.TryGetValue(id, out var al) ? al : "";
                    if (!string.IsNullOrEmpty(a)) distinct.Add(a);
                }
                sumDistinct += distinct.Count;
            }
            double mean = sumEvil / trials;
            double variance = sumEvil2 / trials - mean * mean;
            var p = new double[n];
            double pmean = 0;
            for (int i = 0; i < n; i++) { p[i] = (double)slotEvil[i] / trials; pmean += p[i]; }
            pmean /= n;
            double fvar = 0;
            for (int i = 0; i < n; i++) fvar += (p[i] - pmean) * (p[i] - pmean);
            return new M
            {
                meanImp = sumImp / trials,
                meanEvil = mean,
                sdEvil = Math.Sqrt(Math.Max(0, variance)),
                p0imp = 100.0 * zero / trials,
                pHealthy = 100.0 * healthy / trials,
                fairnessSd = 100.0 * Math.Sqrt(fvar / n),
                pEvilFirst = 100.0 * p[0],
                pEvilLast = 100.0 * p[n - 1],
                meanDistinct = sumDistinct / trials,
                clumpPct = offersBuilt > 0 ? 100.0 * offersDup / offersBuilt : 0,
            };
        }

        public static void Categories()
        {
            var pool = TestPool.MakeCategorized();
            var cfg = TestPool.Config();
            const int trials = 20000, n = 10;
            const double evil = 0.5, afk = 0.10; // typical play

            Console.WriteLine($"Mean roles drafted per sub-category — {n} players, TYPICAL play, {trials} drafts/cell.");
            Console.WriteLine("(faction caps imp2/nk2/neu3; sub-categories have NO caps in either version)\n");

            foreach (int c in new[] { 1, 2 })
            {
                var oldTally = Tally(pool, cfg, n, c, evil, afk, trials, useNew: false);
                var newTally = Tally(pool, cfg, n, c, evil, afk, trials, useNew: true);

                Console.WriteLine($"### Concurrent picks = {c}");
                Console.WriteLine("  sub-category                 orig    new    delta");
                foreach (var (align, _, _) in TestPool.Categories)
                {
                    double o = oldTally.TryGetValue(align, out var ov) ? ov / (double)trials : 0;
                    double nw = newTally.TryGetValue(align, out var nv) ? nv / (double)trials : 0;
                    Console.WriteLine($"  {align,-28} {o,5:F2}  {nw,5:F2}  {(nw - o),6:F2}");
                }
                double ov2 = oldTally.TryGetValue("Crewmate (Vanilla)", out var a) ? a / (double)trials : 0;
                double nv2 = newTally.TryGetValue("Crewmate (Vanilla)", out var b2) ? b2 / (double)trials : 0;
                Console.WriteLine($"  {"Crewmate (Vanilla/no-role)",-28} {ov2,5:F2}  {nv2,5:F2}  {(nv2 - ov2),6:F2}");
                Console.WriteLine();
            }
        }

        private static Dictionary<string, long> Tally(DraftRolePool pool, DraftConfig cfg, int n, int c,
            double evil, double afk, int trials, bool useNew)
        {
            var tally = new Dictionary<string, long>();
            for (int s = 1; s <= trials; s++)
            {
                var rng = new DeterministicRng((uint)(s * 7 + c));
                var r = useNew
                    ? DraftSim.Run(pool, cfg, n, c, evil, afk, rng)
                    : new OriginalSim(pool, cfg, rng).Run(n, c, evil, afk);
                foreach (var id in r.ChosenRoles)
                {
                    var align = pool.Alignments.TryGetValue(id, out var al) ? al : "?";
                    tally[align] = (tally.TryGetValue(align, out var v) ? v : 0) + 1;
                }
            }
            return tally;
        }

        public static void Run()
        {
            var pool = TestPool.Make();
            var cfg = TestPool.Config();
            const int trials = 20000, n = 10;

            Console.WriteLine($"Original vs New — {n} players, caps imp2/nk2/neu3, {trials} drafts/cell.\n");

            var behaviors = new (string name, double evil, double afk)[]
            {
                ("EAGER   (pick evil 80%, 0% AFK)", 0.8, 0.0),
                ("TYPICAL (pick evil 50%, 10% AFK)", 0.5, 0.10),
                ("SHY     (pick evil 20%, 10% AFK)", 0.2, 0.10),
            };

            foreach (var b in behaviors)
            {
                Console.WriteLine($"### {b.name}");
                Console.WriteLine("  C  |  P(0 impostors)        |  mean impostors        |  mean total evil");
                Console.WriteLine("     |  orig    new    delta  |  orig   new            |  orig   new");
                foreach (int c in new[] { 1, 2, 3, 5, 10 })
                {
                    var o = AggOld(pool, cfg, n, c, b.evil, b.afk, trials);
                    var nw = AggNew(pool, cfg, n, c, b.evil, b.afk, trials);
                    Console.WriteLine(
                        $"  {c,-2} |  {o.p0,5:F1}%  {nw.p0,5:F1}%  {(o.p0 - nw.p0),5:F1}  |  " +
                        $"{o.imp,4:F2}  {nw.imp,4:F2}          |  {o.evil,4:F2}  {nw.evil,4:F2}");
                }
                Console.WriteLine();
            }
        }

        private static (double p0, double imp, double evil, double crew) AggOld(
            DraftRolePool pool, DraftConfig cfg, int n, int c, double evil, double afk, int trials)
        {
            long z = 0; double si = 0, se = 0, sc = 0;
            for (int s = 1; s <= trials; s++)
            {
                var r = new OriginalSim(pool, cfg, new DeterministicRng((uint)(s * 7 + c))).Run(n, c, evil, afk);
                if (r.Impostors == 0) z++;
                si += r.Impostors; se += r.Evil; sc += r.Crew;
            }
            return (100.0 * z / trials, si / trials, se / trials, sc / trials);
        }

        private static (double p0, double imp, double evil, double crew) AggNew(
            DraftRolePool pool, DraftConfig cfg, int n, int c, double evil, double afk, int trials)
        {
            long z = 0; double si = 0, se = 0, sc = 0;
            for (int s = 1; s <= trials; s++)
            {
                var r = DraftSim.Run(pool, cfg, n, c, evil, afk, new DeterministicRng((uint)(s * 7 + c)));
                if (r.Impostors == 0) z++;
                si += r.Impostors; se += r.Evil; sc += r.Crew;
            }
            return (100.0 * z / trials, si / trials, se / trials, sc / trials);
        }
    }
}
