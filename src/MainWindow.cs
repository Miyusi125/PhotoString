using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PhotoString
{
    public class MainWindow : Window
    {
        public readonly Scene Scene;
        private readonly Controller controller;
        private readonly TextBlock count, status, selection;
        private readonly Button editButton, visibilityButton, replaceButton, removeButton;
        private readonly Button layerBack, layerBackward, layerForward, layerFront;
        private readonly TextBox caption;
        private readonly Slider size, angle;
        private readonly ComboBox monitor, layerPicker;
        private bool updating;
        public MainWindow(Controller owner)
        {
            controller = owner; Title = "Photo String · 나의 작은 사진 줄"; Width = 1060; Height = 900;
            MinWidth = 900; MinHeight = 720; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Scene.Brush("#f7f5f0"); FontFamily = TextSupport.UserInterface; FontSize = 12; Foreground = Scene.Brush("#363f35");
            var layout = new Grid { Margin = new Thickness(30, 15, 30, 15), Background = Background }; Content = layout;
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(59) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(33) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(144) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); layout.Children.Add(header);
            var headings = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            headings.Children.Add(Label("P H O T O   S T R I N G", 10, "#6b7d63"));
            var title = Label("나의 작은 사진 줄", 28, "#30392f"); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 4, 0, 5); headings.Children.Add(title);
            headings.Children.Add(Label("좋아하는 순간들을, 바탕화면 한쪽에 걸어두세요.", 12, "#8d9183")); header.Children.Add(headings);
            var headerActions = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(headerActions, 1); header.Children.Add(headerActions);
            var badge = new Border { Background = Scene.Brush("#e8edde"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 6, 12, 6), Child = Label("●  바탕화면에만 표시", 11, "#66795d") }; headerActions.Children.Add(badge);
            var designs = Button("끈 · 노브 꾸미기", ShowDesigns, false); designs.Margin = new Thickness(0, 7, 0, 0); headerActions.Children.Add(designs);
            var toolbar = new Grid(); Grid.SetRow(toolbar, 1); layout.Children.Add(toolbar); toolbar.ColumnDefinitions.Add(new ColumnDefinition()); toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; toolbar.Children.Add(actions);
            actions.Children.Add(Button("＋ 사진 추가", AddPhotos, true)); actions.Children.Add(Button("끈 추가", AddLine, false));
            editButton = Button("바탕화면에서 편집", delegate { controller.SetEditing(!controller.Editing); }, false); actions.Children.Add(editButton);
            visibilityButton = Button("사진 숨기기", delegate { controller.ToggleVisibility(); }, false); actions.Children.Add(visibilityButton);
            var extra = Button("···", ShowMore, false); extra.Width = 38; actions.Children.Add(extra);
            var monitorPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(monitorPanel, 1); toolbar.Children.Add(monitorPanel);
            monitorPanel.Children.Add(Label("화면  ", 11, "#929488")); monitor = new ComboBox { Width = 143, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Background = Brushes.White, BorderBrush = Scene.Brush("#dddcd2") };
            var screens = Forms.Screen.AllScreens;
            foreach (var s in screens) { var i = Array.IndexOf(screens, s); monitor.Items.Add(new ComboBoxItem { Content = "모니터 " + (i + 1) + (s.Primary ? " · 기본" : ""), Tag = s.DeviceName }); }
            monitor.SelectedIndex = Math.Max(0, Array.FindIndex(screens, s => s.DeviceName == controller.Store.State.Monitor));
            monitor.SelectionChanged += delegate
            {
                var item = monitor.SelectedItem as ComboBoxItem; if (item == null || controller.Desktop == null) return;
                controller.Store.State.Monitor = (string)item.Tag; controller.Desktop.PositionOnScreen(); controller.Changed();
            }; monitorPanel.Children.Add(monitor);
            var previewFrame = new Border { CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1), BorderBrush = Scene.Brush("#deddd1"), Background = Scene.Brush("#f0f1e9"), ClipToBounds = true };
            Grid.SetRow(previewFrame, 2); layout.Children.Add(previewFrame);
            var previewGrid = new Grid(); previewFrame.Child = previewGrid;
            Scene = new Scene(controller.Store) { ShowApplicationArea = true }; previewGrid.Children.Add(Scene);
            var previewTag = new Border { Background = Scene.Brush("#b3fffff7"), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 5, 9, 5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(17), IsHitTestVisible = false, Child = Label("배치 미리보기 · 테두리 안이 실제 화면", 10, "#7e8375") }; previewGrid.Children.Add(previewTag);
            count = Label("", 10, "#858777"); count.HorizontalAlignment = HorizontalAlignment.Right; count.VerticalAlignment = VerticalAlignment.Top; count.Margin = new Thickness(17); count.IsHitTestVisible = false; previewGrid.Children.Add(count);
            Scene.Changed += controller.Changed; Scene.SelectionChanged += UpdateSelection; Scene.ReplaceRequested += ReplacePhoto;
            previewFrame.AllowDrop = true;
            previewFrame.DragOver += delegate(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            previewFrame.Drop += delegate(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) ImportFiles((string[])e.Data.GetData(DataFormats.FileDrop), null); };
            var tips = new Grid(); Grid.SetRow(tips, 3); layout.Children.Add(tips);
            tips.Children.Add(Label("사진 드래그  ·  더블클릭 교체  ·  휠 크기 조절  ·  Shift+휠 회전", 10, "#8d9183", true));
            var edgeHint = Label("고정핀을 테두리로 드래그하면 화면 끝에 붙어요", 10, "#6f8566", true); edgeHint.HorizontalAlignment = HorizontalAlignment.Right; tips.Children.Add(edgeHint);
            var inspector = new Border { Background = Scene.Brush("#fffef9"), BorderBrush = Scene.Brush("#e5e4d9"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(17, 11, 17, 11) };
            Grid.SetRow(inspector, 4); layout.Children.Add(inspector);
            var inspectorContent = new Grid(); inspector.Child = inspectorContent;
            inspectorContent.RowDefinitions.Add(new RowDefinition()); inspectorContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            var inspectGrid = new Grid(); inspectorContent.Children.Add(inspectGrid);
            inspectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inspectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) }); inspectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) }); inspectGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            var textPanel = new StackPanel { Margin = new Thickness(0, 0, 17, 0) }; selection = Label("사진을 선택하면 편집할 수 있어요", 10, "#8d9183"); textPanel.Children.Add(selection);
            caption = new TextBox { Height = 31, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(7, 4, 7, 4), Background = Scene.Brush("#f7f6ef"), BorderThickness = new Thickness(0), FontSize = 12, FontFamily = TextSupport.CaptionInput };
            caption.TextChanged += delegate
            {
                if (updating || Scene.Selected == null) return; string text = TextSupport.Limit(caption.Text, 48);
                if (text != caption.Text)
                {
                    int caret = caption.CaretIndex; updating = true;
                    try { caption.Text = text; caption.CaretIndex = Math.Min(caret, text.Length); } finally { updating = false; }
                }
                Scene.Selected.Caption = text; controller.Changed();
            }; textPanel.Children.Add(caption); inspectGrid.Children.Add(textPanel);
            var sizePanel = new StackPanel { Margin = new Thickness(0, 0, 17, 0) }; sizePanel.Children.Add(Label("사진 크기", 10, "#8d9183"));
            size = new Slider { Minimum = 90, Maximum = 280, Margin = new Thickness(0, 11, 0, 0), TickFrequency = 10, IsSnapToTickEnabled = true };
            size.ValueChanged += delegate { if (updating || Scene.Selected == null) return; Scene.Selected.Width = size.Value; controller.Changed(); }; sizePanel.Children.Add(size); Grid.SetColumn(sizePanel, 1); inspectGrid.Children.Add(sizePanel);
            var anglePanel = new StackPanel { Margin = new Thickness(0, 0, 17, 0) }; anglePanel.Children.Add(Label("기울기", 10, "#8d9183"));
            angle = new Slider { Minimum = -20, Maximum = 20, Margin = new Thickness(0, 11, 0, 0), TickFrequency = 1, IsSnapToTickEnabled = true };
            angle.ValueChanged += delegate { if (updating || Scene.Selected == null) return; Scene.Selected.Angle = angle.Value; controller.Changed(); }; anglePanel.Children.Add(angle); Grid.SetColumn(anglePanel, 2); inspectGrid.Children.Add(anglePanel);
            var selectedActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(selectedActions, 3); inspectGrid.Children.Add(selectedActions);
            replaceButton = Button("교체", delegate { if (Scene.Selected != null) ReplacePhoto(Scene.Selected); }, false); selectedActions.Children.Add(replaceButton);
            removeButton = Button("사진 떼기", Scene.RemoveSelected, false); selectedActions.Children.Add(removeButton);
            var layerRow = new Grid { Margin = new Thickness(0, 9, 0, 0) }; Grid.SetRow(layerRow, 1); inspectorContent.Children.Add(layerRow);
            layerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var layerLabel = Label("사진 레이어", 11, "#6d7564", true); layerLabel.Margin = new Thickness(0, 0, 10, 0); layerRow.Children.Add(layerLabel);
            layerPicker = new ComboBox { Height = 31, Margin = new Thickness(0, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center, MaxDropDownHeight = 280, Background = Brushes.White, BorderBrush = Scene.Brush("#dddcd2"), FontFamily = TextSupport.CaptionInput, ToolTip = "숫자가 클수록 앞에 표시됩니다. 가려진 사진도 이 목록에서 선택할 수 있어요. 집게는 사진과 함께 이동합니다." };
            Grid.SetColumn(layerPicker, 1); layerRow.Children.Add(layerPicker);
            layerPicker.SelectionChanged += delegate
            {
                if (updating) return; var item = layerPicker.SelectedItem as ComboBoxItem; var photo = item == null ? null : item.Tag as Photo;
                if (photo == null) return; Scene.Selected = photo; Scene.InvalidateVisual(); UpdateSelection();
            };
            var layerActions = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(layerActions, 2); layerRow.Children.Add(layerActions);
            layerBack = Button("맨 뒤", delegate { Scene.SetSelectedLayer(0); }, false); layerActions.Children.Add(layerBack);
            layerBackward = Button("뒤로", delegate { Scene.SetSelectedLayer(controller.Store.State.Photos.IndexOf(Scene.Selected) - 1); }, false); layerActions.Children.Add(layerBackward);
            layerForward = Button("앞으로", delegate { Scene.SetSelectedLayer(controller.Store.State.Photos.IndexOf(Scene.Selected) + 1); }, false); layerActions.Children.Add(layerForward);
            layerFront = Button("맨 앞", delegate { Scene.SetSelectedLayer(controller.Store.State.Photos.Count - 1); }, false); layerFront.Margin = new Thickness(0); layerActions.Children.Add(layerFront);
            var launchRow = new Grid(); Grid.SetRow(launchRow, 5); layout.Children.Add(launchRow);
            launchRow.Children.Add(Label("배치를 저장하고 바탕화면에 사진 장식을 표시합니다.", 11, "#8d9183", true));
            var applyButton = Button("적용 후 실행", delegate { controller.ApplyAndRun(); }, true);
            applyButton.Height = 40; applyButton.MinWidth = 150; applyButton.FontSize = 13;
            applyButton.Margin = new Thickness(0); applyButton.HorizontalAlignment = HorizontalAlignment.Right; applyButton.VerticalAlignment = VerticalAlignment.Center;
            applyButton.ToolTip = "현재 배치를 저장하고 사진 장식을 표시한 뒤 설정 창을 닫습니다.";
            launchRow.Children.Add(applyButton);
            var footer = new Grid(); Grid.SetRow(footer, 6); layout.Children.Add(footer); status = Label("사진과 배치는 이 컴퓨터에 저장됩니다.", 10, "#8d9183", true); status.TextTrimming = TextTrimming.CharacterEllipsis; status.MaxWidth = 730; footer.Children.Add(status);
            var shortcut = Label("관리창 열기   Ctrl + Alt + P", 10, "#a0a295", true); shortcut.HorizontalAlignment = HorizontalAlignment.Right; footer.Children.Add(shortcut);
            Closing += OnClosing; UpdateCount(); UpdateSelection(); RefreshControls(); if (controller.Store.Notice != null) SetStatus(controller.Store.Notice);
        }
        public static TextBlock Label(string text, double fontSize, string color, bool middle = false)
        {
            return new TextBlock { Text = text, FontSize = fontSize, Foreground = Scene.Brush(color), VerticalAlignment = middle ? VerticalAlignment.Center : VerticalAlignment.Top };
        }
        private Button Button(string text, Action action, bool primary)
        {
            var button = new Button { Content = text, Height = 33, Padding = new Thickness(13, 0, 13, 0), Margin = new Thickness(0, 0, 8, 0), Background = Scene.Brush(primary ? "#65795e" : "#fffef8"), Foreground = Scene.Brush(primary ? "#ffffff" : "#6d7564"), BorderBrush = Scene.Brush(primary ? "#65795e" : "#dedfd2"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, FontSize = 11 };
            var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); border.SetValue(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) }); border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); content.SetValue(ContentPresenter.MarginProperty, new Thickness(12, 0, 12, 0)); border.AppendChild(content);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            button.MouseEnter += delegate { if (button.IsEnabled) button.Opacity = .8; }; button.MouseLeave += delegate { button.Opacity = button.IsEnabled ? 1 : .4; };
            button.IsEnabledChanged += delegate { button.Opacity = button.IsEnabled ? 1 : .4; }; button.Click += delegate { action(); }; return button;
        }
        public void AddPhotos()
        {
            var dialog = ImageDialog(true); if (dialog.ShowDialog(this) == true) ImportFiles(dialog.FileNames, null);
        }
        public void ReplacePhoto(Photo photo)
        {
            var dialog = ImageDialog(false); if (dialog.ShowDialog(this) == true) ImportFiles(dialog.FileNames, photo);
        }
        private OpenFileDialog ImageDialog(bool multi)
        {
            return new OpenFileDialog { Title = multi ? "끈에 걸 사진을 골라주세요" : "새 사진으로 교체", Multiselect = multi, Filter = "사진 파일|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|모든 파일|*.*" };
        }
        private void ImportFiles(string[] files, Photo replacing)
        {
            int imported = 0; var errors = new System.Collections.Generic.List<string>();
            foreach (var file in files)
            {
                try { Scene.Selected = controller.Store.Import(file, replacing); imported++; }
                catch (Exception ex) { errors.Add(Path.GetFileName(file) + ": " + ex.Message); }
            }
            UpdateSelection(); if (imported > 0) controller.Changed();
            if (errors.Count > 0) MessageBox.Show(this, String.Join("\n", errors.Take(5).ToArray()), "읽지 못한 사진", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void AddLine()
        {
            if (controller.Store.State.Lines.Count >= 8) { SetStatus("끈은 최대 8개까지 추가할 수 있어요."); return; }
            double y = 100 + controller.Store.State.Lines.Count * 110;
            var appearance = controller.Store.State.Lines.Select(l => l.Appearance).FirstOrDefault(s => s != null) ?? DecorationStyle.Default;
            controller.Store.State.Lines.Add(new StringLine { Id = Guid.NewGuid().ToString("N"), Left = 140, Right = 1060, Y1 = Math.Min(y, 530), Y2 = Math.Min(y, 530) - 10, Sag = 40, Appearance = appearance.Copy() }); controller.Changed();
        }
        private void ShowDesigns()
        {
            if (controller.Store.State.Lines.Count == 0) { SetStatus("끈을 추가한 뒤 디자인을 설정해주세요."); return; }
            var window = new DesignWindow(controller) { Owner = this }; window.ShowDialog();
        }
        private void ShowMore()
        {
            var menu = new ContextMenu();
            var demos = new MenuItem { Header = "샘플 그림 모두 떼기", IsEnabled = controller.Store.State.Photos.Any(p => p.Demo >= 0) };
            demos.Click += delegate { controller.Store.State.Photos.RemoveAll(p => p.Demo >= 0); Scene.Selected = null; UpdateSelection(); controller.Changed(); }; menu.Items.Add(demos);
            var empty = new MenuItem { Header = "비어 있는 끈 정리" }; empty.Click += delegate { controller.Store.State.Lines.RemoveAll(l => !controller.Store.State.Photos.Any(p => p.LineId == l.Id)); controller.Changed(); }; menu.Items.Add(empty);
            var reset = new MenuItem { Header = "기본 배치로 되돌리기…" }; reset.Click += delegate
            {
                if (MessageBox.Show(this, "현재 배치를 기본 샘플 배치로 바꿀까요?\n추가한 사진 파일은 data/photos 폴더에 보관됩니다.", "배치 초기화", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                { var state = Arrangement.CreateDefault(); state.Monitor = controller.Store.State.Monitor; state.Visible = controller.Store.State.Visible; controller.Store.State = state; Scene.Selected = null; UpdateSelection(); controller.Changed(); }
            }; menu.Items.Add(reset); menu.Items.Add(new Separator());
            var exit = new MenuItem { Header = "프로그램 종료" }; exit.Click += delegate { controller.Exit(); }; menu.Items.Add(exit); menu.IsOpen = true;
        }
        public void SetStatus(string text) { status.Text = text; status.ToolTip = text; }
        public void UpdateCount() { count.Text = controller.Store.State.Photos.Count + "장의 사진  /  " + controller.Store.State.Lines.Count + "개의 끈"; UpdateLayers(); }
        private void UpdateLayers()
        {
            bool wasUpdating = updating; updating = true; layerPicker.Items.Clear();
            if (Scene.Selected == null) { var placeholder = new ComboBoxItem { Content = "사진 선택 · 숫자가 클수록 앞" }; layerPicker.Items.Add(placeholder); layerPicker.SelectedItem = placeholder; }
            for (int i = controller.Store.State.Photos.Count - 1; i >= 0; i--)
            {
                var photo = controller.Store.State.Photos[i]; string name = String.IsNullOrWhiteSpace(photo.Caption) ? "사진" : photo.Caption;
                string position = i == controller.Store.State.Photos.Count - 1 ? " · 맨 앞" : i == 0 ? " · 맨 뒤" : "";
                var item = new ComboBoxItem { Content = (i + 1) + " · " + name + position, Tag = photo }; layerPicker.Items.Add(item);
                if (Scene.Selected == photo) layerPicker.SelectedItem = item;
            }
            int index = controller.Store.State.Photos.IndexOf(Scene.Selected);
            layerBack.IsEnabled = layerBackward.IsEnabled = index > 0;
            layerFront.IsEnabled = layerForward.IsEnabled = index >= 0 && index < controller.Store.State.Photos.Count - 1;
            layerPicker.IsEnabled = controller.Store.State.Photos.Count > 0; updating = wasUpdating;
        }
        private void UpdateSelection()
        {
            updating = true; var p = Scene.Selected; bool enabled = p != null;
            caption.IsEnabled = enabled; size.IsEnabled = enabled; angle.IsEnabled = enabled; replaceButton.IsEnabled = enabled; removeButton.IsEnabled = enabled;
            selection.Text = enabled ? "사진 메모" : "사진을 선택하면 편집할 수 있어요";
            caption.Text = enabled ? p.Caption : ""; size.Value = enabled ? p.Width : 180; angle.Value = enabled ? p.Angle : 0;
            UpdateLayers();
            updating = false;
        }
        public void RefreshControls()
        {
            editButton.Content = controller.Editing ? "편집 잠그기" : "바탕화면에서 편집";
            editButton.Background = Scene.Brush(controller.Editing ? "#e8edde" : "#fffef8");
            visibilityButton.Content = controller.Store.State.Visible ? "사진 숨기기" : "사진 표시";
        }
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (controller.Exiting) return; e.Cancel = true; controller.SaveNow(); Hide();
        }
        public UIElement PreviewContent { get { return (UIElement)Content; } }
    }
}
