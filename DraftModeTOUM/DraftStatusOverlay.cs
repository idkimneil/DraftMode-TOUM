using System;
using Reactor.Utilities.Attributes;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;
using DraftModeTOUM.Managers;
using Il2CppInterop.Runtime.Attributes;
using Reactor.Utilities;

namespace DraftModeTOUM
{
    public enum OverlayState { Hidden, Waiting, BackgroundOnly }

    [RegisterInIl2Cpp]
    public sealed class DraftStatusOverlay(IntPtr ip) : MonoBehaviour(ip)
    {
        private static DraftStatusOverlay? _instance;

        private GameObject?  _root;
        private GameObject?  _bgOverlay;
        private GameObject?  _backdropArt;
        private SpriteRenderer? _backdropHaloRenderer;
        private TextMeshPro? _yourNumberLabel;
        private TextMeshPro? _yourNumberValue;
        private TextMeshPro? _nowPickingLabel;
        private TextMeshPro? _nowPickingValue;
        private TextMeshPro? _queueLabel;
        private TextMeshPro? _queueValue;
        private TextMeshPro? _turnStatusLabel;
        private TextMeshPro? _pickReceiptLabel;
        private GameObject?  _progressTrack;
        private GameObject?  _progressFill;
        private SpriteRenderer? _progressFillRenderer;
        private GameObject?  _roleCardNewRoleObj;
        private SpriteRenderer? _roleCardIdleGlowRenderer;
        private bool _roleCardHovering = false;
        private float _roleCardIdleDelay = 0f;

        private static GameObject? _cachedRolePrefab;
        private static GameObject? _outroHandoffRoot;
        private static SpriteRenderer? _outroLaunchBridgeRenderer;

        private ushort?      _pendingRoleId      = null;
        private ushort?      _shownRoleId        = null;
        private int          _cachedMySlot       = -1;
        private int          _cachedPickerSlot   = -1;
        private int          _cachedPickerCount  = -1;
        private float        _waitAnimTime       = 0f;
        private float        _pickReceiptTimer   = 0f;
        private OverlayState _currentState       = OverlayState.Hidden;

        // Track whether we hid the card due to a menu being open so we can restore it
        private bool _cardHiddenForMenu = false;
        private bool _playedStartCeremony = false;

        private List<GameObject> _hiddenHudChildren = new();

        private static readonly Color WaitingBgColor = new Color(0.045f, 0.018f, 0.12f, 1f);

        private static readonly Vector3 CardHudPos = new Vector3(2.0f, 0.3f, -21f);
        private const float CardScale              = 0.55f;
        private const float CardTiltDeg            = -8f;
        private const float TeamNameFontSize       = 3.8f;
        private const float ProgressBarWidth       = 4.6f;
        private const float ProgressBarHeight      = 0.08f;
        private const int RoleCardSortingOrder     = 58;

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("DraftStatusOverlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DraftStatusOverlay>();
        }

        public static void SetState(OverlayState state)
        {
            EnsureExists();
            _instance!._currentState = state;
            _instance.UpdateVisibility();
        }

        public static void Refresh()
        {
            if (_instance == null) return;
            _instance.UpdateContent();
        }

        public static void NotifyLocalPlayerPicked(ushort roleId)
        {
            EnsureExists();
            DraftModePlugin.Logger.LogInfo($"[DraftStatusOverlay] NotifyLocalPlayerPicked roleId={roleId}");
            if (roleId != _instance!._shownRoleId)
            {
                _instance._shownRoleId   = roleId;
                _instance._pendingRoleId = null;
                _instance.ShowRoleCard(roleId);
            }
        }

        public static void NotifySlotLocked(int slot)
        {
            EnsureExists();
            _instance!.ShowSlotLocked(slot);
        }

        public static void ClearHudReferences()
        {
            if (_instance == null) return;
            _instance._hiddenHudChildren.Clear();
            _instance._root             = null;
            _instance._bgOverlay        = null;
            _instance._backdropArt      = null;
            _instance._backdropHaloRenderer = null;
            _instance._yourNumberLabel  = null;
            _instance._yourNumberValue  = null;
            _instance._nowPickingLabel  = null;
            _instance._nowPickingValue  = null;
            _instance._queueLabel       = null;
            _instance._queueValue       = null;
            _instance._turnStatusLabel  = null;
            _instance._pickReceiptLabel = null;
            _instance._pickReceiptTimer = 0f;
            _instance._progressTrack    = null;
            _instance._progressFill     = null;
            _instance._progressFillRenderer = null;
            _instance.DestroyRoleCard();
            _instance._pendingRoleId    = null;
            _instance._shownRoleId      = null;
            _instance._cachedMySlot     = -1;
            _instance._cachedPickerSlot = -1;
            _instance._cachedPickerCount = -1;
            _cachedRolePrefab           = null;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            BuildUI();
        }

        private void OnDestroy()
        {
            RestoreHudElements();
            if (_instance == this) _instance = null;
        }

        // ── UI construction ───────────────────────────────────────────────────

        private void BuildUI()
        {
            if (HudManager.Instance == null) return;
            if (HudManager.Instance.TaskPanel == null || HudManager.Instance.TaskPanel.taskText == null) return;

            var font    = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            _bgOverlay = new GameObject("DraftWaitingBg");
            _bgOverlay.transform.SetParent(HudManager.Instance.transform, false);
            _bgOverlay.transform.localPosition = new Vector3(0f, 0f, 1f);

            var bgSr              = _bgOverlay.AddComponent<SpriteRenderer>();
            bgSr.sprite           = MakeWhiteSprite();
            bgSr.color            = WaitingBgColor;
            bgSr.sortingLayerName = "UI";
            bgSr.sortingOrder     = 42;

            var cam    = Camera.main;
            float camH = cam != null ? cam.orthographicSize * 2f : 6f;
            float camW = camH * ((float)Screen.width / Screen.height);
            _bgOverlay.transform.localScale = new Vector3(camW, camH, 1f);
            _bgOverlay.SetActive(false);
            BuildBackdropArt(camW, camH);

            _root = new GameObject("DraftOverlayRoot");
            _root.transform.SetParent(HudManager.Instance.transform, false);
            _root.transform.localPosition = new Vector3(0f, 0.6f, -20f);

            _yourNumberLabel = MakeText(_root, "YourNumberLabel", font, fontMat,
                "YOUR NUMBER:", 2.2f, new Color(0.6f, 0.9f, 1f),
                new Vector3(0f, 0.55f, 0f), bold: false);
            _yourNumberValue = MakeText(_root, "YourNumberValue", font, fontMat,
                "?", 5.5f, Color.white,
                new Vector3(0f, 0.05f, 0f), bold: true);
            _nowPickingLabel = MakeText(_root, "NowPickingLabel", font, fontMat,
                "NOW PICKING:", 1.6f, new Color(1f, 0.85f, 0.1f),
                new Vector3(0f, -0.55f, 0f), bold: false);
            _nowPickingValue = MakeText(_root, "NowPickingValue", font, fontMat,
                "?", 3.0f, new Color(1f, 0.85f, 0.1f),
                new Vector3(0f, -1.05f, 0f), bold: true);

            _progressTrack = new GameObject("DraftQueueProgressTrack");
            _progressTrack.transform.SetParent(_root.transform, false);
            _progressTrack.transform.localPosition = new Vector3(0f, -1.58f, 0.05f);
            _progressTrack.transform.localScale = new Vector3(ProgressBarWidth, ProgressBarHeight, 1f);
            var trackRenderer = _progressTrack.AddComponent<SpriteRenderer>();
            trackRenderer.sprite = MakeWhiteSprite();
            trackRenderer.color = new Color(1f, 1f, 1f, 0.12f);
            trackRenderer.sortingLayerName = "UI";
            trackRenderer.sortingOrder = 49;

            _progressFill = new GameObject("DraftQueueProgressFill");
            _progressFill.transform.SetParent(_root.transform, false);
            _progressFill.transform.localPosition = new Vector3(-ProgressBarWidth * 0.5f, -1.58f, 0f);
            _progressFill.transform.localScale = new Vector3(0.01f, ProgressBarHeight, 1f);
            _progressFillRenderer = _progressFill.AddComponent<SpriteRenderer>();
            _progressFillRenderer.sprite = MakeWhiteSprite();
            _progressFillRenderer.color = new Color(1f, 0.78f, 0.08f, 0.9f);
            _progressFillRenderer.sortingLayerName = "UI";
            _progressFillRenderer.sortingOrder = 50;

            _queueLabel = MakeText(_root, "QueueLabel", font, fontMat,
                "DRAFT QUEUE", 1.05f, new Color(0.75f, 0.9f, 1f),
                new Vector3(0f, -1.88f, 0f), bold: false);
            _queueValue = MakeText(_root, "QueueValue", font, fontMat,
                "", 1.1f, Color.white,
                new Vector3(0f, -2.18f, 0f), bold: true);
            _turnStatusLabel = MakeText(_root, "TurnStatusLabel", font, fontMat,
                "", 1.0f, new Color(0.75f, 0.95f, 1f),
                new Vector3(0f, -2.55f, 0f), bold: true);
            _pickReceiptLabel = MakeText(_root, "PickReceiptLabel", font, fontMat,
                "", 1.0f, new Color(0.55f, 1f, 0.6f),
                new Vector3(0f, -2.88f, 0f), bold: true);
            _pickReceiptLabel.alpha = 0f;

            _root.SetActive(false);
        }

        // ── Backdrop art ──────────────────────────────────────────────────────

        private void BuildBackdropArt(float camW, float camH)
        {
            if (HudManager.Instance == null) return;

            _backdropArt = new GameObject("DraftBackdropArt");
            _backdropArt.transform.SetParent(HudManager.Instance.transform, false);
            _backdropArt.transform.localPosition = new Vector3(0f, 0f, 0.85f);

            MakeBackdropHalo(camW, camH);

            _backdropArt.SetActive(false);
        }

        private void MakeBackdropHalo(float camW, float camH)
        {
            if (_backdropArt == null) return;
            var halo = new GameObject("DraftBackdropHalo");
            halo.transform.SetParent(_backdropArt.transform, false);
            halo.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            halo.transform.localScale = new Vector3(camW * 0.72f, camH * 0.32f, 1f);

            _backdropHaloRenderer = halo.AddComponent<SpriteRenderer>();
            _backdropHaloRenderer.sprite = MakeSoftGlowSprite();
            _backdropHaloRenderer.color = new Color(0.18f, 0.9f, 1f, 0.075f);
            _backdropHaloRenderer.sortingLayerName = "UI";
            _backdropHaloRenderer.sortingOrder = 43;
        }

        private void UpdateBackdropMotion()
        {
            if (_backdropArt == null || !_backdropArt.activeSelf) return;
            _backdropArt.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_waitAnimTime * 0.18f) * 0.45f);
            if (_backdropHaloRenderer != null)
            {
                var c = _backdropHaloRenderer.color;
                c.a = 0.045f + (Mathf.Sin(_waitAnimTime * 0.7f) + 1f) * 0.018f;
                _backdropHaloRenderer.color = c;
            }
        }

        // ── Role prefab ───────────────────────────────────────────────────────

        private static bool EnsureRolePrefab()
        {
            if (_cachedRolePrefab != null) return true;
            try
            {
                var bundle = TouAssets.MainBundle;
                if (bundle == null) return false;
                var prefab = bundle.LoadAsset("SelectRoleGame")?.TryCast<GameObject>();
                if (prefab == null) return false;
                var holderGo = prefab.transform.Find("RoleCardHolder");
                if (holderGo == null) return false;
                _cachedRolePrefab = holderGo.gameObject;
                return true;
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftStatusOverlay] Prefab load failed: {ex.Message}");
                return false;
            }
        }

        // ── Role card ─────────────────────────────────────────────────────────

        private void ShowRoleCard(ushort roleId)
        {
            DestroyRoleCard();
            if (!EnsureRolePrefab() || HudManager.Instance == null) return;

            var    role     = DraftUiManager.ResolveRole(roleId);
            string roleName = role?.NiceName ?? $"Role {roleId}";
            string teamName = DraftUiManager.GetTeamLabel(role);
            Sprite icon     = DraftUiManager.GetRoleIcon(role);
            Color  color    = DraftUiManager.GetRoleColor(role);

            _roleCardNewRoleObj = UnityEngine.Object.Instantiate(
                _cachedRolePrefab, HudManager.Instance.transform);
            _roleCardNewRoleObj.name = "DraftChosenRoleCard";

            if (_roleCardNewRoleObj.transform.childCount == 0) { DestroyRoleCard(); return; }
            var actualCard = _roleCardNewRoleObj.transform.GetChild(0);
            if (actualCard.childCount < 3) { DestroyRoleCard(); return; }

            var roleText      = actualCard.GetChild(0).GetComponent<TextMeshPro>();
            var roleImage     = actualCard.GetChild(1).GetComponent<SpriteRenderer>();
            var teamText      = actualCard.GetChild(2).GetComponent<TextMeshPro>();
            var passiveButton = actualCard.GetComponent<PassiveButton>();
            var rollover      = actualCard.GetComponent<ButtonRolloverHandler>();

            _roleCardNewRoleObj.transform.localPosition = CardHudPos;
            _roleCardNewRoleObj.transform.localScale    = Vector3.one * CardScale;
            _roleCardNewRoleObj.transform.localRotation = Quaternion.Euler(0f, 0f, CardTiltDeg);

            if (roleText != null) roleText.text = roleName;
            if (teamText != null)
            {
                teamText.text             = teamName;
                teamText.fontSizeMax      = TeamNameFontSize;
                teamText.enableAutoSizing = true;
                teamText.color            = GetTeamColor(teamName);
            }
            if (roleImage != null) { roleImage.sprite = icon; roleImage.SetSizeLimit(2.8f); roleImage.color = Color.white; }

            var cardBg = actualCard.GetComponent<SpriteRenderer>();
            if (cardBg   != null) cardBg.color  = color;
            if (rollover != null) { rollover.OutColor = color; rollover.OverColor = Color.white; }
            if (roleText != null) roleText.color = color;

            ApplyRoleCardSorting(_roleCardNewRoleObj.transform);
            MakeLockedInBadge(_roleCardNewRoleObj);
            MakeChosenRoleIdleGlow(actualCard, teamName);
            _roleCardHovering = false;
            _roleCardIdleDelay = 0.65f;

            var col = actualCard.GetComponent<Collider2D>() as Collider2D
                   ?? actualCard.GetComponent<BoxCollider2D>() as Collider2D;
            if (col == null)
            {
                var box  = actualCard.gameObject.AddComponent<BoxCollider2D>();
                box.size = new Vector2(4f, 6f);
                col      = box;
            }

            if (passiveButton != null)
            {
                passiveButton.enabled   = true;
                passiveButton.Colliders = new Collider2D[] { col };

                passiveButton.OnClick.RemoveAllListeners();
                ushort capturedId = roleId;
                passiveButton.OnClick.AddListener((System.Action)(() => OpenWiki(capturedId)));

                passiveButton.OnMouseOver.RemoveAllListeners();
                passiveButton.OnMouseOver.AddListener((System.Action)(() =>
                {
                    _roleCardHovering = true;
                    if (_roleCardNewRoleObj != null)
                        _roleCardNewRoleObj.transform.localScale = Vector3.one * (CardScale * 1.08f);
                }));
                passiveButton.OnMouseOut.RemoveAllListeners();
                passiveButton.OnMouseOut.AddListener((System.Action)(() =>
                {
                    _roleCardHovering = false;
                    if (_roleCardNewRoleObj != null)
                        _roleCardNewRoleObj.transform.localScale = Vector3.one * CardScale;
                }));
            }

            _roleCardNewRoleObj.SetActive(true);
            _cardHiddenForMenu = false;
            Coroutines.Start(CoPopInCard(_roleCardNewRoleObj.transform));
            Coroutines.Start(CoSparkleBurst(_roleCardNewRoleObj.transform, color));
            Coroutines.Start(CoChosenAllianceReward(_roleCardNewRoleObj.transform, teamName));
        }

        private static void ApplyRoleCardSorting(Transform current)
        {
            if (current == null) return;

            var tmp = current.GetComponent<TMPro.TMP_Text>();
            if (tmp != null)
            {
                var r = tmp.GetComponent<Renderer>();
                if (r != null) { r.sortingLayerName = "UI"; r.sortingOrder = RoleCardSortingOrder; }
            }

            var sr = current.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingLayerName = "UI";
                sr.sortingOrder     = RoleCardSortingOrder;
            }

            for (int i = 0; i < current.childCount; i++)
                ApplyRoleCardSorting(current.GetChild(i));
        }

        private static void MakeLockedInBadge(GameObject parent)
        {
            if (parent == null || HudManager.Instance == null) return;

            var font = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            var badge = new GameObject("DraftLockedInBadge");
            badge.transform.SetParent(parent.transform, false);
            badge.transform.localPosition = new Vector3(0f, -3.35f, -0.25f);

            var tmp = badge.AddComponent<TextMeshPro>();
            tmp.font = font;
            tmp.fontMaterial = fontMat;
            tmp.fontSize = 1.15f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.text = "<color=#88FF88><b>LOCKED IN</b></color>";

            var r = badge.GetComponent<Renderer>();
            if (r != null) { r.sortingLayerName = "UI"; r.sortingOrder = RoleCardSortingOrder + 1; }
        }

        private void MakeChosenRoleIdleGlow(Transform actualCard, string teamName)
        {
            if (actualCard == null) return;
            var glow = new GameObject("DraftChosenRoleIdleGlow");
            glow.transform.SetParent(actualCard, false);
            glow.transform.localPosition = new Vector3(0f, 0f, -0.72f);
            glow.transform.localScale = new Vector3(4.1f, 5.45f, 1f);

            _roleCardIdleGlowRenderer = glow.AddComponent<SpriteRenderer>();
            _roleCardIdleGlowRenderer.sprite = MakeSoftGlowSprite();
            Color color = GetChosenAllianceEffectColor(teamName);
            _roleCardIdleGlowRenderer.color = new Color(color.r, color.g, color.b, 0.12f);
            _roleCardIdleGlowRenderer.sortingLayerName = "UI";
            _roleCardIdleGlowRenderer.sortingOrder = RoleCardSortingOrder - 2;
        }

        private static IEnumerator CoSparkleBurst(Transform parent, Color baseColor)
        {
            if (parent == null) yield break;

            const int count = 3;
            var sparkles = new List<GameObject>();
            var sparkleRenderers = new List<SpriteRenderer>();
            for (int i = 0; i < count; i++)
            {
                var sparkle = new GameObject("DraftPickSparkle");
                sparkle.transform.SetParent(parent, false);
                sparkle.transform.localPosition = new Vector3(0f, 0.1f, -0.35f);
                sparkle.transform.localScale = Vector3.one * 0.08f;

                var sr = sparkle.AddComponent<SpriteRenderer>();
                sr.sprite = MakeWhiteSprite();
                sr.color = new Color(Mathf.Max(baseColor.r, 0.9f), Mathf.Max(baseColor.g, 0.82f), Mathf.Max(baseColor.b, 0.25f), 0.9f);
                sr.sortingLayerName = "UI";
                sr.sortingOrder = RoleCardSortingOrder + 2;
                sparkles.Add(sparkle);
                sparkleRenderers.Add(sr);
            }

            const float duration = 0.48f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / duration);
                for (int i = 0; i < sparkles.Count; i++)
                {
                    var sparkle = sparkles[i];
                    if (sparkle == null) continue;
                    float angle = i * Mathf.PI * 2f / count;
                    Vector3 end = new Vector3(Mathf.Cos(angle) * 1.35f, 0.18f + Mathf.Sin(angle) * 0.85f, -0.35f);
                    sparkle.transform.localPosition = Vector3.Lerp(new Vector3(0f, 0.1f, -0.35f), end, EaseOutBack(u));
                    sparkle.transform.localScale = Vector3.one * Mathf.Lerp(0.08f, 0.02f, u);

                    var sr = i < sparkleRenderers.Count ? sparkleRenderers[i] : null;
                    if (sr == null) continue;
                    var c = sr.color;
                    sr.color = new Color(c.r, c.g, c.b, 1f - u);
                }
                yield return null;
            }

            foreach (var sparkle in sparkles)
                if (sparkle != null)
                    try { Destroy(sparkle); } catch { }
        }

        private static IEnumerator CoChosenAllianceReward(Transform parent, string teamName)
        {
            if (parent == null) yield break;

            var effect = new GameObject("DraftChosenAllianceEffect");
            effect.transform.SetParent(parent, false);
            effect.transform.localPosition = new Vector3(0f, 0.18f, -0.45f);
            effect.transform.localScale = Vector3.one * 0.14f;

            var sr = effect.AddComponent<SpriteRenderer>();
            sr.sprite = MakeWhiteSprite();
            sr.color = GetChosenAllianceEffectColor(teamName);
            sr.sortingLayerName = "UI";
            sr.sortingOrder = RoleCardSortingOrder - 1;

            Color color = GetChosenAllianceEffectColor(teamName);
            Vector3 endScale = GetChosenAllianceEffectShape(teamName);
            float endRotation = GetChosenAllianceEffectRotation(teamName);

            const float duration = 0.55f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (effect == null) yield break;
                float u = Mathf.Clamp01(t / duration);
                float eased = Mathf.SmoothStep(0f, 1f, u);
                effect.transform.localScale = Vector3.Lerp(Vector3.one * 0.14f, endScale, eased);
                effect.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-endRotation * 0.35f, endRotation, eased));
                sr.color = new Color(color.r, color.g, color.b, Mathf.Lerp(color.a, 0f, u));
                yield return null;
            }

            if (effect != null)
                try { Destroy(effect); } catch { }
        }

        private static Color GetChosenAllianceEffectColor(string teamName)
        {
            string lower = teamName?.ToLowerInvariant() ?? string.Empty;
            if (lower.Contains("impostor") || lower.Contains("imposter")) return new Color(1f, 0.05f, 0.05f, 0.5f);
            if (lower.Contains("neutral")) return new Color(0.76f, 0.52f, 1f, 0.46f);
            if (lower.Contains("crewmate")) return new Color(0f, 0.95f, 1f, 0.44f);
            return new Color(1f, 0.84f, 0.2f, 0.42f);
        }

        private static Vector3 GetChosenAllianceEffectShape(string teamName)
        {
            string lower = teamName?.ToLowerInvariant() ?? string.Empty;
            if (lower.Contains("impostor") || lower.Contains("imposter")) return new Vector3(2.9f, 0.12f, 1f);
            if (lower.Contains("neutral")) return new Vector3(1.55f, 1.55f, 1f);
            if (lower.Contains("crewmate")) return new Vector3(2.35f, 0.22f, 1f);
            return new Vector3(1.85f, 0.18f, 1f);
        }

        private static float GetChosenAllianceEffectRotation(string teamName)
        {
            string lower = teamName?.ToLowerInvariant() ?? string.Empty;
            if (lower.Contains("impostor") || lower.Contains("imposter")) return -28f;
            if (lower.Contains("neutral")) return 45f;
            return 0f;
        }

        // ── Wiki ──────────────────────────────────────────────────────────────

        private void OpenWiki(ushort roleId)
        {
            try
            {
                var r = DraftUiManager.ResolveRole(roleId);
                if (r is not TownOfUs.Modules.Wiki.IWikiDiscoverable wikiTarget)
                {
                    DraftModePlugin.Logger.LogWarning($"[DraftStatusOverlay] Role {roleId} not IWikiDiscoverable");
                    return;
                }

                if (_roleCardNewRoleObj != null)
                    _roleCardNewRoleObj.SetActive(false);

                var wiki = TownOfUs.Modules.Wiki.IngameWikiMinigame.Create();
                wiki.Begin(null);
                wiki.OpenFor(wikiTarget);

                Coroutines.Start(CoWaitForWikiDestroyed(wiki));
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftStatusOverlay] Wiki open failed: {ex.Message}");
                if (_roleCardNewRoleObj != null)
                    _roleCardNewRoleObj.SetActive(true);
            }
        }

        [HideFromIl2Cpp]
        private IEnumerator CoWaitForWikiDestroyed(TownOfUs.Modules.Wiki.IngameWikiMinigame wiki)
        {
            while (wiki != null)
                yield return null;
            if (_roleCardNewRoleObj != null && !IsAnyMenuOpen())
                _roleCardNewRoleObj.SetActive(true);
        }

        // ── Menu detection ────────────────────────────────────────────────────

        private static bool IsAnyMenuOpen()
        {
            try
            {
                if (Minigame.Instance != null)                return true;
                if (PlayerCustomizationMenu.Instance != null) return true;
                if (GameSettingMenu.Instance != null)         return true;

                var hud = HudManager.Instance;
                if (hud != null)
                {
                    if (hud.GameMenu != null && hud.GameMenu.IsOpen)         return true;
                    if (hud.Chat != null && hud.Chat.IsOpenOrOpening)        return true;
                }

                if (FriendsListUI.Instance != null && FriendsListUI.Instance.IsOpen) return true;
            }
            catch { }

            return false;
        }

        // ── Role card helpers ─────────────────────────────────────────────────

        private void DestroyRoleCard()
        {
            if (_roleCardNewRoleObj != null)
            {
                try { UnityEngine.Object.Destroy(_roleCardNewRoleObj); } catch { }
                _roleCardNewRoleObj = null;
            }
            _roleCardIdleGlowRenderer = null;
            _roleCardHovering = false;
            _roleCardIdleDelay = 0f;
            _cardHiddenForMenu = false;
        }

        private static IEnumerator CoPopInCard(Transform holder)
        {
            holder.localScale = Vector3.zero;
            float duration    = 0.25f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float s = Mathf.LerpUnclamped(0f, CardScale, EaseOutBack(t / duration));
                holder.localScale = Vector3.one * s;
                yield return null;
            }
            holder.localScale = Vector3.one * CardScale;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private static Color GetTeamColor(string teamName)
        {
            if (string.IsNullOrEmpty(teamName)) return Color.white;
            string lower = teamName.ToLowerInvariant();
            if (lower.Contains("crewmate"))                               return new Color32(0,   255, 255, 255);
            if (lower.Contains("impostor") || lower.Contains("imposter")) return new Color32(255,   0,   0, 255);
            if (lower.Contains("neutral"))                                return new Color32(180, 180, 180, 255);
            return Color.white;
        }

        // ── Update ────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_currentState == OverlayState.Hidden) return;

            // ── Role card menu hide/show ───────────────────────────────────────
            if (_roleCardNewRoleObj != null)
            {
                bool menuOpen = IsAnyMenuOpen();
                if (menuOpen && _roleCardNewRoleObj.activeSelf)
                {
                    _roleCardNewRoleObj.SetActive(false);
                    _cardHiddenForMenu = true;
                }
                else if (!menuOpen && _cardHiddenForMenu && !_roleCardNewRoleObj.activeSelf)
                {
                    _roleCardNewRoleObj.SetActive(true);
                    _cardHiddenForMenu = false;
                }
            }

            if (_currentState == OverlayState.BackgroundOnly)
                _waitAnimTime += Time.deltaTime;
            UpdateChosenCardIdleMotion();

            // ── Waiting state updates ─────────────────────────────────────────
            if (_currentState == OverlayState.Waiting)
            {
                if (_root == null) BuildUI();
                if (_root == null) return;

                if (DraftManager.IsDraftActive)
                {
                    int mySlot      = DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
                    int pickerSlot  = -1;
                    int pickerCount = 0;
                    foreach (var s in DraftManager.GetActivePickerStates())
                    {
                        if (s == null || !s.IsPickingNow) continue;
                        pickerCount++;
                        if (pickerSlot < 0) pickerSlot = s.SlotNumber;
                    }

                    if (mySlot != _cachedMySlot || pickerSlot != _cachedPickerSlot || pickerCount != _cachedPickerCount)
                    {
                        _cachedMySlot      = mySlot;
                        _cachedPickerSlot  = pickerSlot;
                        _cachedPickerCount = pickerCount;
                        _waitAnimTime      = 0f;
                        UpdateContent();
                    }

                    _waitAnimTime += Time.deltaTime;
                    UpdateWaitingMotion();
                }
            }

            if (_pendingRoleId.HasValue && _pendingRoleId != _shownRoleId)
            {
                _shownRoleId   = _pendingRoleId;
                _pendingRoleId = null;
                ShowRoleCard(_shownRoleId.Value);
            }
        }

        private void UpdateContent()
        {
            if (_root == null) return;

            int mySlot      = DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
            int pickerSlot  = -1;
            int pickerCount = 0;
            bool isMyTurn   = false;
            foreach (var s in DraftManager.GetActivePickerStates())
            {
                if (s == null || !s.IsPickingNow) continue;
                pickerCount++;
                if (pickerSlot < 0) pickerSlot = s.SlotNumber;
                if (s.PlayerId == PlayerControl.LocalPlayer.PlayerId) isMyTurn = true;
            }

            if (_yourNumberValue != null)
                _yourNumberValue.text = mySlot > 0 ? mySlot.ToString() : "?";
            if (_nowPickingValue != null)
                _nowPickingValue.text = pickerCount > 1 ? "MULTI" : (pickerSlot > 0 ? pickerSlot.ToString() : "?");
            if (_nowPickingValue != null)
                _nowPickingValue.color = isMyTurn ? new Color(0.1f, 1f, 0.4f) : new Color(1f, 0.85f, 0.1f);
            if (_nowPickingLabel != null)
                _nowPickingLabel.text = isMyTurn ? "YOUR TURN!" : (pickerCount > 1 ? "NOW PICKING (MULTI):" : "NOW PICKING:");
            if (_queueValue != null)
                _queueValue.text = BuildQueueText();
            if (_turnStatusLabel != null)
                _turnStatusLabel.text = BuildTurnDistanceText(mySlot, isMyTurn);
        }

        private void UpdateWaitingMotion()
        {
            UpdateBackdropMotion();

            float duration = Mathf.Max(1f, DraftManager.TurnDuration);
            float progress = AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost
                ? 1f - Mathf.Clamp01(DraftManager.TurnTimeLeft / duration)
                : Mathf.Clamp01(_waitAnimTime / duration);
            SetProgress(progress);
            UpdatePickReceiptMotion();
        }

        private void ShowSlotLocked(int slot)
        {
            if (_root == null) BuildUI();
            if (_pickReceiptLabel == null) return;

            int mySlot = DraftManager.GetSlotForPlayer(PlayerControl.LocalPlayer.PlayerId);
            _pickReceiptLabel.text = slot == mySlot
                ? "<color=#88FF88><b>YOUR PICK LOCKED IN</b></color>"
                : $"<color=#88FF88><b>SLOT {slot} LOCKED IN</b></color>";
            _pickReceiptLabel.alpha = 1f;
            _pickReceiptTimer = 1.25f;
            UpdateContent();
        }

        private void UpdatePickReceiptMotion()
        {
            if (_pickReceiptLabel == null) return;
            if (_pickReceiptTimer <= 0f)
            {
                _pickReceiptLabel.alpha = 0f;
                return;
            }

            _pickReceiptTimer -= Time.deltaTime;
            float age = 1.25f - _pickReceiptTimer;
            float fade = _pickReceiptTimer < 0.35f ? Mathf.Clamp01(_pickReceiptTimer / 0.35f) : 1f;
            _pickReceiptLabel.alpha = fade;
            _pickReceiptLabel.transform.localScale = Vector3.one * (1f + Mathf.Sin(age * 9f) * 0.035f);
        }

        private void UpdateChosenCardIdleMotion()
        {
            if (_roleCardNewRoleObj == null || !_roleCardNewRoleObj.activeSelf) return;
            if (_roleCardIdleDelay > 0f)
            {
                _roleCardIdleDelay -= Time.deltaTime;
                return;
            }

            float bob = Mathf.Sin(_waitAnimTime * 1.18f) * 0.035f;
            float tilt = Mathf.Sin(_waitAnimTime * 0.82f) * 0.75f;
            float breathe = 1f + Mathf.Sin(_waitAnimTime * 1.42f) * 0.012f;
            _roleCardNewRoleObj.transform.localPosition = CardHudPos + new Vector3(0f, bob, 0f);
            _roleCardNewRoleObj.transform.localRotation = Quaternion.Euler(0f, 0f, CardTiltDeg + tilt);
            if (!_roleCardHovering)
                _roleCardNewRoleObj.transform.localScale = Vector3.one * (CardScale * breathe);

            if (_roleCardIdleGlowRenderer != null)
            {
                var c = _roleCardIdleGlowRenderer.color;
                c.a = 0.08f + (Mathf.Sin(_waitAnimTime * 1.65f) + 1f) * 0.035f;
                _roleCardIdleGlowRenderer.color = c;
                float glow = 1f + (Mathf.Sin(_waitAnimTime * 1.05f) + 1f) * 0.035f;
                _roleCardIdleGlowRenderer.transform.localScale = new Vector3(4.1f * glow, 5.45f * glow, 1f);
            }
        }

        private void SetProgress(float progress)
        {
            if (_progressFill == null) return;
            progress = Mathf.Clamp01(progress);
            float width = Mathf.Max(0.01f, ProgressBarWidth * progress);
            _progressFill.transform.localScale = new Vector3(width, ProgressBarHeight, 1f);
            _progressFill.transform.localPosition = new Vector3(
                -ProgressBarWidth * 0.5f + width * 0.5f,
                -1.58f,
                0f);
        }

        private static string BuildQueueText()
        {
            var sb = new System.Text.StringBuilder();
            int shown = 0;
            int remaining = 0;
            foreach (int slot in DraftManager.TurnOrder)
            {
                var state = DraftManager.GetStateForSlot(slot);
                if (state == null || state.HasPicked) continue;
                remaining++;
                if (shown >= 4) continue;

                string color = state.IsPickingNow ? "#FFD700" : "#8FD8FF";
                string suffix = state.IsPickingNow ? "*" : "";
                if (sb.Length > 0) sb.Append("   ");
                sb.Append($"<color={color}>PICK #{slot}{suffix}</color>");
                shown++;
            }

            if (remaining > shown)
                sb.Append($"   <color=#AAAAAA>+{remaining - shown}</color>");
            return sb.Length > 0 ? sb.ToString() : "<color=#88FF88>LOCKED IN</color>";
        }

        private static string BuildTurnDistanceText(int mySlot, bool isMyTurn)
        {
            if (mySlot <= 0) return string.Empty;
            if (isMyTurn) return "<color=#88FF88><b>YOUR TURN NOW — pick a role</b></color>";

            int picksAway = 0;
            bool foundMe = false;
            foreach (int slot in DraftManager.TurnOrder)
            {
                var state = DraftManager.GetStateForSlot(slot);
                if (state == null || state.HasPicked) continue;
                if (slot == mySlot)
                {
                    foundMe = true;
                    break;
                }
                if (!state.IsPickingNow) picksAway++;
            }

            if (!foundMe) return "<color=#88FF88><b>LOCKED IN — wait for launch</b></color>";
            if (picksAway <= 0) return "<color=#FFD700><b>UP NEXT — get ready</b></color>";
            return $"<color=#8FD8FF><b>{picksAway} PICKS AWAY</b></color>";
        }

        private void UpdateVisibility()
        {
            if (_root == null && _currentState != OverlayState.Hidden) BuildUI();
            if (_root == null) return;

            if (_currentState == OverlayState.Hidden)
            {
                _root.SetActive(false);
                if (_bgOverlay != null) _bgOverlay.SetActive(false);
                if (_backdropArt != null) _backdropArt.SetActive(false);
                DestroyOutroHandoff();
                DestroyRoleCard();
                _pendingRoleId = null;
                _shownRoleId   = null;
                _playedStartCeremony = false;
                RestoreHudElements();
            }
            else if (_currentState == OverlayState.Waiting)
            {
                _root.SetActive(true);
                if (_bgOverlay != null) _bgOverlay.SetActive(true);
                if (_backdropArt != null) _backdropArt.SetActive(true);
                HideHudElements();
                if (!_playedStartCeremony)
                {
                    _playedStartCeremony = true;
                    Coroutines.Start(CoShowDraftStartCeremony());
                }
            }
            else if (_currentState == OverlayState.BackgroundOnly)
            {
                _root.SetActive(false);
                if (_bgOverlay != null) _bgOverlay.SetActive(true);
                if (_backdropArt != null) _backdropArt.SetActive(true);
                HideHudElements();
            }
        }

        // ── Draft outro handoff ──────────────────────────────────────────────

        public static IEnumerator CoPlayDraftOutroHandoff()
        {
            if (HudManager.Instance == null || HudManager.Instance.TaskPanel == null) yield break;

            var font = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            DestroyOutroHandoff();
            var handoff = new GameObject("DraftOutroHandoff");
            handoff.transform.SetParent(HudManager.Instance.transform, false);
            handoff.transform.localPosition = new Vector3(0f, 0f, -24f);
            handoff.transform.localScale = Vector3.one * 0.94f;
            _outroHandoffRoot = handoff;

            var wash = new GameObject("DraftOutroLaunchBridge");
            wash.transform.SetParent(handoff.transform, false);
            wash.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            wash.transform.localScale = new Vector3(8.5f, 4.8f, 1f);
            var washRenderer = wash.AddComponent<SpriteRenderer>();
            washRenderer.sprite = MakeSoftGlowSprite();
            washRenderer.color = new Color(0f, 0.95f, 1f, 0f);
            washRenderer.sortingLayerName = "UI";
            washRenderer.sortingOrder = 88;
            _outroLaunchBridgeRenderer = washRenderer;

            var outroParticles = new List<SpriteRenderer>();
            const int outroParticleCount = 6;
            int outroLaneCount = Mathf.Max(1, Mathf.CeilToInt(outroParticleCount * 0.5f));
            for (int i = 0; i < outroParticleCount; i++)
            {
                var particle = new GameObject("DraftOutroHandoffParticle");
                particle.transform.SetParent(handoff.transform, false);
                bool leftSide = i < outroLaneCount;
                int lane = i % outroLaneCount;
                float laneStep = outroLaneCount <= 1 ? 0f : 1.36f / (outroLaneCount - 1);
                particle.transform.localPosition = new Vector3(leftSide ? -3.25f : 3.25f, -0.85f + lane * laneStep, 0f);
                particle.transform.localScale = Vector3.one * (0.055f + (i % 3) * 0.014f);
                var pr = particle.AddComponent<SpriteRenderer>();
                pr.sprite = MakeSoftGlowSprite();
                pr.color = i % 3 == 0 ? new Color(1f, 0.84f, 0.16f, 0f) : (i % 3 == 1 ? new Color(0f, 0.95f, 1f, 0f) : new Color(0.75f, 0.5f, 1f, 0f));
                pr.sortingLayerName = "UI";
                pr.sortingOrder = 90;
                outroParticles.Add(pr);
            }

            var title = MakeText(handoff, "DraftOutroTitle", font, fontMat,
                "ROLES LOCKED", 2.15f, new Color(1f, 0.85f, 0.12f),
                new Vector3(0f, 0.22f, -0.05f), bold: true);
            var subtitle = MakeText(handoff, "DraftOutroSubtitle", font, fontMat,
                "Entering game...", 1.05f, new Color(0.75f, 1f, 1f),
                new Vector3(0f, -0.25f, -0.05f), bold: false);
            title.alpha = 0f;
            subtitle.alpha = 0f;
            var titleRenderer = title.GetComponent<Renderer>();
            if (titleRenderer != null) { titleRenderer.sortingLayerName = "UI"; titleRenderer.sortingOrder = 91; }
            var subtitleRenderer = subtitle.GetComponent<Renderer>();
            if (subtitleRenderer != null) { subtitleRenderer.sortingLayerName = "UI"; subtitleRenderer.sortingOrder = 91; }

            const float inDuration = 0.38f;
            for (float t = 0f; t < inDuration; t += Time.deltaTime)
            {
                if (handoff == null) yield break;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / inDuration));
                handoff.transform.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, u);
                wash.transform.localScale = new Vector3(Mathf.Lerp(6.2f, 9.0f, u), Mathf.Lerp(3.2f, 5.1f, u), 1f);
                washRenderer.color = new Color(0f, 0.95f, 1f, Mathf.Lerp(0f, 0.22f, u));
                for (int i = 0; i < outroParticles.Count; i++)
                {
                    var p = outroParticles[i];
                    if (p == null) continue;
                    float side = i < 6 ? -1f : 1f;
                    float wave = Mathf.Sin(u * Mathf.PI + i * 0.7f) * 0.24f;
                    Vector3 start = new Vector3(side * 3.25f, -0.85f + (i % 6) * 0.34f, 0f);
                    Vector3 end = new Vector3(side * 0.72f, -0.28f + wave, 0f);
                    p.transform.localPosition = Vector3.Lerp(start, end, u);
                    var c = p.color;
                    p.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0f, 0.85f, u));
                }
                title.alpha = u;
                subtitle.alpha = u;
                yield return null;
            }

            yield return new WaitForSeconds(0.22f);

            const float outDuration = 0.22f;
            for (float t = 0f; t < outDuration; t += Time.deltaTime)
            {
                if (handoff == null) yield break;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / outDuration));
                handoff.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.04f, u);
                washRenderer.color = new Color(0f, 0.95f, 1f, Mathf.Lerp(0.22f, 0.14f, u));
                for (int i = 0; i < outroParticles.Count; i++)
                {
                    var p = outroParticles[i];
                    if (p == null) continue;
                    float side = i < 6 ? -1f : 1f;
                    float drift = Mathf.Cos(u * Mathf.PI + i * 0.55f) * 0.18f;
                    Vector3 start = new Vector3(side * 0.72f, -0.28f + drift, 0f);
                    Vector3 end = new Vector3(side * 0.2f, 0.08f + drift, 0f);
                    p.transform.localPosition = Vector3.Lerp(start, end, u);
                    p.transform.localScale = Vector3.one * Mathf.Lerp(0.07f, 0.018f, u);
                    var c = p.color;
                    p.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.85f, 0f, u));
                }
                title.alpha = 1f - u;
                subtitle.alpha = 1f - u;
                yield return null;
            }

            foreach (var p in outroParticles)
                if (p != null)
                    try { Destroy(p.gameObject); } catch { }
        }

        public static IEnumerator CoReleaseDraftOutroLaunchBridge()
        {
            var handoff = _outroHandoffRoot;
            var bridge = _outroLaunchBridgeRenderer;
            if (handoff == null) yield break;

            float startAlpha = bridge != null ? bridge.color.a : 0f;
            const float duration = 0.34f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (handoff == null) yield break;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                handoff.transform.localScale = Vector3.one * Mathf.Lerp(1.04f, 1.08f, u);
                if (bridge != null)
                {
                    var color = bridge.color;
                    bridge.color = new Color(color.r, color.g, color.b, Mathf.Lerp(startAlpha, 0f, u));
                }
                yield return null;
            }

            DestroyOutroHandoff();
        }

        public static IEnumerator CoHoldDraftOutroForRemoteLaunch()
        {
            var handoff = _outroHandoffRoot;
            if (handoff == null) yield break;

            float elapsed = 0f;
            const float maxHold = 2.4f;
            while (handoff != null &&
                   AmongUsClient.Instance != null &&
                   !AmongUsClient.Instance.AmHost &&
                   AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Joined &&
                   elapsed < maxHold)
            {
                elapsed += Time.deltaTime;
                float pulse = (Mathf.Sin(elapsed * 5.2f) + 1f) * 0.5f;
                handoff.transform.localScale = Vector3.one * (1.035f + pulse * 0.018f);
                if (_outroLaunchBridgeRenderer != null)
                {
                    var color = _outroLaunchBridgeRenderer.color;
                    _outroLaunchBridgeRenderer.color = new Color(color.r, color.g, color.b, 0.13f + pulse * 0.045f);
                }
                yield return null;
            }

            yield return CoReleaseDraftOutroLaunchBridge();
        }

        private static void DestroyOutroHandoff()
        {
            if (_outroHandoffRoot != null)
                try { Destroy(_outroHandoffRoot); } catch { }
            _outroHandoffRoot = null;
            _outroLaunchBridgeRenderer = null;
        }

        // ── Draft start ceremony ──────────────────────────────────────────────

        private IEnumerator CoShowDraftStartCeremony()
        {
            if (_root == null || HudManager.Instance == null) yield break;

            var font = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            var ceremony = new GameObject("DraftStartCeremonyBurst");
            ceremony.transform.SetParent(_root.transform, false);
            ceremony.transform.localPosition = new Vector3(0f, 1.55f, -0.2f);
            ceremony.transform.localScale = Vector3.one * 0.65f;

            var spotlight = new GameObject("DraftStartSpotlight");
            spotlight.transform.SetParent(ceremony.transform, false);
            spotlight.transform.localPosition = new Vector3(0f, -0.05f, 0.12f);
            spotlight.transform.localScale = new Vector3(2.35f, 1.05f, 1f);
            var spotlightRenderer = spotlight.AddComponent<SpriteRenderer>();
            spotlightRenderer.sprite = MakeSoftGlowSprite();
            spotlightRenderer.color = new Color(1f, 0.84f, 0.16f, 0f);
            spotlightRenderer.sortingLayerName = "UI";
            spotlightRenderer.sortingOrder = 49;

            var title = MakeText(ceremony, "DraftStartTitle", font, fontMat,
                "DRAFT START!", 2.25f, new Color(1f, 0.82f, 0.08f),
                Vector3.zero, bold: true);
            var subtitle = MakeText(ceremony, "DraftStartSubtitle", font, fontMat,
                "Pick order revealed", 1.0f, new Color(0.75f, 0.95f, 1f),
                new Vector3(0f, -0.42f, 0f), bold: false);

            for (float t = 0f; t < 0.22f; t += Time.deltaTime)
            {
                if (ceremony == null) yield break;
                float u = Mathf.Clamp01(t / 0.22f);
                float pulse = EaseOutBack(u);
                ceremony.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.65f, 1f, pulse);
                if (spotlight != null && spotlightRenderer != null)
                {
                    spotlight.transform.localScale = new Vector3(Mathf.Lerp(2.35f, 4.25f, pulse), Mathf.Lerp(1.05f, 1.85f, pulse), 1f);
                    spotlightRenderer.color = new Color(1f, 0.84f, 0.16f, Mathf.Lerp(0f, 0.26f, u));
                }
                title.alpha = u;
                subtitle.alpha = u;
                yield return null;
            }

            yield return new WaitForSeconds(0.55f);

            for (float t = 0f; t < 0.18f; t += Time.deltaTime)
            {
                if (ceremony == null) yield break;
                float u = Mathf.Clamp01(t / 0.18f);
                ceremony.transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.96f, u);
                if (spotlight != null && spotlightRenderer != null)
                {
                    spotlight.transform.localScale = new Vector3(Mathf.Lerp(4.25f, 4.7f, u), Mathf.Lerp(1.85f, 2.05f, u), 1f);
                    spotlightRenderer.color = new Color(1f, 0.84f, 0.16f, Mathf.Lerp(0.26f, 0f, u));
                }
                title.alpha = 1f - u;
                subtitle.alpha = 1f - u;
                yield return null;
            }

            if (ceremony != null)
                try { Destroy(ceremony); } catch { }
        }

        // ── HUD element hiding ────────────────────────────────────────────────

        private void HideHudElements()
        {
            _hiddenHudChildren.RemoveAll(go => go == null);

            var gsm = UnityEngine.Object.FindObjectOfType<GameStartManager>();
            if (gsm != null && gsm.gameObject.activeSelf)
            {
                gsm.gameObject.SetActive(false);
                if (!_hiddenHudChildren.Contains(gsm.gameObject))
                    _hiddenHudChildren.Add(gsm.gameObject);
            }

            var lobbyInfoPane = UnityEngine.Object.FindObjectOfType<LobbyInfoPane>();
            if (lobbyInfoPane != null && lobbyInfoPane.gameObject.activeSelf)
            {
                lobbyInfoPane.gameObject.SetActive(false);
                if (!_hiddenHudChildren.Contains(lobbyInfoPane.gameObject))
                    _hiddenHudChildren.Add(lobbyInfoPane.gameObject);
            }
        }

        private void RestoreHudElements()
        {
            foreach (var go in _hiddenHudChildren)
                if (go != null)
                    try { go.SetActive(true); } catch { }
            _hiddenHudChildren.Clear();
        }

        // ── Text factory ──────────────────────────────────────────────────────

        private static TextMeshPro MakeText(
            GameObject parent, string name,
            TMP_FontAsset font, Material fontMat,
            string text, float fontSize, Color color,
            Vector3 offset, bool bold)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = offset;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font               = font;
            tmp.fontMaterial       = fontMat;
            tmp.fontSize           = fontSize;
            tmp.color              = color;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.fontStyle          = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.enableWordWrapping = false;
            tmp.text               = text;

            var r = go.GetComponent<Renderer>();
            if (r != null) { r.sortingLayerName = "UI"; r.sortingOrder = 50; }
            return tmp;
        }

        // ── Sprite factories ─────────────────────────────────────────────────

        private static Sprite MakeSoftGlowSprite() => DraftUiSprites.SoftGlow();

        private static Sprite MakeWhiteSprite() => DraftUiSprites.White();
    }

    // ── PingTracker Harmony patches ───────────────────────────────────────────
    //
    // Harmony postfix execution order: HIGHER priority number runs FIRST.
    //   Priority.First = 800  (us, prefix — skips vanilla Update)
    //   Priority.Last  = 0    (Reactor and TOU-Mira postfixes append text here)
    //   int.MinValue          (our postfix — guaranteed to run dead last, wipes text)
    //
    // Two separate patch classes are used because Harmony does not support
    // mixing [HarmonyPrefix] and [HarmonyPostfix] with different priorities
    // inside the same class reliably across all versions.

    [HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
    public static class PingTrackerDraftPrefix
    {
        // Runs before everything — skips the vanilla Update so "PING: X ms"
        // is never written while draft is active.
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix()
        {
            return !DraftManager.IsDraftActive; // false = skip original
        }
    }

    [HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
    public static class PingTrackerDraftPostfix
    {
        // int.MinValue is lower than Priority.Last (0), so this postfix runs
        // after Reactor's and TOU-Mira's Priority.Last postfixes have both
        // finished appending their mod-list and region text, then wipes it all.
        [HarmonyPostfix]
        [HarmonyPriority(int.MinValue)]
        public static void Postfix(PingTracker __instance)
        {
            if (!DraftManager.IsDraftActive) return;
            if (__instance.text != null)
                __instance.text.text = string.Empty;
        }
    }
}