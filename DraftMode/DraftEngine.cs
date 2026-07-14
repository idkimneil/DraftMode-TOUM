using UnityEngine;
using System.Collections;
using Il2CppInterop.Runtime.Attributes;
using Reactor.Utilities;
using Reactor.Utilities.Attributes;
using DraftMode.Options;
using MiraAPI.GameOptions;
using MiraAPI.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DraftMode
{
    [RegisterInIl2Cpp]
    public class DraftEngineBehaviour(IntPtr iPtr) : MonoBehaviour(iPtr)
    {
        public static DraftEngineBehaviour Instance { get; private set; }

        private List<string> _pool = new();
        private readonly List<int> _slotOrder = new();
        private readonly HashSet<int> _specialEligibleSlots = new();
        private int _totalNeutralGroupsInPool;
        private int _totalImpostorGroupsInPool;
        private int _currentTurnNumber;
        private int _totalSlots;
        private int _turnIndex;
        private bool _running;
        private readonly UnityRng _rng = new();
        private List<string> _currentOffers = new();
        private readonly Dictionary<int, List<string>> _currentOffersBySlot = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, "[DraftEngine] Initialized");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null!;
        }

        [HideFromIl2Cpp]
        public void StartHostDraft(int totalSlots, Dictionary<byte, int> pidToSlot)
        {
            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, "[DraftEngine] StartHostDraft called");

            if (!AmongUsClient.Instance.AmHost)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Error, "[DraftEngine] Not host, aborting");
                return;
            }

            if (_running)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Warning, "[DraftEngine] Draft already running!");
                return;
            }

            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, "[DraftEngine] Building draft pool");
            _pool = DraftPoolBuilder.BuildPool(pidToSlot.Count);
            if (_pool == null || _pool.Count == 0)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Error, "[DraftEngine] Pool is empty, aborting and starting game normally");
                Coroutines.Start(CoAutoStartGame());
                return;
            }

            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, $"[DraftEngine] Pool contains {_pool.Count} entries");

            _slotOrder.Clear();
            _slotOrder.AddRange(pidToSlot.Values.OrderBy(x => x));
            _totalSlots = totalSlots;
            _turnIndex = 0;
            _currentTurnNumber = 0;
            _running = true;

            _specialEligibleSlots.Clear();
            var eligiblePool = new List<int>(_slotOrder);
            for (int i = eligiblePool.Count - 1; i > 0; i--)
            {
                int j = _rng.NextInt(i + 1);
                (eligiblePool[i], eligiblePool[j]) = (eligiblePool[j], eligiblePool[i]);
            }
            int specialUnits = CountSpecialUnits(_pool);
            for (int i = 0; i < Math.Min(specialUnits, eligiblePool.Count); i++)
                _specialEligibleSlots.Add(eligiblePool[i]);
            _totalNeutralGroupsInPool = CountGroupsByPredicate(_pool, DraftRolePool.IsNeutralRoleName);
            _totalImpostorGroupsInPool = CountGroupsByPredicate(_pool, DraftRolePool.IsImpostorRoleName);

            DraftManager.SetDraftStateFromHost(totalSlots, pidToSlot.Keys.ToList(), pidToSlot.Values.ToList());
            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, "[DraftEngine] Draft state set locally");
            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, "[DraftEngine] Broadcasting slot notifications");
            DraftNetworkHelper.BroadcastSlotNotifications(totalSlots, pidToSlot);
            DraftCancelButton.Show();
            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, "[DraftEngine] Starting draft loop coroutine");
            Coroutines.Start(HostDraftLoop());
        }

        [HideFromIl2Cpp]
        private IEnumerator HostDraftLoop()
        {
            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, "[DraftEngine] HostDraftLoop started");

            while (_running && _turnIndex < _slotOrder.Count)
            {
                int concurrency = Math.Max(1, Math.Min(2, (int)OptionGroupSingleton<DraftOptions>.Instance.ConcurrentPicks.Value));
                int batchSize   = Math.Min(concurrency, _slotOrder.Count - _turnIndex);

                _currentTurnNumber++;
                _currentOffersBySlot.Clear();

                var activeSlots = new List<int>();
                for (int i = 0; i < batchSize; i++)
                {
                    var slot = _slotOrder[_turnIndex + i];
                    if (SetupTurn(slot))
                        activeSlots.Add(slot);
                }

                if (activeSlots.Count == 0)
                {
                    _turnIndex += Math.Max(1, batchSize);
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                yield return CoWaitForBatch(activeSlots);

                _turnIndex += batchSize;
                yield return new WaitForSeconds(0.5f);
            }

            if (!_running)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, "[DraftEngine] Draft loop exited due to cancellation, skipping FinishDraft");
                yield break;
            }

            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, "[DraftEngine] Draft complete");
            FinishDraft();
        }
        private HashSet<string> GetAvoidNamesForTurn(int excludeSlot)
        {
            var avoid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int currentImps = 0;
            int currentNeuts = 0;
            bool recruiterReserved = false;
            bool nonRecruiterImpReserved = false;

            foreach (var s in DraftManager.GetAllStates())
            {
                if (s.HasPicked && s.ChosenRoleId != 0)
                {
                    var roleName = DraftRolePool.GetRoleNameFromId(s.ChosenRoleId) ?? s.ForcedRoleName;
                    if (!string.IsNullOrEmpty(roleName))
                    {
                        if (DraftRolePool.IsImpostorRoleName(roleName)) currentImps++;
                        else if (DraftRolePool.IsNeutralRoleName(roleName)) currentNeuts++;

                        // Recruiter (DivanMods) is a single impostor at spawn that later
                        // recruits a teammate -- it can never coexist with a normally
                        // spawned impostor, in either order, or drafted players end up
                        // missing the role they picked.
                        if (DraftRolePool.IsRecruiterRoleName(roleName)) recruiterReserved = true;
                        else if (DraftRolePool.IsImpostorRoleName(roleName)) nonRecruiterImpReserved = true;
                    }
                }
            }

            foreach (var kvp in _currentOffersBySlot)
            {
                if (kvp.Key == excludeSlot) continue;

                bool hasImp = false;
                bool hasNeut = false;
                bool hasRecruiter = false;
                bool hasNonRecruiterImp = false;

                foreach (var n in kvp.Value)
                {
                    if (string.IsNullOrEmpty(n) || n == "__RANDOM__") continue;
                    avoid.Add(n);

                    if (DraftRolePool.IsImpostorRoleName(n)) hasImp = true;
                    if (DraftRolePool.IsNeutralRoleName(n)) hasNeut = true;

                    if (DraftRolePool.IsRecruiterRoleName(n)) hasRecruiter = true;
                    else if (DraftRolePool.IsImpostorRoleName(n)) hasNonRecruiterImp = true;
                }

                if (hasImp) currentImps++;
                if (hasNeut) currentNeuts++;
                if (hasRecruiter) recruiterReserved = true;
                if (hasNonRecruiterImp) nonRecruiterImpReserved = true;
            }

             var roleOpts = OptionGroupSingleton<DraftOptions>.Instance;
            int maxImps;
            int maxNeuts;

            if (roleOpts != null && !roleOpts.UseRoleListForPool)
            {
                var impOpts = OptionGroupSingleton<RoleDraftImpOptions>.Instance;
                var neutOpts = OptionGroupSingleton<RoleDraftNeutOptions>.Instance;

                maxImps = impOpts != null ? Math.Max(0, (int)impOpts.MaxImpostors.Value) : int.MaxValue;
                maxNeuts = neutOpts != null ? Math.Max(0, (int)neutOpts.MaxNeutrals.Value) : int.MaxValue;
            }
            else
            {
                // Role List mode has no MaxImpostors/MaxNeutrals option group -- the
                // equivalent caps are however many impostor/neutral-tagged groups were
                // actually built into the pool from the role list slots, not the lobby's
                // base impostor count (which is a separate, unrelated setting and can be
                // higher than the number of impostor slots the role list actually has).
                maxImps = _totalImpostorGroupsInPool;
                maxNeuts = _totalNeutralGroupsInPool;
            }

            bool blockImps = currentImps >= maxImps || recruiterReserved;
            bool blockNeuts = currentNeuts >= maxNeuts;

            if (blockImps || blockNeuts)
            {
                foreach (var n in _pool)
                {
                    if (string.IsNullOrEmpty(n) || n == "__RANDOM__") continue;
                    if (blockImps && DraftRolePool.IsImpostorRoleName(n)) avoid.Add(n);
                    if (blockNeuts && DraftRolePool.IsNeutralRoleName(n)) avoid.Add(n);
                }
            }

            if (nonRecruiterImpReserved)
            {
                foreach (var n in _pool)
                {
                    if (string.IsNullOrEmpty(n) || n == "__RANDOM__") continue;
                    if (DraftRolePool.IsRecruiterRoleName(n)) avoid.Add(n);
                }
            }

            if (!_specialEligibleSlots.Contains(excludeSlot))
            {
                foreach (var n in _pool)
                {
                    if (string.IsNullOrEmpty(n) || n == "__RANDOM__") continue;
                    if (DraftRolePool.IsImpostorRoleName(n) || DraftRolePool.IsNeutralRoleName(n))
                        avoid.Add(n);
                }
            }

            return avoid;
        }

        private static int CountSpecialUnits(List<string> pool)
        {
            var groups = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in pool)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                int pipeIdx = entry.IndexOf('|');
                string tag = pipeIdx >= 0 ? entry.Substring(pipeIdx) : entry;
                bool isSpecial = DraftRolePool.IsImpostorRoleName(entry) || DraftRolePool.IsNeutralRoleName(entry);
                if (!groups.ContainsKey(tag))
                    groups[tag] = isSpecial;
                else if (isSpecial)
                    groups[tag] = true;
            }
            return groups.Values.Count(v => v);
        }

        private static int CountGroupsByPredicate(List<string> pool, Func<string, bool> predicate)
        {
            var groups = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in pool)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                int pipeIdx = entry.IndexOf('|');
                string tag = pipeIdx >= 0 ? entry.Substring(pipeIdx) : entry;
                bool matches = predicate(entry);
                if (!groups.ContainsKey(tag))
                    groups[tag] = matches;
                else if (matches)
                    groups[tag] = true;
            }
            return groups.Values.Count(v => v);
        }

        private bool SetupTurn(int slot)
        {
            try
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, $"[DraftEngine] Turn {_currentTurnNumber}: Starting turn for slot {slot}");

                var avoidNames = GetAvoidNamesForTurn(slot);
                var offers = DraftPoolBuilder.GetOfferedRoles(_pool, _rng, avoidNames);
                _currentOffersBySlot[slot] = offers;
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, $"[DraftEngine] Generated {offers.Count} role offers for slot {slot}");

                var pickedRoleCandidates = new List<ushort>();
                foreach (var roleName in offers)
                {
                    ushort roleId;
                    if (roleName == "__RANDOM__")
                    {
                        roleId = 0;
                    }
                    else
                    {
                        roleId = DraftRolePool.ChooseRepresentativeRoleId(new List<string> { roleName });
                        if (roleId == 0)
                            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Warning,
                                $"[DraftEngine] Role name '{roleName}' failed to resolve to a role id");
                    }
                    pickedRoleCandidates.Add(roleId);
                }

                var state = DraftManager.GetStateForSlot(slot);
                var pickerId = state?.PlayerId ?? 0;

                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, $"[DraftEngine] Announcing turn to picker {pickerId}");
                DraftNetworkHelper.SendTurnAnnouncement(slot, pickerId, pickedRoleCandidates, _currentTurnNumber);

                var turnDuration = (int)Mathf.Max(1f, OptionGroupSingleton<DraftOptions>.Instance.TurnDurationSeconds.Value);
                DraftManager.TurnDuration = turnDuration;

                if (state != null)
                {
                    state.PendingPickIndex = 255;
                    state.IsPickingNow = true;
                }

                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, $"[DraftEngine] Waiting {turnDuration}s for pick (slot {slot})");
                return true;
            }
            catch (Exception e)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Error, $"[DraftEngine] Exception during turn setup for slot {slot}: {e}");
                return false;
            }
        }

        [HideFromIl2Cpp]
        private IEnumerator CoWaitForBatch(List<int> activeSlots)
        {
            var deadlines = new Dictionary<int, float>();
            var isBotOrDc = new Dictionary<int, bool>();
            var pending   = new HashSet<int>(activeSlots);

            foreach (var slot in activeSlots)
            {
                var state = DraftManager.GetStateForSlot(slot);
                var turnDuration = (int)Mathf.Max(1f, OptionGroupSingleton<DraftOptions>.Instance.TurnDurationSeconds.Value);
                bool botDc = state != null && IsBotOrDisconnected(state.PlayerId);
                var waitSeconds = botDc ? Mathf.Min(1f, turnDuration) : turnDuration;
                deadlines[slot] = Time.time + waitSeconds;
                isBotOrDc[slot] = botDc;
            }

            while (pending.Count > 0 && _running)
            {
                float maxRemaining = 0f;

                foreach (var slot in pending.ToList())
                {
                    var state = DraftManager.GetStateForSlot(slot);
                    if (state == null)
                    {
                        pending.Remove(slot);
                        continue;
                    }

                    if (state.HasPicked)
                    {
                        pending.Remove(slot);
                        continue;
                    }

                    if (state.PendingPickIndex != 255)
                    {
                        var index = state.PendingPickIndex;
                        MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, $"[DraftEngine] Pick received for slot {slot}: index {index}");
                        state.PendingPickIndex = 255;
                        ApplyPick(slot, index);
                        pending.Remove(slot);
                        continue;
                    }

                    var remaining = deadlines[slot] - Time.time;
                    if (remaining <= 0f)
                    {
                        var reason  = isBotOrDc[slot] ? "bot/disconnected" : "timeout";
                        var offers  = _currentOffersBySlot.TryGetValue(slot, out var o) ? o : new List<string>();
                        var autoIndex = (byte)_rng.NextInt(Math.Max(1, offers.Count));
                        MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, $"[DraftEngine] Auto-picking index {autoIndex} for slot {slot} ({reason})");
                        ApplyPick(slot, autoIndex);
                        pending.Remove(slot);
                        continue;
                    }

                    maxRemaining = Mathf.Max(maxRemaining, remaining);
                }

                DraftManager.TurnTimeLeft = maxRemaining;
                yield return null;
            }
        }

        private static bool IsBotOrDisconnected(byte playerId)
        {
            var player = PlayerControl.AllPlayerControls?.ToArray()
                .FirstOrDefault(p => p != null && p.PlayerId == playerId);

            if (player == null) return true;
            if (player.Data == null || player.Data.Disconnected) return true;

            try
            {
                var client = AmongUsClient.Instance?.GetClient(player.OwnerId);
                if (client == null) return true;
            }
            catch
            {
                //ignored
            }

            return false;
        }
         public void RequestReroll(byte playerId)
        {
            if (!_running) return;
            if (_turnIndex >= _slotOrder.Count) return;

            var currentSlot = _slotOrder[_turnIndex];
            var state = DraftManager.GetStateForSlot(currentSlot);
            if (state == null || state.PlayerId != playerId || state.HasPicked) return;

            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, $"[DraftEngine] Reroll requested by player {playerId}");

            var avoidNames = GetAvoidNamesForTurn(currentSlot);
            _currentOffers = DraftPoolBuilder.GetOfferedRoles(_pool, _rng, avoidNames);
            _currentOffersBySlot[currentSlot] = _currentOffers;
            var pickedRoleCandidates = new List<ushort>();
            foreach (var roleName in _currentOffers)
            {
                ushort roleId = roleName == "__RANDOM__"
                    ? (ushort)0
                    : DraftRolePool.ChooseRepresentativeRoleId(new List<string> { roleName });
                pickedRoleCandidates.Add(roleId);
            }

            state.PendingPickIndex = 255;
            DraftNetworkHelper.SendTurnAnnouncement(currentSlot, playerId, pickedRoleCandidates, _currentTurnNumber);
        }

        private void RemovePickedSeatFromPool(string chosenName)
        {
            if (string.IsNullOrEmpty(chosenName) || chosenName == "__RANDOM__")
            {
                if (!string.IsNullOrEmpty(chosenName)) _pool.Remove(chosenName);
                return;
            }

            int pipeIdx = chosenName.IndexOf('|');
            if (pipeIdx >= 0)
            {
                string slotSuffix = chosenName.Substring(pipeIdx);
                _pool.RemoveAll(x => x != null && x.EndsWith(slotSuffix, StringComparison.Ordinal));
            }
            else
            {
                _pool.Remove(chosenName);
            }
        }

        private void ApplyPick(int slot, byte index)
        {
            var state = DraftManager.GetStateForSlot(slot);
            if (state == null) return;

            var offers      = _currentOffersBySlot.TryGetValue(slot, out var o) ? o : new List<string>();
            string? chosenName = (index >= offers.Count) ? "__RANDOM__" : offers[index];

            if (chosenName != null && chosenName != "__RANDOM__" && !_pool.Remove(chosenName))
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info,
                    $"[DraftEngine] '{chosenName}' was already taken by a concurrent pick, falling back to random for slot {slot}");
                chosenName = null;
            }
            else if (chosenName != null && chosenName != "__RANDOM__")
            {
                RemovePickedSeatFromPool(chosenName);
            }

            ushort chosenRoleId;
            if (chosenName == "__RANDOM__" || chosenName == null)
            {
                var remaining = _pool.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();

                // Random/auto-picks (timeout, bot/disconnected, or a collision fallback)
                // must respect the same impostor/neutral caps and special-role eligibility
                // as normal offers -- otherwise an auto-pick can hand an impostor or
                // neutral role to a slot that was never eligible for one, or push the
                // impostor/neutral count past the configured max.
                var avoidForSlot = GetAvoidNamesForTurn(slot);
                var eligibleRemaining = remaining.Where(r => !avoidForSlot.Contains(r)).ToList();
                if (eligibleRemaining.Count > 0) remaining = eligibleRemaining;

                if (remaining.Count > 0)
                {
                    var randomName = remaining[_rng.NextInt(remaining.Count)];
                    RemovePickedSeatFromPool(randomName);
                    chosenRoleId = DraftRolePool.ChooseRepresentativeRoleId(new List<string> { randomName });
                }
                else
                {
                    // Pool is completely exhausted. This should be rare now that
                    // BuildPoolFromRoleList guarantees every slot contributes at least
                    // one entry, but if it still happens, don't leave the player with
                    // no role -- grab any currently-usable role instead of returning 0.
                    // This still has to respect each role's max count -- otherwise it can
                    // (and did) hand the same max-1 role to a second player.
                    var assignedCounts = new Dictionary<ushort, int>();
                    bool recruiterAlreadyAssigned = false;
                    bool nonRecruiterImpAlreadyAssigned = false;
                    foreach (var s in DraftManager.GetAllStates())
                    {
                        if (s.HasPicked && s.ChosenRoleId != 0)
                        {
                            assignedCounts[s.ChosenRoleId] = assignedCounts.GetValueOrDefault(s.ChosenRoleId) + 1;

                            var rn = DraftRolePool.GetRoleNameFromId(s.ChosenRoleId) ?? s.ForcedRoleName;
                            if (!string.IsNullOrEmpty(rn))
                            {
                                if (DraftRolePool.IsRecruiterRoleName(rn)) recruiterAlreadyAssigned = true;
                                else if (DraftRolePool.IsImpostorRoleName(rn)) nonRecruiterImpAlreadyAssigned = true;
                            }
                        }
                    }

                    var anyNames = DraftRolePool.ResolveBucketToRoleNames(nameof(RoleListOption.Any))
                        ?.Where(n => !string.IsNullOrWhiteSpace(n))
                        .Where(n =>
                        {
                            var id = DraftRolePool.ChooseRepresentativeRoleId(new List<string> { n });
                            if (assignedCounts.GetValueOrDefault(id) >= DraftRolePool.GetMaxCountForRoleName(n)) return false;
                            if (recruiterAlreadyAssigned && DraftRolePool.IsImpostorRoleName(n)) return false;
                            if (nonRecruiterImpAlreadyAssigned && DraftRolePool.IsRecruiterRoleName(n)) return false;
                            return true;
                        })
                        .ToList() ?? new List<string>();
                    if (anyNames.Count > 0)
                    {
                        var fallbackName = anyNames[_rng.NextInt(anyNames.Count)];
                        chosenRoleId = DraftRolePool.ChooseRepresentativeRoleId(new List<string> { fallbackName });
                        MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Warning,
                            $"[DraftEngine] Pool exhausted for slot {slot}, assigned emergency fallback role id {chosenRoleId}");
                    }
                    else
                    {
                        chosenRoleId = 0;
                    }
                }
            }
            else
            {
                chosenRoleId = DraftRolePool.ChooseRepresentativeRoleId(new List<string> { chosenName });
            }

            if (chosenRoleId == 0)
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Warning, $"[DraftEngine] Pick for slot {slot} resolved to role id 0 (chosen name: '{chosenName ?? "null"}'), this player will not get a proper role assignment");

            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, $"[DraftEngine] Applied pick for slot {slot}: roleId {chosenRoleId}");

            state.PendingPickIndex = 255;
            _currentOffersBySlot.Remove(slot);
            DraftManager.ConfirmPick(slot, chosenRoleId);
            DraftNetworkHelper.BroadcastPickConfirmed(slot, chosenRoleId);
        }

        private void FinishDraft()
        {
            _running = false;

            var recapMode = OptionGroupSingleton<DraftOptions>.Instance?.DraftRecap.Value ?? DraftRecapMode.Nothing;

            var recapEntries = new List<RecapEntry>();
            foreach (var s in DraftManager.GetAllStates())
            {
                var roleName = DraftRolePool.GetRoleNameFromId(s.ChosenRoleId) ?? s.ForcedRoleName ?? "Unknown";

                RoleBehaviour? roleBehaviour = null;
                try
                {
                    roleBehaviour = s.ChosenRoleId != 0
                        ? MiscUtils.GetRegisteredRole((AmongUs.GameOptions.RoleTypes)s.ChosenRoleId)
                          ?? RoleManager.Instance?.GetRole((AmongUs.GameOptions.RoleTypes)s.ChosenRoleId)
                        : null;
                }
                catch
                {
                    // ignored
                }

                string teamLabel = "Unknown";
                Color roleColor = Color.white;

                if (roleBehaviour != null)
                {
                    if (recapMode == DraftRecapMode.Faction || recapMode == DraftRecapMode.Alignment)
                    {
                        teamLabel = DraftUiManager.GetTeamLabel(roleBehaviour).ToUpperInvariant() ?? "Unknown";
                        roleColor = DraftUiManager.GetRoleFactionColor(roleBehaviour, true);
                    }
                    else if (recapMode == DraftRecapMode.Role)
                    {
                        teamLabel = roleBehaviour.GetRoleName()?.ToUpperInvariant() ?? "Unknown";
                        roleColor = roleBehaviour.TeamColor;
                    }
                }
                else
                {
                    if (recapMode == DraftRecapMode.Role)
                        teamLabel = roleName.ToUpperInvariant();
                }

                string colorHex  = ColorUtility.ToHtmlStringRGB(roleColor);

                recapEntries.Add(new RecapEntry(s.SlotNumber, roleName, teamLabel, colorHex));
            }

            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, $"[DraftEngine] Draft finished, recapMode={recapMode}");
            DraftApplier.StorePendingDraftStates(DraftManager.GetAllStates());
            DraftNetworkHelper.BroadcastRecap(recapEntries, recapMode);
            Coroutines.Start(CoAutoStartGame(recapMode != DraftRecapMode.Nothing ? 6f : 0f));
        }

        private static IEnumerator CoAutoStartGame(float delay = 0f)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (!AmongUsClient.Instance.AmHost)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Warning, "[DraftEngine] No longer host");
                yield break;
            }

            if (GameStartManager.Instance == null)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Warning, "[DraftEngine] GameStartManager not found");
                yield break;
            }

            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Warning, "[DraftEngine] Not in joined state");
                yield break;
            }

            MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info, "[DraftEngine] Auto-starting game");

            GameStartPatch.SkipIntercept = true;
            int orig = GameStartManager.Instance.MinPlayers;
            GameStartManager.Instance.MinPlayers = 1;
            try
            {
                GameStartManager.Instance.BeginGame();
            }
            catch (System.Exception ex)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Error, $"[DraftEngine] Exception during GameStartManager.BeginGame: {ex}");
            }

            try
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Info,
                    $"[DraftEngine] Applying {DraftApplier.PendingDraftStates.Count} pending draft results");
                DraftApplier.ApplyDraftResults(DraftApplier.PendingDraftStates);
            }
            catch (System.Exception ex)
            {
                MiscUtils.LogInfo(TownOfUs.Events.TownOfUsEventHandlers.LogLevel.Error, $"[DraftEngine] Exception applying draft results: {ex}");
            }

            GameStartManager.Instance.countDownTimer = 0f;
            GameStartManager.Instance.MinPlayers = orig;
            yield return null;
            GameStartPatch.SkipIntercept = false;
        }

        public void CancelDraft()
        {
            if (!_running) return;
            _running = false;
            DraftManager.Reset(cancelledBeforeCompletion: true);
            DraftNetworkHelper.BroadcastCancelDraft();
        }
    }
}