using System;
using TMPro;

namespace ThaiLanguage
{
    /// <summary>Harmony prefixes. Every body is wrapped so a failure can never break the game.</summary>
    internal static class Patches
    {
        // TMP only positions Thai vowels/tone marks (GPOS mark-to-base / mark-to-mark) when the text object
        // has the "mark" and "mkmk" features enabled; the game's texts only have "kern".
        public static void UguiGenerateTextMeshPrefix(TextMeshProUGUI __instance)
        {
            try { ThaiLanguageMod.EnsureMarkFeatures(__instance); }
            catch (Exception e) { ThaiLanguageMod.LogOnce("TextMeshProUGUI.GenerateTextMesh", e); }
        }

        public static void WorldGenerateTextMeshPrefix(TextMeshPro __instance)
        {
            try { ThaiLanguageMod.EnsureMarkFeatures(__instance); }
            catch (Exception e) { ThaiLanguageMod.LogOnce("TextMeshPro.GenerateTextMesh", e); }
        }
    }
}
