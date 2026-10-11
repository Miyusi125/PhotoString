using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace PhotoString
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--diagnose-desktop") return DesktopDiagnostics.Run(args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
                if (args.Length > 0 && args[0] == "--self-test") return Verification.Run(args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
                if (args.Length > 0 && args[0] == "--desktop-check") return Verification.CheckDesktop(args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
                if (args.Length > 0 && args[0] == "--app-check") return Verification.CheckApp(args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));
                bool created;
                using (var mutex = new Mutex(true, "Local\\PhotoStringDesktop", out created))
                {
                    if (!created) { MessageBox.Show("Photo String이 이미 실행 중입니다. 작업 표시줄의 숨겨진 아이콘에서 관리창을 열어주세요.", "Photo String"); return 0; }
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    var controller = new Controller(app, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"));
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), DateTime.Now.ToString("s") + " " + e.Exception + Environment.NewLine); } catch { }
                        MessageBox.Show("작업을 완료하지 못했습니다. 다시 시도해주세요.\n" + e.Exception.Message, "Photo String", MessageBoxButton.OK, MessageBoxImage.Warning); e.Handled = true;
                    };
                    controller.Start(); app.Run(); controller.Dispose();
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), ex.ToString()); } catch { }
                if (args.Length == 0) MessageBox.Show("프로그램을 시작하지 못했습니다.\n" + ex.Message, "Photo String", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
    public class Controller : IDisposable
    {
        public readonly Store Store;
        public MainWindow Manager;
        public DesktopWindow Desktop;
        public bool Editing;
        public bool Exiting;
        private readonly Application app;
        private Forms.NotifyIcon tray;
        private Forms.ToolStripMenuItem editingItem, visibleItem;
        private readonly DispatcherTimer saveTimer, shellTimer;
        public Controller(Application application, string root)
        {
            app = application; Store = new Store(root); Store.Load();
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };
            shellTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            shellTimer.Tick += delegate { CheckShell(); };
        }
        public void Start()
        {
            Manager = new MainWindow(this); app.MainWindow = Manager;
            CreateDesktop(); CreateTray(); shellTimer.Start(); Manager.Show();
            RegisterHotkey();
        }
        private void CreateDesktop()
        {
            Desktop = new DesktopWindow(Store);
            Desktop.Scene.Changed += Changed;
            Desktop.Scene.ReplaceRequested += Replace;
            Desktop.Show(); Desktop.SetEditing(Editing);
            if (!Store.State.Visible) Desktop.Hide();
        }
        private void RegisterHotkey()
        {
            var hwnd = new WindowInteropHelper(Manager).Handle; if (hwnd == IntPtr.Zero) return;
            if (Native.RegisterHotKey(hwnd, 1, 0x0002 | 0x0001 | 0x4000, 0x50))
            {
                HwndSource.FromHwnd(hwnd).AddHook(delegate(IntPtr handle, int msg, IntPtr wp, IntPtr lp, ref bool handled)
                { if (msg == 0x0312 && wp.ToInt32() == 1) { ShowManager(); handled = true; } return IntPtr.Zero; });
            }
            else Manager.SetStatus("Ctrl+Alt+P 단축키를 사용할 수 없습니다. 트레이 아이콘으로 관리창을 열어주세요.");
        }
        private void CreateTray()
        {
            tray = new Forms.NotifyIcon { Text = "Photo String · 나의 작은 사진 줄", Icon = Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location), Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("사진 줄 관리창 열기", null, delegate { ShowManager(); });
            menu.Items.Add("사진 추가…", null, delegate { ShowManager(); Manager.AddPhotos(); });
            editingItem = new Forms.ToolStripMenuItem("바탕화면에서 편집") { Checked = Editing };
            editingItem.Click += delegate { SetEditing(!Editing); }; menu.Items.Add(editingItem);
            visibleItem = new Forms.ToolStripMenuItem("사진 장식 표시") { Checked = Store.State.Visible };
            visibleItem.Click += delegate { ToggleVisibility(); }; menu.Items.Add(visibleItem);
            menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("종료", null, delegate { Exit(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowManager(); };
        }
        private void CheckShell()
        {
            if (Exiting) return;
            if (Desktop == null || !Native.IsWindow(Desktop.Handle))
            {
                try { if (Desktop != null) Desktop.Close(); } catch { }
                CreateDesktop();
            }
            else if (!Desktop.IsAttached || Native.Owner(Desktop.Handle) != Native.FindDesktopHost()) Desktop.Attach();
            else Desktop.PositionOnScreen();
            if (Desktop.LastError != null) Manager.SetStatus(Desktop.LastError);
        }
        public void ShowManager() { Manager.Show(); if (Manager.WindowState == WindowState.Minimized) Manager.WindowState = WindowState.Normal; Manager.Activate(); }
        public void SetEditing(bool value)
        {
            Editing = value; Desktop.SetEditing(value); if (editingItem != null) editingItem.Checked = value; Manager.RefreshControls();
            Manager.SetStatus(value ? "바탕화면에서 편집 중 · 사진과 끈을 드래그하세요. 끝나면 편집을 잠가주세요." : "바탕화면 편집을 잠갔습니다. 사진 위로 마우스 클릭이 통과합니다.");
        }
        public void ToggleVisibility()
        {
            Store.State.Visible = !Store.State.Visible;
            if (Store.State.Visible) { Desktop.Show(); Desktop.Attach(); } else Desktop.Hide();
            if (visibleItem != null) visibleItem.Checked = Store.State.Visible; Manager.RefreshControls(); Changed();
        }
        public void Changed()
        {
            Desktop.Refresh(); Manager.Scene.InvalidateVisual(); Manager.UpdateCount();
            saveTimer.Stop(); saveTimer.Start(); Manager.SetStatus("배치 저장 중…");
        }
        public bool SaveNow()
        {
            try { Store.Save(); Manager.SetStatus("배치 저장됨 · " + DateTime.Now.ToString("HH:mm")); return true; }
            catch (Exception ex) { Manager.SetStatus("저장 실패: " + ex.Message); return false; }
        }
        public bool ApplyAndRun()
        {
            saveTimer.Stop();
            bool wasVisible = Store.State.Visible;
            Store.State.Visible = true;
            if (!SaveNow()) { Store.State.Visible = wasVisible; Manager.RefreshControls(); return false; }
            CheckShell(); Desktop.Show(); Desktop.Attach(); Desktop.Refresh();
            if (!Desktop.IsAttached)
            {
                Manager.SetStatus(Desktop.LastError ?? "바탕화면에 연결하지 못했습니다. 다시 적용해주세요.");
                return false;
            }
            SetEditing(false);
            if (visibleItem != null) visibleItem.Checked = true;
            Manager.RefreshControls();
            Manager.SetStatus("바탕화면 실행 중 · 설정 변경은 Ctrl+Alt+P로 관리창을 열어주세요.");
            Manager.Hide();
            return true;
        }
        public void Replace(Photo photo) { ShowManager(); Manager.ReplacePhoto(photo); }
        public void Exit()
        {
            if (Exiting) return; Exiting = true; saveTimer.Stop(); shellTimer.Stop(); SaveNow();
            Native.UnregisterHotKey(new WindowInteropHelper(Manager).Handle, 1);
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            Desktop.Close(); Manager.Close(); app.Shutdown();
        }
        public void Dispose() { if (tray != null) tray.Dispose(); saveTimer.Stop(); shellTimer.Stop(); }
    }
}
