using System;
using DraftModeTOUM.Managers;
using Reactor.Utilities.Attributes;
using UnityEngine;

namespace DraftModeTOUM
{
    [RegisterInIl2Cpp]
    public class DraftTicker(IntPtr ip) : MonoBehaviour(ip)
    {
        private static DraftTicker? _instance;

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("DraftTicker");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DraftTicker>();
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

        private void Update()
        {
            if (!HudManager.InstanceExists)
            {
                Destroy(gameObject);
                return;
            }
            DraftManager.Tick(Time.deltaTime);
        }
    }
}
