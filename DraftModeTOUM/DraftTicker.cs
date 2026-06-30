using System;
using DraftModeTOUM.Managers;
using HarmonyLib;
using Reactor.Utilities.Attributes;
using UnityEngine;

namespace DraftModeTOUM
{
    [RegisterInIl2Cpp]
    public class DraftTicker(IntPtr ip) : MonoBehaviour(ip)
    {
        private static DraftTicker _instance;

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("DraftTicker");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DraftTicker>();
        }

        public static void DestroyIfExists()
        {
            if (_instance == null) return;
            Destroy(_instance.gameObject);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            DraftManager.Tick(Time.deltaTime);
        }
    }
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.OnDestroy))]
    public static class DraftTickerHudDestroyPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => DraftTicker.DestroyIfExists();
    }
}
