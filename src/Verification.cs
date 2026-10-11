using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PhotoString
{
    public static class Verification
    {
        private static readonly List<string> Results = new List<string>();
        private static void Check(bool passed, string description) { Results.Add((passed ? "PASS " : "FAIL ") + description); if (!passed) throw new Exception(description); }
        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var root = Path.Combine(folder, "test-data", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                var store = new Store(root); store.Load();
                Check(store.State.Photos.Count == 6 && store.State.Lines.Count == 2, "first-run arrangement has six samples and two strings");
                store.State.Photos[0].Caption = "한글 메모 ✓"; store.State.Photos[0].T = .34; store.State.Photos[0].OffsetY = 36; store.State.Photos[0].Width = 220; store.Save();
                var read = new Store(root); read.Load();
                Check(read.State.Photos[0].Caption == "한글 메모 ✓" && Math.Abs(read.State.Photos[0].T - .34) < .001 && read.State.Photos[0].Width == 220 && read.State.Photos[0].OffsetY == 36, "position, size and Unicode caption survive restart");
                read.State.Photos[0].T = .64; read.Save();
                File.WriteAllText(Path.Combine(root, "arrangement.json"), "broken json"); var recovered = new Store(root); recovered.Load();
                Check(Math.Abs(recovered.State.Photos[0].T - .34) < .001 && recovered.Notice != null, "damaged save recovers from atomic backup");
                VerifyFullScreenPlacement(root, folder);
                var defaultStore = new Store(Path.Combine(root, "clean")); defaultStore.Load();
                var scene = new Scene(defaultStore) { Editable = false }; Render(scene, 1200, 760, Path.Combine(folder, "arrangement.png"), true);
                Render(scene, 1200, 760, Path.Combine(folder, "transparent.png"), false);
                VerifyEmojiCaptions(root, folder);
                VerifyPhotoLayers(root, folder);
                VerifyAdjustmentPins(root, folder);
                VerifyAppearance(root, folder);
                var bitmap = Store.LoadBitmap(Path.Combine(folder, "arrangement.png")); Check(bitmap.PixelWidth == 1000, "large images decode to bounded width");
                var first = defaultStore.State.Photos[0]; string oldId = first.Id; string oldLine = first.LineId; double oldT = first.T;
                defaultStore.Import(Path.Combine(folder, "arrangement.png"), first);
                Check(first.Id == oldId && first.LineId == oldLine && first.T == oldT && first.Demo == -1 && defaultStore.State.Photos.Count == 6, "replace retains photo identity and position");
                var copied = Path.Combine(defaultStore.Root, "photos", first.File);
                Check(File.Exists(copied) && defaultStore.ImageFor(first) != null, "photo is copied into managed storage and can be decoded");
                var fresh = defaultStore.Import(Path.Combine(folder, "arrangement.png"), null);
                Check(defaultStore.State.Photos.Count == 7 && fresh.T > .05 && fresh.T < .95, "adding photo chooses an available string position");
                first.Width = Double.NaN; first.T = 900; first.File = "..\\outside.png"; defaultStore.Normalize();
                Check(first.Width == 90 && first.T == 1 && first.File == null, "invalid numbers and external image paths are rejected");
                defaultStore.State.Photos.Remove(fresh); Check(defaultStore.State.Photos.Count == 6, "removing a photo keeps other photos");
                var anchor = scene.Anchor(defaultStore.State.Photos[1]); var p = defaultStore.State.Photos[1]; p.OffsetY += 25;
                Check(Math.Abs(scene.Anchor(p).Y - anchor.Y - 25) < .001, "photo anchor follows vertical movement");
                Check(scene.StringGeometry(defaultStore.State.Lines.First(l => l.Id == p.LineId)).StrokeContains(new Pen(Brushes.Black, 5), scene.Anchor(p)), "string geometry passes through moved photo clip");
                var controller = new Controller(app, Path.Combine(root, "manager")); controller.Manager = new MainWindow(controller);
                controller.Desktop = new DesktopWindow(controller.Store);
                var layerPicker = Descendants<ComboBox>(controller.Manager.PreviewContent).First(c => c.Items.OfType<ComboBoxItem>().Any(i => i.Tag is Photo));
                var selectedPhoto = controller.Store.State.Photos[0];
                layerPicker.SelectedItem = layerPicker.Items.OfType<ComboBoxItem>().First(i => i.Tag == selectedPhoto);
                var layerFront = Descendants<Button>(controller.Manager.PreviewContent).First(b => (string)b.Content == "맨 앞");
                var layerBack = Descendants<Button>(controller.Manager.PreviewContent).First(b => (string)b.Content == "맨 뒤");
                Check(controller.Manager.Scene.Selected == selectedPhoto && layerFront.IsEnabled && !layerBack.IsEnabled, "layer list can select a photo and enables only valid layer moves");
                layerFront.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(controller.Store.State.Photos.Last() == selectedPhoto && controller.Manager.Scene.Selected == selectedPhoto && !layerFront.IsEnabled && layerBack.IsEnabled && ((ComboBoxItem)layerPicker.SelectedItem).Tag == selectedPhoto, "layer button changes draw order while keeping selection and list synchronized");
                VerifyCaptionInput(controller);
                VerifyDesignControls(controller, folder);
                Render(controller.Manager.PreviewContent, 980, 810, Path.Combine(folder, "manager.png"), false);
                Render(controller.Manager.PreviewContent, 884, 680, Path.Combine(folder, "manager-small.png"), false);
                Check(File.Exists(Path.Combine(folder, "manager.png")) && new FileInfo(Path.Combine(folder, "manager.png")).Length > 10000, "management window renders to a reviewable image");
                controller.Desktop.Close(); controller.Dispose(); app.Shutdown();
                File.WriteAllLines(Path.Combine(folder, "verification.txt"), Results.ToArray(), Encoding.UTF8); return 0;
            }
            catch (Exception ex) { Results.Add(ex.ToString()); File.WriteAllLines(Path.Combine(folder, "verification.txt"), Results.ToArray(), Encoding.UTF8); return 1; }
        }
        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            var match = root as T; if (match != null) yield return match;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
        private static IEnumerable<GlyphRun> GlyphRuns(Drawing drawing)
        {
            var glyph = drawing as GlyphRunDrawing; if (glyph != null) yield return glyph.GlyphRun;
            var group = drawing as DrawingGroup;
            if (group != null) foreach (var child in group.Children) foreach (var run in GlyphRuns(child)) yield return run;
        }
        private static void VerifyEmojiCaptions(string root, string folder)
        {
            var prefix = new string('a', 47);
            Check(TextSupport.Limit(prefix + "😀tail", 48) == prefix && TextSupport.Limit(new string('a', 46) + "😀tail", 48) == new string('a', 46) + "😀", "caption limits preserve whole surrogate-pair emoji at the length boundary");
            Check(TextSupport.Limit(prefix + "❤️tail", 48) == prefix && TextSupport.Limit(prefix + "e\u0301tail", 48) == prefix, "caption limits keep variation selectors and combining marks attached to their base");
            Check(TextSupport.Limit(new string('a', 45) + "👍🏽tail", 48) == new string('a', 45) && TextSupport.Limit(new string('a', 45) + "🇰🇷tail", 48) == new string('a', 45), "caption limits preserve skin-tone and flag emoji as complete sequences");
            Check(TextSupport.Limit(new string('a', 42) + "👨‍👩‍👧‍👦tail", 48) == new string('a', 42), "caption limits never split a joined family emoji");
            var familyVisual = new DrawingVisual(); using (var dc = familyVisual.RenderOpen()) dc.DrawText(Scene.FormatText("👨‍👩‍👧‍👦", 27, Brushes.Black, true, 720), new Point());
            var referenceVisual = new DrawingVisual(); using (var dc = referenceVisual.RenderOpen()) dc.DrawText(new FormattedText("👨‍👩‍👧‍👦", System.Globalization.CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, new Typeface("Segoe UI Emoji"), 27, Brushes.Black, 1.0), new Point());
            Check(GlyphRuns(familyVisual.Drawing).SelectMany(r => r.GlyphIndices).SequenceEqual(GlyphRuns(referenceVisual.Drawing).SelectMany(r => r.GlyphIndices)), "caption font selection keeps the complete glyph sequence for a joined family emoji");
            foreach (double width in new[] { 20.0, 35, 50, 65, 90, 130 })
            {
                var shortened = Scene.FormatText("가족 👨‍👩‍👧‍👦과 함께하는 순간", 27, Brushes.Black, true, width);
                Check((!shortened.Text.Contains("👨") || shortened.Text.Contains("👨‍👩‍👧‍👦")) && shortened.Width <= width + .01, "pixel-width caption ellipsis retains whole emoji sequences at width " + width);
            }
            var captions = new[] { "오늘도 행복해요 😀 🥰", "커피 ☕ 와 사진 📷 ⭐", "좋은 하루 ❤️ ✨", "엄지 척 👍🏽", "함께하는 우리 👨‍👩‍👧‍👦", "여행의 기억 🇰🇷 🌿" };
            var store = new Store(Path.Combine(root, "emoji-captions")); store.Load();
            for (int i = 0; i < captions.Length; i++) { store.State.Photos[i].Caption = captions[i]; store.State.Photos[i].Angle = 0; }
            store.Save(); var reopened = new Store(store.Root); reopened.Load();
            Check(reopened.State.Photos.Select(p => p.Caption).SequenceEqual(captions), "Korean captions and emoji sequences survive save and reload unchanged");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 760, 400));
                for (int i = 0; i < captions.Length; i++) Scene.Text(dc, captions[i], new Point(20, 15 + i * 50), 27, "#363f35", false, true, 720);
                Scene.Text(dc, new string('a', 22) + "😀extra", new Point(20, 330), 20, "#363f35", false, true, 250);
            }
            var glyphProbe = new DrawingVisual(); using (var dc = glyphProbe.RenderOpen()) foreach (string caption in captions) dc.DrawText(Scene.FormatText(caption, 27, Brushes.Black, true, 720), new Point());
            var runs = GlyphRuns(glyphProbe.Drawing).ToList();
            Check(runs.Count > 0 && runs.All(r => r.GlyphIndices.All(g => g != 0)), "caption renderer resolves emoji to real glyphs instead of missing-character boxes");
            Check(runs.Any(r => r.GlyphTypeface.FontUri.ToString().IndexOf("seguiemj", StringComparison.OrdinalIgnoreCase) >= 0), "caption font fallback uses the installed Segoe UI Emoji font");
            Render(new Image { Source = new DrawingImage(visual.Drawing) }, 760, 400, Path.Combine(folder, "emoji-caption-text.png"), false);
            using (var rendered = new System.Drawing.Bitmap(Path.Combine(folder, "emoji-caption-text.png")))
            {
                int colored = 0;
                for (int y = 0; y < rendered.Height; y++) for (int x = 0; x < rendered.Width; x++)
                {
                    var pixel = rendered.GetPixel(x, y);
                    if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B)) > 90) colored++;
                }
                Check(colored > 500, "photo captions paint the emoji font's color layers rather than incomplete monochrome base glyphs");
            }
            Render(new Scene(reopened) { Editable = false }, 1200, 760, Path.Combine(folder, "emoji-captions-desktop.png"), true);
            Render(new Scene(reopened) { ShowApplicationArea = true, PreviewAspectOverride = 1200.0 / 760 }, 1248, 836, Path.Combine(folder, "emoji-captions-settings.png"), false);
            reopened.State.Photos[0].Caption = prefix + "😀tail"; reopened.Normalize();
            Check(reopened.State.Photos[0].Caption == prefix, "saved caption normalization removes overflowing emoji as a whole");
            var source = Path.Combine(store.Root, new string('b', 31) + "😀tail.png"); File.Copy(Path.Combine(folder, "arrangement.png"), source);
            var imported = store.Import(source, null);
            Check(imported.Caption == new string('b', 31), "automatic captions from image filenames do not split emoji at the import limit");
        }
        private static void VerifyCaptionInput(Controller controller)
        {
            var photo = controller.Manager.Scene.Selected; var input = Descendants<TextBox>(controller.Manager.PreviewContent).Single();
            input.Text = new string('a', 47) + "😀tail";
            Check(input.Text == new string('a', 47) && photo.Caption == input.Text, "caption editor safely limits pasted emoji text without leaving a partial surrogate");
            input.Text = "좋아하는 순간 😀 ❤️ 📷"; controller.SaveNow();
            var reopened = new Store(controller.Store.Root); reopened.Load();
            Check(reopened.State.Photos.First(p => p.Id == photo.Id).Caption == input.Text, "emoji entered in the caption editor is saved without corruption");
        }
        private static void VerifyAppearance(string root, string folder)
        {
            var legacyRoot = Path.Combine(root, "legacy-style"); Directory.CreateDirectory(legacyRoot);
            File.WriteAllText(Path.Combine(legacyRoot, "arrangement.json"), "{\"Version\":1,\"Lines\":[{\"Id\":\"legacy\",\"Left\":100,\"Right\":1100,\"Y1\":145,\"Y2\":130,\"Sag\":48}],\"Photos\":[],\"Visible\":true,\"Monitor\":\"\"}");
            var legacy = new Store(legacyRoot); legacy.Load();
            Check(legacy.State.Lines[0].Appearance != null && legacy.State.Lines[0].Appearance.RopeWidth == 2.8 && legacy.State.Lines[0].Appearance.KnobSize == 14, "existing arrangements without appearance fields retain default rope and knob styles");
            var store = new Store(Path.Combine(root, "styles")); store.Load();
            var source = Path.Combine(root, "source-texture.png"); File.Copy(Path.Combine(folder, "arrangement.png"), source);
            var material = store.ImportMaterial(source); File.Delete(source);
            var style = DesignCatalog.BuiltIns().First(p => p.Id == "built-in-silver").Style.Copy();
            style.RopeTexture = "image"; style.RopeImage = material; style.KnobImage = material; style.KnobTexture = "image"; style.KnobArt = material; style.KnobShape = "image"; style.RopeColor = "#FF8800"; style.KnobSize = 22;
            store.State.Lines[0].Appearance = style; var otherBefore = store.State.Lines[1].Appearance.RopeColor; store.Save();
            var loaded = new Store(store.Root); loaded.Load(); var saved = loaded.State.Lines[0].Appearance;
            Check(saved.RopeColor == "#FF8800" && saved.RopeMaterial == "metal" && saved.RopeDesign == "double" && saved.KnobSize == 22 && saved.KnobShape == "image" && saved.KnobArt == material && loaded.State.Lines[1].Appearance.RopeColor == otherBefore, "per-rope color, material, texture, design and image settings survive restart independently");
            Check(loaded.MaterialImage(material) != null && File.Exists(Path.Combine(store.Root, "materials", material)), "imported material images remain available after the source is removed");
            Check(loaded.MaterialImage("..\\outside.png") == null, "material loading rejects paths outside managed storage");
            var icon = new DrawingVisual(); using (var dc = icon.RenderOpen()) dc.DrawEllipse(Brushes.Magenta, null, new Point(32, 32), 20, 20);
            var iconBitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); iconBitmap.Render(icon);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(iconBitmap)); var iconSource = Path.Combine(root, "transparent-icon.png");
            using (var stream = File.Create(iconSource)) encoder.Save(stream);
            var artFile = loaded.ImportMaterial(iconSource); var art = (BitmapSource)loaded.MaterialImage(artFile);
            var pixelData = new byte[4]; new FormatConvertedBitmap(art, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(0, 0, 1, 1), pixelData, 4, 0);
            Check(pixelData[3] == 0, "imported knob PNG retains its transparent background");
            var artStyle = new DecorationStyle { KnobShape = "image", KnobArt = artFile, KnobSize = 22 };
            Render(new DesignPreview(loaded) { Appearance = artStyle }, 600, 150, Path.Combine(folder, "transparent-knob.png"), false);
            using (var transparent = new System.Drawing.Bitmap(Path.Combine(folder, "transparent-knob.png")))
            {
                var outside = transparent.GetPixel(26, 70); var center = transparent.GetPixel(42, 60);
                Check(outside.R == 247 && outside.G == 245 && outside.B == 240 && center.R == 255 && center.B == 255, "custom knob artwork uses its own silhouette without filling transparent pixels");
            }
            VerifyIndependentKnobs(loaded, folder, artFile);
            var bad = new DecorationStyle { RopeColor = "#nothex", KnobColor = null, RopeWidth = 500, KnobSize = 1000, RopeImage = "..\\outside.png", KnobShape = "unknown" }; bad.Normalize();
            Check(bad.RopeColor == DecorationStyle.Default.RopeColor && bad.KnobColor == DecorationStyle.Default.KnobColor && bad.RopeWidth == 12 && bad.KnobSize == 26 && bad.RopeImage == null && bad.KnobShape == "circle", "invalid appearance data is normalized to supported colors, dimensions, shapes and paths");
            var catalog = new DesignCatalog(store.Root); Check(catalog.Items.Count == 8, "eight built-in design presets are available without downloads");
            var personal = catalog.Add("내 질감", style); style.RopeColor = "#000000";
            var reopened = new DesignCatalog(store.Root); var recovered = reopened.Items.First(p => p.Id == personal.Id);
            Check(recovered.Style.RopeColor == "#FF8800" && recovered.Style.RopeImage == material && recovered.Style.KnobArt == material && !recovered.BuiltIn, "personal catalog saves an independent design with imported asset references");
            reopened.Remove(recovered); Check(new DesignCatalog(store.Root).Items.Count == 8 && loaded.MaterialImage(material) != null && loaded.State.Lines[0].Appearance.RopeColor == "#FF8800", "removing a personal preset retains applied styles and imported images");
            var sheet = new Grid(); sheet.ColumnDefinitions.Add(new ColumnDefinition()); sheet.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 4; i++) sheet.RowDefinitions.Add(new RowDefinition());
            var fingerprints = new HashSet<string>(); var presets = DesignCatalog.BuiltIns();
            for (int i = 0; i < presets.Count; i++)
            {
                var sample = new DesignPreview(loaded) { Appearance = presets[i].Style.Copy() }; var file = Path.Combine(folder, "preset-" + i + ".png");
                Render(sample, 380, 120, file, true);
                using (var sha = System.Security.Cryptography.SHA256.Create()) fingerprints.Add(Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(file))));
                var card = new Border { Margin = new Thickness(8), Padding = new Thickness(14), Background = Scene.Brush("#fffef8"), BorderBrush = Scene.Brush("#deddd1"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
                var content = new Grid(); content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) }); content.RowDefinitions.Add(new RowDefinition()); card.Child = content;
                content.Children.Add(MainWindow.Label(presets[i].Name, 13, "#4e694a")); var drawing = new DesignPreview(loaded) { Appearance = presets[i].Style.Copy() }; Grid.SetRow(drawing, 1); content.Children.Add(drawing);
                Grid.SetRow(card, i / 2); Grid.SetColumn(card, i % 2); sheet.Children.Add(card);
            }
            Check(fingerprints.Count == presets.Count, "all built-in presets produce distinct rendered designs"); Render(sheet, 960, 660, Path.Combine(folder, "catalog.png"), false);
            Render(new DesignPreview(loaded) { Appearance = saved.Copy() }, 600, 150, Path.Combine(folder, "imported-design.png"), true);
        }
        private static void VerifyIndependentKnobs(Store store, string folder, string leftArt)
        {
            var icon = new DrawingVisual(); using (var dc = icon.RenderOpen()) dc.DrawRectangle(Brushes.Cyan, null, new Rect(12, 12, 40, 40));
            var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); bitmap.Render(icon);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); var source = Path.Combine(store.Root, "cyan-icon.png");
            using (var stream = File.Create(source)) encoder.Save(stream);
            var rightArt = store.ImportMaterial(source); File.Delete(source);
            var style = store.State.Lines[0].Appearance.Copy(); var commonArt = style.KnobArt;
            Check(style.LeftKnob == null && style.RightKnob == null && style.ForKnob(-1).KnobArt == commonArt && style.ForKnob(1).KnobArt == commonArt, "saved shared knob images remain applied to both endpoints without migration");
            style.EditKnobs(-1, k => { k.KnobArt = leftArt; k.KnobShape = "image"; k.KnobSize = 22; k.KnobImage = leftArt; k.KnobTexture = "image"; });
            Check(style.ForKnob(-1).KnobArt == leftArt && style.ForKnob(1).KnobArt == commonArt && style.RightKnob == null, "changing the left knob image preserves the right knob's inherited artwork");
            style.EditKnobs(1, k => { k.KnobArt = rightArt; k.KnobShape = "image"; k.KnobSize = 12; k.KnobImage = rightArt; k.KnobTexture = "image"; });
            store.State.Lines[0].Appearance = style; store.Save(); var reopened = new Store(store.Root); reopened.Load(); var saved = reopened.State.Lines[0].Appearance;
            Check(saved.ForKnob(-1).KnobArt == leftArt && saved.ForKnob(1).KnobArt == rightArt && saved.ForKnob(-1).KnobImage == leftArt && saved.ForKnob(1).KnobImage == rightArt && saved.ForKnob(-1).KnobSize == 22 && saved.ForKnob(1).KnobSize == 12, "distinct left and right artwork, texture images and sizes survive restart");
            Render(new DesignPreview(reopened) { Appearance = saved.Copy() }, 600, 150, Path.Combine(folder, "independent-knobs.png"), false);
            using (var rendered = new System.Drawing.Bitmap(Path.Combine(folder, "independent-knobs.png")))
            {
                var left = rendered.GetPixel(42, 60); var right = rendered.GetPixel(558, 60);
                Check(left.R == 255 && left.G == 0 && left.B == 255 && right.R == 0 && right.G == 255 && right.B == 255, "design preview renders the selected artwork separately at each endpoint");
            }
            var displayStore = new Store(store.Root); displayStore.State = new Arrangement();
            var line = new StringLine { Id = "side-images", Left = 160, Right = 1040, Y1 = 220, Y2 = 220, Appearance = saved.Copy() }; displayStore.State.Lines.Add(line);
            var scene = new Scene(displayStore) { Editable = false }; Render(scene, 1200, 760, Path.Combine(folder, "independent-knobs-desktop.png"), false);
            using (var rendered = new System.Drawing.Bitmap(Path.Combine(folder, "independent-knobs-desktop.png")))
            {
                var left = rendered.GetPixel(160, 220); var right = rendered.GetPixel(1040, 220);
                Check(left.R == 255 && left.G == 0 && left.B == 255 && right.R == 0 && right.G == 255 && right.B == 255, "desktop scene draws separate left and right knob images");
            }
            scene.Editable = true; scene.ShowApplicationArea = true; scene.PreviewAspectOverride = 1200.0 / 760;
            Render(scene, 1248, 836, Path.Combine(folder, "independent-knobs-settings.png"), false);
            Photo hitPhoto; StringLine hitLine; int handle;
            scene.FindDragTarget(new Point(160, 245), out hitPhoto, out hitLine, out handle);
            Check(hitLine == line && handle == -1, "larger left knob retains its full draggable hit area");
            scene.FindDragTarget(new Point(1040, 245), out hitPhoto, out hitLine, out handle);
            Check(hitLine == null, "smaller right knob uses its own hit radius");
            var copy = saved.Copy(); copy.LeftKnob.Art = rightArt; copy.RightKnob.Size = 26;
            Check(saved.LeftKnob.Art == leftArt && saved.RightKnob.Size == 12, "copying a rope style does not share mutable endpoint settings");
            var catalog = new DesignCatalog(Path.Combine(store.Root, "side-catalog")); var preset = catalog.Add("좌우 이미지", saved);
            saved.LeftKnob.Art = rightArt;
            var restored = new DesignCatalog(Path.Combine(store.Root, "side-catalog")).Items.First(p => p.Id == preset.Id).Style;
            Check(preset.Style.LeftKnob.Art == leftArt && restored.ForKnob(-1).KnobArt == leftArt && restored.ForKnob(1).KnobArt == rightArt, "personal presets preserve independent endpoint images without sharing style references");
            restored.EditKnobs(-1, k => { k.KnobArt = null; k.KnobImage = null; k.KnobTexture = "smooth"; k.KnobShape = "circle"; });
            Check(restored.ForKnob(-1).KnobArt == null && restored.ForKnob(-1).KnobImage == null && restored.ForKnob(1).KnobArt == rightArt && restored.ForKnob(1).KnobImage == rightArt, "clearing one knob's images preserves the opposite knob's images");
            restored.EditKnobs(0, k => k.KnobColor = "#123456");
            Check(restored.ForKnob(-1).KnobColor == "#123456" && restored.ForKnob(1).KnobColor == "#123456" && restored.ForKnob(-1).KnobArt == null && restored.ForKnob(1).KnobArt == rightArt, "editing both knobs updates only the chosen property while preserving distinct images");
            restored.LeftKnob.Art = "..\\outside.png"; restored.RightKnob.Size = Double.NaN; restored.Normalize();
            Check(restored.LeftKnob.Art == null && restored.RightKnob.Size == 14, "endpoint overrides validate managed image paths and dimensions");
        }
        private static void VerifyDesignControls(Controller controller, string folder)
        {
            var window = new DesignWindow(controller);
            var targets = Descendants<ComboBox>(window.PreviewContent).First(c => c.Items.OfType<ComboBoxItem>().Any(i => i.Tag is StringLine));
            var presets = Descendants<ComboBox>(window.PreviewContent).First(c => c.Items.OfType<ComboBoxItem>().Any(i => i.Tag is DesignPreset));
            presets.SelectedItem = presets.Items.OfType<ComboBoxItem>().First(i => ((i.Tag as DesignPreset) ?? new DesignPreset()).Id == "built-in-pink");
            Check(controller.Store.State.Lines.All(l => l.Appearance.KnobShape == "flower") && !Object.ReferenceEquals(controller.Store.State.Lines[0].Appearance, controller.Store.State.Lines[1].Appearance), "catalog selection applies independent copies to all ropes");
            targets.SelectedIndex = 1;
            presets.SelectedItem = presets.Items.OfType<ComboBoxItem>().First(i => ((i.Tag as DesignPreset) ?? new DesignPreset()).Id == "built-in-silver");
            Check(controller.Store.State.Lines[0].Appearance.KnobMaterial == "metal" && controller.Store.State.Lines[1].Appearance.KnobShape == "flower", "individual rope selection changes only that rope's design");
            var color = Descendants<TextBox>(window.PreviewContent).First(t => t.Text == "#ADB8C4"); color.Text = "bad";
            color.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, 0, null, null) { RoutedEvent = System.Windows.Input.Keyboard.LostKeyboardFocusEvent });
            Check(controller.Store.State.Lines[0].Appearance.RopeColor == "#ADB8C4", "invalid typed colors are rejected without modifying the applied style");
            color.Text = "#ff8800";
            color.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, 0, null, null) { RoutedEvent = System.Windows.Input.Keyboard.LostKeyboardFocusEvent });
            Check(controller.Store.State.Lines[0].Appearance.RopeColor == "#FF8800" && controller.Store.State.Lines[1].Appearance.RopeColor == "#D6A0B8", "valid typed colors update only the selected rope");
            var sides = Descendants<ComboBox>(window.PreviewContent).First(c => (string)c.Tag == "knob-side");
            Check(sides.SelectedIndex == 0, "knob controls start with the left endpoint selected");
            var knobColor = Descendants<TextBox>(window.PreviewContent).First(t => t.Text == "#BBC5D1"); knobColor.Text = "#12ab34";
            knobColor.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, 0, null, null) { RoutedEvent = System.Windows.Input.Keyboard.LostKeyboardFocusEvent });
            var lineStyle = controller.Store.State.Lines[0].Appearance;
            Check(lineStyle.ForKnob(-1).KnobColor == "#12AB34" && lineStyle.ForKnob(1).KnobColor == "#BBC5D1", "knob field edits affect only the selected endpoint");
            sides.SelectedIndex = 1;
            var shape = Descendants<ComboBox>(window.PreviewContent).First(c => c.Items.Contains("내 노브 이미지")); shape.SelectedIndex = 3;
            Check(knobColor.Text == "#BBC5D1" && lineStyle.ForKnob(1).KnobShape == "flower" && lineStyle.ForKnob(-1).KnobShape == "hexagon", "switching sides shows the opposite knob's settings and edits it independently");
            var material = controller.Store.ImportMaterial(Path.Combine(folder, "arrangement.png"));
            lineStyle.EditKnobs(0, k => { k.KnobArt = material; k.KnobImage = material; k.KnobShape = "image"; k.KnobTexture = "image"; });
            sides.SelectedIndex = 0;
            var clearImages = Descendants<Button>(window.PreviewContent).Last(b => (string)b.Content == "이미지 해제"); clearImages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(lineStyle.ForKnob(-1).KnobArt == null && lineStyle.ForKnob(-1).KnobImage == null && lineStyle.ForKnob(1).KnobArt == material && lineStyle.ForKnob(1).KnobImage == material, "image clear button removes only the selected knob's artwork and texture");
            sides.SelectedIndex = 2; shape.SelectedIndex = 2;
            Check(lineStyle.ForKnob(-1).KnobShape == "hexagon" && lineStyle.ForKnob(1).KnobShape == "hexagon" && lineStyle.ForKnob(1).KnobArt == material && lineStyle.ForKnob(-1).KnobColor == "#12AB34", "both-knob control changes apply to both endpoints without overwriting unrelated properties");
            sides.SelectedIndex = 0;
            var name = Descendants<TextBox>(window.PreviewContent).First(t => t.Text == "나의 디자인"); name.Text = "내 오렌지 메탈";
            Descendants<Button>(window.PreviewContent).First(b => (string)b.Content == "내 디자인 저장").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(new DesignCatalog(controller.Store.Root).Items.Any(p => p.Name == "내 오렌지 메탈" && p.Style.RopeColor == "#FF8800" && p.Style.ForKnob(-1).KnobArt == null && p.Style.ForKnob(1).KnobArt == material), "save button adds the customized design and independent knob images to the personal catalog");
            Render(window.PreviewContent, 830, 780, Path.Combine(folder, "design-window.png"), false);
            Render(window.PreviewContent, 750, 675, Path.Combine(folder, "design-window-small.png"), false);
            window.Close();
        }
        private static void VerifyAdjustmentPins(string root, string folder)
        {
            var store = new Store(Path.Combine(root, "knobs")); store.State = new Arrangement();
            var line = new StringLine { Id = "covered-knobs", Left = 500, Right = 700, Y1 = 320, Y2 = 320 }; store.State.Lines.Add(line);
            var photo = new Photo { Id = "cover", LineId = line.Id, T = .5, OffsetY = -170, Width = 280, Demo = 0, Caption = "노브를 덮는 사진" }; store.State.Photos.Add(photo);
            var second = new Photo { Id = "front-cover", LineId = line.Id, T = .5, OffsetY = -170, Width = 280, Demo = 4, Caption = "맨 앞 사진" }; store.State.Photos.Add(second);
            var preview = new Scene(store) { ShowApplicationArea = true, PreviewAspectOverride = 1200.0 / 760 };
            Render(preview, 1248, 836, Path.Combine(folder, "settings-knobs.png"), false);
            using (var bitmap = new System.Drawing.Bitmap(Path.Combine(folder, "settings-knobs.png")))
            {
                var left = bitmap.GetPixel(524, 366); var right = bitmap.GetPixel(724, 366);
                Check(left.R == 232 && left.G == 221 && left.B == 196 && right.R == 232 && right.G == 221 && right.B == 196, "settings preview draws both adjustment knobs above overlapping photos");
            }
            Photo hitPhoto; StringLine hitLine; int handle;
            preview.FindDragTarget(line.At(0), out hitPhoto, out hitLine, out handle);
            Check(hitPhoto == null && hitLine == line && handle == -1, "covered left knob receives endpoint dragging before the photo");
            preview.FindDragTarget(line.At(1), out hitPhoto, out hitLine, out handle);
            Check(hitPhoto == null && hitLine == line && handle == 1, "covered right knob receives endpoint dragging before the photo");
            preview.FindDragTarget(new Point(600, 320), out hitPhoto, out hitLine, out handle);
            Check(hitPhoto == second && hitLine == null, "photo selection outside the knobs still follows the front layer");
            preview.Selected = photo; preview.SetSelectedLayer(store.State.Photos.Count - 1);
            Render(preview, 1248, 836, Path.Combine(folder, "settings-knobs-reordered.png"), false);
            using (var bitmap = new System.Drawing.Bitmap(Path.Combine(folder, "settings-knobs-reordered.png")))
            {
                var left = bitmap.GetPixel(524, 366); var right = bitmap.GetPixel(724, 366);
                Check(left.R == 232 && left.G == 221 && left.B == 196 && right.R == 232 && right.G == 221 && right.B == 196, "adjustment knobs stay in front after photo layers are reordered");
            }
            var desktop = new Scene(store) { Editable = true, ShowSelection = false };
            Render(desktop, 1200, 760, Path.Combine(folder, "desktop-knobs.png"), true);
            using (var bitmap = new System.Drawing.Bitmap(Path.Combine(folder, "desktop-knobs.png")))
            {
                var pixel = bitmap.GetPixel(500, 320);
                Check(!(pixel.R == 232 && pixel.G == 221 && pixel.B == 196), "desktop keeps adjustment knobs beneath photos even in desktop edit mode");
            }
            desktop.FindDragTarget(line.At(0), out hitPhoto, out hitLine, out handle);
            Check(hitPhoto == photo && hitLine == null, "desktop retains photo input priority where a photo covers a knob");
        }
        private static void VerifyPhotoLayers(string root, string folder)
        {
            var store = new Store(Path.Combine(root, "layers"));
            store.State = new Arrangement(); var line = new StringLine { Id = "overlap", Left = 100, Right = 1100, Y1 = 190, Y2 = 190 };
            store.State.Lines.Add(line);
            var upper = new Photo { Id = "upper", LineId = line.Id, T = .5, Width = 240, Demo = 0, Caption = "위쪽 사진" };
            var lower = new Photo { Id = "lower", LineId = line.Id, T = .5, OffsetY = 90, Width = 150, Demo = 4, Caption = "아래쪽 사진" };
            store.State.Photos.Add(upper); store.State.Photos.Add(lower);
            var scene = new Scene(store) { Editable = false, Selected = lower }; int changed = 0;
            scene.Changed += delegate { changed++; }; var overlap = new Point(600, 273);
            Render(scene, 1200, 760, Path.Combine(folder, "clip-in-front.png"), true);
            using (var before = new System.Drawing.Bitmap(Path.Combine(folder, "clip-in-front.png")))
            {
                var pixel = before.GetPixel(600, 273);
                Check(!(pixel.R == 200 && pixel.G == 212 && pixel.B == 206) && scene.HitPhoto(overlap) == lower, "overlap fixture reproduces a lower photo clip covering the upper image");
            }
            string ropeBefore = scene.StringGeometry(line).ToString();
            scene.SetSelectedLayer(0);
            Render(scene, 1200, 760, Path.Combine(folder, "clip-behind.png"), true);
            using (var after = new System.Drawing.Bitmap(Path.Combine(folder, "clip-behind.png")))
            {
                var pixel = after.GetPixel(600, 273);
                Check(pixel.R == 200 && pixel.G == 212 && pixel.B == 206 && scene.HitPhoto(overlap) == upper, "sending the lower photo behind also hides its clip beneath the upper image and selects the visible front photo");
            }
            Check(changed == 1 && scene.Selected == lower && upper.T == .5 && lower.OffsetY == 90 && scene.StringGeometry(line).ToString() == ropeBefore, "layer changes notify saving while retaining photo positions, selection and rope geometry");
            scene.SetSelectedLayer(0); Check(changed == 1, "moving the backmost photo farther back does not change the arrangement");
            store.Save(); var loaded = new Store(store.Root); loaded.Load();
            Check(loaded.State.Photos[0].Id == lower.Id && loaded.State.Photos[1].Id == upper.Id && loaded.State.Photos[0].OffsetY == 90, "layer order and clip positions survive save and restart");
            var restored = new Scene(loaded) { Editable = false }; Render(restored, 1200, 760, Path.Combine(folder, "layers-restored.png"), true);
            using (var image = new System.Drawing.Bitmap(Path.Combine(folder, "layers-restored.png")))
            {
                var pixel = image.GetPixel(600, 273);
                Check(pixel.R == 200 && pixel.G == 212 && pixel.B == 206 && restored.HitPhoto(overlap).Id == upper.Id, "reopened arrangement renders and selects the saved front layer");
            }
            store.Import(Path.Combine(folder, "arrangement.png"), lower);
            Check(store.State.Photos[0] == lower && store.State.Photos[1] == upper && lower.T == .5 && lower.OffsetY == 90, "replacing an image retains its layer and position");
            var added = store.Import(Path.Combine(folder, "arrangement.png"), null);
            Check(store.State.Photos.Last() == added && store.State.Photos[0] == lower, "new images appear in front without reordering existing layers");
            scene.SetSelectedLayer(1);
            Check(store.State.Photos[0] == upper && store.State.Photos[1] == lower && store.State.Photos[2] == added, "moving forward one layer swaps only the adjacent photo");
            scene.SetSelectedLayer(0);
            Check(store.State.Photos[0] == lower && store.State.Photos[1] == upper && store.State.Photos[2] == added, "moving back one layer restores adjacent order without changing other layers");
        }
        private static void VerifyFullScreenPlacement(string root, string folder)
        {
            var edgeStore = new Store(Path.Combine(root, "edges")); edgeStore.Load();
            var line = edgeStore.State.Lines[0];
            Scene.MoveLine(line, -1, line.Left, line.Right, line.Y1, line.Y2, new Vector(-10000, -10000));
            Scene.MoveLine(line, 1, line.Left, line.Right, line.Y1, line.Y2, new Vector(10000, 10000));
            Check(line.Left == 0 && line.Right == Scene.DesignWidth && line.Y1 == 0 && line.Y2 == Scene.DesignHeight, "rope endpoints reach all four screen edges");
            edgeStore.State.Photos[0].T = 0; edgeStore.State.Photos[0].OffsetY = Scene.DesignHeight;
            edgeStore.State.Photos[1].T = 1; edgeStore.State.Photos[1].OffsetY = -Scene.DesignHeight;
            edgeStore.Save(); var restored = new Store(edgeStore.Root); restored.Load(); var savedLine = restored.State.Lines[0];
            Check(savedLine.Left == 0 && savedLine.Right == Scene.DesignWidth && savedLine.Y1 == 0 && savedLine.Y2 == Scene.DesignHeight && restored.State.Photos[0].T == 0 && restored.State.Photos[0].OffsetY == Scene.DesignHeight && restored.State.Photos[1].T == 1 && restored.State.Photos[1].OffsetY == -Scene.DesignHeight, "edge positions and photo anchors survive save and restart without inset clamping");
            Scene.MoveLine(line, -1, 100, 1100, 145, 130, new Vector(-95, -141));
            Check(line.Left == 0 && line.Y1 == 0, "endpoint drag snaps to nearby screen edges");
            Scene.MoveLine(line, 1, 0, 1100, 0, 130, new Vector(95, 626));
            Check(line.Right == Scene.DesignWidth && line.Y2 == Scene.DesignHeight, "right and bottom endpoint snapping reaches exact screen boundary");
            Scene.MoveLine(line, 0, 100, 1100, 145, 130, new Vector(10000, 10000));
            Check(line.Right == Scene.DesignWidth && line.Y1 == Scene.DesignHeight && line.Right - line.Left == 1000 && line.Y1 - line.Y2 == 15, "moving a whole rope reaches right and bottom while preserving its shape");
            Scene.MoveLine(line, 0, line.Left, line.Right, line.Y1, line.Y2, new Vector(-10000, -10000));
            Check(line.Left == 0 && line.Y2 == 0 && line.Right - line.Left == 1000 && line.Y1 - line.Y2 == 15, "moving a whole rope reaches left and top while preserving its shape");
            var previewStore = new Store(Path.Combine(root, "preview")); previewStore.Load();
            var scene = new Scene(previewStore) { Editable = false };
            Render(scene, 1920, 1080, Path.Combine(folder, "full-screen.png"), true);
            Check(scene.DesignToView(new Point(0, 0)) == new Point(0, 0) && scene.DesignToView(new Point(Scene.DesignWidth, Scene.DesignHeight)) == new Point(1920, 1080), "desktop coordinates cover the whole widescreen monitor with no letterbox margins");
            scene.ShowApplicationArea = true; scene.PreviewAspectOverride = 16.0 / 9;
            Render(scene, 900, 550, Path.Combine(folder, "preview-wide.png"), true); var wide = scene.ApplicationRect;
            Check(Math.Abs(wide.Width / wide.Height - 16.0 / 9) < .001 && wide.Left > 0 && wide.Top > 0 && wide.Right < 900 && wide.Bottom < 550 && scene.DesignToView(new Point(0, 0)) == wide.TopLeft && scene.DesignToView(new Point(Scene.DesignWidth, Scene.DesignHeight)) == wide.BottomRight, "preview screen frame has the monitor aspect ratio and maps its corners to the applied area");
            var position = new Point(85, 702); var inverse = scene.ViewToDesign(scene.DesignToView(position));
            Check((position - inverse).Length < .001, "drag coordinates inside the inset preview map back to full-screen positions");
            var portraitScene = new Scene(previewStore) { Editable = false, ShowApplicationArea = true, PreviewAspectOverride = 9.0 / 16 };
            Render(portraitScene, 900, 550, Path.Combine(folder, "preview-portrait.png"), true); var portrait = portraitScene.ApplicationRect;
            Check(Math.Abs(portrait.Width / portrait.Height - 9.0 / 16) < .001 && portraitScene.DesignToView(new Point(Scene.DesignWidth, Scene.DesignHeight)) == portrait.BottomRight, "portrait monitor preview also matches its applied screen boundary");
        }
        public static void Render(UIElement element, int width, int height, string file, bool wall)
        {
            element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            if (wall || !(element is Scene))
            {
                var bg = new DrawingVisual(); using (var dc = bg.RenderOpen()) dc.DrawRectangle(wall ? (Brush)new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#eeece3"), (Color)ColorConverter.ConvertFromString("#dfded0"), 90) : Scene.Brush("#f7f5f0"), null, new Rect(0, 0, width, height)); target.Render(bg);
            }
            target.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target)); using (var stream = File.Create(file)) encoder.Save(stream);
        }
        public static int CheckDesktop(string folder)
        {
            Directory.CreateDirectory(folder); var output = new List<string>(); int code = 1;
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var store = new Store(Path.Combine(folder, "desktop-check-data")); store.Load(); var desktop = new DesktopWindow(store);
                desktop.Show();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                timer.Tick += delegate
                {
                    timer.Stop();
                    bool attached = desktop.IsAttached && Native.Owner(desktop.Handle) == Native.FindDesktopHost();
                    output.Add((attached ? "PASS " : "FAIL ") + "transparent window is owned by the current Explorer desktop host (" + Native.ClassName(Native.Owner(desktop.Handle)) + ")");
                    long ex = Native.GetWindowLongPtr(desktop.Handle, -20).ToInt64(), style = Native.GetWindowLongPtr(desktop.Handle, -16).ToInt64();
                    bool locked = (ex & 0x20) != 0, topLevel = (style & 0x40000000) == 0, notTopmost = (ex & 8) == 0;
                    output.Add((locked && topLevel && notTopmost ? "PASS " : "FAIL ") + "locked widget is click-through, top-level, and not topmost");
                    bool belowApps = Native.GetWindow(desktop.Handle, 2) == Native.Owner(desktop.Handle);
                    output.Add((belowApps ? "PASS " : "FAIL ") + "photo window is placed immediately above wallpaper desktop and below ordinary apps");
                    bool painted = VerifyNativePixels(desktop.Handle, output);
                    desktop.SetEditing(true); bool editing = (Native.GetWindowLongPtr(desktop.Handle, -20).ToInt64() & 0x20) == 0;
                    output.Add((editing ? "PASS " : "FAIL ") + "edit mode enables pointer input");
                    Native.RECT rect; Native.GetWindowRect(desktop.Handle, out rect); var area = desktop.TargetScreen.Bounds;
                    bool placement = rect.Left == area.Left && rect.Top == area.Top && rect.Right - rect.Left == area.Width && rect.Bottom - rect.Top == area.Height;
                    output.Add((placement ? "PASS " : "FAIL ") + "widget occupies the entire target monitor without work-area margins");
                    if (desktop.LastError != null) output.Add(desktop.LastError);
                    code = attached && locked && topLevel && notTopmost && belowApps && painted && editing && placement ? 0 : 1;
                    desktop.Close(); File.WriteAllLines(Path.Combine(folder, "desktop-verification.txt"), output.ToArray(), Encoding.UTF8); app.Shutdown();
                };
                timer.Start(); app.Run(); return code;
            }
            catch (Exception ex) { output.Add(ex.ToString()); File.WriteAllLines(Path.Combine(folder, "desktop-verification.txt"), output.ToArray(), Encoding.UTF8); return 1; }
        }
        private static bool VerifyNativePixels(IntPtr handle, List<string> output)
        {
            Native.RECT rect; Native.GetWindowRect(handle, out rect);
            using (var bitmap = new System.Drawing.Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                var dc = graphics.GetHdc(); bool captured;
                try { captured = Native.PrintWindow(handle, dc, 2); } finally { graphics.ReleaseHdc(dc); }
                int paper = 0, colors = 0;
                for (int y = 0; y < bitmap.Height; y += 4) for (int x = 0; x < bitmap.Width; x += 4)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.R == 255 && pixel.G == 254 && pixel.B == 248) paper++;
                    if (pixel.R == 200 && pixel.G == 212 && pixel.B == 206) colors++;
                }
                bool painted = captured && paper > 100 && colors > 100;
                output.Add((painted ? "PASS " : "FAIL ") + "native window surface contains rendered photo pixels (paper=" + paper + ", sample=" + colors + ")");
                return painted;
            }
        }
        public static int CheckApp(string folder)
        {
            Directory.CreateDirectory(folder); var output = new List<string>(); int code = 1;
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var controller = new Controller(app, Path.Combine(folder, "app-check-data"));
                File.WriteAllText(Path.Combine(folder, "app-check-progress.txt"), "Starting application\n");
                controller.Start(); controller.Manager.Hide();
                File.AppendAllText(Path.Combine(folder, "app-check-progress.txt"), "Application started; waiting for timer\n");
                var initialWindow = controller.Desktop.Handle;
                output.Add((controller.Desktop.IsAttached ? "PASS " : "FAIL ") + "full application starts and attaches wallpaper with management window and tray");
                controller.SetEditing(true); controller.SetEditing(false);
                if (controller.Store.State.Visible) controller.ToggleVisibility();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3500) }; int phase = 0;
                timer.Tick += delegate
                {
                    if (phase++ == 0)
                    {
                        File.AppendAllText(Path.Combine(folder, "app-check-progress.txt"), "First timer tick\n");
                        bool hidden = !Native.IsWindowVisible(controller.Desktop.Handle); output.Add((hidden ? "PASS " : "FAIL ") + "hidden decoration stays hidden after shell maintenance timer");
                        File.AppendAllText(Path.Combine(folder, "app-check-progress.txt"), "Reopening settings\n");
                        controller.ShowManager();
                        File.AppendAllText(Path.Combine(folder, "app-check-progress.txt"), "Settings reopened; enabling edit\n");
                        controller.SetEditing(true);
                        File.AppendAllText(Path.Combine(folder, "app-check-progress.txt"), "Edit enabled; applying\n");
                        var appliedPhoto = controller.Store.State.Photos[0]; appliedPhoto.Caption = "적용한 배치";
                        controller.Manager.Scene.Selected = appliedPhoto;
                        controller.Manager.Scene.SetSelectedLayer(controller.Store.State.Photos.Count - 1);
                        output.Add((controller.Store.State.Photos.Last() == appliedPhoto && controller.Desktop.Scene.Store.State.Photos.Last() == appliedPhoto ? "PASS " : "FAIL ") + "layer changes update both desktop and management preview order");
                        var appliedStyle = DesignCatalog.BuiltIns().First(p => p.Id == "built-in-silver").Style.Copy();
                        appliedStyle.EditKnobs(-1, k => { k.KnobColor = "#FF88CC"; k.KnobShape = "square"; k.KnobSize = 23; });
                        appliedStyle.EditKnobs(1, k => { k.KnobColor = "#4488FF"; k.KnobShape = "flower"; k.KnobSize = 11; });
                        controller.Store.State.Lines[0].Appearance = appliedStyle; controller.Changed();
                        bool applied = controller.ApplyAndRun();
                        File.AppendAllText(Path.Combine(folder, "app-check-progress.txt"), "Applied and started\n");
                        bool visible = Native.IsWindowVisible(controller.Desktop.Handle);
                        output.Add((applied && visible && controller.Store.State.Visible && !controller.Manager.IsVisible ? "PASS " : "FAIL ") + "apply-and-run restores hidden decoration and closes settings window");
                        bool locked = !controller.Editing && (Native.GetWindowLongPtr(controller.Desktop.Handle, -20).ToInt64() & 0x20) != 0;
                        output.Add((locked ? "PASS " : "FAIL ") + "apply-and-run locks editing and enables click-through");
                        var appliedStore = new Store(Path.Combine(folder, "app-check-data")); appliedStore.Load();
                        output.Add((appliedStore.State.Visible && appliedStore.State.Photos.Last().Id == appliedPhoto.Id && appliedStore.State.Photos.Last().Caption == "적용한 배치" ? "PASS " : "FAIL ") + "apply-and-run immediately saves edited arrangement, front layer and enabled visibility");
                        output.Add((appliedStore.State.Lines[0].Appearance.KnobMaterial == "metal" && appliedStore.State.Lines[0].Appearance.RopeDesign == "double" ? "PASS " : "FAIL ") + "custom rope and knob appearance persists through desktop apply-and-run");
                        var savedStyle = appliedStore.State.Lines[0].Appearance;
                        output.Add((savedStyle.ForKnob(-1).KnobColor == "#FF88CC" && savedStyle.ForKnob(1).KnobColor == "#4488FF" && savedStyle.ForKnob(-1).KnobShape == "square" && savedStyle.ForKnob(1).KnobShape == "flower" && savedStyle.ForKnob(-1).KnobSize == 23 && savedStyle.ForKnob(1).KnobSize == 11 ? "PASS " : "FAIL ") + "separate endpoint settings persist through desktop apply-and-run");
                        controller.ShowManager(); bool management = controller.Manager.IsVisible; output.Add((management ? "PASS " : "FAIL ") + "management window reopens after being hidden"); controller.Manager.Hide();
                        controller.Desktop.Close(); File.AppendAllText(Path.Combine(folder, "app-check-progress.txt"), "Closed test desktop; waiting for recreation\n"); return;
                    }
                    timer.Stop(); bool rebuilt = controller.Desktop.Handle != IntPtr.Zero && Native.IsWindow(controller.Desktop.Handle) && controller.Desktop.IsAttached;
                    File.AppendAllText(Path.Combine(folder, "app-check-progress.txt"), "Second timer tick; exiting\n");
                    output.Add((rebuilt ? "PASS " : "FAIL ") + "destroyed desktop window is recreated and reattached");
                    controller.Exit(); controller.Dispose();
                    var restored = new Store(Path.Combine(folder, "app-check-data")); restored.Load(); bool saved = File.Exists(Path.Combine(restored.Root, "arrangement.json"));
                    output.Add((saved ? "PASS " : "FAIL ") + "application exits cleanly and persists arrangement");
                    code = output.All(l => l.StartsWith("PASS ", StringComparison.Ordinal)) ? 0 : 1;
                    File.WriteAllLines(Path.Combine(folder, "app-verification.txt"), output.ToArray(), Encoding.UTF8);
                };
                timer.Start(); app.Run(); return code;
            }
            catch (Exception ex) { output.Add(ex.ToString()); File.WriteAllLines(Path.Combine(folder, "app-verification.txt"), output.ToArray(), Encoding.UTF8); return 1; }
        }
    }
}
