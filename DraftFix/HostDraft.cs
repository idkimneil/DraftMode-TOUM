using System.Collections;
using System.Reflection;
using AmongUs.GameOptions;
using HarmonyLib;
using MiraAPI.GameOptions;
using MiraAPI.Utilities;
using Reactor.Utilities;
using TownOfUs.Modules.DraftMode;
using TownOfUs.Options;
using TownOfUs.Utilities;
using UnityEngine;
using Random = UnityEngine.Random;

namespace DraftFix;

internal static class HostDraft
{
    private const float SubmissionGrace = 1f;
    private const float ReadyResendDelay = 2.5f;
    private const float MinShuffleWindow = 5f;
    private const float DiversityPenalty = 0.3f;

    private static readonly Dictionary<int, DraftCopy> Picked = [];
    private static readonly Dictionary<int, List<ushort>> Offers = [];
    private static readonly Dictionary<int, HashSet<ushort>> Seen = [];
    private static readonly Dictionary<int, int> ShufflesUsed = [];
    private static readonly Dictionary<int, float> AnnouncedAt = [];
    private static readonly HashSet<int> Resent = [];
    private static readonly Dictionary<ushort, RoleBehaviour?> Roles = [];

    private static List<DraftCopy> _pool = [];
    private static DraftPlan _plan = null!;
    private static bool _running;
    private static int _session;
    private static int _turn;
    private static float _deadline;

    internal static Dictionary<byte, ushort> FinalPicks { get; } = [];

    private static RoleOptions Opts => OptionGroupSingleton<RoleOptions>.Instance;

    private static bool Active(int session) => _running && _session == session && DraftManager.IsDraftActive;

    private static RoleBehaviour? RoleOf(ushort id)
    {
        if (!Roles.TryGetValue(id, out var role))
        {
            Roles[id] = role = MiscUtils.GetRegisteredRole((RoleTypes)id);
        }

        return role;
    }

    internal static bool Start(Dictionary<byte, int> pidToSlot)
    {
        Stop();
        FinalPicks.Clear();

        var basePool = HostDraftPool.Build();
        if (basePool.Count == 0)
        {
            DraftHostFixPlugin.Logger.LogWarning("Role pool is empty, leaving draft start to the original engine");
            return false;
        }

        Roles.Clear();
        _plan = DraftPlan.Create(pidToSlot.Count, basePool);
        _pool = [.. basePool];
        _turn = 0;
        _running = true;

        DraftManager.SetDraftStateFromHost(pidToSlot.Count, pidToSlot.Keys.ToList(), pidToSlot.Values.ToList());
        DraftNetworkHelper.BroadcastSlotNotifications(pidToSlot.Count, pidToSlot);
        DraftCancelButton.Show();
        Coroutines.Start(Run(_session));
        return true;
    }

    internal static void Stop()
    {
        _session++;
        _running = false;
        Picked.Clear();
        Offers.Clear();
        Seen.Clear();
        ShufflesUsed.Clear();
        AnnouncedAt.Clear();
        Resent.Clear();
    }

    internal static void OnPick(byte playerId, byte index, ushort roleId)
    {
        if (!_running) return;

        var state = DraftManager.GetStateForPlayer(playerId);
        if (state is not { IsPickingNow: true, HasPicked: false } || !Offers.TryGetValue(state.SlotNumber, out var offer)) return;

        var slot = state.SlotNumber;
        if (roleId == 0 && index == offer.Count && Opts.ShowRandomOption.Value)
        {
            Commit(slot, Choose(Legal(slot, true), 1).FirstOrDefault() ?? Choose(Legal(slot, false), 1).FirstOrDefault(), false);
            return;
        }

        var copy = index < offer.Count && offer[index] == roleId ? _pool.FirstOrDefault(x => x.RoleId == roleId) : null;
        if (copy == null)
        {
            ClearPending(state);
            Announce(slot);
            return;
        }

        Commit(slot, copy, false);
    }

    internal static void RequestShuffle(byte playerId)
    {
        if (!_running) return;

        var state = DraftManager.GetStateForPlayer(playerId);
        if (state is not { IsPickingNow: true, HasPicked: false }) return;

        var slot = state.SlotNumber;
        if (ShufflesUsed.GetValueOrDefault(slot) >= (int)Opts.ShufflesPerPlayer.Value) return;

        ShufflesUsed[slot] = ShufflesUsed.GetValueOrDefault(slot) + 1;
        if (Opts.TurnDurationSeconds.Value <= MinShuffleWindow)
        {
            _deadline = Mathf.Max(_deadline, Time.time + MinShuffleWindow);
        }

        ClearPending(state);
        Offer(slot);
    }

    private static IEnumerator Run(int session)
    {
        var concurrency = Math.Clamp((int)Opts.ConcurrentPicks.Value, 1, 2);
        while (Active(session))
        {
            var pending = DraftManager.GetAllStates().Where(s => !s.HasPicked).Select(s => s.SlotNumber).OrderBy(x => x).ToList();
            if (pending.Count == 0) break;

            _turn++;
            var duration = Mathf.Max(1f, Opts.TurnDurationSeconds.Value);
            DraftManager.TurnDuration = duration;
            _deadline = Time.time + duration;

            var batch = pending.Take(concurrency).ToList();
            foreach (var slot in batch)
            {
                Seen[slot] = [];
                Offer(slot);
            }

            yield return Wait(batch, session);
            yield return new WaitForSeconds(0.5f);
        }

        if (Active(session))
        {
            Finish();
        }
    }

    private static IEnumerator Wait(List<int> batch, int session)
    {
        var graceUntil = 0f;
        while (Active(session) && batch.Any(IsOpen))
        {
            DraftManager.TurnTimeLeft = Mathf.Max(0f, _deadline - Time.time);
            foreach (var slot in batch.Where(IsOpen).ToList())
            {
                var state = DraftManager.GetStateForSlot(slot);
                if (Offers[slot].Count == 0 || DraftManager.IsPlayerDisconnected(state.PlayerId))
                {
                    AutoPick(slot);
                }
                else if (!state.IsPickerReady && Time.time - AnnouncedAt[slot] > ReadyResendDelay && Resent.Add(slot))
                {
                    Announce(slot);
                }
            }

            if (Time.time < _deadline)
            {
                graceUntil = 0f;
            }
            else if (graceUntil == 0f)
            {
                graceUntil = Time.time + SubmissionGrace;
            }
            else if (Time.time >= graceUntil)
            {
                foreach (var slot in batch.Where(IsOpen).ToList())
                {
                    AutoPick(slot);
                }
            }

            yield return null;
        }
    }

    private static bool IsOpen(int slot) => DraftManager.GetStateForSlot(slot) is { HasPicked: false };

    private static void ClearPending(DraftSlotState state)
    {
        state.PendingPickIndex = 255;
        state.PendingPickRoleId = 0;
        state.PendingPickTurnNumber = -1;
    }

    private static void Take(DraftCopy copy)
    {
        var index = _pool.FindIndex(x => x.RoleId == copy.RoleId);
        if (index >= 0)
        {
            _pool.RemoveAt(index);
        }

        _pool.RemoveAll(x => Clashes(copy.RoleId, x.RoleId));
    }

    private static bool Clashes(ushort a, ushort b)
    {
        return a != b && RoleOf(a) is { } roleA && RoleOf(b) is { } roleB && MiscUtils.AreRolesExclusive(roleA, roleB);
    }

    private static IEnumerable<DraftCopy> Without(params DraftCopy[] taken)
    {
        var skipped = new HashSet<ushort>();
        foreach (var x in _pool)
        {
            if (taken.Any(t => t.RoleId == x.RoleId && skipped.Add(t.RoleId)))
            {
                continue;
            }

            if (!taken.Any(t => Clashes(t.RoleId, x.RoleId)))
            {
                yield return x;
            }
        }
    }

    private static bool IsLegal(DraftCopy copy, List<DraftCopy> picks, List<ushort> concurrent)
    {
        if (!_plan.CanComplete([.. picks, copy], Without(copy)))
        {
            return false;
        }

        foreach (var id in concurrent.Where(id => id != copy.RoleId))
        {
            var other = _pool.FirstOrDefault(x => x.RoleId == id);
            if (other == null)
            {
                continue;
            }

            if (Clashes(copy.RoleId, id) || !_plan.CanComplete([.. picks, copy, other], Without(copy, other)))
            {
                return false;
            }
        }

        return true;
    }

    private static List<DraftCopy> Legal(int slot, bool respectReserved)
    {
        var concurrent = Offers.Where(o => o.Key != slot).SelectMany(o => o.Value).ToList();
        var picks = Picked.Values.ToList();
        return _pool.GroupBy(x => x.RoleId)
            .Where(g => !respectReserved || g.Count() > concurrent.Count(id => id == g.Key))
            .Select(g => g.First())
            .Where(c => IsLegal(c, picks, concurrent))
            .ToList();
    }

    private static List<DraftCopy> RandomCrewBucket(int slot, bool respectReserved)
    {
        return Legal(slot, respectReserved).Where(x => HostDraftPool.Crew.Contains(x.Alignment)).ToList();
    }

    private static List<DraftCopy> Choose(List<DraftCopy> candidates, int count)
    {
        var chosen = new List<DraftCopy>();
        while (chosen.Count < count)
        {
            var remaining = candidates.Where(c => chosen.All(x => x.RoleId != c.RoleId)).ToList();
            if (remaining.Count == 0) break;

            var weights = remaining
                .Select(c => c.Chance * Mathf.Pow(DiversityPenalty, chosen.Count(x => x.Alignment == c.Alignment)))
                .ToList();
            var roll = Random.value * weights.Sum();
            var index = 0;
            while (index < remaining.Count - 1 && (roll -= weights[index]) > 0f)
            {
                index++;
            }

            chosen.Add(remaining[index]);
        }

        return chosen;
    }

    private static void TopUp(List<DraftCopy> chosen, List<DraftCopy> source, int count)
    {
        if (chosen.Count >= count) return;

        var rest = source.Where(x => chosen.All(c => c.RoleId != x.RoleId)).ToList();
        chosen.AddRange(Choose(rest, count - chosen.Count));
    }

    private static void Offer(int slot)
    {
        var seen = Seen.TryGetValue(slot, out var known) ? known : Seen[slot] = [];
        var count = Math.Max(1, (int)Opts.OfferedRolesCount.Value);

        var preferred = Legal(slot, true);
        var chosen = Choose(preferred.Where(x => !seen.Contains(x.RoleId)).ToList(), count);
        if (chosen.Count < count)
        {
            var fallback = RandomCrewBucket(slot, true);
            TopUp(chosen, fallback.Where(x => !seen.Contains(x.RoleId)).ToList(), count);
            TopUp(chosen, preferred, count);
            TopUp(chosen, fallback, count);
            TopUp(chosen, RandomCrewBucket(slot, false), count);
        }

        if (chosen.Count < count)
        {
            DraftHostFixPlugin.Logger.LogWarning($"Only {chosen.Count}/{count} legal roles exist for slot {slot}");
        }

        seen.UnionWith(chosen.Select(x => x.RoleId));
        Offers[slot] = chosen.Select(x => x.RoleId).ToList();
        Announce(slot);
    }

    private static void Announce(int slot)
    {
        var state = DraftManager.GetStateForSlot(slot);
        var ids = Offers[slot];
        var names = ids.Select(id => DraftRolePool.GetRoleNameFromId(id) ?? string.Empty).ToList();

        state.IsPickerReady = false;
        AnnouncedAt[slot] = Time.time;
        DraftNetworkHelper.SendTurnAnnouncement(slot, state.PlayerId, ids, names, _turn);
    }

    private static void Revalidate()
    {
        foreach (var slot in Offers.Keys.ToList())
        {
            var legal = Legal(slot, false).Select(x => x.RoleId).ToHashSet();
            if (IsOpen(slot) && Offers[slot].Any(id => !legal.Contains(id)))
            {
                Offer(slot);
            }
        }
    }

    private static void AutoPick(int slot)
    {
        var offer = Offers.GetValueOrDefault(slot) ?? [];
        var roleId = offer.Count > 0 ? offer[Random.RandomRangeInt(0, offer.Count)] : (ushort)0;
        var copy = _pool.FirstOrDefault(x => offer.Count > 0 && x.RoleId == roleId);
        Commit(slot, copy ?? Choose(Legal(slot, true), 1).FirstOrDefault() ?? Choose(Legal(slot, false), 1).FirstOrDefault(), true);
    }

    private static void Commit(int slot, DraftCopy? copy, bool timedOut)
    {
        var state = DraftManager.GetStateForSlot(slot);
        if (state == null || state.HasPicked) return;

        var roleId = copy?.RoleId ?? (ushort)0;
        Offers.Remove(slot);
        ClearPending(state);
        state.IsPickingNow = false;

        if (copy != null)
        {
            Picked[slot] = copy;
            Take(copy);
        }

        FinalPicks[state.PlayerId] = roleId;
        DraftManager.ConfirmPick(slot, roleId);
        DraftNetworkHelper.BroadcastPickConfirmed(slot, roleId, timedOut);
        Revalidate();
    }

    private static void Finish()
    {
        _running = false;
        var mode = Opts.DraftRecap.Value;
        var entries = new List<RecapEntry>();
        foreach (var state in DraftManager.GetAllStates().OrderBy(s => s.SlotNumber))
        {
            var name = DraftRolePool.GetRoleNameFromId(state.ChosenRoleId) ?? "Unknown";
            var role = state.ChosenRoleId != 0 ? MiscUtils.GetRegisteredRole((RoleTypes)state.ChosenRoleId) : null;

            var label = "Unknown";
            var color = Color.white;
            if (role != null)
            {
                if (mode is DraftRecapMode.Faction or DraftRecapMode.Alignment)
                {
                    label = DraftUiManager.GetTeamLabel(role).ToUpperInvariant();
                    color = MiscUtils.GetRoleFactionColor(role, true);
                }
                else if (mode == DraftRecapMode.Role)
                {
                    label = role.GetRoleName()?.ToUpperInvariant() ?? "Unknown";
                    color = role.TeamColor;
                }
            }
            else if (mode == DraftRecapMode.Role)
            {
                label = name.ToUpperInvariant();
            }

            entries.Add(new RecapEntry(state.SlotNumber, name, label, ColorUtility.ToHtmlStringRGB(color)));
        }

        DraftApplier.StorePendingDraftStates(DraftManager.GetAllStates());
        DraftNetworkHelper.BroadcastRecap(entries, mode);

        var start = AccessTools.Method(typeof(DraftEngineBehaviour), "CoAutoStartGame");
        Coroutines.Start((IEnumerator)start.Invoke(null, [mode != DraftRecapMode.Nothing ? 6f : 0f])!);
    }
}
