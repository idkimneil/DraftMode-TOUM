using System.Collections.Generic;

namespace DraftModeTOUM.Managers
{
    public class PlayerDraftState
    {
        public byte PlayerId { get; set; }
        public int SlotNumber { get; set; }
        public ushort? ChosenRoleId { get; set; }
        public bool HasPicked { get; set; }
        public bool IsPickingNow { get; set; }
        public bool IsDisconnected { get; set; }
        public List<ushort> OfferedRoleIds { get; set; } = new();

        public RoleFaction? GuaranteedFaction { get; set; }
        public RoleFaction? FloorLock { get; set; }
        public int LockShare { get; set; } = int.MaxValue;
        public int RerollsRemaining { get; set; }
    }
}
