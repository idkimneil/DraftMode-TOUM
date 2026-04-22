using MiraAPI.Utilities.Assets;
using UnityEngine;

namespace DraftModeTOUM;

public sealed class EmbeddedSpriteAsset : LoadableAsset<Sprite>
{
    private readonly Sprite _sprite;
    public EmbeddedSpriteAsset(Sprite sprite) => _sprite = sprite;
    public override Sprite LoadAsset() => _sprite;
}