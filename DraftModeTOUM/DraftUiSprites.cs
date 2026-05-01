using UnityEngine;

namespace DraftModeTOUM;

public static class DraftUiSprites
{
    private static Sprite _softGlow;
    private static Sprite _white;

    public static Sprite SoftGlow()
    {
        if (_softGlow != null) return _softGlow;

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
        _softGlow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        _softGlow.hideFlags = HideFlags.HideAndDontSave;
        return _softGlow;
    }

    public static Sprite White()
    {
        if (_white != null) return _white;

        const int size = 4;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = Color.white;

        tex.SetPixels(px);
        tex.Apply();
        _white = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        _white.hideFlags = HideFlags.HideAndDontSave;
        return _white;
    }
}
