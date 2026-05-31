using DraftModeTOUM.Managers;
using Reactor.Networking.Rpc;
using Hazel;
using AmongUs.GameOptions;
using System.Collections.Generic;
using System.Linq;
using MiraAPI.Utilities;
using System;
using System.Collections;
using Reactor.Utilities;
using UnityEngine;
using TownOfUs.Utilities;
using TownOfUs.Assets;

namespace DraftModeTOUM.Networking
{
    /// <summary>
    /// Contains all Reactor MethodRpc definitions for Draft Mode networking.
    /// These replace the manual RPC enum and patch system with Reactor's cleaner MethodRpc approach.
    /// </summary>
    public static class DraftMethodRpcs
    {
        /// <summary>
        /// Client sends this to host when submitting their role pick
        /// </summary>
        [MethodRpc(nameof(SubmitPick))]
        public static void SubmitPick(PlayerControl sender, int index)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            
            DraftManager.SubmitPick(sender.PlayerId, (byte)index);
        }

        /// <summary>
        /// Host broadcasts to all clients to start the draft
        /// </summary>
        [MethodRpc(nameof(StartDraft))]
        public static void StartDraft(PlayerControl sender, int totalSlots, byte[] playerIds, int[] slotNumbers)
        {
            if (AmongUsClient.Instance.AmHost)
            {
                // Host just consumes the packet
                return;
            }

            var pids = playerIds.ToList();
            var slots = slotNumbers.ToList();
            DraftManager.SetDraftStateFromHost(totalSlots, pids, slots);
            DraftUiManager.CloseAll();
        }

        /// <summary>
        /// Host announces whose turn it is and what roles are available
        /// </summary>
        [MethodRpc(nameof(AnnounceTurn))]
        public static void AnnounceTurn(PlayerControl sender, int turnNumber, int slot, byte pickerId, ushort[] roleIds)
        {
            if (AmongUsClient.Instance.AmHost)
            {
                // Host just consumes the packet
                return;
            }

            DraftModePlugin.Logger.LogInfo(
                $"[DraftMethodRpc] Received turn announcement for player {pickerId}, " +
                $"roles: {string.Join(",", roleIds.Select(r => ((RoleTypes)r).ToString()))}");

            DraftManager.SetClientTurn(turnNumber, slot);
            DisplayTurnAnnouncement(slot, pickerId, roleIds);
        }

        /// <summary>
        /// Host notifies all clients which slot each player is in
        /// </summary>
        [MethodRpc(nameof(SlotNotify))]
        public static void SlotNotify(PlayerControl sender, byte[] playerIds, int[] slotNumbers)
        {
            if (AmongUsClient.Instance.AmHost)
            {
                // Host just consumes the packet
                return;
            }

            var pids = playerIds.ToList();
            var slots = slotNumbers.ToList();
            DraftManager.SetDraftStateFromHost(pids.Count, pids, slots);
            DraftUiManager.RefreshTurnList();
        }

        /// <summary>
        /// Client notifies host that their picker UI is ready
        /// </summary>
        [MethodRpc(nameof(PickerReady))]
        public static void PickerReady(PlayerControl sender)
        {
            if (!AmongUsClient.Instance.AmHost) return;

            DraftManager.NotifyPickerReady(sender.PlayerId);
        }

        /// <summary>
        /// Host confirms a pick was received and broadcasts it to all clients
        /// </summary>
        [MethodRpc(nameof(PickConfirmed))]
        public static void PickConfirmed(PlayerControl sender, int slot, ushort roleId)
        {
            if (AmongUsClient.Instance.AmHost)
            {
                // Host just consumes the packet
                return;
            }

            var state = DraftManager.GetStateForSlot(slot);
            if (state != null)
            {
                state.ChosenRoleId = roleId;
                state.HasPicked = true;
                if (state.PlayerId == PlayerControl.LocalPlayer.PlayerId)
                    DraftStatusOverlay.NotifyLocalPlayerPicked(roleId);
            }
        }

        /// <summary>
        /// Client with admin role sends forced role assignment to host
        /// </summary>
        [MethodRpc(nameof(ForceRole))]
        public static void ForceRole(PlayerControl sender, string roleName, byte targetId)
        {
            if (!AmongUsClient.Instance.AmHost) return;

            DraftManager.SetForcedDraftRole(roleName, targetId);
            DraftModePlugin.Logger.LogDebug($"[DraftMethodRpc] Host received ForceRole '{roleName}' for player {targetId}");
        }

        /// <summary>
        /// Host broadcasts to cancel the current draft
        /// </summary>
        [MethodRpc(nameof(CancelDraft))]
        public static void CancelDraft(PlayerControl sender)
        {
            if (!AmongUsClient.Instance.AmHost)
            {
                DraftUiManager.CloseAll();
                DraftStatusOverlay.SetState(OverlayState.Hidden);
                DraftManager.Reset(cancelledBeforeCompletion: true);
            }
        }

        /// <summary>
        /// Host broadcasts to end the draft
        /// </summary>
        [MethodRpc(nameof(EndDraft))]
        public static void EndDraft(PlayerControl sender)
        {
            DraftManager.Reset(cancelledBeforeCompletion: true);
        }

        /// <summary>
        /// Host broadcasts a notification to all clients
        /// </summary>
        [MethodRpc(nameof(CreateNotif))]
        public static void CreateNotif(PlayerControl sender, string message)
        {
            Helpers.CreateAndShowNotification(message, Color.white,
                new Vector3(0f, 1f, -20f), spr: TouRoleIcons.Traitor.LoadAsset());
        }

        /// <summary>
        /// Host broadcasts the draft recap to all clients
        /// </summary>
        [MethodRpc(nameof(BroadcastRecap))]
        public static void BroadcastRecap(PlayerControl sender, bool showRecap, string[] recapData)
        {
            if (AmongUsClient.Instance.AmHost)
            {
                // Host just consumes the packet
                return;
            }

            if (showRecap)
            {
                var entries = new List<RecapEntry>();
                for (int i = 0; i < recapData.Length; i += 2)
                {
                    if (i + 1 < recapData.Length && int.TryParse(recapData[i], out int slot))
                    {
                        entries.Add(new RecapEntry(slot, recapData[i + 1]));
                    }
                }
                DraftRecapOverlay.Show(entries);
            }
            DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);
            DraftManager.Reset(cancelledBeforeCompletion: false);
            DraftManager.TriggerEndDraftSequence();
        }

        /// <summary>
        /// Helper to display turn announcement locally
        /// </summary>
        private static void DisplayTurnAnnouncement(int slot, byte pickerId, ushort[] roleIds)
        {
            byte localId = PlayerControl.LocalPlayer.PlayerId;
            if (localId == pickerId)
            {
                DraftModePlugin.Logger.LogInfo(
                    $"[DraftMethodRpc] Showing picker for local player with roles: " +
                    $"{string.Join(",", roleIds.Select(r => ((RoleTypes)r).ToString()))}");
                DraftUiManager.ShowPicker(roleIds.ToList());

                var localSettings = MiraAPI.LocalSettings.LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
                if (localSettings.AudioCueTiming.Value == AudioTiming.TurnStart)
                    DraftAudio.PlayDraftStartCue();
            }
            else
            {
                var localState = DraftManager.GetStateForPlayer(localId);
                if (localState == null || !localState.IsPickingNow)
                    DraftUiManager.CloseAll();
            }
        }
    }

    /// <summary>
    /// Network helper class for sending Draft RPCs using Reactor's MethodRpc system
    /// Replaces the previous manual RPC sending code
    /// </summary>
    public static class DraftNetworkHelper
    {
        /// <summary>
        /// Client calls this to send their pick to the host
        /// </summary>
        public static void SendPickToHost(int index)
        {
            DraftUiManager.CloseAll();
            if (AmongUsClient.Instance.AmHost)
            {
                DraftManager.SubmitPick(PlayerControl.LocalPlayer.PlayerId, (byte)index);
            }
            else
            {
                PlayerControl.LocalPlayer.RpcCall(nameof(DraftMethodRpcs.SubmitPick), index);
            }
        }

        /// <summary>
        /// Host calls this to broadcast draft start to all clients
        /// </summary>
        public static void BroadcastDraftStart(int totalSlots, List<byte> pids, List<int> slots)
        {
            DraftManager.SetDraftStateFromHost(totalSlots, pids, slots);
            PlayerControl.LocalPlayer.RpcCall(
                nameof(DraftMethodRpcs.StartDraft),
                totalSlots,
                pids.ToArray(),
                slots.ToArray());
        }

        /// <summary>
        /// Host calls this to create a notification on all clients
        /// </summary>
        public static void BroadcastCreateNotif(string message)
        {
            Helpers.CreateAndShowNotification(message, Color.white,
                new Vector3(0f, 1f, -20f), spr: TouRoleIcons.Traitor.LoadAsset());
            PlayerControl.LocalPlayer.RpcCall(nameof(DraftMethodRpcs.CreateNotif), message);
        }

        /// <summary>
        /// Host calls this to announce whose turn it is and what roles they can pick
        /// </summary>
        public static void SendTurnAnnouncement(int slot, byte playerId, List<ushort> roleIds, int turnNumber)
        {
            DraftModePlugin.Logger.LogInfo(
                $"[DraftMethodRpc] Sending turn announcement to player {playerId}, " +
                $"roles: {string.Join(",", roleIds.Select(r => ((RoleTypes)r).ToString()))}");

            // Local handling
            DraftMethodRpcs.DisplayTurnAnnouncement(slot, playerId, roleIds.ToArray());

            // Network broadcast
            PlayerControl.LocalPlayer.RpcCall(
                nameof(DraftMethodRpcs.AnnounceTurn),
                turnNumber,
                slot,
                playerId,
                roleIds.ToArray());
        }

        /// <summary>
        /// Host calls this to notify clients about draft slot assignments
        /// </summary>
        public static void BroadcastSlotNotifications(Dictionary<byte, int> pidToSlot)
        {
            var pids = pidToSlot.Keys.ToList();
            var slots = pidToSlot.Values.ToList();
            PlayerControl.LocalPlayer.RpcCall(
                nameof(DraftMethodRpcs.SlotNotify),
                pids.ToArray(),
                slots.ToArray());
        }

        /// <summary>
        /// Host calls this to confirm a pick was received
        /// </summary>
        public static void BroadcastPickConfirmed(int slot, ushort roleId)
        {
            var state = DraftManager.GetStateForSlot(slot);
            if (state != null)
            {
                state.ChosenRoleId = roleId;
                state.HasPicked = true;
                if (state.PlayerId == PlayerControl.LocalPlayer.PlayerId)
                    DraftStatusOverlay.NotifyLocalPlayerPicked(roleId);
            }

            PlayerControl.LocalPlayer.RpcCall(
                nameof(DraftMethodRpcs.PickConfirmed),
                slot,
                roleId);
        }

        /// <summary>
        /// Client calls this to notify host their picker UI is ready
        /// </summary>
        public static void NotifyPickerReady()
        {
            if (AmongUsClient.Instance.AmHost)
            {
                DraftManager.NotifyPickerReady(PlayerControl.LocalPlayer.PlayerId);
            }
            else
            {
                PlayerControl.LocalPlayer.RpcCall(nameof(DraftMethodRpcs.PickerReady));
            }
        }

        /// <summary>
        /// Admin calls this to force assign a role to a player during draft
        /// </summary>
        public static void SendForceRoleToHost(string roleName, byte targetId)
        {
            if (AmongUsClient.Instance.AmHost)
            {
                DraftManager.SetForcedDraftRole(roleName, targetId);
            }
            else
            {
                PlayerControl.LocalPlayer.RpcCall(
                    nameof(DraftMethodRpcs.ForceRole),
                    roleName,
                    targetId);
            }
        }

        /// <summary>
        /// Host calls this to cancel the draft
        /// </summary>
        public static void BroadcastCancelDraft()
        {
            PlayerControl.LocalPlayer.RpcCall(nameof(DraftMethodRpcs.CancelDraft));
        }

        /// <summary>
        /// Host calls this to broadcast the draft recap
        /// </summary>
        public static void BroadcastRecap(List<RecapEntry> entries, bool showRecap)
        {
            if (showRecap) DraftRecapOverlay.Show(entries);

            var recapData = new List<string>();
            if (showRecap)
            {
                foreach (var e in entries)
                {
                    recapData.Add(e.SlotNumber.ToString());
                    recapData.Add(e.RoleName);
                }
            }

            PlayerControl.LocalPlayer.RpcCall(
                nameof(DraftMethodRpcs.BroadcastRecap),
                showRecap,
                recapData.ToArray());
        }

        /// <summary>
        /// Host calls this to end the draft
        /// </summary>
        public static void BroadcastDraftEnd()
        {
            DraftManager.Reset(cancelledBeforeCompletion: true);
            PlayerControl.LocalPlayer.RpcCall(nameof(DraftMethodRpcs.EndDraft));
        }
    }
}
