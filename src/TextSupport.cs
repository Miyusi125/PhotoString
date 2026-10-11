using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace PhotoString
{
    internal static class TextSupport
    {
        internal static readonly FontFamily UserInterface = new FontFamily("Malgun Gothic, Segoe UI Emoji, Segoe UI Symbol");
        internal static readonly FontFamily CaptionInput = new FontFamily("Segoe UI Emoji, Malgun Gothic, Segoe UI Symbol");
        internal static readonly Typeface CaptionTypeface = new Typeface(new FontFamily("Georgia, Malgun Gothic, Segoe UI Emoji, Segoe UI Symbol"), System.Windows.FontStyles.Normal, System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal);
        internal static readonly Typeface InterfaceTypeface = new Typeface(UserInterface, System.Windows.FontStyles.Normal, System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal);
        private static int ScalarAt(string text, int index)
        {
            return Char.IsHighSurrogate(text[index]) && index + 1 < text.Length && Char.IsLowSurrogate(text[index + 1]) ? Char.ConvertToUtf32(text[index], text[index + 1]) : text[index];
        }
        // Framework 4.8's text elements handle surrogate pairs and combining marks.
        // Join emoji modifiers, flags, tags and ZWJ sequences before imposing a limit.
        internal static string Limit(string value, int maxLength)
        {
            value = value ?? ""; if (value.Length <= maxLength) return value;
            var starts = Boundaries(value);
            for (int i = 1; i < starts.Count; i++) if (starts[i] > maxLength) return value.Substring(0, starts[i - 1]);
            return value.Substring(0, starts[starts.Count - 1]);
        }
        internal static IEnumerable<string> Elements(string value)
        {
            var starts = Boundaries(value);
            for (int i = 0; i < starts.Count; i++) yield return value.Substring(starts[i], (i + 1 < starts.Count ? starts[i + 1] : value.Length) - starts[i]);
        }
        private static List<int> Boundaries(string value)
        {
            var starts = new List<int>(); int previous = -1, regionalCount = 0;
            foreach (int index in StringInfo.ParseCombiningCharacters(value))
            {
                int scalar = ScalarAt(value, index);
                bool regional = scalar >= 0x1F1E6 && scalar <= 0x1F1FF;
                bool joined = scalar == 0x200D || previous == 0x200D || (scalar >= 0x1F3FB && scalar <= 0x1F3FF) || (scalar >= 0xE0020 && scalar <= 0xE007F) || (regional && regionalCount % 2 == 1);
                if (starts.Count == 0 || !joined) starts.Add(index);
                regionalCount = regional ? regionalCount + 1 : 0;
                previous = scalar;
            }
            return starts;
        }
    }
}
