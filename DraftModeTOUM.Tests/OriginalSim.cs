using System;
using System.Collections.Generic;
using System.Linq;
using DraftModeTOUM.Managers;

namespace DraftModeTOUM.Tests
{
    // Faithful reimplementation of the ORIGINAL (pre-refactor) DraftManager distribution logic
    // (baseline commit a4f5246), for apples-to-apples comparison against the new engine. Mirrors:
    //  - position bias chance = 0.25 + 0.55*(1 - slotIndex/(n-1))
    //  - BuildRoundFactionAllowList: at concurrency > 1, only ONE active picker may take evil; others crew-only
    //  - PickFullRandomForState: NO guaranteed-faction honoring
    //  - no soft impostor nudge, no offer diversity, no per-round faction budget
    public sealed class OriginalSim
    {
        private readonly DraftRolePool _pool;
        private readonly int _maxImp, _maxNk, _maxNp, _target;
        private readonly bool _useChances;
        private readonly ushort _crewId;
        private readonly IRng _rng;
        private readonly Dictionary<ushort, int> _drafted = new();
        private int _imp, _nk, _np;

        public OriginalSim(DraftRolePool pool, DraftConfig cfg, IRng rng)
        {
            _pool = pool; _rng = rng; _crewId = cfg.CrewmateRoleId;
            _maxImp = cfg.MaxImpostors; _maxNk = cfg.MaxNeutralKillings; _maxNp = cfg.MaxNeutralPassives;
            _target = cfg.OfferedRolesCount; _useChances = cfg.UseRoleChances;
        }

        private RoleFaction Fac(ushort id) => _pool.Factions.TryGetValue(id, out var f) ? f : RoleFaction.Crewmate;
        private int Drafted(ushort id) => _drafted.TryGetValue(id, out var c) ? c : 0;
        private int MaxOf(ushort id) => _pool.MaxCounts.TryGetValue(id, out var c) ? c : 1;
        private int W(ushort id) => _pool.Weights.TryGetValue(id, out var w) ? Math.Max(1, w) : 1;
        private bool Unique(ushort id) => id != _crewId;
        private int FacDrafted(RoleFaction f) => f == RoleFaction.Impostor ? _imp : f == RoleFaction.NeutralKilling ? _nk : f == RoleFaction.Neutral ? _np : 0;
        private int FacCap(RoleFaction f) => f == RoleFaction.Impostor ? _maxImp : f == RoleFaction.NeutralKilling ? _maxNk : f == RoleFaction.Neutral ? _maxNp : int.MaxValue;
        private bool RoleAvail(ushort id)
        {
            if (Drafted(id) >= MaxOf(id)) return false;
            var f = Fac(id);
            return f == RoleFaction.Crewmate || FacDrafted(f) < FacCap(f);
        }

        private List<ushort> Available(ISet<ushort> ex)
        {
            var r = new List<ushort>();
            foreach (var id in _pool.RoleIds)
            {
                if (ex != null && ex.Contains(id)) continue;
                if (Drafted(id) >= MaxOf(id)) continue;
                var f = Fac(id);
                if (f != RoleFaction.Crewmate && FacDrafted(f) >= FacCap(f)) continue;
                r.Add(id);
            }
            return r;
        }
        private List<ushort> AvailForFac(RoleFaction f) => Available(null).Where(id => Fac(id) == f).ToList();

        private ushort PickW(IList<ushort> c)
        {
            if (c.Count == 0) return _crewId;
            if (!_useChances) return c[_rng.NextInt(c.Count)];
            int t = 0; foreach (var x in c) t += W(x);
            if (t <= 0) return c[_rng.NextInt(c.Count)];
            int roll = _rng.NextInt(1, t + 1), a = 0;
            foreach (var id in c) { a += W(id); if (roll <= a) return id; }
            return c[_rng.NextInt(c.Count)];
        }
        private List<ushort> PickWU(IList<ushort> c, int n)
        {
            var res = new List<ushort>(); var tmp = new List<ushort>(c);
            while (res.Count < n && tmp.Count > 0) { var p = PickW(tmp); res.Add(p); tmp.Remove(p); }
            return res;
        }

        private Dictionary<int, HashSet<RoleFaction>> BuildAllow(List<PlayerDraftState> active)
        {
            var map = new Dictionary<int, HashSet<RoleFaction>>();
            if (active.Count <= 1) return map; // empty => no restriction
            int rImp = Math.Max(0, _maxImp - _imp), rNk = Math.Max(0, _maxNk - _nk), rNp = Math.Max(0, _maxNp - _np);
            if (rImp + rNk + rNp <= 0) { foreach (var s in active) map[s.SlotNumber] = new HashSet<RoleFaction>(); return map; }
            var pref = active.Where(s =>
            {
                if (!s.GuaranteedFaction.HasValue) return false;
                var f = s.GuaranteedFaction.Value;
                return (f == RoleFaction.Impostor && rImp > 0) || (f == RoleFaction.NeutralKilling && rNk > 0) || (f == RoleFaction.Neutral && rNp > 0);
            }).ToList();
            int allowed = pref.Count > 0 ? pref[_rng.NextInt(pref.Count)].SlotNumber : active[_rng.NextInt(active.Count)].SlotNumber;
            foreach (var s in active) map[s.SlotNumber] = new HashSet<RoleFaction>();
            var set = map[allowed];
            if (rImp > 0) set.Add(RoleFaction.Impostor);
            if (rNk > 0) set.Add(RoleFaction.NeutralKilling);
            if (rNp > 0) set.Add(RoleFaction.Neutral);
            return map;
        }
        private List<ushort> Filter(PlayerDraftState st, List<ushort> ids, Dictionary<int, HashSet<RoleFaction>> allow)
        {
            if (allow.Count == 0) return ids;
            if (!allow.TryGetValue(st.SlotNumber, out var a) || a.Count == 0)
                return ids.Where(id => Fac(id) == RoleFaction.Crewmate).ToList();
            return ids.Where(id => { var f = Fac(id); return f == RoleFaction.Crewmate || a.Contains(f); }).ToList();
        }

        private List<ushort> BuildOffer(PlayerDraftState st, int n, HashSet<ushort> reserved, Dictionary<int, HashSet<RoleFaction>> allow)
        {
            var available = Filter(st, Available(reserved), allow);
            var offered = new List<ushort>();
            if (available.Count > 0)
            {
                var nonCrew = available.Where(id => Fac(id) != RoleFaction.Crewmate).ToList();
                var crew = available.Where(id => Fac(id) == RoleFaction.Crewmate).ToList();
                int slotIndex = Math.Max(0, st.SlotNumber - 1);
                float t = n == 1 ? 0f : (float)slotIndex / (n - 1f);
                float bias = 1f - t;
                int maxEvil = Math.Min(nonCrew.Count, _target >= 4 ? 4 : _target);
                int minEvil = nonCrew.Count > 0 ? 1 : 0;
                int evil = minEvil, extra = Math.Max(0, maxEvil - minEvil);
                for (int i = 0; i < extra; i++) if (_rng.NextDouble() < 0.25 + 0.55 * bias) evil++;
                evil = Math.Min(evil, maxEvil);
                if (st.GuaranteedFaction.HasValue && nonCrew.Count > 0)
                {
                    var bp = AvailForFac(st.GuaranteedFaction.Value).Where(id => !(Unique(id) && reserved.Contains(id))).ToList();
                    if (bp.Count > 0) { offered.AddRange(PickWU(bp, 1)); evil = Math.Max(0, evil - 1); }
                }
                if (evil > 0)
                {
                    var ep = nonCrew.Where(id => !offered.Contains(id)).ToList();
                    offered.AddRange(PickWU(ep, Math.Min(evil, ep.Count)));
                }
                int rem = _target - offered.Count;
                if (rem > 0)
                {
                    var cp = crew.Where(id => !offered.Contains(id)).ToList();
                    offered.AddRange(PickWU(cp, Math.Min(rem, cp.Count)));
                }
                while (offered.Count < _target)
                {
                    var tu = available.Where(id => !offered.Contains(id)).ToList();
                    if (tu.Count == 0) break;
                    offered.AddRange(PickWU(tu, 1));
                }
            }
            while (offered.Count < _target) offered.Add(_crewId);
            var fin = offered.OrderBy(_ => _rng.NextInt(1000000)).ToList();
            foreach (var id in fin) if (Unique(id)) reserved.Add(id);
            return fin;
        }

        private ushort PickFullRandom(ISet<ushort> ex)
        {
            var a = Available(ex);
            if (a.Count == 0) return _crewId;
            return _useChances ? PickW(a) : a[_rng.NextInt(a.Count)];
        }
        private ushort PickFRForState(PlayerDraftState st, HashSet<ushort> chosen, HashSet<ushort> reserved)
        {
            var ex = new HashSet<ushort>(chosen);
            foreach (var id in reserved) if (!st.OfferedRoleIds.Contains(id)) ex.Add(id);
            var pick = PickFullRandom(ex);
            if (Unique(pick) && ex.Contains(pick)) pick = PickFullRandom(chosen);
            return pick;
        }
        private RoleFaction Commit(ushort id)
        {
            _drafted[id] = Drafted(id) + 1;
            var f = Fac(id);
            if (f == RoleFaction.Impostor) _imp++;
            else if (f == RoleFaction.NeutralKilling) _nk++;
            else if (f == RoleFaction.Neutral) _np++;
            return f;
        }

        public DraftSim.Result Run(int n, int concurrent, double evilProb, double afkProb)
        {
            var buckets = new RoleFaction?[n];
            var want = new List<RoleFaction>();
            for (int i = 0; i < _maxImp; i++) want.Add(RoleFaction.Impostor);
            for (int i = 0; i < _maxNk; i++) want.Add(RoleFaction.NeutralKilling);
            for (int i = 0; i < _maxNp; i++) want.Add(RoleFaction.Neutral);
            var pos = Enumerable.Range(0, n).OrderBy(_ => _rng.NextInt(100000)).ToList();
            for (int i = 0; i < want.Count && i < n; i++) buckets[pos[i]] = want[i];

            var states = new List<PlayerDraftState>();
            for (int i = 0; i < n; i++)
                states.Add(new PlayerDraftState { PlayerId = (byte)i, SlotNumber = i + 1, GuaranteedFaction = buckets[i] });

            var res = new DraftSim.Result { SlotFaction = new RoleFaction[n] };
            int ti = 0;
            while (ti < n)
            {
                var active = new List<PlayerDraftState>();
                for (int k = 0; k < concurrent && ti + k < n; k++) active.Add(states[ti + k]);
                var reserved = new HashSet<ushort>();
                var allow = BuildAllow(active);
                foreach (var s in active)
                {
                    s.OfferedRoleIds = BuildOffer(s, n, reserved, allow);
                    res.OffersBuilt++;
                    if (DraftSim.HasDupAlignment(s.OfferedRoleIds, id => _pool.Alignments.TryGetValue(id, out var a) ? a : "")) res.OffersWithDupAlignment++;
                }

                var chosen = new HashSet<ushort>();
                foreach (var s in active)
                {
                    ushort pick;
                    bool afk = _rng.NextDouble() < afkProb;
                    if (afk) pick = PickFRForState(s, chosen, reserved);
                    else
                    {
                        var offer = s.OfferedRoleIds;
                        var ev = new List<int>(); var cr = new List<int>();
                        for (int i = 0; i < offer.Count; i++) { if (Fac(offer[i]) == RoleFaction.Crewmate) cr.Add(i); else ev.Add(i); }
                        if (ev.Count > 0 && _rng.NextDouble() < evilProb) pick = offer[ev[_rng.NextInt(ev.Count)]];
                        else if (cr.Count > 0) pick = offer[cr[_rng.NextInt(cr.Count)]];
                        else pick = offer[_rng.NextInt(offer.Count)];
                    }
                    if (Unique(pick) && chosen.Contains(pick)) pick = PickFullRandom(chosen);
                    if (!RoleAvail(pick)) pick = PickFullRandom(chosen);
                    if (Unique(pick)) chosen.Add(pick);
                    var f = Commit(pick);
                    res.ChosenRoles.Add(pick);
                    res.SlotFaction[s.SlotNumber - 1] = f;
                    if (f == RoleFaction.Impostor) res.Impostors++;
                    else if (f == RoleFaction.NeutralKilling) res.NeutralKillings++;
                    else if (f == RoleFaction.Neutral) res.Neutrals++;
                    else res.Crew++;
                }
                ti += active.Count;
            }
            return res;
        }
    }
}
