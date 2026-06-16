namespace DraftModeTOUM.Managers
{
    public sealed class DraftConfig
    {
        public int OfferedRolesCount { get; set; } = 3;
        public int MaxImpostors { get; set; } = 2;
        public int MaxNeutralKillings { get; set; } = 2;
        public int MaxNeutralPassives { get; set; } = 3;
        public bool UseRoleChances { get; set; } = true;
        public ushort CrewmateRoleId { get; set; } = 0;
        public double EvilOfferChance { get; set; } = 0.5;
        public bool SoftImpostorNudge { get; set; } = true;
        public bool OfferDiversity { get; set; } = true;
        public double FloorSpreadBias { get; set; } = 0.0;
        public double ImpostorSpreadPower { get; set; } = 0.0;
        // Slight early-slot edge: tilts evil-offer rate by pick position. 0 = flat (no edge).
        // Symmetric around the midpoint, so it redistributes the edge early->late without
        // changing total evil count. Clamped so even the last slot keeps a nonzero shot and
        // even slot 1 can still whiff into an all-crew offer.
        public double PositionEdge { get; set; } = 0.0;
    }
}
