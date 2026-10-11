using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PhotoString
{
    public class DesignWindow : Window
    {
        private readonly Controller controller;
        private readonly DesignCatalog catalog;
        private readonly ComboBox target, presetPicker, knobSide;
        private readonly DesignPreview preview;
        private readonly TextBox presetName;
        private readonly Button deletePreset;
        private readonly TextBlock status;
        private readonly List<Action> refreshers = new List<Action>();
        private bool updating;
        private DecorationStyle Current { get { var line = Targets().FirstOrDefault(); return line == null ? DecorationStyle.Default : line.Appearance ?? DecorationStyle.Default; } }
        private int KnobSide { get { return knobSide.SelectedIndex == 2 ? 0 : knobSide.SelectedIndex == 1 ? 1 : -1; } }
        private DecorationStyle SelectedKnob(DecorationStyle style) { return style.ForKnob(KnobSide == 0 ? -1 : KnobSide); }
        private void EditKnob(DecorationStyle style, Action<DecorationStyle> action) { style.EditKnobs(KnobSide, action); }
        private List<StringLine> Targets()
        {
            var item = target.SelectedItem as ComboBoxItem; var line = item == null ? null : item.Tag as StringLine;
            return line == null ? controller.Store.State.Lines.ToList() : new List<StringLine> { line };
        }
        public DesignWindow(Controller owner)
        {
            controller = owner; catalog = new DesignCatalog(controller.Store.Root);
            Title = "끈 · 노브 꾸미기"; Width = 860; Height = 840; MinWidth = 780; MinHeight = 730;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Scene.Brush("#f7f5f0");
            FontFamily = TextSupport.UserInterface; FontSize = 12; Foreground = Scene.Brush("#363f35");
            var layout = new Grid { Margin = new Thickness(24) }; Content = layout;
            foreach (double height in new[] { 54.0, 124, 52, -1, 58, 48 }) layout.RowDefinitions.Add(new RowDefinition { Height = height < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(height) });
            var heading = MainWindow.Label("나만의 끈과 노브", 23, "#30392f", true); heading.FontWeight = FontWeights.SemiBold; layout.Children.Add(heading);
            var previewFrame = new Border { Background = Scene.Brush("#eeece3"), BorderBrush = Scene.Brush("#deddd1"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
            Grid.SetRow(previewFrame, 1); layout.Children.Add(previewFrame); preview = new DesignPreview(controller.Store); previewFrame.Child = preview;
            var selectors = new Grid { Margin = new Thickness(0, 10, 0, 8) }; Grid.SetRow(selectors, 2); layout.Children.Add(selectors);
            selectors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); selectors.ColumnDefinitions.Add(new ColumnDefinition());
            target = new ComboBox { Height = 32, Margin = new Thickness(0, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
            target.Items.Add(new ComboBoxItem { Content = "적용 대상 · 전체 끈" });
            foreach (var line in controller.Store.State.Lines) target.Items.Add(new ComboBoxItem { Content = "적용 대상 · 끈 " + (controller.Store.State.Lines.IndexOf(line) + 1), Tag = line });
            target.SelectedIndex = 0; selectors.Children.Add(target); target.SelectionChanged += delegate { if (!updating) { ClearPreset(); Refresh(); } };
            presetPicker = new ComboBox { Height = 32, VerticalContentAlignment = VerticalAlignment.Center, MaxDropDownHeight = 300 }; Grid.SetColumn(presetPicker, 1); selectors.Children.Add(presetPicker);
            presetPicker.SelectionChanged += delegate
            {
                if (updating) return; var item = presetPicker.SelectedItem as ComboBoxItem; var preset = item == null ? null : item.Tag as DesignPreset;
                if (preset == null) return; foreach (var line in Targets()) line.Appearance = preset.Style.Copy(); Commit(); Refresh();
                deletePreset.IsEnabled = !preset.BuiltIn;
            };
            var forms = new Grid { Margin = new Thickness(0, 3, 0, 0) }; forms.ColumnDefinitions.Add(new ColumnDefinition()); forms.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(forms, 3); layout.Children.Add(forms);
            var rope = Section(forms, "끈", 0); var knob = Section(forms, "노브", 1);
            ColorField(rope, "색상", s => s.RopeColor, (s, v) => s.RopeColor = v);
            Choice(rope, "재질", new[] { "jute", "cotton", "nylon", "leather", "metal" }, new[] { "황마", "면 / 코튼", "나일론", "가죽", "금속" }, s => s.RopeMaterial, (s, v) => s.RopeMaterial = v);
            Choice(rope, "질감", new[] { "smooth", "fiber", "braid", "image" }, new[] { "매끈", "섬유 결", "땋은 무늬", "내 질감 이미지" }, s => s.RopeTexture, (s, v) => s.RopeTexture = v);
            Choice(rope, "디자인", new[] { "single", "double" }, new[] { "한 줄", "두 줄" }, s => s.RopeDesign, (s, v) => s.RopeDesign = v);
            Number(rope, "두께", 1, 12, s => s.RopeWidth, (s, v) => s.RopeWidth = v);
            var ropeImport = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; rope.Children.Add(ropeImport);
            ropeImport.Children.Add(ActionButton("질감 이미지 가져오기", delegate { ImportImage("끈 질감 이미지", (s, file) => { s.RopeImage = file; s.RopeTexture = "image"; }); }));
            ropeImport.Children.Add(ActionButton("이미지 해제", delegate { Change(s => { s.RopeImage = null; s.RopeTexture = "smooth"; }); }));
            var ropeHelp = Help("JPG·PNG 질감을 반복해 입힙니다.\n색상은 바탕색, 재질은 빛과 표면 표현을 바꿉니다."); rope.Children.Add(ropeHelp);
            var sideRow = Field(knob, "적용 쪽");
            knobSide = new ComboBox { Tag = "knob-side", Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var name in new[] { "왼쪽 노브", "오른쪽 노브", "양쪽 노브" }) knobSide.Items.Add(name);
            knobSide.SelectedIndex = 0; Grid.SetColumn(knobSide, 1); sideRow.Children.Add(knobSide);
            knobSide.SelectionChanged += delegate { if (!updating) Refresh(); };
            ColorField(knob, "색상", s => SelectedKnob(s).KnobColor, (s, v) => EditKnob(s, k => k.KnobColor = v));
            Choice(knob, "재질", new[] { "wood", "metal", "plastic", "stone", "glass" }, new[] { "나무", "금속", "플라스틱", "돌", "유리" }, s => SelectedKnob(s).KnobMaterial, (s, v) => EditKnob(s, k => k.KnobMaterial = v));
            Choice(knob, "질감", new[] { "smooth", "grain", "brushed", "speckle", "image" }, new[] { "매끈", "나뭇결", "브러시 결", "돌 무늬", "내 질감 이미지" }, s => SelectedKnob(s).KnobTexture, (s, v) => EditKnob(s, k => k.KnobTexture = v));
            Choice(knob, "디자인", new[] { "circle", "square", "hexagon", "flower", "image" }, new[] { "원형", "둥근 사각", "육각형", "꽃 모양", "내 노브 이미지" }, s => SelectedKnob(s).KnobShape, (s, v) => EditKnob(s, k => k.KnobShape = v));
            Number(knob, "크기", 8, 26, s => SelectedKnob(s).KnobSize, (s, v) => EditKnob(s, k => k.KnobSize = v));
            var knobImport = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; knob.Children.Add(knobImport);
            knobImport.Children.Add(ActionButton("질감 이미지", delegate { ImportImage("노브 표면 질감", (s, file) => EditKnob(s, k => { k.KnobImage = file; k.KnobTexture = "image"; })); }));
            knobImport.Children.Add(ActionButton("노브 PNG", delegate { ImportImage("내 노브 디자인 · 투명 PNG 권장", (s, file) => EditKnob(s, k => { k.KnobArt = file; k.KnobShape = "image"; })); }));
            knobImport.Children.Add(ActionButton("이미지 해제", delegate { Change(s => EditKnob(s, k => { k.KnobImage = null; k.KnobArt = null; k.KnobTexture = "smooth"; k.KnobShape = "circle"; })); }));
            knob.Children.Add(Help("이미지와 설정은 선택한 쪽에만 적용됩니다.\n질감은 모양 안에, 노브 PNG는 투명도와 모양 그대로 표시합니다.\n양쪽을 선택하면 변경한 항목을 함께 적용하며, 왼쪽 값을 표시합니다."));
            var saveRow = new Grid { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(saveRow, 4); layout.Children.Add(saveRow);
            saveRow.ColumnDefinitions.Add(new ColumnDefinition()); saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            presetName = new TextBox { Text = "나의 디자인", MaxLength = 40, Height = 32, Padding = new Thickness(8, 5, 8, 4), Margin = new Thickness(0, 0, 10, 0), ToolTip = "현재 디자인을 카탈로그에 저장할 이름" }; saveRow.Children.Add(presetName);
            var savePreset = ActionButton("내 디자인 저장", SavePreset); Grid.SetColumn(savePreset, 1); saveRow.Children.Add(savePreset);
            deletePreset = ActionButton("선택한 내 디자인 삭제", DeletePreset); deletePreset.IsEnabled = false; Grid.SetColumn(deletePreset, 2); saveRow.Children.Add(deletePreset);
            var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) }; Grid.SetRow(footer, 5); layout.Children.Add(footer);
            status = MainWindow.Label("변경은 바로 적용되고 자동 저장됩니다.", 10, "#7e8375", true); status.TextWrapping = TextWrapping.Wrap; status.MaxWidth = 420; status.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(status);
            var done = ActionButton("완료", delegate { Close(); }, true); done.HorizontalAlignment = HorizontalAlignment.Right; footer.Children.Add(done);
            ReloadCatalog(null); Refresh(); if (catalog.Notice != null) status.Text = catalog.Notice;
            Closed += delegate { controller.SaveNow(); };
        }
        private StackPanel Section(Grid parent, string title, int column)
        {
            var panel = new StackPanel(); panel.Children.Add(MainWindow.Label(title, 16, "#4e694a")); panel.Children[0].SetValue(MarginProperty, new Thickness(0, 0, 0, 12));
            var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var border = new Border { Padding = new Thickness(15), Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 0 ? 6 : 0, 0), Background = Scene.Brush("#fffef9"), BorderBrush = Scene.Brush("#e5e4d9"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Child = scroll };
            Grid.SetColumn(border, column); parent.Children.Add(border); return panel;
        }
        private static TextBlock Help(string text) { var block = MainWindow.Label(text, 10, "#8d9183"); block.Margin = new Thickness(0, 10, 0, 0); block.TextWrapping = TextWrapping.Wrap; return block; }
        private Grid Field(StackPanel panel, string label)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 9) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(MainWindow.Label(label, 11, "#7e8375", true)); panel.Children.Add(row); return row;
        }
        private void Choice(StackPanel panel, string label, string[] values, string[] names, Func<DecorationStyle, string> get, Action<DecorationStyle, string> set)
        {
            var row = Field(panel, label); var box = new ComboBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            foreach (var name in names) box.Items.Add(name); Grid.SetColumn(box, 1); row.Children.Add(box);
            box.SelectionChanged += delegate { if (!updating && box.SelectedIndex >= 0) Change(s => set(s, values[box.SelectedIndex])); };
            refreshers.Add(delegate { box.SelectedIndex = Math.Max(0, Array.IndexOf(values, get(Current))); });
        }
        private void Number(StackPanel panel, string label, double min, double max, Func<DecorationStyle, double> get, Action<DecorationStyle, double> set)
        {
            var row = Field(panel, label); var group = new Grid(); group.ColumnDefinitions.Add(new ColumnDefinition()); group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Height = 30 };
            var value = MainWindow.Label("", 11, "#6d7564", true); value.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(value, 1); group.Children.Add(slider); group.Children.Add(value); Grid.SetColumn(group, 1); row.Children.Add(group);
            slider.ValueChanged += delegate { if (!updating) Change(s => set(s, slider.Value)); };
            refreshers.Add(delegate { slider.Value = get(Current); value.Text = get(Current).ToString("0.#"); });
        }
        private void ColorField(StackPanel panel, string label, Func<DecorationStyle, string> get, Action<DecorationStyle, string> set)
        {
            var row = Field(panel, label); var group = new Grid(); group.ColumnDefinitions.Add(new ColumnDefinition()); group.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            var text = new TextBox { Height = 30, Padding = new Thickness(6, 4, 6, 4), MaxLength = 7, Margin = new Thickness(0, 0, 8, 0), ToolTip = "#RRGGBB 형식으로 입력하거나 오른쪽 색 버튼을 누르세요." }; group.Children.Add(text);
            var pick = ActionButton("색", delegate
            {
                var color = (Color)ColorConverter.ConvertFromString(get(Current));
                using (var dialog = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B) })
                {
                    if (dialog.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle)) != Forms.DialogResult.OK) return;
                    string result = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2"); Change(s => set(s, result));
                }
            }); pick.Margin = new Thickness(0); Grid.SetColumn(pick, 1); group.Children.Add(pick); Grid.SetColumn(group, 1); row.Children.Add(group);
            Action commit = delegate
            {
                if (updating) return; var result = DecorationStyle.ValidColor(text.Text, null);
                if (result == null) { text.BorderBrush = Scene.Brush("#bd655c"); status.Text = "색상을 #RRGGBB 형식으로 입력해주세요. 예: #C5AE84"; return; }
                if (result != get(Current)) Change(s => set(s, result));
            };
            text.LostKeyboardFocus += delegate { commit(); }; text.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { commit(); e.Handled = true; } };
            refreshers.Add(delegate { text.Text = get(Current); text.BorderBrush = Scene.Brush("#dddcd2"); pick.Background = Scene.Brush(get(Current)); });
        }
        private Button ActionButton(string text, Action action, bool primary = false)
        {
            var button = new Button { Content = text, Height = 32, Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(0, 0, 6, 6), Background = Scene.Brush(primary ? "#65795e" : "#fffef8"), Foreground = Scene.Brush(primary ? "#ffffff" : "#6d7564"), BorderBrush = Scene.Brush("#dedfd2"), BorderThickness = new Thickness(1), FontSize = 11, Cursor = Cursors.Hand };
            button.Click += delegate { action(); }; return button;
        }
        private void ClearPreset() { bool old = updating; updating = true; presetPicker.SelectedIndex = 0; updating = old; deletePreset.IsEnabled = false; }
        private void Refresh()
        {
            updating = true; foreach (var action in refreshers) action(); preview.Appearance = Current.Copy(); preview.InvalidateVisual(); updating = false;
        }
        private void Change(Action<DecorationStyle> action)
        {
            foreach (var line in Targets()) { if (line.Appearance == null) line.Appearance = new DecorationStyle(); action(line.Appearance); line.Appearance.Normalize(); }
            ClearPreset(); Commit(); Refresh();
        }
        private void Commit()
        {
            controller.Changed(); status.Text = "바로 적용됨 · " + (Targets().Count > 1 ? "전체 끈" : "선택한 끈") + " · 자동 저장";
            var style = Current; var knobs = new[] { style.ForKnob(-1), style.ForKnob(1) };
            if ((style.RopeTexture == "image" && controller.Store.MaterialImage(style.RopeImage) == null) || knobs.Any(k => (k.KnobTexture == "image" && controller.Store.MaterialImage(k.KnobImage) == null) || (k.KnobShape == "image" && controller.Store.MaterialImage(k.KnobArt) == null)))
                status.Text = "이미지 가져오기 버튼으로 사용할 파일을 선택해주세요.";
        }
        private void ImportImage(string title, Action<DecorationStyle, string> apply)
        {
            var dialog = new OpenFileDialog { Title = title, Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
            if (dialog.ShowDialog(this) != true) return;
            try { string file = controller.Store.ImportMaterial(dialog.FileName); Change(s => apply(s, file)); }
            catch (Exception ex) { status.Text = "이미지를 가져오지 못했습니다: " + ex.Message; }
        }
        private void ReloadCatalog(DesignPreset selected)
        {
            updating = true; presetPicker.Items.Clear(); presetPicker.Items.Add(new ComboBoxItem { Content = "디자인 카탈로그에서 선택" });
            presetPicker.SelectedIndex = 0;
            foreach (var preset in catalog.Items) { var item = new ComboBoxItem { Content = preset.ToString(), Tag = preset }; presetPicker.Items.Add(item); if (preset == selected) presetPicker.SelectedItem = item; }
            deletePreset.IsEnabled = selected != null && !selected.BuiltIn; updating = false;
        }
        private void SavePreset()
        {
            try { var preset = catalog.Add(presetName.Text, Current); ReloadCatalog(preset); status.Text = "내 디자인 저장됨 · " + preset.Name; }
            catch (Exception ex) { status.Text = "카탈로그 저장 실패: " + ex.Message; }
        }
        private void DeletePreset()
        {
            var item = presetPicker.SelectedItem as ComboBoxItem; var preset = item == null ? null : item.Tag as DesignPreset; if (preset == null || preset.BuiltIn) return;
            try { catalog.Remove(preset); ReloadCatalog(null); status.Text = "내 디자인 목록에서 삭제했습니다."; } catch (Exception ex) { status.Text = "삭제 실패: " + ex.Message; }
        }
        private class DialogOwner : Forms.IWin32Window { public IntPtr Handle { get; private set; } public DialogOwner(IntPtr handle) { Handle = handle; } }
        public UIElement PreviewContent { get { return (UIElement)Content; } }
    }
}
