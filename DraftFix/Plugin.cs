using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Reactor;
using TownOfUs;

namespace DraftFix;

[BepInPlugin(Id, "TOU Draft Host Fix", "1.0.0")]
[BepInProcess("Among Us.exe")]
[BepInDependency(ReactorPlugin.Id)]
[BepInDependency(TownOfUsPlugin.Id)]
public sealed class DraftHostFixPlugin : BasePlugin
{
    public const string Id = "com.drafthostfix.idkimneil";

    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> ReassertPicks = null!;

    public override void Load()
    {
        Logger = Log;
        ReassertPicks = Config.Bind("Draft", "ReassertPicks", true,
            "After roles are assigned, re-apply any drafted role that was changed. Off by default because Crewpostor and similar settings rewrite roles on purpose. Mismatches are always logged.");
        new Harmony(Id).PatchAll();
    }
}
