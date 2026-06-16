using AmongUs.GameOptions;
using Reactor.Utilities.Attributes;
using TownOfUs.Networking;
using DraftModeTOUM;
using DraftModeTOUM.Patches;
using MiraAPI.Utilities;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Reactor.Utilities;
using UnityEngine;
using TownOfUs.Utilities;
using TownOfUs.Assets;


namespace DraftModeTOUM.Managers
{
    public static class DraftManager
    {
        public static bool  IsDraftActive   { get; private set; }
        public static int   CurrentTurn     { get; private set; }
        public static float TurnTimeLeft    { get; private set; }
        public static float TurnDuration    { get; set; } = 10f;

        public static bool ShowRecap             { get; set; } = true;
        public static bool AutoStartAfterDraft   { get; set; } = true;
        public static bool LockLobbyOnDraftStart { get; set; } = true;
        public static bool UseRoleChances        { get; set; } = true;
        public static bool UseRoleListForPool    { get; set; } = false;
        public static int  OfferedRolesCount     { get; set; } = 3;
        public static bool ShowRandomOption      { get; set; } = true;
        public static int  ConcurrentPickCount   { get; set; } = 1;
        public static int  RerollsPerPlayer      { get; set; } = 1;

        public static int MaxImpostors       { get; set; } = 2;
        public static int MaxNeutralKillings { get; set; } = 2;
        public static int MaxNeutralPassives { get; set; } = 3;

        internal static bool SkipCountdown { get; private set; } = false;

        public static List<int>                           TurnOrder      { get; private set; } = new();
        private static Dictionary<int, PlayerDraftState> _slotMap       = new();
        private static Dictionary<byte, int>             _pidToSlot     = new();
        private static DraftRolePool                     _pool          = new();
        private static DraftDistribution                 _engine;

        private static int _turnIndex = 0;
        private static List<int> _activeSlots = new();
        private static readonly HashSet<ushort> _roundOfferReserved = new();
        private static readonly HashSet<ushort> _roundChosenRoles   = new();
        private static readonly HashSet<byte>   _roundReadyPickers  = new();
        private static bool _suppressAdvance = false;
        private static FactionBudget _roundBudget;

        public static readonly Dictionary<byte, RoleTypes> PendingRoleAssignments = new();
        private static readonly HashSet<byte>              _appliedPlayers        = new();

        private static string  _forcedRoleName     = null;
        private static ushort? _forcedRoleId       = null;
        private static byte    _forcedRoleTargetId  = 255;
        private static readonly HashSet<byte> _forcedRolePlayers = new();

        private static bool _endSequenceRunning = false;

        public static void SetForcedDraftRole(string roleName, byte targetPlayerId)
        {
            _forcedRoleName     = roleName;
            _forcedRoleId       = null;
            _forcedRoleTargetId = targetPlayerId;
            _forcedRolePlayers.Add(targetPlayerId);

            var player = PlayerControl.AllPlayerControls.ToArray()
                .FirstOrDefault(p => p.PlayerId == targetPlayerId);
            if (player != null)
            {
                UpCommandRequests.SetRequest(player.Data.PlayerName, roleName);
            }

            if (IsDraftActive)
            {
                ResolveForcedRoleId();
            }
        }

        public static int GetSlotForPlayer(byte playerId) =>
            _pidToSlot.TryGetValue(playerId, out int slot) ? slot : -1;
        public static PlayerDraftState GetStateForSlot(int slot) =>
            _slotMap.TryGetValue(slot, out var s) ? s : null;
        public static PlayerDraftState GetStateForPlayer(byte playerId)
        {
            int slot = GetSlotForPlayer(playerId);
            return slot >= 0 ? GetStateForSlot(slot) : null;
        }

        public static PlayerDraftState GetCurrentPickerState()
        {
            if (!IsDraftActive) return null;
            if (_activeSlots.Count > 0)
                return GetStateForSlot(_activeSlots[0]);
            if (CurrentTurn < 1 || CurrentTurn > TurnOrder.Count) return null;
            return GetStateForSlot(TurnOrder[CurrentTurn - 1]);
        }

        public static List<PlayerDraftState> GetActivePickerStates()
        {
            var list = new List<PlayerDraftState>();

            if (_activeSlots == null || _activeSlots.Count == 0)
            {
                foreach (var s in _slotMap.Values)
                {
                    if (s != null && s.IsPickingNow && !s.HasPicked)
                        list.Add(s);
                }
                return list;
            }

            foreach (var slot in _activeSlots)
            {
                var s = GetStateForSlot(slot);
                if (s != null) list.Add(s);
            }
            return list;
        }

        public static void SetClientTurn(int turnNumber, int currentPickerSlot)
        {
            if (AmongUsClient.Instance.AmHost) return;
            bool newRound = turnNumber != CurrentTurn;
            if (newRound)
            {
                CurrentTurn  = turnNumber;
                TurnTimeLeft = TurnDuration;
                int turnStartIndex = Math.Max(0, turnNumber - 1);
                foreach (var state in _slotMap.Values)
                {
                    int idx = TurnOrder.IndexOf(state.SlotNumber);
                    if (idx >= 0 && idx < turnStartIndex)
                    {
                        state.HasPicked    = true;
                        state.IsPickingNow = false;
                    }
                    else if (!state.HasPicked)
                    {
                        state.IsPickingNow = false;
                    }
                }
            }

            var current = GetStateForSlot(currentPickerSlot);
            if (current != null && !current.HasPicked)
                current.IsPickingNow = true;
        }

        public static void SetDraftStateFromHost(int totalSlots, List<byte> playerIds, List<int> slotNumbers)
        {
            ApplyLocalSettings();
            _slotMap.Clear();
            _pidToSlot.Clear();
            TurnOrder.Clear();

            IsDraftActive = true;
            for (int i = 0; i < playerIds.Count; i++)
            {
                var state = new PlayerDraftState { PlayerId = playerIds[i], SlotNumber = slotNumbers[i], RerollsRemaining = RerollsPerPlayer };
                _slotMap[slotNumbers[i]]  = state;
                _pidToSlot[playerIds[i]] = slotNumbers[i];
            }
            TurnOrder    = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn  = 1;
            TurnTimeLeft = TurnDuration;
            _turnIndex   = 0;
            _activeSlots.Clear();
            _roundOfferReserved.Clear();
            _roundChosenRoles.Clear();
            _roundReadyPickers.Clear();
            DraftStatusOverlay.SetState(OverlayState.Waiting);
        }

        public static void StartDraft()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined) return;

            DraftTicker.EnsureExists();

            string  savedForcedName   = _forcedRoleName;
            byte    savedForcedTarget = _forcedRoleTargetId;

            Reset(cancelledBeforeCompletion: true);
            ApplyLocalSettings();

            if (!string.IsNullOrWhiteSpace(savedForcedName) && savedForcedTarget != 255)
            {
                _forcedRoleName     = savedForcedName;
                _forcedRoleTargetId = savedForcedTarget;
                _forcedRoleId       = null;
            }

            var players = PlayerControl.AllPlayerControls.ToArray()
                .Where(p => p != null && !p.Data.Disconnected).ToList();

            // ── Pool selection ────────────────────────────────────────────────
            if (UseRoleListForPool)
            {
                bool roleListActive = false;
                try
                {
                    var roleOptions = MiraAPI.GameOptions.OptionGroupSingleton<TownOfUs.Options.RoleOptions>.Instance;
                    roleListActive = roleOptions != null && roleOptions.RoleListEnabled;
                }
                catch { }

                if (roleListActive)
                {
                    _pool = RoleListPoolBuilder.BuildFromRoleList(players.Count);
                    DraftModePlugin.Logger.LogInfo(
                        $"[DraftManager] Using Role List pool ({_pool.RoleIds.Count} roles) for {players.Count} players.");
                }
                else
                {
                    DraftModePlugin.Logger.LogWarning(
                        "[DraftManager] UseRoleListForPool is ON but Role List is not active in game options — falling back to standard pool.");
                    _pool = RolePoolBuilder.BuildPool();
                }
            }
            else
            {
                _pool = RolePoolBuilder.BuildPool();
            }

            if (_pool.RoleIds.Count == 0) return;
            // ─────────────────────────────────────────────────────────────────

            _engine = new DraftDistribution(_pool, BuildConfig(), new UnityRng());

            int totalSlots    = players.Count;
            var shuffledSlots = Enumerable.Range(1, totalSlots).OrderBy(_ => UnityEngine.Random.value).ToList();

            List<byte> syncPids  = new();
            List<int>  syncSlots = new();

            for (int i = 0; i < totalSlots; i++)
            {
                int  slot = shuffledSlots[i];
                byte pid  = players[i].PlayerId;
                _slotMap[slot]  = new PlayerDraftState { PlayerId = pid, SlotNumber = slot, RerollsRemaining = RerollsPerPlayer };
                _pidToSlot[pid] = slot;
                syncPids.Add(pid);
                syncSlots.Add(slot);
            }

            TurnOrder     = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn   = 1;
            TurnTimeLeft  = TurnDuration;
            _turnIndex    = 0;
            _activeSlots.Clear();
            _roundOfferReserved.Clear();
            _roundChosenRoles.Clear();
            _roundReadyPickers.Clear();
            IsDraftActive = true;

            AssignFactionBuckets();
            ResolveForcedRoleId();

            DraftNetworkHelper.BroadcastDraftStart(totalSlots, syncPids, syncSlots);
            DraftNetworkHelper.BroadcastSlotNotifications(_pidToSlot);
            DraftNetworkHelper.BroadcastCreateNotif("<color=#FF0000>Draft Mode</color> has Started. Be Ready to Pick Your Role!");

            DraftStatusOverlay.SetState(OverlayState.Waiting);

            var localSettings = MiraAPI.LocalSettings.LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
            if (localSettings.AudioCueTiming.Value == AudioTiming.DraftStart)
            {
                DraftAudio.PlayDraftStartCue();
            }

            StartRound();
        }

        private static void ResolveForcedRoleId()
        {
            if (string.IsNullOrWhiteSpace(_forcedRoleName)) return;
            _forcedRoleId = null;

            foreach (var id in _pool.RoleIds)
            {
                var role = RoleManager.Instance?.GetRole((RoleTypes)id);
                if (role == null) continue;
                if (string.Equals(role.NiceName, _forcedRoleName, StringComparison.OrdinalIgnoreCase))
                {
                    _forcedRoleId = id;
                    return;
                }
            }

            foreach (RoleTypes rt in System.Enum.GetValues(typeof(RoleTypes)))
            {
                var role = RoleManager.Instance?.GetRole(rt);
                if (role != null && string.Equals(role.NiceName, _forcedRoleName, StringComparison.OrdinalIgnoreCase))
                {
                    _forcedRoleId = (ushort)rt;
                    return;
                }
            }
        }

        private static void AssignFactionBuckets()
        {
            int playerCount = TurnOrder.Count;

            int impSlots = Mathf.Min(MaxImpostors,       playerCount);
            int nkSlots  = Mathf.Min(MaxNeutralKillings, playerCount);
            int npSlots  = Mathf.Min(MaxNeutralPassives, playerCount);

            int poolImp = _pool.RoleIds.Count(id => GetFaction(id) == RoleFaction.Impostor);
            int poolNK  = _pool.RoleIds.Count(id => GetFaction(id) == RoleFaction.NeutralKilling);
            int poolNP  = _pool.RoleIds.Count(id => GetFaction(id) == RoleFaction.Neutral);

            impSlots = Mathf.Min(impSlots, poolImp);
            nkSlots  = Mathf.Min(nkSlots,  poolNK);
            npSlots  = Mathf.Min(npSlots,  poolNP);

            int nonCrewTotal = impSlots + nkSlots + npSlots;
            if (nonCrewTotal > playerCount)
            {
                float scale = (float)playerCount / nonCrewTotal;
                impSlots = Mathf.FloorToInt(impSlots * scale);
                nkSlots  = Mathf.FloorToInt(nkSlots  * scale);
                npSlots  = Mathf.FloorToInt(npSlots  * scale);
            }

            var buckets = new List<RoleFaction?>();
            for (int i = 0; i < impSlots; i++) buckets.Add(RoleFaction.Impostor);
            for (int i = 0; i < nkSlots;  i++) buckets.Add(RoleFaction.NeutralKilling);
            for (int i = 0; i < npSlots;  i++) buckets.Add(RoleFaction.Neutral);
            while (buckets.Count < playerCount) buckets.Add(null);

            int nonCrew = impSlots + nkSlots + npSlots;
            if (nonCrew > 0 && playerCount >= 3)
            {
                var nonCrewBuckets = buckets.Where(b => b.HasValue)
                                            .OrderBy(_ => UnityEngine.Random.value)
                                            .ToList();

                var positions = Enumerable.Range(0, playerCount)
                                          .OrderBy(_ => UnityEngine.Random.value)
                                          .ToList();

                int third = playerCount / 3;
                var earlyPos = positions.Take(third).OrderBy(_ => UnityEngine.Random.value).ToList();
                var midPos   = positions.Skip(third).Take(third).OrderBy(_ => UnityEngine.Random.value).ToList();
                var latePos  = positions.Skip(third * 2).OrderBy(_ => UnityEngine.Random.value).ToList();

                var assignedBuckets = new RoleFaction?[playerCount];

                int perThird = nonCrew / 3;
                int extra    = nonCrew % 3;

                var thirds = new List<List<int>> { earlyPos, midPos, latePos };
                int bucketIdx = 0;
                for (int t = 0; t < 3 && bucketIdx < nonCrewBuckets.Count; t++)
                {
                    int count = perThird + (t < extra ? 1 : 0);
                    foreach (var pos in thirds[t].Take(count))
                    {
                        if (bucketIdx >= nonCrewBuckets.Count) break;
                        assignedBuckets[pos] = nonCrewBuckets[bucketIdx++];
                    }
                }

                for (int i = 0; i < playerCount; i++)
                    if (!assignedBuckets[i].HasValue && bucketIdx >= nonCrewBuckets.Count)
                        assignedBuckets[i] = null;

                for (int i = 0; i < TurnOrder.Count; i++)
                {
                    var state = GetStateForSlot(TurnOrder[i]);
                    if (state != null) state.GuaranteedFaction = assignedBuckets[i];
                }
            }
            else
            {
                buckets = buckets.OrderBy(_ => UnityEngine.Random.value).ToList();
                for (int i = 0; i < TurnOrder.Count; i++)
                {
                    var state = GetStateForSlot(TurnOrder[i]);
                    if (state != null) state.GuaranteedFaction = buckets[i];
                }
            }

            LoggingSystem.Debug(
                $"[DraftManager] Buckets assigned: {impSlots} Imp, {nkSlots} NK, " +
                $"{npSlots} NP, {playerCount - impSlots - nkSlots - npSlots} Crew");
        }

        public static void Reset(bool cancelledBeforeCompletion = true)
        {
            IsDraftActive    = false;
            TurnTimerRunning = false;
            _endSequenceRunning = false;
            CurrentTurn      = 0;
            TurnTimeLeft     = 0f;
            DraftUiManager.CloseAll();

            if (cancelledBeforeCompletion)
            {
                PendingRoleAssignments.Clear();
                _appliedPlayers.Clear();
                DraftRecapOverlay.Hide();
                DraftStatusOverlay.SetState(OverlayState.Hidden);
            }

            _slotMap.Clear();
            _pidToSlot.Clear();
            _pool = new DraftRolePool();
            _engine = null;
            _roundBudget = null;
            TurnOrder.Clear();
            _turnIndex = 0;
            _activeSlots.Clear();
            _roundOfferReserved.Clear();
            _roundChosenRoles.Clear();
            _roundReadyPickers.Clear();

            _forcedRoleName     = null;
            _forcedRoleId       = null;
            _forcedRoleTargetId = 255;
            _forcedRolePlayers.Clear();

            if (cancelledBeforeCompletion)
                UpCommandRequests.Clear();
        }

        public static bool TurnTimerRunning { get; private set; } = false;

        public static void StartTurnTimer()
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            TurnTimerRunning = true;
        }

        public static void NotifyPickerReady(byte playerId)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            var state = GetStateForPlayer(playerId);
            if (state == null || state.HasPicked) return;
            if (!_activeSlots.Contains(state.SlotNumber)) return;

            _roundReadyPickers.Add(playerId);
            int needed = 0;
            foreach (var slot in _activeSlots)
            {
                var s = GetStateForSlot(slot);
                if (s != null && !s.HasPicked && !s.IsDisconnected) needed++;
            }

            if (needed > 0 && _roundReadyPickers.Count >= needed)
                TurnTimerRunning = true;
        }

        public static void HandlePlayerDisconnected(byte playerId)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            var state = GetStateForPlayer(playerId);
            if (state == null || state.HasPicked) return;

            if (_activeSlots.Contains(state.SlotNumber))
            {
                DraftModePlugin.Logger.LogInfo($"[DraftManager] DC'd player was active picker — auto-picking");
                AutoPickForState(state);
                return;
            }

            DraftModePlugin.Logger.LogInfo($"[DraftManager] Marking DC'd player slot {state.SlotNumber} for auto-skip");
            state.IsDisconnected = true;
        }

        public static void Tick(float deltaTime)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost || !TurnTimerRunning) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f)
            {
                TurnTimerRunning = false;
                AutoPickRandom();
            }
        }

        private static void StartRound()
        {
            if (!IsDraftActive) return;

            if (_turnIndex >= TurnOrder.Count)
            {
                FinishDraft();
                return;
            }

            _roundOfferReserved.Clear();
            _roundChosenRoles.Clear();
            _roundReadyPickers.Clear();
            _activeSlots = GetNextActiveSlots();

            if (_forcedRoleId.HasValue && _forcedRoleTargetId != 255)
            {
                _roundOfferReserved.Add(_forcedRoleId.Value);
                _roundChosenRoles.Add(_forcedRoleId.Value);
            }

            _roundBudget = _engine.CreateRoundBudget();

            if (_activeSlots.Count == 0)
            {
                FinishDraft();
                return;
            }

            foreach (var s in _slotMap.Values)
                if (!s.HasPicked) s.IsPickingNow = false;

            CurrentTurn      = _turnIndex + 1;
            TurnTimeLeft     = TurnDuration;
            TurnTimerRunning = false;

            // Just-in-time floor locks: constrain trailing offers to a faction when the players left can no
            // longer satisfy the configured floors otherwise. Computed up front so DC auto-picks honor it too.
            int playersRemaining = 0;
            for (int i = _turnIndex; i < TurnOrder.Count; i++)
            {
                var s = GetStateForSlot(TurnOrder[i]);
                if (s != null && !s.HasPicked) playersRemaining++;
            }
            var activeStates = new List<PlayerDraftState>();
            foreach (var slot in _activeSlots)
            {
                var s = GetStateForSlot(slot);
                if (s != null) activeStates.Add(s);
            }
            var locks = _engine.AssignFloorLocks(playersRemaining, activeStates.Count, TurnOrder.Count);
            var lockOf = new Dictionary<PlayerDraftState, RoleFaction?>();
            for (int i = 0; i < activeStates.Count; i++)
            {
                lockOf[activeStates[i]] = locks[i];
                activeStates[i].FloorLock = locks[i];
                activeStates[i].LockShare = int.MaxValue;
                if (locks[i].HasValue) activeStates[i].GuaranteedFaction = locks[i];
            }

            foreach (var grp in activeStates.Where(s => s.FloorLock.HasValue).GroupBy(s => s.FloorLock.Value))
            {
                var peers = grp.ToList();
                if (peers.Count <= 1) continue;
                int avail = _engine.GetAvailableForFaction(grp.Key).Count(id => !_roundOfferReserved.Contains(id));
                int share = Math.Max(1, (avail + peers.Count - 1) / peers.Count);
                foreach (var s in peers) s.LockShare = share;
            }

            var pending = new List<PlayerDraftState>();
            _suppressAdvance = true;
            foreach (var state in activeStates)
            {
                state.IsPickingNow = true;

                if (state.IsDisconnected)
                {
                    DraftModePlugin.Logger.LogInfo($"[DraftManager] Skipping DC'd player slot {state.SlotNumber}");
                    AutoPickForState(state);
                }
                else
                {
                    pending.Add(state);
                }
            }
            _suppressAdvance = false;

            foreach (var state in pending)
            {
                DraftModePlugin.Logger.LogInfo(
                    $"[DraftManager] Offering roles for slot {state.SlotNumber} (pid {state.PlayerId})");
                var offered = BuildOfferForState(state, _roundOfferReserved,
                    lockOf.TryGetValue(state, out var lf) ? lf : null);
                state.OfferedRoleIds = offered;
                DraftNetworkHelper.SendTurnAnnouncement(state.SlotNumber, state.PlayerId, offered, CurrentTurn, state.RerollsRemaining);
            }

            DraftUiManager.RefreshTurnList();
            AdvanceIfRoundComplete();
        }

        private static List<int> GetNextActiveSlots()
        {
            var result = new List<int>();
            int idx = _turnIndex;

            while (idx < TurnOrder.Count)
            {
                int slot = TurnOrder[idx];
                var state = GetStateForSlot(slot);
                if (state == null) { idx++; continue; }

                if (_forcedRoleId.HasValue && _forcedRoleTargetId != 255 &&
                    state.PlayerId == _forcedRoleTargetId)
                {
                    state.HasPicked    = true;
                    state.IsPickingNow = false;
                    idx++;
                    continue;
                }

                if (!state.HasPicked)
                {
                    result.Add(slot);
                    if (result.Count >= ConcurrentPickCount)
                        break;
                }
                idx++;
            }
            return result;
        }

        private static List<ushort> BuildOfferForState(PlayerDraftState state, HashSet<ushort> reserved,
                                                       RoleFaction? lockFaction = null, HashSet<ushort> extraAvoid = null)
        {
            if (_forcedRoleId.HasValue && state != null && state.PlayerId == _forcedRoleTargetId)
                return new List<ushort>();

            HashSet<ushort> extraExclude = null;
            if (_forcedRoleId.HasValue && state != null && state.PlayerId != _forcedRoleTargetId)
                extraExclude = new HashSet<ushort> { _forcedRoleId.Value };
            if (extraAvoid != null && extraAvoid.Count > 0)
            {
                extraExclude ??= new HashSet<ushort>();
                foreach (var id in extraAvoid) extraExclude.Add(id);
            }

            int slotIndex  = state != null ? Math.Max(0, TurnOrder.IndexOf(state.SlotNumber)) : 0;
            int totalSlots = Math.Max(1, TurnOrder.Count);

            return _engine.BuildOffer(state, slotIndex, totalSlots, reserved, extraExclude, _roundBudget, lockFaction,
                state?.LockShare ?? int.MaxValue);
        }

        private static IEnumerator CoAutoPickForced(byte playerId, int cardIndex)
        {
            yield return new WaitForSeconds(1.5f);
            if (!IsDraftActive) yield break;
            DraftModePlugin.Logger.LogInfo($"[DraftManager] Auto-submitting forced pick at index {cardIndex}");
            LoggingSystem.Debug($"[DraftManager] Auto-submitting forced pick at index {cardIndex}");
            SubmitPick(playerId, cardIndex);
        }

        public static bool SubmitPick(byte playerId, int choiceIndex)
        {
            if (!AmongUsClient.Instance.AmHost || !IsDraftActive) return false;
            var state = GetStateForPlayer(playerId);
            if (state == null || state.HasPicked) return false;
            if (!_activeSlots.Contains(state.SlotNumber)) return false;

            ushort chosenId = (choiceIndex >= state.OfferedRoleIds.Count)
                ? _engine.PickFullRandomForState(state, _roundChosenRoles, _roundOfferReserved)
                : state.OfferedRoleIds[choiceIndex];

            FinalisePickForState(state, chosenId);
            return true;
        }

        public static bool RequestReroll(byte playerId)
        {
            if (!AmongUsClient.Instance.AmHost || !IsDraftActive) return false;
            var state = GetStateForPlayer(playerId);
            if (state == null || state.HasPicked) return false;
            if (!_activeSlots.Contains(state.SlotNumber)) return false;
            if (state.RerollsRemaining <= 0) return false;

            state.RerollsRemaining--;

            // Return this player's current cards to the pool so the fresh offer can draw on them again
            // (essential when a floor lock leaves only a few cards of the locked faction).
            var prev = new HashSet<ushort>();
            if (state.OfferedRoleIds != null)
                foreach (var id in state.OfferedRoleIds)
                    if (_engine.IsUniqueRole(id)) { _roundOfferReserved.Remove(id); prev.Add(id); }

            HashSet<ushort> avoid = prev;
            if (state.FloorLock.HasValue)
            {
                int freshLockCards = _engine.GetAvailableForFaction(state.FloorLock.Value)
                    .Count(id => !prev.Contains(id) && !_roundOfferReserved.Contains(id));
                if (freshLockCards <= 0) avoid = null;
            }

            var offered = BuildOfferForState(state, _roundOfferReserved, state.FloorLock, avoid);
            state.OfferedRoleIds = offered;
            DraftNetworkHelper.SendTurnAnnouncement(state.SlotNumber, state.PlayerId, offered, CurrentTurn, state.RerollsRemaining);
            return true;
        }

        private static void AutoPickRandom()
        {
            if (!IsDraftActive) return;

            var pending = _activeSlots.Select(GetStateForSlot)
                                     .Where(s => s != null && !s.HasPicked)
                                     .ToList();
            _suppressAdvance = true;
            foreach (var state in pending)
            {
                ushort pick;
                if (!ShowRandomOption && state.OfferedRoleIds.Count > 0)
                    pick = state.OfferedRoleIds[UnityEngine.Random.Range(0, state.OfferedRoleIds.Count)];
                else
                    pick = _engine.PickFullRandomForState(state, _roundChosenRoles, _roundOfferReserved);
                FinalisePickForState(state, pick);
            }
            _suppressAdvance = false;
            AdvanceIfRoundComplete();
        }

        private static void AutoPickForState(PlayerDraftState state)
        {
            var pick = _engine.PickFullRandomForState(state, _roundChosenRoles, _roundOfferReserved);
            FinalisePickForState(state, pick);
        }

        private static void FinalisePickForState(PlayerDraftState state, ushort roleId)
        {
            if (!IsDraftActive || state == null) return;
            bool isForced = _forcedRolePlayers.Contains(state.PlayerId);

            if (isForced && _forcedRoleId.HasValue && state.PlayerId == _forcedRoleTargetId)
            {
                if (roleId == _forcedRoleId.Value)
                {
                    roleId = _forcedRoleId.Value;
                }
                _forcedRoleName     = null;
                _forcedRoleId       = null;
                _forcedRoleTargetId = 255;
            }

            if (_engine.IsUniqueRole(roleId) && _roundChosenRoles.Contains(roleId))
            {
                roleId = _engine.PickFullRandom(_roundChosenRoles);
            }
            if (!isForced && !_engine.IsRoleAvailable(roleId))
            {
                roleId = _engine.PickFullRandom(_roundChosenRoles);
            }

            if (_engine.IsUniqueRole(roleId))
                _roundChosenRoles.Add(roleId);

            _forcedRolePlayers.Remove(state.PlayerId);

            state.ChosenRoleId = roleId;
            state.HasPicked    = true;
            state.IsPickingNow = false;

            DraftNetworkHelper.BroadcastPickConfirmed(state.SlotNumber, roleId);

            var faction = _engine.Commit(roleId);

            LoggingSystem.Debug(
                $"[DraftManager] Slot {state.SlotNumber} picked {(RoleTypes)roleId} ({faction}). " +
                $"Caps: Imp={_engine.ImpostorsDrafted}/{MaxImpostors}, " +
                $"NK={_engine.NeutralKillingsDrafted}/{MaxNeutralKillings}, " +
                $"NP={_engine.NeutralPassivesDrafted}/{MaxNeutralPassives}");

            DraftUiManager.RefreshTurnList();
            if (!_suppressAdvance)
                AdvanceIfRoundComplete();
        }

        private static void AdvanceIfRoundComplete()
        {
            if (_activeSlots.Count == 0) return;
            foreach (var slot in _activeSlots)
            {
                var s = GetStateForSlot(slot);
                if (s != null && !s.HasPicked) return;
            }

            _turnIndex += _activeSlots.Count;
            while (_turnIndex < TurnOrder.Count)
            {
                var s = GetStateForSlot(TurnOrder[_turnIndex]);
                if (s == null || s.HasPicked) { _turnIndex++; continue; }
                break;
            }

            if (_turnIndex >= TurnOrder.Count)
                FinishDraft();
            else
            {
                TurnTimeLeft = TurnDuration;
                StartRound();
            }
        }

        private static void FinishDraft()
        {
            ApplyAllRoles();
            IsDraftActive    = false;
            TurnTimerRunning = false;
            DraftUiManager.CloseAll();
            DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);
            var recapEntries = BuildRecapEntries();
            DraftNetworkHelper.BroadcastRecap(recapEntries, ShowRecap);
            Reset(cancelledBeforeCompletion: false);
            TriggerEndDraftSequence();
        }

        public static List<RecapEntry> BuildRecapEntries()
        {
            var entries = new List<RecapEntry>();
            foreach (var slot in TurnOrder)
            {
                var s = GetStateForSlot(slot);
                if (s == null) continue;
                string roleName = "?";
                if (s.ChosenRoleId.HasValue)
                {
                    var role = RoleManager.Instance?.GetRole((RoleTypes)s.ChosenRoleId.Value);
                    roleName = role?.NiceName ?? s.ChosenRoleId.Value.ToString();
                }
                entries.Add(new RecapEntry(s.SlotNumber, roleName));
            }
            return entries;
        }

        private static void ApplyAllRoles()
        {
            PendingRoleAssignments.Clear();
            _appliedPlayers.Clear();

            if (_forcedRoleId.HasValue && _forcedRoleTargetId != 255)
            {
                PendingRoleAssignments[_forcedRoleTargetId] = (RoleTypes)_forcedRoleId.Value;
            }

            foreach (var state in _slotMap.Values)
            {
                if (!state.ChosenRoleId.HasValue) continue;
                if (state.PlayerId >= 200) continue;
                if (_forcedRoleId.HasValue && state.PlayerId == _forcedRoleTargetId) continue;
                PendingRoleAssignments[state.PlayerId] = (RoleTypes)state.ChosenRoleId.Value;
                LoggingSystem.Debug(
                    $"[DraftManager] Queued {(RoleTypes)state.ChosenRoleId.Value} for player {state.PlayerId}");
            }

            LoggingSystem.Debug(
                $"[DraftManager] {PendingRoleAssignments.Count} roles queued for game start");
        }

        public static bool ApplyPendingRolesOnGameStart()
        {
            if (!AmongUsClient.Instance.AmHost) return true;
            if (PendingRoleAssignments.Count == 0) return true;

            LoggingSystem.Debug(
                $"[DraftManager] Attempting to apply " +
                $"{PendingRoleAssignments.Count - _appliedPlayers.Count} remaining roles...");

            foreach (var kvp in PendingRoleAssignments)
            {
                if (_appliedPlayers.Contains(kvp.Key)) continue;

                var p = PlayerControl.AllPlayerControls.ToArray()
                    .FirstOrDefault(x => x.PlayerId == kvp.Key);
                if (p == null)
                {
                    LoggingSystem.Warning($"[DraftManager] Player {kvp.Key} not found yet — will retry");
                    continue;
                }

                try
                {
                    p.RpcSetRole(kvp.Value, false);
                    _appliedPlayers.Add(kvp.Key);
                    LoggingSystem.Debug(
                        $"[DraftManager] Applied {kvp.Value} to {p.Data.PlayerName} (id {kvp.Key})");
                }
                catch (Exception ex)
                {
                    LoggingSystem.Warning(
                        $"[DraftManager] RpcSetRole failed for player {kvp.Key}: {ex.Message} — will retry");
                }
            }

            bool allDone = _appliedPlayers.Count >= PendingRoleAssignments.Count;
            if (allDone)
            {
                LoggingSystem.Debug("[DraftManager] All roles applied successfully.");
                PendingRoleAssignments.Clear();
                _appliedPlayers.Clear();
            }
            return allDone;
        }

        public static IEnumerator CoApplyRolesWithRetry()
        {
            if (!AmongUsClient.Instance.AmHost) yield break;
            if (PendingRoleAssignments.Count == 0) yield break;

            LoggingSystem.Debug("[DraftManager] Starting role application retry loop...");

            float elapsed  = 0f;
            float timeout  = 10f;
            float interval = 0.5f;

            while (elapsed < timeout)
            {
                yield return new WaitForSeconds(interval);
                elapsed += interval;
                if (PendingRoleAssignments.Count == 0) yield break;
                bool done = ApplyPendingRolesOnGameStart();
                if (done)
                {
                    LoggingSystem.Debug($"[DraftManager] Role retry loop finished after {elapsed:F1}s");
                    yield break;
                }
            }

            if (PendingRoleAssignments.Count > 0)
            {
                foreach (var kvp in PendingRoleAssignments)
                {
                    if (_appliedPlayers.Contains(kvp.Key)) continue;
                    var role = RoleManager.Instance?.GetRole(kvp.Value);
                    var p    = PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(x => x.PlayerId == kvp.Key);
                    if (role != null && p != null)
                    {
                        UpCommandRequests.SetRequest(p.Data.PlayerName, role.NiceName);
                    }
                }
                PendingRoleAssignments.Clear();
                _appliedPlayers.Clear();
            }
        }

        private static void ApplyLocalSettings()
        {
            var opts = MiraAPI.GameOptions.OptionGroupSingleton<DraftModeOptions>.Instance;
            TurnDuration          = Mathf.Clamp(opts.TurnDurationSeconds.Value, 5f, 60f);
            ShowRecap             = opts.ShowRecap;
            AutoStartAfterDraft   = opts.AutoStartAfterDraft;
            LockLobbyOnDraftStart = opts.LockLobbyOnDraftStart;
            UseRoleChances        = opts.UseRoleChances;
            UseRoleListForPool    = opts.UseRoleListForPool;   // NEW
            OfferedRolesCount     = Mathf.Clamp(Mathf.RoundToInt(opts.OfferedRolesCount.Value), 1, 9);
            ConcurrentPickCount   = Mathf.Clamp(Mathf.RoundToInt(opts.ConcurrentPicks.Value), 1, 2);
            RerollsPerPlayer      = Mathf.Clamp(Mathf.RoundToInt(opts.RerollsPerPlayer.Value), 0, 3);
            ShowRandomOption      = opts.ShowRandomOption;
            MaxImpostors          = Mathf.Clamp(Mathf.RoundToInt(opts.MaxImpostors.Value), 0, 10);
            MaxNeutralKillings    = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralKillings.Value), 0, 10);
            MaxNeutralPassives    = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralPassives.Value), 0, 10);
        }

        private static DraftConfig BuildConfig() => new DraftConfig
        {
            OfferedRolesCount  = OfferedRolesCount,
            MaxImpostors       = MaxImpostors,
            MaxNeutralKillings = MaxNeutralKillings,
            MaxNeutralPassives = MaxNeutralPassives,
            UseRoleChances     = UseRoleChances,
            CrewmateRoleId     = (ushort)RoleTypes.Crewmate,
            EvilOfferChance    = 0.5,
            SoftImpostorNudge  = true,
            OfferDiversity     = true,
            FloorSpreadBias    = 1.0,
            ImpostorSpreadPower = 1.5,
            PositionEdge       = 0.25,  // slight early-slot edge: earlier picks favoured, but early can still whiff to all-crew and late slots keep a real shot
        };

        private static RoleFaction GetFaction(ushort id)
        {
            if (_pool.Factions.TryGetValue(id, out var f)) return f;
            var role = RoleManager.Instance?.GetRole((RoleTypes)id);
            return role != null ? RoleCategory.GetFactionFromRole(role) : RoleFaction.Crewmate;
        }

        public static void TriggerEndDraftSequence()
        {
            if (_endSequenceRunning) return;
            _endSequenceRunning = true;
            Coroutines.Start(CoEndDraftSequence());
        }

        private static IEnumerator CoEndDraftSequence()
        {
            yield return new WaitForSeconds(ShowRecap ? 5.0f : 0.5f);

            if (!_endSequenceRunning) yield break;

            try { DraftRecapOverlay.Hide(); } catch { }

            if (!AutoStartAfterDraft)
            {
                _endSequenceRunning = false;
                try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
                yield break;
            }

            bool shouldAutoStart = AutoStartAfterDraft && AmongUsClient.Instance.AmHost;
            if (shouldAutoStart &&
                GameStartManager.Instance != null &&
                AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Joined)
            {
                SkipCountdown = true;
                int orig = GameStartManager.Instance.MinPlayers;
                GameStartManager.Instance.MinPlayers = 1;
                GameStartManager.Instance.BeginGame();
                GameStartManager.Instance.MinPlayers = orig;
                yield return null;
                SkipCountdown = false;
            }

            yield return new WaitForSeconds(0.6f);
            _endSequenceRunning = false;
            try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
        }
    }
}