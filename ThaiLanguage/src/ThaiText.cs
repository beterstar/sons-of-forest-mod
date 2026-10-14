using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ThaiLanguage
{
    /// <summary>
    /// Thai text helpers. Thai has no spaces between words, so TextMeshPro can only wrap
    /// at spaces (or mid-word). We ask Windows' own Thai word breaker (Uniscribe, usp10.dll)
    /// for word boundaries and insert ZERO WIDTH SPACE there, which TMP treats as a wrap point.
    /// </summary>
    internal static class ThaiText
    {
        internal const char Zwsp = '​';

        [StructLayout(LayoutKind.Sequential)]
        private struct ScriptAnalysis { public ushort Bits; public ushort State; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ScriptItem { public int CharPos; public ScriptAnalysis Analysis; }

        [DllImport("usp10.dll", CharSet = CharSet.Unicode)]
        private static extern int ScriptItemize(string chars, int count, int maxItems, IntPtr control, IntPtr state,
            [Out] ScriptItem[] items, out int itemCount);

        [DllImport("usp10.dll", CharSet = CharSet.Unicode)]
        private static extern int ScriptBreak(string chars, int count, ref ScriptAnalysis analysis, [Out] byte[] logAttr);

        private static bool _breakerFailed;

        internal static bool IsThai(char c) => c >= 'ก' && c <= '๛';

        internal static bool ContainsThai(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s) if (IsThai(c)) return true;
            return false;
        }

        /// <summary>Inserts ZWSP at Thai word boundaries, never inside {placeholders} or &lt;rich text tags&gt;.</summary>
        internal static string InsertWordBreaks(string s)
        {
            if (_breakerFailed || string.IsNullOrEmpty(s) || !ContainsThai(s)) return s;
            try
            {
                var protectedChar = new bool[s.Length];
                int brace = 0, tag = 0;
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '{') brace++;
                    if (c == '<') tag++;
                    protectedChar[i] = brace > 0 || tag > 0;
                    if (c == '}' && brace > 0) brace--;
                    if (c == '>' && tag > 0) tag--;
                }

                int max = s.Length + 2;
                var items = new ScriptItem[max + 1];
                if (ScriptItemize(s, s.Length, max, IntPtr.Zero, IntPtr.Zero, items, out int n) != 0) return s;

                var breakBefore = new bool[s.Length];
                for (int i = 0; i < n; i++)
                {
                    int start = items[i].CharPos, end = items[i + 1].CharPos;
                    if (end - start < 2) continue;
                    string run = s.Substring(start, end - start);
                    if (!ContainsThai(run)) continue;
                    var attrs = new byte[run.Length];
                    var analysis = items[i].Analysis;
                    if (ScriptBreak(run, run.Length, ref analysis, attrs) != 0) continue;
                    for (int j = 1; j < run.Length; j++)
                    {
                        int k = start + j;
                        // bit 0 = fSoftBreak: a line may break before this character
                        if ((attrs[j] & 1) != 0 && IsThai(s[k - 1]) && IsThai(s[k]) && !protectedChar[k] && !protectedChar[k - 1])
                            breakBefore[k] = true;
                    }
                }

                var sb = new StringBuilder(s.Length + 32);
                for (int k = 0; k < s.Length; k++)
                {
                    if (breakBefore[k] && s[k - 1] != Zwsp) sb.Append(Zwsp);
                    sb.Append(s[k]);
                }
                return sb.ToString();
            }
            catch (Exception)
            {
                _breakerFailed = true; // usp10 unavailable: keep text as-is
                return s;
            }
        }

        internal static bool HasLatinWord(string s)
        {
            int run = 0;
            foreach (var c in s)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) { if (++run >= 2) return true; }
                else run = 0;
            }
            return false;
        }

        /// <summary>All Thai letters, vowels, tone marks and digits, used to pre-fill the font atlas.</summary>
        internal static string AllThaiCharacters()
        {
            var sb = new StringBuilder();
            for (char c = 'ก'; c <= 'ฺ'; c++) sb.Append(c);
            for (char c = '฿'; c <= '๛'; c++) sb.Append(c);
            sb.Append(Zwsp);
            return sb.ToString();
        }
    }
}
