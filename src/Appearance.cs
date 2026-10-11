using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Media;

namespace PhotoString
{
    [DataContract]
    public class DecorationStyle
    {
        [DataMember] public string RopeColor = "#A59479";
        [DataMember] public string RopeMaterial = "jute";
        [DataMember] public string RopeTexture = "smooth";
        [DataMember] public string RopeDesign = "single";
        [DataMember] public double RopeWidth = 2.8;
        [DataMember] public string RopeImage;
        [DataMember] public string KnobColor = "#C5AE84";
        [DataMember] public string KnobMaterial = "wood";
        [DataMember] public string KnobTexture = "smooth";
        [DataMember] public string KnobShape = "circle";
        [DataMember] public double KnobSize = 14;
        [DataMember] public string KnobImage;
        [DataMember] public string KnobArt;
        [DataMember(EmitDefaultValue = false)] public KnobAppearance LeftKnob;
        [DataMember(EmitDefaultValue = false)] public KnobAppearance RightKnob;
        public DecorationStyle Copy()
        {
            var copy = (DecorationStyle)MemberwiseClone();
            copy.LeftKnob = LeftKnob == null ? null : LeftKnob.Copy(); copy.RightKnob = RightKnob == null ? null : RightKnob.Copy(); return copy;
        }
        public DecorationStyle ForKnob(int side)
        {
            var knob = side < 0 ? LeftKnob : RightKnob; if (knob == null) return this;
            var copy = (DecorationStyle)MemberwiseClone(); knob.ApplyTo(copy); return copy;
        }
        public void EditKnobs(int side, Action<DecorationStyle> change)
        {
            if (side == 0)
            {
                change(this);
                if (LeftKnob != null) { var left = ForKnob(-1).Copy(); change(left); LeftKnob = KnobAppearance.From(left); }
                if (RightKnob != null) { var right = ForKnob(1).Copy(); change(right); RightKnob = KnobAppearance.From(right); }
            }
            else
            {
                var copy = ForKnob(side).Copy(); change(copy);
                if (side < 0) LeftKnob = KnobAppearance.From(copy); else RightKnob = KnobAppearance.From(copy);
            }
        }
        public static readonly DecorationStyle Default = new DecorationStyle();
        public static string ValidColor(string value, string fallback)
        {
            uint parsed;
            return value != null && value.Length == 7 && value[0] == '#' && UInt32.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed) ? value.ToUpperInvariant() : fallback;
        }
        private static string Choice(string value, string fallback, params string[] values) { return values.Contains(value) ? value : fallback; }
        internal static string SafeAsset(string value)
        {
            return !String.IsNullOrEmpty(value) && value.Length < 100 && Path.GetFileName(value) == value && value.StartsWith("material-", StringComparison.Ordinal) ? value : null;
        }
        public void Normalize()
        {
            RopeColor = ValidColor(RopeColor, Default.RopeColor); KnobColor = ValidColor(KnobColor, Default.KnobColor);
            RopeMaterial = Choice(RopeMaterial, "jute", "jute", "cotton", "nylon", "leather", "metal");
            RopeTexture = Choice(RopeTexture, "smooth", "smooth", "fiber", "braid", "image");
            RopeDesign = Choice(RopeDesign, "single", "single", "double");
            KnobMaterial = Choice(KnobMaterial, "wood", "wood", "metal", "plastic", "stone", "glass");
            KnobTexture = Choice(KnobTexture, "smooth", "smooth", "grain", "brushed", "speckle", "image");
            KnobShape = Choice(KnobShape, "circle", "circle", "square", "hexagon", "flower", "image");
            RopeWidth = RopeWidth <= 0 || Double.IsNaN(RopeWidth) ? Default.RopeWidth : Store.Clamp(RopeWidth, 1, 12);
            KnobSize = KnobSize <= 0 || Double.IsNaN(KnobSize) ? Default.KnobSize : Store.Clamp(KnobSize, 8, 26);
            RopeImage = SafeAsset(RopeImage); KnobImage = SafeAsset(KnobImage); KnobArt = SafeAsset(KnobArt);
            if (LeftKnob != null) LeftKnob.Normalize(); if (RightKnob != null) RightKnob.Normalize();
        }
    }
    [DataContract]
    public class KnobAppearance
    {
        [DataMember] public string Color;
        [DataMember] public string Material;
        [DataMember] public string Texture;
        [DataMember] public string Shape;
        [DataMember] public double Size;
        [DataMember] public string Image;
        [DataMember] public string Art;
        public KnobAppearance Copy() { return (KnobAppearance)MemberwiseClone(); }
        public static KnobAppearance From(DecorationStyle style)
        {
            return new KnobAppearance { Color = style.KnobColor, Material = style.KnobMaterial, Texture = style.KnobTexture, Shape = style.KnobShape, Size = style.KnobSize, Image = style.KnobImage, Art = style.KnobArt };
        }
        public void ApplyTo(DecorationStyle style)
        {
            style.KnobColor = Color; style.KnobMaterial = Material; style.KnobTexture = Texture; style.KnobShape = Shape; style.KnobSize = Size; style.KnobImage = Image; style.KnobArt = Art;
        }
        public void Normalize()
        {
            var style = new DecorationStyle(); ApplyTo(style); style.Normalize();
            Color = style.KnobColor; Material = style.KnobMaterial; Texture = style.KnobTexture; Shape = style.KnobShape; Size = style.KnobSize; Image = style.KnobImage; Art = style.KnobArt;
        }
    }
    [DataContract]
    public class DesignPreset
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public DecorationStyle Style;
        public bool BuiltIn;
        public override string ToString() { return Name + (BuiltIn ? "" : " · 내 디자인"); }
    }
    public class DesignCatalog
    {
        private readonly string root;
        public readonly List<DesignPreset> Items = new List<DesignPreset>();
        public string Notice;
        public DesignCatalog(string dataRoot)
        {
            root = dataRoot; Items.AddRange(BuiltIns());
            var file = Path.Combine(root, "catalog.json");
            if (!File.Exists(file)) return;
            try { Load(file); }
            catch
            {
                Items.RemoveAll(p => !p.BuiltIn);
                try { Load(file + ".bak"); Notice = "내 디자인을 이전 저장본에서 복구했습니다."; }
                catch { Notice = "내 디자인 목록을 읽지 못했습니다. 기존 파일은 보관됩니다."; }
            }
        }
        private void Load(string path)
        {
            List<DesignPreset> presets;
            using (var stream = File.OpenRead(path)) presets = (List<DesignPreset>)new DataContractJsonSerializer(typeof(List<DesignPreset>)).ReadObject(stream);
            if (presets == null || presets.Count > 100) throw new InvalidDataException("카탈로그 형식이 올바르지 않습니다.");
            var ids = new HashSet<string>(Items.Select(p => p.Id));
            foreach (var preset in presets)
            {
                if (preset == null || String.IsNullOrEmpty(preset.Id) || !ids.Add(preset.Id) || preset.Style == null) continue;
                preset.Name = CleanName(preset.Name); preset.Style.Normalize(); preset.BuiltIn = false; Items.Add(preset);
            }
        }
        private static string CleanName(string name)
        {
            name = (name ?? "").Trim(); if (name.Length == 0) name = "나의 디자인";
            return name.Length > 40 ? name.Substring(0, 40) : name;
        }
        public DesignPreset Add(string name, DecorationStyle style)
        {
            if (Items.Count(p => !p.BuiltIn) >= 100) throw new InvalidOperationException("내 디자인은 최대 100개까지 저장할 수 있어요.");
            var preset = new DesignPreset { Id = Guid.NewGuid().ToString("N"), Name = CleanName(name), Style = style.Copy() }; preset.Style.Normalize();
            Items.Add(preset); try { Save(); } catch { Items.Remove(preset); throw; } return preset;
        }
        public void Remove(DesignPreset preset)
        {
            if (preset == null || preset.BuiltIn) return; int index = Items.IndexOf(preset); if (index < 0) return;
            Items.RemoveAt(index); try { Save(); } catch { Items.Insert(index, preset); throw; }
        }
        private void Save()
        {
            Directory.CreateDirectory(root); var file = Path.Combine(root, "catalog.json"); var temp = file + ".tmp";
            using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(List<DesignPreset>)).WriteObject(stream, Items.Where(p => !p.BuiltIn).ToList());
            if (File.Exists(file)) File.Replace(temp, file, file + ".bak"); else File.Move(temp, file);
        }
        public static List<DesignPreset> BuiltIns()
        {
            return new List<DesignPreset> {
                Preset("natural", "내추럴 황마", new DecorationStyle()),
                Preset("cotton", "아이보리 코튼", new DecorationStyle { RopeColor = "#E5D9BD", RopeMaterial = "cotton", RopeTexture = "fiber", RopeWidth = 5, KnobTexture = "grain" }),
                Preset("pink", "파스텔 핑크", new DecorationStyle { RopeColor = "#D6A0B8", RopeMaterial = "nylon", RopeTexture = "braid", RopeWidth = 5, KnobColor = "#E3A6C0", KnobMaterial = "plastic", KnobShape = "flower", KnobSize = 17 }),
                Preset("leather", "브라운 가죽", new DecorationStyle { RopeColor = "#78553C", RopeMaterial = "leather", RopeWidth = 6, KnobColor = "#A27951", KnobMaterial = "wood", KnobTexture = "grain", KnobShape = "square" }),
                Preset("silver", "실버 메탈", new DecorationStyle { RopeColor = "#ADB8C4", RopeMaterial = "metal", RopeDesign = "double", RopeWidth = 2.6, KnobColor = "#BBC5D1", KnobMaterial = "metal", KnobTexture = "brushed", KnobShape = "hexagon", KnobSize = 16 }),
                Preset("black", "블랙 미니멀", new DecorationStyle { RopeColor = "#353A41", RopeMaterial = "nylon", RopeWidth = 3, KnobColor = "#343941", KnobMaterial = "plastic", KnobSize = 13 }),
                Preset("forest", "숲과 돌", new DecorationStyle { RopeColor = "#647655", RopeMaterial = "cotton", RopeTexture = "braid", RopeWidth = 5, KnobColor = "#939581", KnobMaterial = "stone", KnobTexture = "speckle", KnobShape = "hexagon", KnobSize = 17 }),
                Preset("glass", "맑은 유리", new DecorationStyle { RopeColor = "#8FA8AD", RopeMaterial = "nylon", RopeWidth = 3, KnobColor = "#A9CED5", KnobMaterial = "glass", KnobSize = 18 })
            };
        }
        private static DesignPreset Preset(string id, string name, DecorationStyle style) { return new DesignPreset { Id = "built-in-" + id, Name = name, Style = style, BuiltIn = true }; }
    }
    public static class DecorationDrawing
    {
        private static Color ColorOf(string color) { return (Color)ColorConverter.ConvertFromString(color); }
        private static Color Mix(Color color, Color target, double amount)
        {
            return Color.FromArgb(color.A, (byte)(color.R + (target.R - color.R) * amount), (byte)(color.G + (target.G - color.G) * amount), (byte)(color.B + (target.B - color.B) * amount));
        }
        private static Brush Solid(Color color, double opacity) { var brush = new SolidColorBrush(color); brush.Opacity = opacity; return brush; }
        private static ImageBrush Tile(ImageSource image, double size, double opacity)
        {
            return new ImageBrush(image) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, size, size), Stretch = Stretch.Fill, Opacity = opacity };
        }
        public static void Rope(DrawingContext dc, Geometry path, DecorationStyle style, ImageSource texture)
        {
            if (style.RopeDesign == "double")
            {
                dc.PushTransform(new TranslateTransform(0, -style.RopeWidth)); Strand(dc, path, style, texture); dc.Pop();
                dc.PushTransform(new TranslateTransform(0, style.RopeWidth)); Strand(dc, path, style, texture); dc.Pop();
            }
            else Strand(dc, path, style, texture);
        }
        private static void Strand(DrawingContext dc, Geometry path, DecorationStyle style, ImageSource texture)
        {
            Color color = ColorOf(style.RopeColor); double width = style.RopeWidth;
            bool glossy = style.RopeMaterial == "metal" || style.RopeMaterial == "nylon";
            double light = style.RopeMaterial == "metal" ? .85 : style.RopeMaterial == "nylon" ? .65 : style.RopeMaterial == "cotton" ? .58 : style.RopeMaterial == "leather" ? .25 : .40;
            var pen = new Pen(new SolidColorBrush(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.PushTransform(new TranslateTransform(0, width * .55)); dc.DrawGeometry(null, new Pen(Scene.Brush("#3393856f"), width + 1.2) { DashStyle = pen.DashStyle }, path); dc.Pop();
            if (style.RopeMaterial == "cotton") dc.DrawGeometry(null, new Pen(Solid(Mix(color, Colors.White, .55), .35), width + 1), path);
            dc.DrawGeometry(null, pen, path);
            dc.PushTransform(new TranslateTransform(0, -width * .18));
            var shine = new Pen(Solid(Mix(color, Colors.White, light), 1), Math.Max(.6, width * (glossy ? .23 : .28))) { DashStyle = pen.DashStyle };
            dc.DrawGeometry(null, shine, path); dc.Pop();
            if (style.RopeTexture == "image" && texture != null)
                dc.DrawGeometry(null, new Pen(Tile(texture, 32, .7), width) { DashStyle = pen.DashStyle }, path);
            else if (style.RopeTexture == "fiber" || style.RopeTexture == "braid")
            {
                var flat = path.GetFlattenedPathGeometry(.6, ToleranceType.Absolute); int count = Math.Max(8, Math.Min(320, (int)(path.Bounds.Width / Math.Max(4, width * 1.6))));
                var detail = new Pen(Solid(Mix(color, Colors.Black, .4), .45), Math.Max(.5, width * .17));
                for (int i = 1; i < count; i++)
                {
                    Point point, tangent; flat.GetPointAtFractionLength((double)i / count, out point, out tangent);
                    var direction = new Vector(tangent.X, tangent.Y); if (direction.Length < .001) continue; direction.Normalize(); var normal = new Vector(-direction.Y, direction.X);
                    double slant = style.RopeTexture == "braid" && i % 2 == 0 ? -1 : 1;
                    dc.DrawLine(detail, point - normal * width * .38 - direction * width * .3 * slant, point + normal * width * .38 + direction * width * .3 * slant);
                }
            }
            if (style.RopeMaterial == "leather")
                dc.DrawGeometry(null, new Pen(Solid(Mix(color, Colors.White, .65), .65), .65) { DashStyle = DashStyles.Dot }, path);
        }
        private static Geometry Shape(string shape, double size)
        {
            if (shape == "square") return new RectangleGeometry(new Rect(-size, -size, size * 2, size * 2), size * .22, size * .22);
            if (shape == "flower")
            {
                var flower = new StreamGeometry(); double step = Math.PI * 2 / 48;
                using (var ctx = flower.Open())
                {
                    double angle = -Math.PI / 2; double radius = size * (.84 + .16 * Math.Cos(6 * angle)); var point = new Point(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
                    ctx.BeginFigure(point, true, true);
                    for (int i = 0; i < 48; i++)
                    {
                        double nextAngle = angle + step, nextRadius = size * (.84 + .16 * Math.Cos(6 * nextAngle));
                        var next = new Point(Math.Cos(nextAngle) * nextRadius, Math.Sin(nextAngle) * nextRadius);
                        double derivative = -size * .96 * Math.Sin(6 * angle), nextDerivative = -size * .96 * Math.Sin(6 * nextAngle);
                        var direction = new Vector(Math.Cos(angle) * derivative - Math.Sin(angle) * radius, Math.Sin(angle) * derivative + Math.Cos(angle) * radius);
                        var nextDirection = new Vector(Math.Cos(nextAngle) * nextDerivative - Math.Sin(nextAngle) * nextRadius, Math.Sin(nextAngle) * nextDerivative + Math.Cos(nextAngle) * nextRadius);
                        ctx.BezierTo(point + direction * step / 3, next - nextDirection * step / 3, next, true, false); angle = nextAngle; radius = nextRadius; point = next;
                    }
                }
                flower.Freeze(); return flower;
            }
            if (shape == "hexagon")
            {
                int points = 6; var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    for (int i = 0; i < points; i++)
                    {
                        double angle = -Math.PI / 2 + Math.PI * 2 * i / points, radius = size;
                        var point = new Point(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
                        if (i == 0) ctx.BeginFigure(point, true, true); else ctx.LineTo(point, true, false);
                    }
                }
                geometry.Freeze(); return geometry;
            }
            return new EllipseGeometry(new Point(), size, size);
        }
        public static void Knob(DrawingContext dc, DecorationStyle style, ImageSource texture, ImageSource art, bool editable)
        {
            double size = style.KnobSize; Color color = ColorOf(style.KnobColor); var shape = Shape(style.KnobShape, size);
            if (style.KnobShape == "image" && art != null)
            {
                double scale = size * 2 / Math.Max(art.Width, art.Height);
                var area = new Rect(-art.Width * scale / 2, -art.Height * scale / 2, art.Width * scale, art.Height * scale);
                dc.PushTransform(new TranslateTransform(1, 3)); dc.PushOpacity(.18); dc.DrawImage(art, area); dc.Pop(); dc.Pop(); dc.DrawImage(art, area);
            }
            else
            {
                dc.PushTransform(new TranslateTransform(1, 4)); dc.DrawGeometry(Scene.Brush("#24857454"), null, shape); dc.Pop();
                bool glossy = style.KnobMaterial == "metal" || style.KnobMaterial == "glass" || style.KnobMaterial == "plastic";
                var fill = new LinearGradientBrush(Mix(color, Colors.White, style.KnobMaterial == "plastic" ? .28 : glossy ? .55 : .16), Mix(color, Colors.Black, style.KnobMaterial == "plastic" ? .18 : glossy ? .34 : .13), glossy ? 135 : 80);
                if (style.KnobMaterial == "metal")
                {
                    fill.GradientStops.Clear(); fill.GradientStops.Add(new GradientStop(Mix(color, Colors.White, .7), 0)); fill.GradientStops.Add(new GradientStop(Mix(color, Colors.Black, .3), .38)); fill.GradientStops.Add(new GradientStop(Mix(color, Colors.White, .85), .45)); fill.GradientStops.Add(new GradientStop(color, .62)); fill.GradientStops.Add(new GradientStop(Mix(color, Colors.Black, .35), 1));
                }
                if (style.KnobMaterial == "glass") fill.Opacity = .58;
                dc.DrawGeometry(fill, new Pen(Solid(Mix(color, Colors.Black, .30), 1), 1), shape);
                dc.PushClip(shape);
                if (style.KnobTexture == "image" && texture != null) dc.DrawRectangle(Tile(texture, size * 2, .72), null, new Rect(-size, -size, size * 2, size * 2));
                else if (style.KnobTexture == "grain")
                {
                    for (int i = -4; i <= 4; i++) dc.DrawEllipse(null, new Pen(Solid(Mix(color, Colors.Black, .38), .26), .7), new Point(-size * .45, i * 4), size * 1.2, size * .38);
                }
                else if (style.KnobTexture == "brushed")
                {
                    for (double y = -size; y < size; y += 2) dc.DrawLine(new Pen(Solid(Mix(color, Colors.White, .55), .28), .6), new Point(-size, y), new Point(size, y - 3));
                }
                else if (style.KnobTexture == "speckle" || style.KnobMaterial == "stone")
                {
                    for (int i = 0; i < 48; i++) dc.DrawEllipse(Solid(Mix(color, i % 2 == 0 ? Colors.Black : Colors.White, .55), .35), null, new Point(((i * 17) % 47 / 46.0 * 2 - 1) * size, ((i * 29) % 53 / 52.0 * 2 - 1) * size), .8, .5);
                }
                if (style.KnobMaterial == "wood" && style.KnobShape == "circle") dc.DrawEllipse(null, new Pen(Scene.Brush("#33f8e9c7"), .7), new Point(), size * .79, size * .79);
                if (glossy)
                {
                    dc.DrawEllipse(Solid(Colors.White, style.KnobMaterial == "glass" ? .8 : .4), null, new Point(-size * .32, -size * .35), size * .23, size * .12);
                    dc.DrawLine(new Pen(Solid(Colors.White, .4), .8), new Point(-size * .7, size * .4), new Point(size * .35, size * .72));
                }
                dc.Pop();
            }
            if (editable) dc.DrawEllipse(Scene.Brush("#e8ddc4"), null, new Point(), 2, 2);
        }
    }
    public class DesignPreview : FrameworkElement
    {
        private readonly Store store;
        public DecorationStyle Appearance = new DecorationStyle();
        public DesignPreview(Store source) { store = source; ClipToBounds = true; }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); var left = new Point(42, ActualHeight * .4); var right = new Point(Math.Max(55, ActualWidth - 42), ActualHeight * .4);
            var path = new StreamGeometry(); using (var ctx = path.Open()) { ctx.BeginFigure(left, false, false); ctx.BezierTo(new Point(ActualWidth * .35, ActualHeight * .68), new Point(ActualWidth * .65, ActualHeight * .68), right, true, false); }
            DecorationDrawing.Rope(dc, path, Appearance, store.MaterialImage(Appearance.RopeImage));
            foreach (int side in new[] { -1, 1 })
            {
                var point = side < 0 ? left : right; var style = Appearance.ForKnob(side);
                dc.PushTransform(new TranslateTransform(point.X, point.Y)); DecorationDrawing.Knob(dc, style, store.MaterialImage(style.KnobImage), store.MaterialImage(style.KnobArt), false); dc.Pop();
            }
        }
    }
}
