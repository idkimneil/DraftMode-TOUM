namespace DraftModeTOUM.Managers
{
    public sealed class DraftConfig
    {
        public int OfferedRolesCount { get; set; } = 3;
        public int MaxImpostors { get; set; } = 2;
        public int MaxNeutralKillings { get; set; } = 2;
        public int MaxNeutralPassives { get; set; } = 3;
        public int MaxCrewKillings { get; set; } = 3;
        public int MaxCrewPowers { get; set; } = 3;
        public bool UseRoleChances { get; set; } = true;
        public ushort CrewmateRoleId { get; set; } = 0;
        public double EvilOfferChance { get; set; } = 0.5;
        public bool SoftImpostorNudge { get; set; } = true;
        public bool OfferDiversity { get; set; } = true;
        public double FloorSpreadBias { get; set; } = 0.0;
        public double ImpostorSpreadPower { get; set; } = 0.0;
        public double PositionEdge { get; set; } = 0.0;
    }
}