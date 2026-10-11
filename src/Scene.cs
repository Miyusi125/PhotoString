using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace PhotoString
{
    public class Scene : FrameworkElement
    {
        public const double DesignWidth = 1200, DesignHeight = 760;
        public readonly Store Store;
        public bool Editable = true;
        public bool ShowSelection = true;
        public bool ShowApplicationArea;
        internal double PreviewAspectOverride;
        public Photo Selected;
        public event Action Changed;
        public event Action SelectionChanged;
        public event Action<Photo> ReplaceRequested;
        private Photo draggingPhoto;
        private StringLine draggingLine;
        private int endpoint;
        private Point start;
        private double oldT, oldOffset, oldLeft, oldRight, oldY1, oldY2;
        private bool moved;
        public Scene(Store store)
        {
            Store = store; Focusable = true; ClipToBounds = true;
            ToolTip = "사진: 드래그로 이동 · 더블클릭으로 교체 · 마우스 휠로 크기 조절\n끈: 드래그로 전체 이동 · 양 끝의 고정핀으로 길이와 높이 조절";
        }
        private Forms.Screen TargetScreen
        {
            get { return Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Store.State.Monitor) ?? Forms.Screen.PrimaryScreen; }
        }
        internal Rect ApplicationRect
        {
            get
            {
                if (!ShowApplicationArea) return new Rect(0, 0, ActualWidth, ActualHeight);
                var screen = TargetScreen.Bounds;
                double aspect = PreviewAspectOverride > 0 ? PreviewAspectOverride : (double)screen.Width / screen.Height;
                double availableWidth = Math.Max(1, ActualWidth - 48), availableHeight = Math.Max(1, ActualHeight - 76);
                double width = Math.Min(availableWidth, availableHeight * aspect), height = width / aspect;
                return new Rect((ActualWidth - width) / 2, 46 + (availableHeight - height) / 2, width, height);
            }
        }
        private double ScaleX { get { return Math.Max(.001, ApplicationRect.Width / DesignWidth); } }
        private double ScaleY { get { return Math.Max(.001, ApplicationRect.Height / DesignHeight); } }
        private bool PinsInFront { get { return Editable && ShowApplicationArea; } }
        internal Point DesignToView(Point point) { var r = ApplicationRect; return new Point(r.X + point.X * ScaleX, r.Y + point.Y * ScaleY); }
        internal Point ViewToDesign(Point point) { var r = ApplicationRect; return new Point((point.X - r.X) / ScaleX, (point.Y - r.Y) / ScaleY); }
        private Point Position(MouseEventArgs e) { return ViewToDesign(e.GetPosition(this)); }
        public Point Anchor(Photo p) { var line = Store.State.Lines.FirstOrDefault(l => l.Id == p.LineId); var point = line == null ? new Point(600, 180) : line.At(p.T); point.Y += p.OffsetY; return point; }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var r = ApplicationRect;
            if (ShowApplicationArea) dc.DrawRectangle(new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#eae8de"), (Color)ColorConverter.ConvertFromString("#e0dfd2"), 90), null, r);
            dc.PushClip(new RectangleGeometry(r));
            dc.PushTransform(new TranslateTransform(r.X, r.Y)); dc.PushTransform(new ScaleTransform(ScaleX, ScaleY));
            foreach (var line in Store.State.Lines) DrawString(dc, line);
            foreach (var photo in Store.State.Photos) DrawPhoto(dc, photo);
            if (Store.State.Photos.Count == 0) { Text(dc, "마음에 드는 사진을 걸어보세요", new Point(600, 300), 25, "#8c8579", true, false); Text(dc, "사진 추가 버튼을 누르거나 이곳에 파일을 놓아주세요", new Point(600, 345), 17, "#a49b8e", true, false); }
            if (PinsInFront) foreach (var line in Store.State.Lines) { DrawPin(dc, line, -1); DrawPin(dc, line, 1); }
            dc.Pop(); dc.Pop(); dc.Pop();
            if (ShowApplicationArea) DrawApplicationBorder(dc, r);
        }
        private void DrawApplicationBorder(DrawingContext dc, Rect r)
        {
            var pen = new Pen(Brush("#6f8566"), 1.5);
            dc.DrawRectangle(null, pen, r);
            var cornerPen = new Pen(Brush("#4e694a"), 3);
            foreach (var corner in new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight })
            {
                double x = corner.X == r.Left ? 12 : -12, y = corner.Y == r.Top ? 12 : -12;
                dc.DrawLine(cornerPen, corner, new Point(corner.X + x, corner.Y));
                dc.DrawLine(cornerPen, corner, new Point(corner.X, corner.Y + y));
            }
            var bounds = TargetScreen.Bounds;
            Text(dc, "적용 영역 · 화면 전체  " + bounds.Width + " × " + bounds.Height, new Point(r.Left + 10, r.Bottom + 7), 10, "#63765a", false, false);
        }
        public static Brush Brush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        public static void Text(DrawingContext dc, string text, Point p, double size, string color, bool center, bool serif, double maxWidth = 0)
        {
            var ft = FormatText(text, size, Brush(color), serif, maxWidth);
            if (center) p.X -= ft.Width / 2;
            if (EmojiDrawing.MayContainEmoji(text)) EmojiDrawing.Draw(dc, ft, p); else dc.DrawText(ft, p);
        }
        internal static FormattedText FormatText(string text, double size, Brush brush, bool serif, double maxWidth = 0)
        {
            text = text ?? ""; var ft = CreateText(text, size, brush, serif);
            if (maxWidth > 0)
            {
                if (ft.WidthIncludingTrailingWhitespace > maxWidth)
                {
                    var elements = TextSupport.Elements(text).ToArray(); int low = 0, high = elements.Length;
                    // Truncate between complete emoji sequences, rather than trusting
                    // Framework's character ellipsis to retain an entire ZWJ cluster.
                    var best = CreateText("…", size, brush, serif);
                    if (best.WidthIncludingTrailingWhitespace > maxWidth) best = CreateText("", size, brush, serif);
                    while (low <= high)
                    {
                        int mid = (low + high) / 2; var candidate = CreateText(String.Concat(elements.Take(mid)) + "…", size, brush, serif);
                        if (candidate.WidthIncludingTrailingWhitespace <= maxWidth) { best = candidate; low = mid + 1; } else high = mid - 1;
                    }
                    ft = best;
                }
                ft.MaxTextWidth = maxWidth; ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis;
            }
            return ft;
        }
        private static FormattedText CreateText(string text, double size, Brush brush, bool serif)
        {
            var ft = new FormattedText(text ?? "", CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight, serif ? TextSupport.CaptionTypeface : TextSupport.InterfaceTypeface, size, brush, 1.0);
            int index = 0;
            foreach (var element in TextSupport.Elements(text ?? ""))
            {
                if (EmojiDrawing.MayContainEmoji(element)) ft.SetFontFamily("Segoe UI Emoji", index, element.Length);
                index += element.Length;
            }
            return ft;
        }
        internal Geometry StringGeometry(StringLine line)
        {
            var knots = new List<Point> { line.At(0) };
            var photos = Store.State.Photos.Where(p => p.LineId == line.Id).OrderBy(p => p.T).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
            if (photos.Count == 0) knots.Add(line.At(.5));
            foreach (var p in photos) { var knot = Anchor(p); if ((knot - knots.Last()).Length > .1) knots.Add(knot); }
            knots.Add(line.At(1));
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(knots[0], false, false);
                for (int i = 0; i < knots.Count - 1; i++)
                {
                    var a = knots[i]; var b = knots[i + 1]; var before = knots[Math.Max(0, i - 1)]; var after = knots[Math.Min(knots.Count - 1, i + 2)];
                    double distance = (b.X - a.X) / 3;
                    double slopeA = (b.Y - before.Y) / Math.Max(1, b.X - before.X), slopeB = (after.Y - a.Y) / Math.Max(1, after.X - a.X);
                    ctx.BezierTo(new Point(a.X + distance, a.Y + slopeA * distance), new Point(b.X - distance, b.Y - slopeB * distance), b, true, false);
                }
            }
            geometry.Freeze(); return geometry;
        }
        private void DrawString(DrawingContext dc, StringLine line)
        {
            var geometry = StringGeometry(line); var style = line.Appearance ?? DecorationStyle.Default;
            if (Editable) dc.DrawGeometry(null, new Pen(Brushes.Transparent, RopeHitWidth(line)), geometry);
            DecorationDrawing.Rope(dc, geometry, style, Store.MaterialImage(style.RopeImage));
            if (!PinsInFront) { DrawPin(dc, line, -1); DrawPin(dc, line, 1); }
        }
        private static double PinHitRadius(StringLine line, int side) { return (line.Appearance ?? DecorationStyle.Default).ForKnob(side).KnobSize + 8; }
        private static double RopeHitWidth(StringLine line) { var style = line.Appearance ?? DecorationStyle.Default; return Math.Max(24, style.RopeWidth * (style.RopeDesign == "double" ? 4 : 1.5) + 8); }
        private void DrawPin(DrawingContext dc, StringLine line, int side)
        {
            var p = line.At(side < 0 ? 0 : 1); var style = (line.Appearance ?? DecorationStyle.Default).ForKnob(side);
            dc.PushTransform(new TranslateTransform(p.X, p.Y)); dc.PushTransform(new ScaleTransform(1, ScaleX / ScaleY));
            DecorationDrawing.Knob(dc, style, Store.MaterialImage(style.KnobImage), Store.MaterialImage(style.KnobArt), Editable);
            dc.Pop(); dc.Pop();
        }
        private void DrawPhoto(DrawingContext dc, Photo p)
        {
            var anchor = Anchor(p); double w = p.Width, imageH = (w - 18) / p.Aspect, h = imageH + 42;
            dc.PushTransform(new TranslateTransform(anchor.X, anchor.Y)); dc.PushTransform(new ScaleTransform(1, ScaleX / ScaleY)); dc.PushTransform(new RotateTransform(p.Angle));
            var paper = new Rect(-w / 2, 12, w, h);
            for (int i = 8; i >= 2; i -= 2) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(i == 2 ? 17 : 5), 42, 32, 19)), null, new Rect(paper.X - i / 2, paper.Y + i / 2, w + i, h + i / 2), 2, 2);
            dc.DrawRectangle(Brush("#fffef8"), new Pen(Brush("#d9d5cb"), .7), paper);
            var imageRect = new Rect(-w / 2 + 9, 21, w - 18, imageH);
            var image = Store.ImageFor(p);
            if (image != null)
            {
                dc.PushClip(new RectangleGeometry(imageRect));
                double fit = Math.Max(imageRect.Width / image.Width, imageRect.Height / image.Height);
                dc.DrawImage(image, new Rect(imageRect.X + (imageRect.Width - image.Width * fit) / 2, imageRect.Y + (imageRect.Height - image.Height * fit) / 2, image.Width * fit, image.Height * fit)); dc.Pop();
            }
            else if (p.Demo >= 0) DrawDemo(dc, imageRect, p.Demo);
            else { dc.DrawRectangle(Brush("#e8e5dc"), null, imageRect); Text(dc, "사진을 읽을 수 없어요", new Point(0, imageRect.Y + imageH / 2 - 6), 11, "#938879", true, false); }
            Text(dc, p.Caption, new Point(0, imageRect.Bottom + 9), 10.5, "#7b7b70", true, true, imageRect.Width);
            if (Editable && ShowSelection && Selected == p) dc.DrawRectangle(null, new Pen(Brush("#617a64"), 2), new Rect(paper.X - 3, paper.Y - 3, w + 6, h + 6));
            DrawPeg(dc);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        private void DrawPeg(DrawingContext dc)
        {
            dc.PushTransform(new RotateTransform(-3));
            dc.DrawRoundedRectangle(Brush("#39706145"), null, new Rect(-4, -18, 13, 40), 2, 2);
            var wood = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#e6d4b1"), (Color)ColorConverter.ConvertFromString("#bfa476"), 0);
            dc.DrawRoundedRectangle(wood, new Pen(Brush("#a89166"), .55), new Rect(-5, -21, 11, 39), 1, 1);
            dc.DrawLine(new Pen(Brush("#7abea778"), .7), new Point(-1, -18), new Point(-1, 15));
            dc.DrawLine(new Pen(Brush("#5af9e7c8"), 1), new Point(3, -18), new Point(3, 14));
            dc.DrawLine(new Pen(Brush("#8e8b7e"), 1.3), new Point(-6, 1), new Point(6, 1));
            dc.DrawLine(new Pen(Brush("#b0aaa0"), 1), new Point(-6, 4), new Point(6, 4));
            dc.DrawEllipse(null, new Pen(Brush("#777969"), 1), new Point(0, 2.5), 2.8, 3);
            dc.Pop();
        }
        // Built-in vector postcards make the first run usable without downloading images.
        public static void DrawDemo(DrawingContext dc, Rect r, int number)
        {
            dc.PushClip(new RectangleGeometry(r)); dc.PushTransform(new TranslateTransform(r.X, r.Y)); dc.PushTransform(new ScaleTransform(r.Width / 180, r.Height / 180));
            string[] skies = { "#c8d4ce", "#c6d0bb", "#dfc8b1", "#e6d7b5", "#b4d1d4", "#d9c0b1" };
            dc.DrawRectangle(Brush(skies[number]), null, new Rect(0, 0, 180, 180));
            if (number == 0 || number == 1)
            {
                dc.DrawEllipse(Brush("#f5ead1"), null, new Point(136, 39), 18, 18);
                Polygon(dc, "#839a8c", new Point(0, 112), new Point(43, 52), new Point(93, 112), new Point(127, 70), new Point(180, 127), new Point(180, 180), new Point(0, 180));
                Polygon(dc, "#536e62", new Point(0, 147), new Point(70, 88), new Point(121, 147), new Point(156, 108), new Point(180, 140), new Point(180, 180), new Point(0, 180));
                dc.DrawRectangle(Brush("#9cb7ac"), null, new Rect(0, 149, 180, 31));
                if (number == 1) for (int i = 0; i < 6; i++) { double x = i * 40 - 12, y = 84 + i % 2 * 30; dc.DrawRectangle(Brush("#3b5345"), null, new Rect(x + 6, y, 3, 68)); Polygon(dc, "#49634e", new Point(x - 12, y + 45), new Point(x + 7, y), new Point(x + 28, y + 45)); }
            }
            else if (number == 2)
            {
                dc.DrawRectangle(Brush("#b57c5b"), null, new Rect(20, 28, 140, 152));
                dc.DrawRectangle(Brush("#e7d6b9"), null, new Rect(29, 39, 122, 141));
                dc.DrawRoundedRectangle(Brush("#536560"), null, new Rect(61, 60, 58, 117), 29, 29);
                dc.DrawRectangle(Brush("#5c6d61"), null, new Rect(61, 91, 58, 89));
                for (int i = 0; i < 4; i++) dc.DrawLine(new Pen(Brush("#c6b895"), 2), new Point(66, 100 + i * 17), new Point(114, 100 + i * 17));
                dc.DrawRectangle(Brush("#886c52"), null, new Rect(0, 168, 180, 12));
                dc.DrawEllipse(Brush("#6f8561"), null, new Point(29, 143), 18, 23); dc.DrawRectangle(Brush("#b48b68"), null, new Rect(17, 150, 25, 24));
            }
            else if (number == 3)
            {
                dc.DrawRectangle(Brush("#ba9d7a"), null, new Rect(0, 100, 180, 80));
                dc.DrawEllipse(Brush("#d7cbb3"), null, new Point(94, 133), 50, 20);
                dc.DrawRoundedRectangle(Brush("#f3eee0"), new Pen(Brush("#d6cbb6"), 2), new Rect(58, 74, 66, 59), 9, 9);
                dc.DrawEllipse(null, new Pen(Brush("#f3eee0"), 8), new Point(126, 99), 16, 16);
                dc.DrawEllipse(Brush("#eee7d5"), null, new Point(91, 75), 33, 12);
                dc.DrawEllipse(Brush("#795943"), null, new Point(91, 75), 28, 9);
                dc.DrawEllipse(Brush("#d0b28b"), null, new Point(91, 75), 13, 5);
                dc.DrawLine(new Pen(Brush("#8aa081"), 4), new Point(24, 103), new Point(32, 22));
                for (int i = 0; i < 4; i++) dc.DrawEllipse(Brush("#8fa383"), null, new Point(30 + (i % 2 == 0 ? -8 : 9), 32 + i * 15), 10, 5);
            }
            else if (number == 4)
            {
                dc.DrawEllipse(Brush("#faedd3"), null, new Point(127, 49), 23, 23);
                dc.DrawRectangle(Brush("#7daeb0"), null, new Rect(0, 98, 180, 82));
                Polygon(dc, "#dfd1b0", new Point(0, 125), new Point(63, 157), new Point(180, 166), new Point(180, 180), new Point(0, 180));
                dc.DrawLine(new Pen(Brush("#e3e8dc"), 3), new Point(16, 111), new Point(62, 111));
                dc.DrawLine(new Pen(Brush("#bdd5d0"), 2), new Point(87, 135), new Point(171, 135));
                Polygon(dc, "#777c69", new Point(31, 142), new Point(39, 62), new Point(44, 68), new Point(37, 144));
                for (int i = 0; i < 5; i++) dc.DrawEllipse(Brush("#738e72"), null, new Point(40 + (i - 2) * 12, 63 + Math.Abs(i - 2) * 6), 23, 6);
            }
            else
            {
                dc.DrawRectangle(Brush("#b67b65"), null, new Rect(0, 116, 180, 64));
                for (int i = 0; i < 4; i++) { double x = i * 47 - 7, h = 65 + i % 2 * 26; dc.DrawRectangle(Brush(i % 2 == 0 ? "#c39d73" : "#aeb8a0"), null, new Rect(x, 116 - h, 42, h + 64)); for (int j = 0; j < 3; j++) dc.DrawRectangle(Brush("#5c7069"), null, new Rect(x + 9, 126 - h + j * 28, 12, 17)); }
                dc.DrawRectangle(Brush("#786e60"), null, new Rect(0, 171, 180, 9));
            }
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        private static void Polygon(DrawingContext dc, string color, params Point[] points)
        {
            var geometry = new StreamGeometry(); using (var ctx = geometry.Open()) { ctx.BeginFigure(points[0], true, true); ctx.PolyLineTo(points.Skip(1).ToArray(), true, false); } dc.DrawGeometry(Brush(color), null, geometry);
        }
        internal Photo HitPhoto(Point point)
        {
            for (int i = Store.State.Photos.Count - 1; i >= 0; i--)
            {
                var p = Store.State.Photos[i]; var a = Anchor(p); var local = new RotateTransform(-p.Angle).Transform(new Point(point.X - a.X, (point.Y - a.Y) * ScaleY / ScaleX));
                if (new Rect(-p.Width / 2 - 5, -24, p.Width + 10, (p.Width - 18) / p.Aspect + 80).Contains(local)) return p;
            }
            return null;
        }
        internal void FindDragTarget(Point point, out Photo photo, out StringLine line, out int handle)
        {
            line = null; handle = 0;
            if (PinsInFront)
            {
                for (int i = Store.State.Lines.Count - 1; i >= 0; i--)
                {
                    var candidate = Store.State.Lines[i]; var a = candidate.At(0); var b = candidate.At(1);
                    if (new Vector(point.X - a.X, (point.Y - a.Y) * ScaleY / ScaleX).Length < PinHitRadius(candidate, -1)) { line = candidate; handle = -1; photo = null; return; }
                    if (new Vector(point.X - b.X, (point.Y - b.Y) * ScaleY / ScaleX).Length < PinHitRadius(candidate, 1)) { line = candidate; handle = 1; photo = null; return; }
                }
            }
            photo = HitPhoto(point); if (photo != null) return;
            foreach (var candidate in Store.State.Lines)
            {
                var a = candidate.At(0); var b = candidate.At(1);
                if (new Vector(point.X - a.X, (point.Y - a.Y) * ScaleY / ScaleX).Length < PinHitRadius(candidate, -1)) { line = candidate; handle = -1; return; }
                if (new Vector(point.X - b.X, (point.Y - b.Y) * ScaleY / ScaleX).Length < PinHitRadius(candidate, 1)) { line = candidate; handle = 1; return; }
                if (StringGeometry(candidate).StrokeContains(new Pen(Brushes.Black, RopeHitWidth(candidate)), point)) { line = candidate; return; }
            }
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e); if (!Editable) return;
            if (!ApplicationRect.Contains(e.GetPosition(this))) return;
            Focus(); start = Position(e); moved = false; Photo photo;
            FindDragTarget(start, out photo, out draggingLine, out endpoint);
            Selected = photo; if (SelectionChanged != null) SelectionChanged(); InvalidateVisual();
            if (photo != null)
            {
                if (e.ClickCount == 2) { if (ReplaceRequested != null) ReplaceRequested(photo); e.Handled = true; return; }
                draggingPhoto = photo; oldT = photo.T; oldOffset = photo.OffsetY;
            }
            if (draggingLine != null) { oldLeft = draggingLine.Left; oldRight = draggingLine.Right; oldY1 = draggingLine.Y1; oldY2 = draggingLine.Y2; }
            if (draggingPhoto != null || draggingLine != null) { CaptureMouse(); Cursor = Cursors.SizeAll; e.Handled = true; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (!IsMouseCaptured) return;
            var point = Position(e); var delta = point - start; if (delta.Length < 2 && !moved) return; moved = true;
            if (draggingPhoto != null)
            {
                var line = Store.State.Lines.First(l => l.Id == draggingPhoto.LineId);
                double t = Store.Clamp(oldT + delta.X / (line.Right - line.Left), 0, 1);
                double startY = line.At(oldT).Y + oldOffset;
                draggingPhoto.T = t; draggingPhoto.OffsetY = Store.Clamp(startY + delta.Y, 0, DesignHeight) - line.At(t).Y;
            }
            else if (draggingLine != null)
            {
                MoveLine(draggingLine, endpoint, oldLeft, oldRight, oldY1, oldY2, delta);
            }
            InvalidateVisual(); if (Changed != null) Changed();
        }
        private static double SnapEdge(double value, double maximum)
        {
            double distance = maximum * .01;
            if (value < distance) return 0;
            return value > maximum - distance ? maximum : value;
        }
        internal static void MoveLine(StringLine line, int endpoint, double left, double right, double y1, double y2, Vector delta)
        {
            if (endpoint == 0)
            {
                double dx = Store.Clamp(delta.X, -left, DesignWidth - right), dy = Store.Clamp(delta.Y, -Math.Min(y1, y2), DesignHeight - Math.Max(y1, y2));
                double newLeft = left + dx, newRight = right + dx;
                if (newLeft < DesignWidth * .01) dx = -left;
                else if (newRight > DesignWidth * .99) dx = DesignWidth - right;
                line.Left = left + dx; line.Right = right + dx; line.Y1 = y1 + dy; line.Y2 = y2 + dy;
            }
            else if (endpoint < 0)
            {
                line.Left = Store.Clamp(SnapEdge(left + delta.X, DesignWidth), 0, line.Right - 100);
                line.Y1 = Store.Clamp(SnapEdge(y1 + delta.Y, DesignHeight), 0, DesignHeight);
            }
            else
            {
                line.Right = Store.Clamp(SnapEdge(right + delta.X, DesignWidth), line.Left + 100, DesignWidth);
                line.Y2 = Store.Clamp(SnapEdge(y2 + delta.Y, DesignHeight), 0, DesignHeight);
            }
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); EndDrag(); }
        protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); draggingPhoto = null; draggingLine = null; Cursor = Cursors.Arrow; }
        private void EndDrag() { if (IsMouseCaptured) ReleaseMouseCapture(); draggingPhoto = null; draggingLine = null; Cursor = Cursors.Arrow; if (moved && Changed != null) Changed(); }
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e); if (!Editable || !ApplicationRect.Contains(e.GetPosition(this))) return;
            Photo p; StringLine line; int handle; FindDragTarget(Position(e), out p, out line, out handle); if (p == null) return;
            Selected = p; if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) p.Angle = Store.Clamp(p.Angle + Math.Sign(e.Delta) * 2, -20, 20); else p.Width = Store.Clamp(p.Width + Math.Sign(e.Delta) * 10, 90, 280);
            if (SelectionChanged != null) SelectionChanged(); InvalidateVisual(); if (Changed != null) Changed(); e.Handled = true;
        }
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e); if (!Editable || !ApplicationRect.Contains(e.GetPosition(this))) return;
            Photo p; StringLine hitLine; int handle; FindDragTarget(Position(e), out p, out hitLine, out handle); if (p == null) return; Selected = p; if (SelectionChanged != null) SelectionChanged(); InvalidateVisual();
            var menu = new ContextMenu();
            AddMenu(menu, "사진 교체…", delegate { if (ReplaceRequested != null) ReplaceRequested(p); });
            int layer = Store.State.Photos.IndexOf(p), lastLayer = Store.State.Photos.Count - 1;
            menu.Items.Add(new Separator());
            AddMenu(menu, "맨 앞으로", delegate { SetSelectedLayer(lastLayer); }, layer < lastLayer);
            AddMenu(menu, "앞으로 한 칸", delegate { SetSelectedLayer(layer + 1); }, layer < lastLayer);
            AddMenu(menu, "뒤로 한 칸", delegate { SetSelectedLayer(layer - 1); }, layer > 0);
            AddMenu(menu, "맨 뒤로", delegate { SetSelectedLayer(0); }, layer > 0);
            menu.Items.Add(new Separator());
            AddMenu(menu, "반시계 방향으로 회전", delegate { p.Angle = Store.Clamp(p.Angle - 3, -20, 20); Notify(); });
            AddMenu(menu, "시계 방향으로 회전", delegate { p.Angle = Store.Clamp(p.Angle + 3, -20, 20); Notify(); });
            foreach (var line in Store.State.Lines.Where(l => l.Id != p.LineId)) { var target = line; AddMenu(menu, "다른 끈으로 옮기기 · " + (Store.State.Lines.IndexOf(line) + 1), delegate { p.LineId = target.Id; p.OffsetY = 0; Notify(); }); }
            menu.Items.Add(new Separator()); AddMenu(menu, "사진 떼기", delegate { RemoveSelected(); }); menu.IsOpen = true; e.Handled = true;
        }
        private void AddMenu(ContextMenu menu, string text, Action action, bool enabled = true) { var item = new MenuItem { Header = text, IsEnabled = enabled }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        public void SetSelectedLayer(int targetIndex)
        {
            if (Selected == null || !Store.MovePhotoLayer(Selected, targetIndex)) return;
            if (SelectionChanged != null) SelectionChanged(); Notify();
        }
        public void RemoveSelected() { if (Selected == null) return; Store.State.Photos.Remove(Selected); Selected = null; if (SelectionChanged != null) SelectionChanged(); Notify(); }
        private void Notify() { InvalidateVisual(); if (Changed != null) Changed(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (Editable && e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; } }
    }
}
