using MiraAPI.Utilities.Assets;
using Reactor.Utilities;
using UnityEngine;

namespace DraftModeTOUM;

public static class DraftAssets
{
    private const string ShortPath = "DraftModeTOUM.Resources";
    public static LoadableAsset<Sprite> QuitSprite { get; } =
        new LoadableResourceAsset($"{ShortPath}.QuitButton.png", 83.33f);

}
