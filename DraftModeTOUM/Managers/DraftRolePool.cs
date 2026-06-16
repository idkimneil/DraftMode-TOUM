using System.Collections.Generic;

namespace DraftModeTOUM.Managers
{
    public sealed class DraftRolePool
    {
        public List<ushort> RoleIds { get; } = new();
        public Dictionary<ushort, int> MaxCounts { get; } = new();
        public Dictionary<ushort, int> Weights { get; } = new();
        public Dictionary<ushort, RoleFaction> Factions { get; } = new();
        public Dictionary<ushort, string> Alignments { get; } = new();
    }
}
