using MiraAPI.Utilities.Assets;
using Reactor.Utilities;
using UnityEngine;

namespace DraftMode;

public static class DraftAssets
{
    private const string ShortPath = "DraftMode.Resources";
    public static LoadableAsset<Sprite> QuitSprite { get; } =
        new LoadableResourceAsset($"{ShortPath}.QuitButton.png", 83.33f);
    public static LoadableAsset<Sprite> RerollSprite { get; } =
        new LoadableResourceAsset($"{ShortPath}.RerollButton.png");
    public static LoadableAsset<Sprite> DraftIcon { get; } =
        new LoadableResourceAsset($"{ShortPath}.DraftLogo.png");
    public static LoadableAsset<Sprite> DraftBanner { get; } =
        new LoadableResourceAsset($"{ShortPath}.DraftBanner.png");
}
