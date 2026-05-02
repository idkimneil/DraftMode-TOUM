using System;
using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using DraftModeTOUM.Managers;
using Il2CppInterop.Runtime.Attributes;
using Reactor.Utilities;
using Reactor.Utilities.Attributes;

namespace DraftModeTOUM
{
    public sealed class RecapEntry
    {
        public int SlotNumber { get; }
        public string RoleName { get; }

        public RecapEntry(int slot, string role)
        {
            SlotNumber = slot;
            RoleName = role;
        }
    }

    [RegisterInIl2Cpp]
    public class DraftRecapOverlay(IntPtr ip) : MonoBehaviour(ip)
    {
        private static DraftRecapOverlay _instance;  // removed ?

        public static void Show(List<RecapEntry> entries)
        {
            Hide();
            var go = new GameObject("DraftRecapOverlay");

            if (HudManager.Instance != null)
            {
                go.transform.SetParent(HudManager.Instance.transform, false);
                go.transform.localPosition = new Vector3(0, 0, -25f);
            }
            else
            {
                DontDestroyOnLoad(go);
            }

            _instance = go.AddComponent<DraftRecapOverlay>();
            _instance.BuildUI(entries);
        }

        public static void Hide()
        {
            if (_instance != null)
            {
                try
                {
                    if (_instance.gameObject != null)
                    {
                        _instance.gameObject.SetActive(false);
                        Destroy(_instance.gameObject);
                    }
                }
                catch { }
                _instance = null;
            }

            var leftover = GameObject.Find("DraftRecapOverlay");
            if (leftover != null)
            {
                leftover.SetActive(false);
                Destroy(leftover);
            }

            var oldRoot = GameObject.Find("RecapRoot");
            if (oldRoot != null)
            {
                oldRoot.SetActive(false);
                Destroy(oldRoot);
            }
        }

        [HideFromIl2Cpp]
        private void BuildUI(List<RecapEntry> entries)
        {
            if (HudManager.Instance == null) return;

            var font = HudManager.Instance.TaskPanel.taskText.font;
            var fontMat = HudManager.Instance.TaskPanel.taskText.fontMaterial;

            var revealTexts = new List<TextMeshPro>();

            var title = MakeText(gameObject, "Title", font, fontMat, 2.5f, new Vector3(0, 2.45f, 0));
            title.text = "<color=#FFD700><b>── DRAFT COMPLETE ──</b></color>";
            title.alpha = 0f;
            revealTexts.Add(title);

            var subtitle = MakeText(gameObject, "Subtitle", font, fontMat, 1.25f, new Vector3(0, 2.05f, 0));
            subtitle.text = "<color=#88FF88><b>ROLES LOCKED IN</b></color>";
            subtitle.alpha = 0f;
            revealTexts.Add(subtitle);

            int count = entries.Count;
            int cols = count > 8 ? 2 : 1;
            int perCol = Mathf.CeilToInt(count / (float)cols);

            float startY = 1.4f;
            float ySpacing = 0.45f;
            float xOffset = cols == 2 ? 3.0f : 0f;

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                int col = i / perCol;
                int row = i % perCol;

                float x = (col == 0 && cols == 2) ? -xOffset : (col == 1 ? xOffset : 0f);
                float y = startY - (row * ySpacing);

                var txt = MakeText(gameObject, $"Entry_{i}", font, fontMat, 1.8f, new Vector3(x, y, 0));

                Color c = RoleColors.GetColor(entry.RoleName);
                string colorHex = ColorUtility.ToHtmlStringRGB(c);

                txt.text = $"Player {entry.SlotNumber}: <color=#{colorHex}><b>{entry.RoleName}</b></color>";
                txt.alpha = 0f;
                txt.transform.localScale = Vector3.one * 0.75f;
                revealTexts.Add(txt);
            }

            Coroutines.Start(CoRevealRecap(revealTexts));
        }

        private static IEnumerator CoRevealRecap(List<TextMeshPro> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var tmp = items[i];
                if (tmp == null) continue;
                yield return CoRevealText(tmp);
                yield return new WaitForSeconds(i < 2 ? 0.08f : 0.045f);
            }

            if (items.Count > 0 && items[0] != null)
                yield return CoRecapConfettiBurst(items[0].transform.parent);
        }

        private static IEnumerator CoRecapConfettiBurst(Transform parent)
        {
            if (parent == null) yield break;

            const int count = 16;
            var pieces = new List<GameObject>();
            for (int i = 0; i < count; i++)
            {
                var confetti = new GameObject("DraftRecapConfetti");
                confetti.transform.SetParent(parent, false);
                confetti.transform.localPosition = new Vector3(0f, 1.95f, -0.2f);
                confetti.transform.localScale = Vector3.one * 0.08f;
                var sr = confetti.AddComponent<SpriteRenderer>();
                sr.sprite = MakeSoftGlowSprite();
                sr.color = i % 3 == 0 ? new Color(1f, 0.82f, 0.08f, 0.9f) : (i % 3 == 1 ? new Color(0.25f, 1f, 1f, 0.82f) : new Color(0.82f, 0.55f, 1f, 0.82f));
                sr.sortingLayerName = "UI";
                sr.sortingOrder = 62;
                pieces.Add(confetti);
            }

            const float duration = 0.62f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float u = Mathf.Clamp01(t / duration);
                for (int i = 0; i < pieces.Count; i++)
                {
                    var confetti = pieces[i];
                    if (confetti == null) continue;
                    float angle = i * Mathf.PI * 2f / count;
                    Vector3 target = new Vector3(Mathf.Cos(angle) * 2.65f, 1.45f + Mathf.Sin(angle) * 0.82f - u * 0.65f, -0.2f);
                    confetti.transform.localPosition = Vector3.Lerp(new Vector3(0f, 1.95f, -0.2f), target, EaseOutBack(u));
                    confetti.transform.localScale = Vector3.one * Mathf.Lerp(0.1f, 0.025f, u);
                    confetti.transform.localRotation = Quaternion.Euler(0f, 0f, i * 31f + u * 210f);
                    var sr = confetti.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        var c = sr.color;
                        sr.color = new Color(c.r, c.g, c.b, 1f - u);
                    }
                }
                yield return null;
            }

            foreach (var confetti in pieces)
                if (confetti != null)
                    try { Destroy(confetti); } catch { }
        }

        private static IEnumerator CoRevealText(TextMeshPro tmp)
        {
            Vector3 start = tmp.transform.localScale == Vector3.one ? Vector3.one * 0.8f : tmp.transform.localScale;
            Vector3 end = Vector3.one;
            const float duration = 0.2f;

            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (tmp == null) yield break;
                float u = Mathf.Clamp01(t / duration);
                tmp.alpha = u;
                tmp.transform.localScale = Vector3.LerpUnclamped(start, end, EaseOutBack(u));
                yield return null;
            }

            if (tmp != null)
            {
                tmp.alpha = 1f;
                tmp.transform.localScale = end;
            }
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private static Sprite _softGlowSprite;
        private static Sprite MakeSoftGlowSprite()
        {
            if (_softGlowSprite != null) return _softGlowSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            var px = new Color[size * size];
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / radius;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply();
            _softGlowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _softGlowSprite.hideFlags = HideFlags.HideAndDontSave;
            return _softGlowSprite;
        }

        private TextMeshPro MakeText(GameObject parent, string name, TMP_FontAsset font, Material fontMat, float size, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;

            var tmp = go.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<TextMeshPro>()).Cast<TextMeshPro>();
            tmp.font = font;
            tmp.fontMaterial = fontMat;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.sortingLayerName = "UI";
                r.sortingOrder = 60;
            }
            return tmp;
        }
    }
}
