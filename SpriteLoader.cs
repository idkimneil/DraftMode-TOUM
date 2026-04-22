using System.IO;
using System.Reflection;
using UnityEngine;

namespace DraftModeTOUM
{
    public static class SpriteLoader
    {
        /// <summary>
        /// Loads an embedded PNG resource from inside the DLL and returns a Sprite.
        ///
        /// The resource name is: "<AssemblyName>.<FileName>"
        /// e.g. for button.png in the root of the project: "DraftModeTOUM.button.png"
        /// e.g. for Resources/button.png:                  "DraftModeTOUM.Resources.button.png"
        ///
        /// To embed button.png, add this to your .csproj:
        ///   <ItemGroup>
        ///     <EmbeddedResource Include="button.png" />
        ///   </ItemGroup>
        ///
        /// Usage:
        ///   Sprite? s = SpriteLoader.LoadEmbedded("button.png");
        ///   Sprite? s = SpriteLoader.LoadEmbedded("button.png", pixelsPerUnit: 200f);
        /// </summary>
        /// <param name="fileName">Just the file name, e.g. "button.png".</param>
        /// <param name="pixelsPerUnit">How many pixels equal one Unity unit. Default 100.</param>
        /// <returns>A Sprite, or null if the resource couldn't be loaded.</returns>
        public static Sprite? LoadEmbedded(string fileName, float pixelsPerUnit = 100f)
        {
            var asm = Assembly.GetExecutingAssembly();

            // Resource names are: AssemblyName.FileName
            // Subdirectory separators become dots, e.g. Resources/button.png -> AssemblyName.Resources.button.png
            string resourceName = $"{asm.GetName().Name}.{fileName}";

            using Stream? stream = asm.GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                // Log all available resource names to help diagnose mismatches
                string available = string.Join(", ", asm.GetManifestResourceNames());
                DraftModePlugin.Logger.LogWarning(
                    $"[SpriteLoader] Embedded resource '{resourceName}' not found. " +
                    $"Available: [{available}]");
                return null;
            }

            try
            {
                byte[] bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);

                var tex       = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.HideAndDontSave;

                if (!ImageConversion.LoadImage(tex, bytes))
                {
                    DraftModePlugin.Logger.LogWarning(
                        $"[SpriteLoader] ImageConversion failed for '{resourceName}'.");
                    return null;
                }

                var sprite       = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit);
                sprite.hideFlags = HideFlags.HideAndDontSave;

                DraftModePlugin.Logger.LogInfo(
                    $"[SpriteLoader] Loaded '{resourceName}' ({tex.width}x{tex.height}).");
                return sprite;
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning(
                    $"[SpriteLoader] Exception loading '{resourceName}': {ex.Message}");
                return null;
            }
        }
    }
}
