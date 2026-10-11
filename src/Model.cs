using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoString
{
    [DataContract]
    public class Arrangement
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<StringLine> Lines = new List<StringLine>();
        // Photos are stored from back to front; the array order is the saved layer order.
        [DataMember] public List<Photo> Photos = new List<Photo>();
        [DataMember] public bool Visible = true;
        [DataMember] public string Monitor = "";
        public static Arrangement CreateDefault()
        {
            var result = new Arrangement();
            result.Lines.Add(new StringLine { Id = "first", Left = 100, Right = 1100, Y1 = 145, Y2 = 130, Sag = 48 });
            result.Lines.Add(new StringLine { Id = "second", Left = 100, Right = 1100, Y1 = 420, Y2 = 395, Sag = 42 });
            string[] captions = { "a quiet morning", "somewhere green", "our little corner", "slow days", "by the sea", "stay a little" };
            for (int i = 0; i < 6; i++)
                result.Photos.Add(new Photo { Id = Guid.NewGuid().ToString("N"), LineId = i < 3 ? "first" : "second", T = .19 + (i % 3) * .31, Width = i == 1 || i == 4 ? 155 : 185, Aspect = i == 1 || i == 4 ? .8 : 1.22, Angle = new double[] { -5, 3, -3, 4, -4, 5 }[i], Caption = captions[i], Demo = i });
            return result;
        }
    }
    [DataContract]
    public class StringLine
    {
        [DataMember] public string Id;
        [DataMember] public double Left;
        [DataMember] public double Right;
        [DataMember] public double Y1;
        [DataMember] public double Y2;
        [DataMember] public double Sag;
        [DataMember(EmitDefaultValue = false)] public DecorationStyle Appearance;
        public Point At(double t) { return new Point(Left + (Right - Left) * t, Y1 + (Y2 - Y1) * t + 4 * Sag * t * (1 - t)); }
    }
    [DataContract]
    public class Photo
    {
        [DataMember] public string Id;
        [DataMember] public string LineId;
        [DataMember] public string File;
        [DataMember] public string Caption = "";
        [DataMember] public double T;
        [DataMember] public double OffsetY;
        [DataMember] public double Width = 180;
        [DataMember] public double Aspect = 1;
        [DataMember] public double Angle;
        [DataMember] public int Demo = -1;
    }
    public class Store
    {
        public readonly string Root;
        public Arrangement State;
        public string Notice;
        private readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();
        public Store(string root) { Root = root; }
        public void Load()
        {
            State = null;
            var path = Path.Combine(Root, "arrangement.json");
            if (File.Exists(path))
            {
                try { State = Read(path); Normalize(); }
                catch
                {
                    try { State = Read(path + ".bak"); Normalize(); Notice = "이전 저장본에서 배치를 복구했습니다."; }
                    catch { State = null; Notice = "저장 파일을 읽을 수 없어 기본 배치로 시작했습니다. 기존 파일은 보관됩니다."; }
                }
            }
            if (State == null) State = Arrangement.CreateDefault();
            Normalize();
        }
        private Arrangement Read(string path)
        {
            using (var stream = File.OpenRead(path)) return (Arrangement)new DataContractJsonSerializer(typeof(Arrangement)).ReadObject(stream);
        }
        public void Normalize()
        {
            if (State.Lines == null || State.Photos == null || State.Lines.Count > 12 || State.Photos.Count > 120) throw new InvalidDataException("배치 파일 형식이 올바르지 않습니다.");
            var ids = new HashSet<string>();
            State.Lines.RemoveAll(l => l == null || String.IsNullOrEmpty(l.Id) || !ids.Add(l.Id));
            foreach (var l in State.Lines)
            {
                l.Left = Clamp(l.Left, 0, Scene.DesignWidth - 100); l.Right = Clamp(l.Right, l.Left + 100, Scene.DesignWidth);
                l.Y1 = Clamp(l.Y1, 0, Scene.DesignHeight); l.Y2 = Clamp(l.Y2, 0, Scene.DesignHeight); l.Sag = Clamp(l.Sag, 0, 130);
                if (l.Appearance == null) l.Appearance = new DecorationStyle(); l.Appearance.Normalize();
            }
            ids.Clear();
            State.Photos.RemoveAll(p => p == null || String.IsNullOrEmpty(p.Id) || !ids.Add(p.Id) || !State.Lines.Any(l => l.Id == p.LineId));
            foreach (var p in State.Photos)
            {
                p.Width = Clamp(p.Width, 90, 280); p.T = Clamp(p.T, 0, 1); p.OffsetY = Clamp(p.OffsetY, -Scene.DesignHeight - 130, Scene.DesignHeight);
                p.Angle = Clamp(p.Angle, -20, 20); p.Aspect = Clamp(p.Aspect, .5, 2);
                if (!String.IsNullOrEmpty(p.File) && (Path.GetFileName(p.File) != p.File || !p.File.StartsWith("photo-", StringComparison.Ordinal))) p.File = null;
                p.Caption = TextSupport.Limit(p.Caption, 48);
                p.Demo = Math.Max(-1, Math.Min(5, p.Demo));
            }
        }
        public static double Clamp(double n, double min, double max) { return Double.IsNaN(n) || Double.IsInfinity(n) ? min : Math.Max(min, Math.Min(max, n)); }
        public void Save()
        {
            Directory.CreateDirectory(Root);
            var path = Path.Combine(Root, "arrangement.json");
            var temp = path + ".tmp";
            using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(Arrangement)).WriteObject(stream, State);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
        }
        public bool MovePhotoLayer(Photo photo, int targetIndex)
        {
            int current = State.Photos.IndexOf(photo);
            if (current < 0) return false;
            targetIndex = Math.Max(0, Math.Min(State.Photos.Count - 1, targetIndex));
            if (current == targetIndex) return false;
            State.Photos.RemoveAt(current); State.Photos.Insert(targetIndex, photo); return true;
        }
        public string ImportMaterial(string path)
        {
            var info = new FileInfo(path); if (info.Length > 25 * 1024 * 1024) throw new InvalidDataException("25MB 이하의 이미지를 선택해주세요.");
            var image = LoadBitmap(path); var folder = Path.Combine(Root, "materials"); Directory.CreateDirectory(folder);
            string file = "material-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path).ToLowerInvariant();
            File.Copy(path, Path.Combine(folder, file)); cache["material:" + file] = image; return file;
        }
        public ImageSource MaterialImage(string file)
        {
            file = DecorationStyle.SafeAsset(file); if (file == null) return null;
            ImageSource image; string key = "material:" + file; if (cache.TryGetValue(key, out image)) return image;
            try { image = LoadBitmap(Path.Combine(Root, "materials", file)); } catch { image = null; }
            cache[key] = image; return image;
        }
        public Photo Import(string path, Photo replacing)
        {
            if (replacing == null && State.Photos.Count >= 120) throw new InvalidOperationException("사진은 최대 120장까지 걸 수 있습니다.");
            var info = new FileInfo(path);
            if (info.Length > 75 * 1024 * 1024) throw new InvalidDataException("75MB 이하의 사진을 선택해주세요.");
            var bitmap = LoadBitmap(path);
            Directory.CreateDirectory(Path.Combine(Root, "photos"));
            var file = "photo-" + Guid.NewGuid().ToString("N") + Path.GetExtension(path).ToLowerInvariant();
            System.IO.File.Copy(path, Path.Combine(Root, "photos", file));
            var photo = replacing;
            if (photo == null)
            {
                if (State.Lines.Count == 0) State.Lines.Add(new StringLine { Id = Guid.NewGuid().ToString("N"), Left = 100, Right = 1100, Y1 = 160, Y2 = 150, Sag = 45 });
                var line = State.Lines.OrderBy(l => State.Photos.Count(p => p.LineId == l.Id)).First();
                var existing = State.Photos.Where(p => p.LineId == line.Id).Select(p => p.T).Concat(new[] { .05, .95 }).OrderBy(t => t).ToList();
                double best = 0, tBest = .5;
                for (int i = 1; i < existing.Count; i++) if (existing[i] - existing[i - 1] > best) { best = existing[i] - existing[i - 1]; tBest = (existing[i] + existing[i - 1]) / 2; }
                photo = new Photo { Id = Guid.NewGuid().ToString("N"), LineId = line.Id, T = tBest, Angle = (State.Photos.Count % 5 - 2) * 2 };
                State.Photos.Add(photo);
            }
            photo.File = file; photo.Demo = -1; photo.Aspect = Clamp(bitmap.Width / bitmap.Height, .5, 2);
            photo.Caption = TextSupport.Limit(Path.GetFileNameWithoutExtension(path), 32);
            cache[file] = bitmap;
            return photo;
        }
        public ImageSource ImageFor(Photo photo)
        {
            if (String.IsNullOrEmpty(photo.File)) return null;
            ImageSource image;
            if (cache.TryGetValue(photo.File, out image)) return image;
            try { image = LoadBitmap(Path.Combine(Root, "photos", photo.File)); } catch { image = null; }
            cache[photo.File] = image;
            return image;
        }
        public static BitmapSource LoadBitmap(string path)
        {
            var bitmap = new BitmapImage();
            using (var stream = File.OpenRead(path))
            {
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 1000;
                bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            }
            int orientation = 1;
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    var metadata = decoder.Frames[0].Metadata as BitmapMetadata;
                    if (metadata != null && metadata.ContainsQuery("/app1/ifd/{ushort=274}")) orientation = Convert.ToInt32(metadata.GetQuery("/app1/ifd/{ushort=274}"));
                }
            }
            catch { }
            var transform = new TransformGroup();
            if (orientation == 2 || orientation == 5 || orientation == 7) transform.Children.Add(new ScaleTransform(-1, 1));
            if (orientation == 4) transform.Children.Add(new ScaleTransform(1, -1));
            if (orientation == 3) transform.Children.Add(new RotateTransform(180));
            if (orientation == 5 || orientation == 6) transform.Children.Add(new RotateTransform(90));
            if (orientation == 7 || orientation == 8) transform.Children.Add(new RotateTransform(270));
            if (transform.Children.Count == 0) return bitmap;
            var oriented = new TransformedBitmap(bitmap, transform); oriented.Freeze(); return oriented;
        }
    }
}
