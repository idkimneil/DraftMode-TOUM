using DraftModeTOUM.Managers;
using HarmonyLib;
using Reactor.Utilities;

namespace DraftModeTOUM.Patches
{
    
    
    
    
    
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    public static class IntroCutsceneBeginPatch
    {
        public static void Prefix()
        {
            DraftScreenController.Hide();
            DraftStatusOverlay.SetState(OverlayState.Hidden);
            DraftRecapOverlay.Hide();
            DraftCancelButton.Hide();

            if (DraftManager.PendingRoleAssignments.Count == 0) return;

            DraftModePlugin.Logger.LogInfo(
                "[GameStartPatch] IntroCutscene.CoBegin fired — attempting first role application");

            
            DraftManager.ApplyPendingRolesOnGameStart();

            
            Coroutines.Start(DraftManager.CoApplyRolesWithRetry());
        }
    }

    
    
    
    
    
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
    public static class ShipStatusRoleApplyPatch
    {
        public static void Postfix()
        {
            DraftScreenController.Hide();
            DraftStatusOverlay.SetState(OverlayState.Hidden);
            DraftRecapOverlay.Hide();
            DraftCancelButton.Hide();
            if (DraftManager.PendingRoleAssignments.Count == 0) return;

            DraftModePlugin.Logger.LogInfo(
                "[GameStartPatch] ShipStatus.Start fired — re-attempting any unfinished role assignments");

            
            
            
            
            DraftManager.ApplyPendingRolesOnGameStart();
        }
    }
}

