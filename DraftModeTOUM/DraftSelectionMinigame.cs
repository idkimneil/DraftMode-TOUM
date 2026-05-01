using System;
using System.Collections;
using System.Collections.Generic;
using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using Reactor.Utilities;
using Reactor.Utilities.Attributes;
using TMPro;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;
using UnityEngine.Events;


namespace DraftModeTOUM
{
    [RegisterInIl2Cpp]
    public class DraftScreenController(IntPtr ip) : MonoBehaviour(ip)
    {
        public static DraftScreenController Instance { get; private set; }

        private sealed class DraftCardVisuals
        {
            public SpriteRenderer Aura;
        }

        private sealed class DraftCardRenderCache
        {
            public readonly List<SpriteRenderer> Sprites = new();
            public readonly List<TMP_Text> Texts = new();
        }

        private sealed class DraftCardIdleCache
        {
            public Transform Card;
            public Vector3 BasePosition;
            public Quaternion BaseRotation;
        }

        private static readonly Dictionary<Transform, DraftCardVisuals> _cardVisuals = new();

        private GameObject _screenRoot;
        private ushort[] _offeredRoleIds;
        private bool _hasPicked;
        private TextMeshPro _statusText;      // "Pick Your Role!" — top, kept as-is
        private TextMeshPro _timerText;       // "# seconds remain" — bottom center, new
        private GameObject _timerRoot;        // holder for the bottom timer labe
        private GameObject _selectionBackdrop;
        private SpriteRenderer _selectionBackdropWash;
        private SpriteRenderer _selectionBackdropBeam;
        private SpriteRenderer _selectionBackdropHorizon;
        private SpriteRenderer _selectionBackdropFlash;
        private GameObject _roleInfoRoot;
        private SpriteRenderer _roleInfoGlow;
        private TextMeshPro _roleInfoDescription;
        private readonly List<SpriteRenderer> _selectionBackdropParticles = new();
        private readonly List<Vector3> _selectionBackdropParticleBase = new();
        private readonly List<SpriteRenderer> _selectionBackdropBeams = new();
        private readonly Dictionary<Transform, DraftCardRenderCache> _cardRenderCaches = new();
        private readonly List<DraftCardIdleCache> _idleCardCaches = new();
        private readonly Dictionary<Transform, DraftCardIdleCache> _idleCardCacheByTransform = new();
        private Transform _hoveredCard;
        private Color _backdropPulseColor = new Color(0f, 0.95f, 1f, 0.4f);
        private float _backdropPulse;
        private float _backdropTime;

        private const string PrefabName = "SelectRoleGame";
        private const float TeamNameFontSize = 3.8f;
        private const float DraftReadyGateSeconds = 0.92f;
        private const float DraftCardRevealStaggerSeconds = 0.045f;
        private static readonly float DraftFastPickCloseSeconds = 0f;
       private static float CardScaleForCount(int count) => count switch
        {
            <= 3 => 0.55f,
            <= 4 => 0.55f,
            <= 5 => 0.55f,
            <= 6 => 0.55f,
            <= 7 => 0.55f,
            <= 8 => 0.55f,
            _    => 0.55f,
        };

        private static float SpacingForCount(int count) => count switch
        {
            <= 3 => -1f,
            <= 4 => -1f,
            <= 5 => -1f,
            <= 6 => 0f,
            <= 8 => 0f,
            _    => 0f,
        };

        private static Color GetTeamColor(string teamName)
        {
            if (string.IsNullOrEmpty(teamName)) return Color.white;
            string lower = teamName.ToLowerInvariant();
            if (lower.Contains("crewmate")) return new Color32(0,   255, 255, 255);
            if (lower.Contains("impostor") ||
                lower.Contains("imposter")) return new Color32(255,   0,   0, 255);
            if (lower.Contains("neutral"))  return new Color32(180, 180, 180, 255);
            return Color.white;
        }



        // ── Bottom-center timer label ─────────────────────────────────────────

        private void BuildBottomTimer()
        {
            if (HudManager.Instance == null) return;

            _timerRoot = new GameObject("DraftBottomTimer");
            _timerRoot.transform.SetParent(HudManager.Instance.transform, false);

            // Position at bottom center of the screen, just where the ping tracker sits.
            // Typical HUD space: y ≈ -2.6 puts it near the bottom edge in world units.
            _timerRoot.transform.localPosition = new Vector3(0f, -2.6f, -25f);

            _timerText = _timerRoot.AddComponent(
                Il2CppInterop.Runtime.Il2CppType.Of<TextMeshPro>()).Cast<TextMeshPro>();

            _timerText.font         = HudManager.Instance.TaskPanel.taskText.font;
            _timerText.fontMaterial = HudManager.Instance.TaskPanel.taskText.fontMaterial;
            _timerText.fontSize     = 2.5f;
            _timerText.alignment    = TextAlignmentOptions.Center;
            _timerText.enableWordWrapping = false;
            _timerText.text         = string.Empty;

            // Make sure it renders on top of most HUD elements
            var r = _timerRoot.GetComponent<Renderer>();
            if (r != null) { r.sortingLayerName = "UI"; r.sortingOrder = 82; }
        }

        private void DestroyBottomTimer()
        {
            if (_timerRoot != null)
            {
                try { Destroy(_timerRoot); } catch { }
                _timerRoot = null;
                _timerText = null;
            }
        }

        // ── Show / Hide ───────────────────────────────────────────────────────

        public static void Show(ushort[] roleIds)
        {
            Hide();
            if (HudManager.Instance?.FullScreen != null)
                HudManager.Instance.FullScreen.color = Color.clear;
            var go = new GameObject("DraftScreenController");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DraftScreenController>();
            Instance._offeredRoleIds = roleIds;
            Instance.BuildScreen();
        }

        public static void Hide()
        {
            if (Instance == null) return;

            Instance.DestroyBottomTimer();
            Instance.DestroySelectionBackdrop();
            Instance.DestroyRoleInfoPanel();

            if (Instance._screenRoot != null) Destroy(Instance._screenRoot);

            if (HudManager.Instance != null)
            {
                var hud = HudManager.Instance.transform;
                for (int i = hud.childCount - 1; i >= 0; i--)
                {
                    var child = hud.GetChild(i);
                    if (child != null && child.name.StartsWith("DraftCard_"))
                        Destroy(child.gameObject);
                }
            }

            Instance._cardRenderCaches.Clear();
            Instance._idleCardCaches.Clear();
            Instance._idleCardCacheByTransform.Clear();
            _cardVisuals.Clear();
            Destroy(Instance.gameObject);
            Instance = null;
        }

        // ── Build screen ──────────────────────────────────────────────────────

        private void BuildScreen()
        {
            if (HudManager.Instance == null) return;
            if (HudManager.Instance.FullScreen != null)
                HudManager.Instance.FullScreen.color = Color.clear;

           
            BuildBottomTimer();

            GameObject prefab = null;
            try
            {
                var bundle = TouAssets.MainBundle;
                if (bundle != null)
                    prefab = bundle.LoadAsset(PrefabName)?.TryCast<GameObject>();
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftScreenController] Bundle load failed: {ex.Message}");
            }

            if (prefab == null)
            {
                DraftModePlugin.Logger.LogError("[DraftScreenController] SelectRoleGame prefab not found.");
                DestroyBottomTimer();
                Destroy(gameObject); Instance = null; return;
            }

            _screenRoot = Instantiate(prefab);
            _screenRoot.name = "DraftRoleSelectScreen";
            DontDestroyOnLoad(_screenRoot);

            if (HudManager.Instance != null)
            {
                _screenRoot.transform.SetParent(HudManager.Instance.transform, false);
                _screenRoot.transform.localPosition = Vector3.zero;
            }
            BuildSelectionBackdrop();
            BuildRoleInfoPanel();

            var holderGo    = _screenRoot.transform.Find("RoleCardHolder");
            var statusGo    = _screenRoot.transform.Find("Status");
            var rolesHolder = _screenRoot.transform.Find("Roles");
            HidePrefabBackdrop(_screenRoot.transform, rolesHolder, statusGo, holderGo);

            // "Pick Your Role!" label — stays at the top exactly as before
            if (statusGo != null)
            {
                _statusText = statusGo.GetComponent<TextMeshPro>();
                if (_statusText != null)
                {
                    _statusText.font         = HudManager.Instance.TaskPanel.taskText.font;
                    _statusText.fontMaterial = HudManager.Instance.TaskPanel.taskText.fontMaterial;
                    _statusText.text         = "<color=#FFFFFF><b>Pick Your Role!</b></color>";
                    var sr = statusGo.GetComponent<Renderer>();
                    if (sr != null) { sr.sortingLayerName = "UI"; sr.sortingOrder = 82; }
                    statusGo.gameObject.SetActive(true);
                }
            }

            if (holderGo == null)
            {
                DestroyBottomTimer();
                Destroy(_screenRoot); Destroy(gameObject); Instance = null; return;
            }

            var rolePrefab = holderGo.gameObject;

            var idList = new List<ushort>();
            if (_offeredRoleIds != null) idList.AddRange(_offeredRoleIds);
            var cards = DraftUiManager.BuildCards(idList);

            int totalCards = cards.Count;
            float cardScale = CardScaleForCount(totalCards);
            float spacing   = SpacingForCount(totalCards);

            bool useGrid = totalCards > 5;

            if (useGrid)
            {
                var hLayout = rolesHolder?.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                if (hLayout != null) hLayout.enabled = false;

                var rt = rolesHolder?.GetComponent<RectTransform>();
                if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, 12f);
            }
            else
            {
                var layoutGroup = rolesHolder?.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                if (layoutGroup != null)
                    layoutGroup.spacing = spacing;
            }

            for (int i = 0; i < totalCards; i++)
            {
                var card       = cards[i];
                int capturedIdx = card.Index;

                var btn = CreateCard(
                    rolePrefab, rolesHolder,
                    card.RoleName, card.TeamName,
                    card.Icon ?? TouRoleIcons.RandomAny.LoadAsset(),
                    i, totalCards, card.Color,
                    card.Description,
                    cardScale, useGrid, spacing);

                btn.OnClick.RemoveAllListeners();
                btn.OnClick.AddListener((UnityAction)(() => OnCardClicked(capturedIdx)));
            }

            Coroutines.Start(CoAnimateCards(rolesHolder, cardScale, useGrid, totalCards));
        }

        private void HidePrefabBackdrop(Transform root, Transform rolesHolder, Transform statusGo, Transform holderGo)
        {
            if (root == null) return;
            HidePrefabBackdropRecursive(root, rolesHolder, statusGo, holderGo);
        }

        private static void HidePrefabBackdropRecursive(Transform current, Transform rolesHolder, Transform statusGo, Transform holderGo)
        {
            if (current == null) return;
            var sr = current.GetComponent<SpriteRenderer>();
            if (sr != null && sr.gameObject != null &&
                !IsChildOf(current, rolesHolder) && !IsChildOf(current, statusGo) && !IsChildOf(current, holderGo))
            {
                string n = sr.gameObject.name.ToLowerInvariant();
                Vector3 size = sr.bounds.size;
                if (n.Contains("background") || n.Contains("backdrop") || n.Contains("bg") || size.x > 4f || size.y > 3f)
                    sr.enabled = false;
            }

            for (int i = 0; i < current.childCount; i++)
                HidePrefabBackdropRecursive(current.GetChild(i), rolesHolder, statusGo, holderGo);
        }

        private static bool IsChildOf(Transform child, Transform parent)
        {
            if (child == null || parent == null) return false;
            var cur = child;
            while (cur != null)
            {
                if (cur == parent) return true;
                cur = cur.parent;
            }
            return false;
        }

        // ── Dynamic backdrop ─────────────────────────────────────────────────

        private void BuildSelectionBackdrop()
        {
            if (HudManager.Instance == null) return;
            DestroySelectionBackdrop();
            _selectionBackdropParticles.Clear();
            _selectionBackdropParticleBase.Clear();
            _selectionBackdropBeams.Clear();

            var cam = Camera.main;
            float camH = cam != null ? cam.orthographicSize * 2f : 6f;
            float camW = camH * ((float)Screen.width / Screen.height);

            _selectionBackdrop = new GameObject("DraftSelectionBackdropRoot");
            _selectionBackdrop.transform.SetParent(HudManager.Instance.transform, false);
            _selectionBackdrop.transform.localPosition = new Vector3(0f, 0f, 0.7f);

            MakeBackdropSprite("DraftSelectionBackdropBase", Vector3.zero, new Vector3(camW, camH, 1f), MakeWhiteSprite(), new Color(0.010f, 0.006f, 0.035f, 1f), 46);
            _selectionBackdropWash = MakeBackdropSprite("DraftSelectionBackdropWash", new Vector3(0f, 0.05f, -0.02f), new Vector3(camW * 1.05f, camH * 0.55f, 1f), MakeSoftGlowSprite(), new Color(0f, 0.95f, 1f, 0.18f), 47);
            _selectionBackdropBeam = MakeBackdropSprite("DraftSelectionBackdropBeam", new Vector3(-camW * 0.36f, 0f, -0.04f), new Vector3(camW * 0.18f, camH * 0.9f, 1f), MakeSoftGlowSprite(), new Color(1f, 0.84f, 0.2f, 0.16f), 49);
            _selectionBackdropBeam.transform.localRotation = Quaternion.Euler(0f, 0f, -13f);
            _selectionBackdropBeams.Add(_selectionBackdropBeam);

            var beam2 = MakeBackdropSprite("DraftSelectionBackdropBeam", new Vector3(camW * 0.34f, 0f, -0.04f), new Vector3(camW * 0.14f, camH * 0.85f, 1f), MakeSoftGlowSprite(), new Color(0.72f, 0.42f, 1f, 0.12f), 49);
            beam2.transform.localRotation = Quaternion.Euler(0f, 0f, 14f);
            _selectionBackdropBeams.Add(beam2);

            _selectionBackdropHorizon = MakeBackdropSprite("DraftSelectionBackdropHorizon", new Vector3(0f, -0.58f, -0.03f), new Vector3(camW * 0.9f, 0.06f, 1f), MakeSoftGlowSprite(), new Color(0f, 0.95f, 1f, 0.28f), 50);
            _selectionBackdropFlash = MakeBackdropSprite("DraftSelectionBackdropFlash", Vector3.zero, new Vector3(camW, camH, 1f), MakeSoftGlowSprite(), new Color(1f, 1f, 1f, 0f), 53);

            const int particleCount = 8;
            for (int i = 0; i < particleCount; i++)
            {
                float x = Mathf.Sin(i * 1.91f) * camW * 0.46f;
                float y = Mathf.Cos(i * 1.37f) * camH * 0.37f;
                var p = MakeBackdropSprite("DraftSelectionBackdropParticle", new Vector3(x, y, -0.05f), Vector3.one * (0.035f + (i % 4) * 0.012f), MakeSoftGlowSprite(), new Color(0.45f, 0.95f, 1f, 0.24f), 52);
                _selectionBackdropParticles.Add(p);
                _selectionBackdropParticleBase.Add(p.transform.localPosition);
            }
        }

        private SpriteRenderer MakeBackdropSprite(string name, Vector3 pos, Vector3 scale, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_selectionBackdrop.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingLayerName = "UI";
            sr.sortingOrder = order;
            return sr;
        }

        private void DestroySelectionBackdrop()
        {
            if (_selectionBackdrop != null)
            {
                try { Destroy(_selectionBackdrop); } catch { }
                _selectionBackdrop = null;
                _selectionBackdropWash = null;
                _selectionBackdropBeam = null;
                _selectionBackdropHorizon = null;
                _selectionBackdropFlash = null;
                _selectionBackdropParticles.Clear();
                _selectionBackdropParticleBase.Clear();
                _selectionBackdropBeams.Clear();
            }
        }

        private void BuildRoleInfoPanel()
        {
            if (HudManager.Instance == null || HudManager.Instance.TaskPanel == null) return;
            DestroyRoleInfoPanel();

            var font = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            _roleInfoRoot = new GameObject("DraftRoleInfoPanel");
            _roleInfoRoot.transform.SetParent(HudManager.Instance.transform, false);
            _roleInfoRoot.transform.localPosition = new Vector3(0f, -2.36f, -23f);
            _roleInfoRoot.transform.localScale = Vector3.one * 0.92f;

            var glow = new GameObject("DraftRoleInfoPanelGlow");
            glow.transform.SetParent(_roleInfoRoot.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            glow.transform.localScale = new Vector3(5.6f, 0.95f, 1f);
            _roleInfoGlow = glow.AddComponent<SpriteRenderer>();
            _roleInfoGlow.sprite = MakeSoftGlowSprite();
            _roleInfoGlow.color = new Color(0f, 0.95f, 1f, 0.18f);
            _roleInfoGlow.sortingLayerName = "UI";
            _roleInfoGlow.sortingOrder = 84;

            _roleInfoDescription = MakeRoleInfoText("DraftRoleInfoDescription", font, fontMat, 0.68f, new Vector3(0f, 0f, -0.06f), TextAlignmentOptions.Center, false);
            _roleInfoDescription.enableWordWrapping = true;
            _roleInfoDescription.rectTransform.sizeDelta = new Vector2(6.2f, 0.9f);
            _roleInfoRoot.SetActive(false);
        }

        private TextMeshPro MakeRoleInfoText(string name, TMP_FontAsset font, Material fontMat, float size, Vector3 pos, TextAlignmentOptions alignment, bool bold)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_roleInfoRoot.transform, false);
            go.transform.localPosition = pos;
            var tmp = go.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<TextMeshPro>()).Cast<TextMeshPro>();
            tmp.font = font;
            tmp.fontMaterial = fontMat;
            tmp.fontSize = size;
            tmp.alignment = alignment;
            tmp.enableWordWrapping = false;
            tmp.text = bold ? "<b></b>" : string.Empty;
            var r = go.GetComponent<Renderer>();
            if (r != null) { r.sortingLayerName = "UI"; r.sortingOrder = 86; }
            return tmp;
        }

        private void DestroyRoleInfoPanel()
        {
            if (_roleInfoRoot != null)
            {
                try { Destroy(_roleInfoRoot); } catch { }
                _roleInfoRoot = null;
                _roleInfoGlow = null;
                _roleInfoDescription = null;
            }
        }

        private void ShowRoleInfoPanel(string roleName, string teamName, string description, Color color)
        {
            if (_roleInfoRoot == null) BuildRoleInfoPanel();
            if (_roleInfoRoot == null) return;

            _roleInfoRoot.SetActive(true);
            _roleInfoRoot.transform.localScale = Vector3.one;
            if (_roleInfoGlow != null)
                _roleInfoGlow.color = new Color(color.r, color.g, color.b, 0.22f);
            if (_roleInfoDescription != null)
                _roleInfoDescription.text = string.IsNullOrWhiteSpace(description) ? "No role preview text available." : description;
        }

        private void HideRoleInfoPanel()
        {
            if (_roleInfoRoot != null)
                _roleInfoRoot.SetActive(false);
        }

        private void PulseSelectionBackdrop(string cardName, int cardIndex, int totalCards)
        {
            _backdropPulseColor = GetAllianceEffectColor(cardName);
            _backdropPulseColor.a = 0.85f;
            _backdropPulse = 1.25f;
            float lane = totalCards <= 1 ? 0.5f : cardIndex / (float)(totalCards - 1);
            for (int i = 0; i < _selectionBackdropBeams.Count; i++)
            {
                var beam = _selectionBackdropBeams[i];
                if (beam == null) continue;
                beam.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-22f, 22f, lane) + (i == 0 ? -8f : 8f));
            }
        }

        private void PreviewCardHover(Transform card, string cardName, string roleName, string teamName, string description)
        {
            if (_hasPicked || card == null) return;
            _hoveredCard = card;
            ShowRoleInfoPanel(roleName, teamName, description, GetAllianceEffectColor(cardName));
            PulseSelectionBackdrop(cardName, 0, 1);
            Color color = GetAllianceEffectColor(cardName);
            SetAllianceAura(card, color, 0.54f, 1.14f);
            SetCardToIdleBase(card);
        }

        private void ClearCardHover(Transform card, string cardName)
        {
            if (_hasPicked || card == null) return;
            if (_hoveredCard == card) _hoveredCard = null;
            HideRoleInfoPanel();
            Color color = GetAllianceEffectColor(cardName);
            SetAllianceAura(card, color, 0.26f, 1f);
        }

        private void RegisterIdleCard(Transform card)
        {
            if (card == null) return;
            if (!_idleCardCacheByTransform.TryGetValue(card, out var cache))
            {
                cache = new DraftCardIdleCache { Card = card };
                _idleCardCacheByTransform[card] = cache;
                _idleCardCaches.Add(cache);
            }
            cache.BasePosition = card.localPosition;
            cache.BaseRotation = card.localRotation;
        }

        private void SetCardToIdleBase(Transform card)
        {
            if (card == null) return;
            if (!_idleCardCacheByTransform.TryGetValue(card, out var cache)) return;
            card.localPosition = cache.BasePosition;
            card.localRotation = cache.BaseRotation;
        }

        private void UpdateCardIdleMotion()
        {
            if (!_cardsReady || _hasPicked) return;
            for (int i = 0; i < _idleCardCaches.Count; i++)
            {
                var cache = _idleCardCaches[i];
                var card = cache.Card;
                if (card == null) continue;
                if (card == _hoveredCard) { SetCardToIdleBase(card); continue; }

                float phase = i * 0.79f;
                float bob = Mathf.Sin(_backdropTime * 1.15f + phase) * 0.035f;
                float sway = Mathf.Sin(_backdropTime * 0.62f + phase) * 0.016f;
                card.localPosition = cache.BasePosition + new Vector3(sway, bob, 0f);
                card.localRotation = cache.BaseRotation * Quaternion.Euler(0f, 0f, Mathf.Sin(_backdropTime * 0.9f + phase) * 1.35f);
            }
        }

        private void UpdateSelectionBackdrop()
        {
            if (_selectionBackdrop == null) return;

            _backdropTime += Time.deltaTime;
            _backdropPulse = Mathf.MoveTowards(_backdropPulse, 0f, Time.deltaTime * 1.35f);

            Color idle = new Color(0.0f, 0.55f, 0.95f, 0.24f);
            Color wash = Color.Lerp(idle, _backdropPulseColor, Mathf.Clamp01(_backdropPulse));
            if (_selectionBackdropWash != null)
            {
                wash.a = 0.16f + _backdropPulse * 0.26f + (Mathf.Sin(_backdropTime * 0.9f) + 1f) * 0.025f;
                _selectionBackdropWash.color = wash;
                _selectionBackdropWash.transform.localScale = new Vector3(9.4f + _backdropPulse * 0.8f, 3.35f + _backdropPulse * 0.4f, 1f);
            }

            for (int i = 0; i < _selectionBackdropBeams.Count; i++)
            {
                var beam = _selectionBackdropBeams[i];
                if (beam == null) continue;
                var c = Color.Lerp(new Color(0f, 0.95f, 1f, 0.12f), _backdropPulseColor, Mathf.Clamp01(_backdropPulse));
                c.a = 0.14f + _backdropPulse * 0.38f;
                beam.color = c;
                float dir = i == 0 ? -1f : 1f;
                beam.transform.localPosition = new Vector3(dir * (2.8f + Mathf.Sin(_backdropTime * 0.55f + i) * 0.35f), Mathf.Cos(_backdropTime * 0.42f + i) * 0.18f, -0.04f);
            }

            if (_selectionBackdropHorizon != null)
            {
                var c = Color.Lerp(new Color(0f, 0.95f, 1f, 0.28f), _backdropPulseColor, Mathf.Clamp01(_backdropPulse));
                c.a = 0.24f + _backdropPulse * 0.32f;
                _selectionBackdropHorizon.color = c;
                _selectionBackdropHorizon.transform.localScale = new Vector3(8.2f + Mathf.Sin(_backdropTime * 0.65f) * 0.22f, 0.06f + _backdropPulse * 0.04f, 1f);
            }

            if (_selectionBackdropFlash != null)
            {
                var c = _backdropPulseColor;
                c.a = _backdropPulse * 0.12f;
                _selectionBackdropFlash.color = c;
            }

            for (int i = 0; i < _selectionBackdropParticles.Count; i++)
            {
                var p = _selectionBackdropParticles[i];
                if (p == null || i >= _selectionBackdropParticleBase.Count) continue;
                Vector3 b = _selectionBackdropParticleBase[i];
                float phase = i * 0.73f;
                p.transform.localPosition = b + new Vector3(Mathf.Sin(_backdropTime * 0.8f + phase) * 0.22f, Mathf.Cos(_backdropTime * 0.55f + phase) * 0.18f, 0f);
                p.transform.localScale = Vector3.one * (0.035f + (i % 4) * 0.012f + _backdropPulse * 0.045f);
                var c = Color.Lerp(new Color(0.5f, 0.95f, 1f, 0.22f), _backdropPulseColor, Mathf.Clamp01(_backdropPulse));
                c.a = 0.18f + (Mathf.Sin(_backdropTime * 1.4f + phase) + 1f) * 0.10f + _backdropPulse * 0.28f;
                p.color = c;
            }
        }

        // ── Card creation ─────────────────────────────────────────────────────

        private static PassiveButton CreateCard(
            GameObject rolePrefab,
            Transform  rolesHolder,
            string     roleName,
            string     teamName,
            Sprite     icon,
            int        cardIndex,
            int        totalCards,
            Color      color,
            string     roleDescription,
            float      cardScale,
            bool       useGrid = false,
            float      spacing = 0f)
        {
            var newRoleObj    = UnityEngine.Object.Instantiate(rolePrefab, rolesHolder);
            var actualCard    = newRoleObj!.transform.GetChild(0);
            var roleText      = actualCard.GetChild(0).GetComponent<TextMeshPro>();
            var roleImage     = actualCard.GetChild(1).GetComponent<SpriteRenderer>();
            var teamText      = actualCard.GetChild(2).GetComponent<TextMeshPro>();
            var passiveButton = actualCard.GetComponent<PassiveButton>();
            var rollover      = actualCard.GetComponent<ButtonRolloverHandler>();
            string allianceKey = GetAllianceEffectKey(teamName);
            newRoleObj.name = $"DraftCard_{allianceKey}_{cardIndex}";

            int   tiltIndex = useGrid ? (cardIndex % Mathf.CeilToInt(totalCards / 2f)) : cardIndex;
            float tiltScale = Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(3f, 9f, totalCards));
            float randZ     = (-10f + tiltIndex * 5f) * tiltScale
                              + UnityEngine.Random.Range(-1.5f, 1.5f) * tiltScale;
            Vector3 hoverBasePosition = Vector3.zero;

            passiveButton.OnMouseOver.AddListener((UnityAction)(() =>
            {
                Instance?.PreviewCardHover(actualCard, newRoleObj.name, roleName, teamName, roleDescription);
                SetCardHoverDepth(newRoleObj.transform, hoverBasePosition, hovered: true);
                newRoleObj.transform.localScale = Vector3.one * (cardScale * 1.075f);
            }));
            passiveButton.OnMouseOut.AddListener((UnityAction)(() =>
            {
                Instance?.ClearCardHover(actualCard, newRoleObj.name);
                SetCardHoverDepth(newRoleObj.transform, hoverBasePosition, hovered: false);
                newRoleObj.transform.localScale = Vector3.one * cardScale;
            }));

            newRoleObj.transform.localRotation = Quaternion.Euler(0f, 0f, -randZ);

            if (useGrid)
            {
                int cols    = Mathf.CeilToInt(totalCards / 2f);
                int row     = cardIndex / cols;
                int col     = cardIndex % cols;

                float cardW = 2.5f * cardScale;
                float cardH = 3.7f * cardScale;
                float xGap  = cardW + spacing;
                float yGap  = cardH + spacing * 0.5f;

                float totalW = cols * xGap - spacing;
                float startX = -totalW / 2f + cardW / 2f;
                float startY = yGap / 2f;

                float xPos = startX + col * xGap;
                float yPos = startY - row * yGap;

                newRoleObj.transform.localPosition = new Vector3(xPos, yPos, cardIndex);
            }
            else
            {
                newRoleObj.transform.localPosition = new Vector3(
                    newRoleObj.transform.localPosition.x, 0f, cardIndex);
            }

            hoverBasePosition = newRoleObj.transform.localPosition;
            newRoleObj.transform.localScale = Vector3.one * cardScale;

            roleText.text    = roleName;
            teamText.text    = teamName;
            roleImage.sprite = icon;
            roleImage.SetSizeLimit(2.8f);
            var cardBgRenderer = actualCard.GetComponent<SpriteRenderer>();
            if (cardBgRenderer != null) cardBgRenderer.color = color;
            roleImage.color = Color.white;

            teamText.fontSizeMax = Mathf.Lerp(4f, 2f, Mathf.InverseLerp(3f, 9f, totalCards));
            teamText.enableAutoSizing = true;

            rollover.OutColor  = color;
            rollover.OverColor = Color.white;
            roleText.color     = color;
            teamText.fontSizeMax = 3.8f;
            teamText.color       = GetTeamColor(teamName);

            var renderCache = CacheCardRenderers(newRoleObj.transform, 70, 74);
            if (Instance != null)
                Instance._cardRenderCaches[newRoleObj.transform] = renderCache;
            AttachAllianceAura(actualCard, teamName);

            return passiveButton;
        }

        private static DraftCardRenderCache CacheCardRenderers(Transform root, int spriteOrder, int textOrder)
        {
            var cache = new DraftCardRenderCache();
            CacheCardRenderersRecursive(root, cache, spriteOrder, textOrder);
            return cache;
        }

        private static void CacheCardRenderersRecursive(Transform current, DraftCardRenderCache cache, int spriteOrder, int textOrder)
        {
            if (current == null || cache == null) return;

            var sr = current.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingLayerName = "UI";
                sr.sortingOrder = spriteOrder;
                cache.Sprites.Add(sr);
            }

            var tmp = current.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                var r = tmp.GetComponent<Renderer>();
                if (r != null) { r.sortingLayerName = "UI"; r.sortingOrder = textOrder; }
                cache.Texts.Add(tmp);
            }

            for (int i = 0; i < current.childCount; i++)
                CacheCardRenderersRecursive(current.GetChild(i), cache, spriteOrder, textOrder);
        }

        private static void SetCardHoverDepth(Transform holder, Vector3 hoverBasePosition, bool hovered)
        {
            if (holder == null) return;
            float z = hoverBasePosition.z + (hovered ? -10f : 0f);
            holder.localPosition = new Vector3(hoverBasePosition.x, hoverBasePosition.y, z);
        }

        private static DraftCardVisuals DraftCardVisualCache(Transform card)
        {
            if (!_cardVisuals.TryGetValue(card, out var visuals))
            {
                visuals = new DraftCardVisuals();
                _cardVisuals[card] = visuals;
            }
            return visuals;
        }

        private static void AttachAllianceAura(Transform card, string teamName)
        {
            if (card == null) return;
            Color aura = GetAllianceEffectColor($"DraftCard_{GetAllianceEffectKey(teamName)}");
            SetAllianceAura(card, aura, 0.26f, 1f);
        }

        private static void SetAllianceAura(Transform card, Color color, float alpha, float scaleBoost)
        {
            if (card == null) return;
            var visuals = DraftCardVisualCache(card);
            var aura = visuals.Aura;

            if (aura == null)
            {
                var go = new GameObject("DraftCardAllianceAura");
                go.transform.SetParent(card, false);
                go.transform.localPosition = new Vector3(0f, 0f, -0.72f);
                aura = go.AddComponent<SpriteRenderer>();
                aura.sprite = MakeSoftGlowSprite();
                aura.sortingLayerName = "UI";
                aura.sortingOrder = 66;
                visuals.Aura = aura;
            }

            aura.color = new Color(color.r, color.g, color.b, alpha);
            aura.transform.localScale = new Vector3(4.45f * scaleBoost, 5.9f * scaleBoost, 1f);
        }

        // ── Card animation ────────────────────────────────────────────────────

        private static IEnumerator CoAnimateCards(Transform rolesHolder, float cardScale, bool useGrid, int totalCards)
        {
            if (rolesHolder == null) yield break;
            int cols = Mathf.CeilToInt(totalCards / 2f);

            for (int i = 0; i < rolesHolder.childCount; i++)
            {
                if (rolesHolder == null) yield break;
                Transform card = rolesHolder.GetChild(i);
                if (card == null || card.childCount == 0) continue;
                Transform child = card.GetChild(0);
                if (child == null) continue;
                int animIndex = useGrid ? (i % cols) : i;
                float stagger = Mathf.Min(i, 7) * DraftCardRevealStaggerSeconds;
                Coroutines.Start(CoAnimateCardRevealSequence(child, card.name, animIndex, totalCards, cardScale, stagger));
            }

            yield return CoWaitForSharedReadyGate(totalCards);
            if (Instance != null) Instance._cardsReady = true;
            DraftNetworkHelper.NotifyPickerReady();
        }

        private static IEnumerator CoWaitForSharedReadyGate(int totalCards)
        {
            float revealBudget = 0.48f + Mathf.Min(Mathf.Max(0, totalCards - 1), 7) * DraftCardRevealStaggerSeconds;
            yield return new WaitForSeconds(Mathf.Max(0f, Mathf.Max(DraftReadyGateSeconds, revealBudget)));
        }

        private static IEnumerator CoAnimateCardRevealSequence(Transform child, string cardName, int animIndex, int totalCards, float cardScale, float stagger)
        {
            if (stagger > 0f)
                yield return new WaitForSeconds(stagger);
            if (child == null) yield break;

            Instance?.PulseSelectionBackdrop(cardName, animIndex, totalCards);
            yield return CoAnimateCardIn(child, animIndex, totalCards, cardScale);
            if (child == null) yield break;

            Instance?.RegisterIdleCard(child);
            try { Coroutines.Start(MiscUtils.BetterBloop(child, finalSize: cardScale, duration: 0.22f, intensity: 0.16f)); }
            catch (Exception bex) { DraftModePlugin.Logger.LogWarning($"[DraftScreen] BetterBloop failed: {bex.Message}"); }
            Coroutines.Start(CoAllianceRevealEffect(child, cardName));
        }

        private static string GetAllianceEffectKey(string teamName)
        {
            if (string.IsNullOrEmpty(teamName)) return "unknown";
            string lower = teamName.ToLowerInvariant();
            if (lower.Contains("impostor") || lower.Contains("imposter")) return "impostor";
            if (lower.Contains("neutral")) return "neutral";
            if (lower.Contains("crewmate")) return "crewmate";
            return "unknown";
        }

        private static Color GetAllianceEffectColor(string cardName)
        {
            string lower = cardName?.ToLowerInvariant() ?? string.Empty;
            if (lower.Contains("impostor")) return new Color(1f, 0.08f, 0.08f, 0.55f);
            if (lower.Contains("neutral"))  return new Color(0.72f, 0.58f, 1f, 0.48f);
            if (lower.Contains("crewmate")) return new Color(0.0f, 0.95f, 1f, 0.46f);
            return new Color(1f, 0.86f, 0.22f, 0.42f);
        }

        private static IEnumerator CoAllianceRevealEffect(Transform card, string cardName)
        {
            if (card == null) yield break;

            Color color = GetAllianceEffectColor(cardName);
            SetAllianceAura(card, color, 0.68f, 1.18f);

            var auraBurst = new GameObject("DraftCardAllianceAuraBurst");
            auraBurst.transform.SetParent(card, false);
            auraBurst.transform.localPosition = new Vector3(0f, 0f, -0.74f);
            auraBurst.transform.localScale = new Vector3(4.2f, 5.6f, 1f);
            var auraSr = auraBurst.AddComponent<SpriteRenderer>();
            auraSr.sprite = MakeSoftGlowSprite();
            auraSr.color = new Color(color.r, color.g, color.b, 0.56f);
            auraSr.sortingLayerName = "UI";
            auraSr.sortingOrder = 77;

            var go = new GameObject("DraftAllianceEffect");
            go.transform.SetParent(card, false);
            go.transform.localPosition = new Vector3(-1.9f, 0f, -0.62f);
            go.transform.localScale = Vector3.one * 0.18f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = MakeWhiteSprite();
            sr.color = color;
            sr.sortingLayerName = "UI";
            sr.sortingOrder = 79;

            string lower = cardName?.ToLowerInvariant() ?? string.Empty;
            bool impostor = lower.Contains("impostor");
            bool neutral = lower.Contains("neutral");
            Vector3 startScale = neutral ? new Vector3(0.32f, 0.32f, 1f) : new Vector3(0.25f, 0.12f, 1f);
            Vector3 endScale = impostor ? new Vector3(4.0f, 0.22f, 1f) : (neutral ? new Vector3(2.1f, 2.1f, 1f) : new Vector3(3.6f, 0.28f, 1f));
            float startRot = impostor ? -34f : (neutral ? -45f : 0f);
            float endRot = impostor ? -22f : (neutral ? 45f : 0f);
            float startX = neutral ? 0f : -1.95f;
            float endX = neutral ? 0f : 1.95f;

            const float duration = 0.5f;
            for (float timer = 0f; timer < duration; timer += Time.deltaTime)
            {
                if (go == null) yield break;
                float t = Mathf.Clamp01(timer / duration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                SetAllianceAura(card, color, Mathf.Lerp(0.72f, 0.26f, t), Mathf.Lerp(1.22f, 1f, t));
                if (auraBurst != null && auraSr != null)
                {
                    auraBurst.transform.localScale = new Vector3(Mathf.Lerp(4.2f, 5.4f, eased), Mathf.Lerp(5.6f, 7.1f, eased), 1f);
                    auraSr.color = new Color(color.r, color.g, color.b, Mathf.Lerp(0.56f, 0f, t));
                }
                go.transform.localPosition = new Vector3(Mathf.Lerp(startX, endX, eased), 0f, -0.62f);
                go.transform.localScale = Vector3.LerpUnclamped(startScale, endScale, EaseOutBack(Mathf.Min(1f, t * 1.15f)));
                go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startRot, endRot, eased));
                sr.color = new Color(color.r, color.g, color.b, Mathf.Lerp(color.a, 0f, t));
                yield return null;
            }

            SetAllianceAura(card, color, 0.26f, 1f);
            if (auraBurst != null)
                try { Destroy(auraBurst); } catch { }
            if (go != null)
                try { Destroy(go); } catch { }
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private static Sprite MakeSoftGlowSprite() => DraftUiSprites.SoftGlow();

        private static Sprite MakeWhiteSprite() => DraftUiSprites.White();

        private static IEnumerator CoAnimateCardIn(Transform card, int currentCard, int totalCards, float cardScale)
        {
            if (card == null) yield break;

            float randY = (currentCard * currentCard * 0.5f - currentCard) * 0.05f
                          + UnityEngine.Random.Range(-0.08f, 0f);
            float randZ = -10f + currentCard * 5f + UnityEngine.Random.Range(-1.5f, 0f);
            if (currentCard == 0) { randY = 0f; randZ = -2f; }

            float center = (totalCards - 1) * 0.5f;
            float side = totalCards <= 1 ? 0f : (currentCard <= center ? -1f : 1f);
            Vector3 targetPos = new Vector3(card.localPosition.x, randY, card.localPosition.z);
            Vector3 startPos = targetPos + new Vector3(side * 2.7f, -2.35f, 0f);
            Quaternion targetRot = Quaternion.Euler(0f, 0f, -randZ);
            Quaternion startRot = Quaternion.Euler(0f, 0f, -randZ + side * 26f + 10f);

            if (card == null) yield break;

            card.localPosition = startPos;
            card.localRotation = startRot;
            card.localScale = Vector3.one * (cardScale * 0.62f);
            card.parent.gameObject.SetActive(true);

            const float duration = 0.46f;
            for (float timer = 0f; timer < duration; timer += Time.deltaTime)
            {
                if (card == null) yield break;
                float t = Mathf.Clamp01(timer / duration);
                float eased = EaseOutBack(t);
                card.localPosition = Vector3.LerpUnclamped(startPos, targetPos, eased);
                card.localRotation = Quaternion.Lerp(startRot, targetRot, Mathf.SmoothStep(0f, 1f, t));
                card.localScale = Vector3.one * Mathf.Lerp(cardScale * 0.62f, cardScale, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }

            if (card == null) yield break;
            card.localPosition = targetPos;
            card.localRotation = targetRot;
            card.localScale = Vector3.one * cardScale;
        }

        // ── Update ────────────────────────────────────────────────────────────

        private float _localTimeLeft = -1f;
        private bool  _cardsReady    = false;
        private bool  _lastStatusPicked;
        private bool  _lastTimerHidden = true;
        private int   _lastTimerSeconds = -1;

        private void Update()
        {
            if (HudManager.Instance?.FullScreen != null)
                HudManager.Instance.FullScreen.color = Color.clear;
            UpdateSelectionBackdrop();
            UpdateCardIdleMotion();

            // Keep the top label clean — only the static title or post-click confirmation, no timer
            if (_statusText != null && _lastStatusPicked != _hasPicked)
            {
                _lastStatusPicked = _hasPicked;
                _statusText.text = _hasPicked
                    ? "<color=#88FF88><b>LOCKED IN!</b></color>"
                    : "<color=#FFFFFF><b>Pick Your Role!</b></color>";
            }

            // Bottom-center timer label
            if (_timerText != null)
            {
                if (_hasPicked || !DraftManager.IsDraftActive || !_cardsReady)
                {
                    if (!_lastTimerHidden)
                    {
                        _timerText.text = string.Empty;
                        _lastTimerHidden = true;
                        _lastTimerSeconds = -1;
                    }
                    _timerText.transform.localScale = Vector3.one;
                }
                else
                {
                    int secs;
                    if (AmongUsClient.Instance.AmHost)
                    {
                        secs = Mathf.Max(0, Mathf.CeilToInt(DraftManager.TurnTimeLeft));
                    }
                    else
                    {
                        if (_localTimeLeft < -0.5f) _localTimeLeft = DraftManager.TurnDuration;
                        if (_localTimeLeft > 0f) _localTimeLeft -= Time.deltaTime;
                        secs = Mathf.Max(0, Mathf.CeilToInt(_localTimeLeft));
                    }

                    string color = secs <= 5 ? "#FF5555" : "#FFD700";
                    float timerPulse = secs <= 5 ? 1f + Mathf.Sin(Time.time * 10f) * 0.08f : 1f;
                    _timerText.transform.localScale = Vector3.one * timerPulse;
                    if (_lastTimerHidden || _lastTimerSeconds != secs)
                    {
                        _lastTimerHidden = false;
                        _lastTimerSeconds = secs;
                        _timerText.text =
                            $"<color={color}><b>{secs} Second{(secs != 1 ? "s" : "")} Remaining</b></color>";
                    }
                }
            }
        }

        // ── Card click ────────────────────────────────────────────────────────

        private void OnCardClicked(int index)
        {
            if (_hasPicked) return;
            _hasPicked = true;
            Coroutines.Start(CoConfirmPick(index));
        }

        private IEnumerator CoConfirmPick(int index)
        {
            var selectedCard = TryGetCard(index);
            DimUnselectedCards(index);

            if (_timerText != null)
                _timerText.text = "<color=#88FF88><b>Pick Sent</b></color>";

            DraftNetworkHelper.SendPickToHost(index, closePicker: false);

            if (selectedCard != null)
            {
                string pickedCardName = selectedCard.parent != null ? selectedCard.parent.name : "picked";
                PulseSelectionBackdrop(pickedCardName, 0, 1);
            }

            if (DraftFastPickCloseSeconds > 0f)
                yield return new WaitForSeconds(DraftFastPickCloseSeconds);
            DraftUiManager.CloseAll();
        }

        private Transform TryGetCard(int index)
        {
            if (_screenRoot == null) return null;
            var rolesHolder = _screenRoot.transform.Find("Roles");
            if (rolesHolder == null || index < 0 || index >= rolesHolder.childCount) return null;
            var holder = rolesHolder.GetChild(index);
            return holder != null && holder.childCount > 0 ? holder.GetChild(0) : holder;
        }

        private void DimUnselectedCards(int selectedIndex)
        {
            if (_screenRoot == null) return;
            var rolesHolder = _screenRoot.transform.Find("Roles");
            if (rolesHolder == null) return;

            for (int i = 0; i < rolesHolder.childCount; i++)
            {
                if (i == selectedIndex) continue;
                var card = rolesHolder.GetChild(i);
                if (card == null) continue;
                if (!_cardRenderCaches.TryGetValue(card, out var cache))
                {
                    cache = CacheCardRenderers(card, 70, 74);
                    _cardRenderCaches[card] = cache;
                }

                foreach (var sr in cache.Sprites)
                {
                    if (sr == null) continue;
                    Color c = sr.color;
                    sr.color = new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, c.a * 0.6f);
                }
                foreach (var tmp in cache.Texts)
                {
                    if (tmp == null) continue;
                    Color c = tmp.color;
                    tmp.color = new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, c.a * 0.75f);
                }
            }
        }

        private void DestroySelf() => Hide();
    }
}