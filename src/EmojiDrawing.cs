using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PhotoString
{
    // Paint COLR/CPAL layers from the installed Windows font while preserving
    // WPF's glyph shaping, offsets, font fallback and ellipsis layout.
    // OpenType: https://learn.microsoft.com/typography/opentype/spec/colr
    internal static class EmojiDrawing
    {
        private sealed class Layer { internal ushort Glyph, Palette; }
        private sealed class ColorFont
        {
            internal readonly Dictionary<ushort, Layer[]> Glyphs = new Dictionary<ushort, Layer[]>();
            internal readonly Dictionary<ushort, Geometry> Outlines = new Dictionary<ushort, Geometry>();
            internal Brush[] Palette;
        }
        private static readonly Dictionary<string, ColorFont> Fonts = new Dictionary<string, ColorFont>();
        private static int UInt16(byte[] data, int offset) { return (data[offset] << 8) | data[offset + 1]; }
        private static int UInt32(byte[] data, int offset) { return checked((int)(((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3])); }
        private static int Table(byte[] data, string tag)
        {
            int count = UInt16(data, 4);
            for (int i = 0; i < count; i++)
            {
                int entry = 12 + i * 16;
                if (data[entry] == tag[0] && data[entry + 1] == tag[1] && data[entry + 2] == tag[2] && data[entry + 3] == tag[3]) return UInt32(data, entry + 8);
            }
            return 0;
        }
        private static ColorFont LoadFont(GlyphTypeface typeface)
        {
            string key = typeface.FontUri.ToString().ToUpperInvariant();
            if (key.IndexOf("SEGUIEMJ", StringComparison.Ordinal) < 0 || !typeface.FontUri.IsFile) return null;
            ColorFont font; if (Fonts.TryGetValue(key, out font)) return font;
            try
            {
                var data = File.ReadAllBytes(typeface.FontUri.LocalPath); int colr = Table(data, "COLR"), cpal = Table(data, "CPAL");
                if (colr == 0 || cpal == 0 || UInt16(data, colr) > 1) throw new InvalidDataException();
                // Version 1 can carry version 0 layers for older renderers.
                int glyphCount = UInt16(data, colr + 2), glyphStart = colr + UInt32(data, colr + 4), layerStart = colr + UInt32(data, colr + 8), layerCount = UInt16(data, colr + 12);
                int paletteCount = UInt16(data, cpal + 2), colorCount = UInt16(data, cpal + 6), firstColor = UInt16(data, cpal + 12), colorStart = cpal + UInt32(data, cpal + 8);
                if (glyphCount == 0 || UInt16(data, cpal + 4) == 0 || firstColor + paletteCount > colorCount) throw new InvalidDataException();
                font = new ColorFont { Palette = new Brush[paletteCount] };
                for (int i = 0; i < paletteCount; i++)
                {
                    int color = colorStart + (firstColor + i) * 4;
                    var brush = new SolidColorBrush(Color.FromArgb(data[color + 3], data[color + 2], data[color + 1], data[color])); brush.Freeze(); font.Palette[i] = brush;
                }
                for (int i = 0; i < glyphCount; i++)
                {
                    int record = glyphStart + i * 6; ushort glyph = (ushort)UInt16(data, record); int first = UInt16(data, record + 2), count = UInt16(data, record + 4);
                    if (first + count > layerCount) throw new InvalidDataException();
                    var layers = new Layer[count];
                    for (int j = 0; j < count; j++)
                    {
                        int layer = layerStart + (first + j) * 4; ushort palette = (ushort)UInt16(data, layer + 2);
                        if (palette != 0xFFFF && palette >= paletteCount) throw new InvalidDataException();
                        layers[j] = new Layer { Glyph = (ushort)UInt16(data, layer), Palette = palette };
                    }
                    font.Glyphs[glyph] = layers;
                }
            }
            catch (IOException) { font = null; }
            catch (UnauthorizedAccessException) { font = null; }
            catch (InvalidDataException) { font = null; }
            catch (IndexOutOfRangeException) { font = null; }
            catch (OverflowException) { font = null; }
            Fonts[key] = font; return font;
        }
        internal static bool MayContainEmoji(string text)
        {
            return text != null && text.Any(c => Char.IsSurrogate(c) || (c >= '\u2600' && c <= '\u27BF') || (c >= '\u2194' && c <= '\u2199') || (c >= '\u23E9' && c <= '\u23FA') || (c >= '\u2B05' && c <= '\u2B07') || c == '\u20E3' || c == '\uFE0F' || c == '\u00A9' || c == '\u00AE' || c == '\u203C' || c == '\u2049' || c == '\u2122' || c == '\u2139' || c == '\u231A' || c == '\u231B' || c == '\u2328' || c == '\u23CF' || c == '\u24C2' || c == '\u25AA' || c == '\u25AB' || c == '\u25B6' || c == '\u25C0' || (c >= '\u25FB' && c <= '\u25FE') || c == '\u2934' || c == '\u2935' || c == '\u2B1B' || c == '\u2B1C' || c == '\u2B50' || c == '\u2B55' || c == '\u3030' || c == '\u303D' || c == '\u3297' || c == '\u3299');
        }
        internal static void Draw(DrawingContext dc, FormattedText text, Point origin)
        {
            var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawText(text, origin);
            DrawDrawing(dc, visual.Drawing);
        }
        private static void DrawDrawing(DrawingContext dc, Drawing drawing)
        {
            var glyph = drawing as GlyphRunDrawing;
            if (glyph != null)
            {
                var run = glyph.GlyphRun; var font = LoadFont(run.GlyphTypeface);
                if (font == null || run.IsSideways || !run.GlyphIndices.Any(g => font.Glyphs.ContainsKey(g))) { dc.DrawDrawing(drawing); return; }
                double x = run.BaselineOrigin.X; bool rtl = (run.BidiLevel & 1) != 0;
                for (int i = 0; i < run.GlyphIndices.Count; i++)
                {
                    ushort id = run.GlyphIndices[i]; double advance = run.AdvanceWidths[i]; if (rtl) x -= advance;
                    Point offset = run.GlyphOffsets == null || run.GlyphOffsets.Count == 0 ? new Point() : run.GlyphOffsets[i];
                    dc.PushTransform(new TranslateTransform(x + (rtl ? -offset.X : offset.X), run.BaselineOrigin.Y - offset.Y));
                    dc.PushTransform(new ScaleTransform(run.FontRenderingEmSize, run.FontRenderingEmSize));
                    Layer[] layers;
                    if (font.Glyphs.TryGetValue(id, out layers)) foreach (var layer in layers) dc.DrawGeometry(layer.Palette == 0xFFFF ? glyph.ForegroundBrush : font.Palette[layer.Palette], null, Outline(font, run.GlyphTypeface, layer.Glyph));
                    else dc.DrawGeometry(glyph.ForegroundBrush, null, Outline(font, run.GlyphTypeface, id));
                    dc.Pop(); dc.Pop(); if (!rtl) x += advance;
                }
                return;
            }
            var group = drawing as DrawingGroup;
            if (group == null) { dc.DrawDrawing(drawing); return; }
            int pushes = 0;
            if (group.Transform != null && !group.Transform.Value.IsIdentity) { dc.PushTransform(group.Transform); pushes++; }
            if (group.ClipGeometry != null) { dc.PushClip(group.ClipGeometry); pushes++; }
            if (group.Opacity != 1) { dc.PushOpacity(group.Opacity); pushes++; }
            foreach (var child in group.Children) DrawDrawing(dc, child);
            while (pushes-- > 0) dc.Pop();
        }
        private static Geometry Outline(ColorFont font, GlyphTypeface typeface, ushort glyph)
        {
            Geometry geometry;
            if (!font.Outlines.TryGetValue(glyph, out geometry))
            {
                geometry = typeface.GetGlyphOutline(glyph, 1, 1); if (geometry.CanFreeze) geometry.Freeze();
                if (font.Outlines.Count >= 2048) font.Outlines.Clear(); font.Outlines[glyph] = geometry;
            }
            return geometry;
        }
    }
}
