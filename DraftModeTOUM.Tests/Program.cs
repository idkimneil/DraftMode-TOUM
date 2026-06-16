using System;

namespace DraftModeTOUM.Tests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "lobby")
            {
                LobbySim.Run();
                return 0;
            }
            if (args != null && args.Length > 0 && args[0] == "fairness")
            {
                LobbySim.Fairness();
                return 0;
            }
            if (args != null && args.Length > 0 && args[0] == "compare")
            {
                Compare.Run();
                return 0;
            }
            if (args != null && args.Length > 0 && args[0] == "categories")
            {
                Compare.Categories();
                return 0;
            }
            if (args != null && args.Length > 0 && args[0] == "report")
            {
                Compare.Report();
                return 0;
            }
            if (args != null && args.Length > 0 && args[0] == "sweep")
            {
                Compare.Sweep();
                return 0;
            }
            if (args != null && args.Length > 0 && args[0] == "floorbias")
            {
                Compare.FloorBias();
                return 0;
            }
            if (args != null && args.Length > 0 && args[0] == "fullsim")
            {
                Compare.FullSim();
                return 0;
            }
            if (args != null && args.Length > 0 && args[0] == "impbias")
            {
                Compare.ImpostorBias();
                return 0;
            }

            Console.WriteLine("DraftDistribution engine tests\n");

            Run("OfferAlwaysHasTargetSize", Tests.OfferAlwaysHasTargetSize);
            Run("ConcurrentOffersRespectFactionBudget", Tests.ConcurrentOffersRespectFactionBudget);
            Run("NoDuplicateUniqueAcrossConcurrentOffers", Tests.NoDuplicateUniqueAcrossConcurrentOffers);
            Run("SoftImpostorNudgeOffersImpostor", Tests.SoftImpostorNudgeOffersImpostor);
            Run("CapsNeverExceeded", Tests.CapsNeverExceeded);
            Run("FloorTargetsAlwaysMet", Tests.FloorTargetsAlwaysMet);
            Run("FloorMetWhenFactionPoolScarceUnderConcurrency", Tests.FloorMetWhenFactionPoolScarceUnderConcurrency);
            Run("ImpostorChanceIsPositionFair", Tests.ImpostorChanceIsPositionFair);
            Run("DeterministicOffers", Tests.DeterministicOffers);
            Run("BucketHonoredOnAfk", Tests.BucketHonoredOnAfk);
            Run("BucketAutoPickRespectsReservation", Tests.BucketAutoPickRespectsReservation);
            Run("PositionDoesNotChangeEvilRate", Tests.PositionDoesNotChangeEvilRate);
            Run("ConcurrencyDoesNotStarveImpostorsInSupportedRange", Tests.ConcurrencyDoesNotStarveImpostorsInSupportedRange);
            Run("HighConcurrencyBeatsOriginalThrottle", Tests.HighConcurrencyBeatsOriginalThrottle);
            Run("OffersPreferDistinctAlignments", Tests.OffersPreferDistinctAlignments);
            Run("RerollRebuildsOfferWithoutCorruptingState", Tests.RerollRebuildsOfferWithoutCorruptingState);
            Run("RerollHonoursFloorLock", Tests.RerollHonoursFloorLock);

            Console.WriteLine($"\n{Check.Passed} checks passed, {Check.Failures} failed.");
            return Check.Failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            Console.WriteLine($"[test] {name}");
            try { test(); }
            catch (Exception ex) { Check.Failures++; Console.WriteLine("  EXCEPTION: " + ex); }
        }
    }
}
