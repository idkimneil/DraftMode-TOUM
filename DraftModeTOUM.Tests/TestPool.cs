using DraftModeTOUM.Managers;

namespace DraftModeTOUM.Tests
{
    public static class TestPool
    {
        public const ushort CrewmateId = 0;

        // Representative TOU:M pool after bans (counts approximate; ratios are what matter).
        public static DraftRolePool Make(int crew = 30, int imp = 19, int nk = 7, int neu = 9)
        {
            var p = new DraftRolePool();

            // Plain repeatable crewmate (the pad/fallback role).
            p.RoleIds.Add(CrewmateId);
            p.MaxCounts[CrewmateId] = 99;
            p.Weights[CrewmateId] = 1;
            p.Factions[CrewmateId] = RoleFaction.Crewmate;
            p.Alignments[CrewmateId] = "Crewmate";

            ushort id = 1;
            void Add(int n, RoleFaction faction, string align)
            {
                for (int i = 0; i < n; i++)
                {
                    p.RoleIds.Add(id);
                    p.MaxCounts[id] = 1;
                    p.Weights[id] = 100;
                    p.Factions[id] = faction;
                    p.Alignments[id] = align + "_" + (i % 3);
                    id++;
                }
            }

            Add(crew, RoleFaction.Crewmate, "Crew");
            Add(imp, RoleFaction.Impostor, "Imp");
            Add(nk, RoleFaction.NeutralKilling, "NK");
            Add(neu, RoleFaction.Neutral, "Neu");
            return p;
        }

        // Representative TOU:M roster tagged with REAL sub-category alignments (post-bans, approximate counts).
        public static readonly (string align, RoleFaction faction, int count)[] Categories =
        {
            ("Crewmate (Investigative)", RoleFaction.Crewmate, 10),
            ("Crewmate (Killing)",       RoleFaction.Crewmate, 5),
            ("Crewmate (Power)",         RoleFaction.Crewmate, 6),
            ("Crewmate (Protective)",    RoleFaction.Crewmate, 6),
            ("Crewmate (Support)",       RoleFaction.Crewmate, 6),
            ("Impostor (Concealing)",    RoleFaction.Impostor, 6),
            ("Impostor (Killing)",       RoleFaction.Impostor, 5),
            ("Impostor (Power)",         RoleFaction.Impostor, 3),
            ("Impostor (Support)",       RoleFaction.Impostor, 5),
            ("Neutral (Killing)",        RoleFaction.NeutralKilling, 7),
            ("Neutral (Benign)",         RoleFaction.Neutral, 4),
            ("Neutral (Evil)",           RoleFaction.Neutral, 4),
            ("Neutral (Outlier)",        RoleFaction.Neutral, 2),
        };

        public static DraftRolePool MakeCategorized()
        {
            var p = new DraftRolePool();
            p.RoleIds.Add(CrewmateId);
            p.MaxCounts[CrewmateId] = 99;
            p.Weights[CrewmateId] = 1;
            p.Factions[CrewmateId] = RoleFaction.Crewmate;
            p.Alignments[CrewmateId] = "Crewmate (Vanilla)";

            ushort id = 1;
            foreach (var (align, faction, count) in Categories)
            {
                for (int i = 0; i < count; i++)
                {
                    p.RoleIds.Add(id);
                    p.MaxCounts[id] = 1;
                    p.Weights[id] = 100;
                    p.Factions[id] = faction;
                    p.Alignments[id] = align;
                    id++;
                }
            }
            return p;
        }

        public static DraftConfig Config(int offered = 3, int maxImp = 2, int maxNk = 2, int maxNeu = 3, double positionEdge = 0.0) => new DraftConfig
        {
            PositionEdge = positionEdge,
            OfferedRolesCount = offered,
            MaxImpostors = maxImp,
            MaxNeutralKillings = maxNk,
            MaxNeutralPassives = maxNeu,
            UseRoleChances = true,
            CrewmateRoleId = CrewmateId,
            EvilOfferChance = 0.5,
            SoftImpostorNudge = true,
            OfferDiversity = true,
            FloorSpreadBias = 1.0,
            ImpostorSpreadPower = 1.5,
        };
    }
}
