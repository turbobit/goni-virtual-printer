using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace GoniPdfPrinter
{
    static class Program
    {
        public const string PipeName = "GoniPdfPrinter";
        public const string PrinterName = "Goni PDF";

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("encrypt", StringComparison.OrdinalIgnoreCase))
                return Cli.Encrypt(args);
            if (args.Length > 0 && args[0].Equals("name", StringComparison.OrdinalIgnoreCase))
                return Cli.Name(args);

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0 && args[0].Equals("--settings", StringComparison.OrdinalIgnoreCase))
            {
                TrayApp.ShowSettings(null);
                return 0;
            }
            if (args.Length > 1 && args[0].Equals("--test-job", StringComparison.OrdinalIgnoreCase))
            {
                // 프린터 없이 동작 확인용: 기존 PDF를 인쇄 작업처럼 처리
                TrayApp.ProcessJob(File.ReadAllBytes(args[1]), null);
                return 0;
            }

            bool created;
            using (var mutex = new Mutex(true, @"Local\GoniPdfPrinter.Tray", out created))
            {
                if (!created) return 0; // 이미 실행 중
                Application.Run(new TrayApp());
            }
            return 0;
        }

        internal static class Cli
        {
            static void Out(string text)
            {
                byte[] b = System.Text.Encoding.UTF8.GetBytes(text + Environment.NewLine);
                using (var o = Console.OpenStandardOutput()) o.Write(b, 0, b.Length);
            }

            // 파일명 규칙 확인용:
            // GoniPdfPrinter.exe name --title T [--pattern P] [--folder F] [--now 2026-11-23T09:05:07] [--keep-app]
            //   [--conflict number|overwrite|ask]  → 최종 경로(폴더 지정 시) 또는 파일명 출력
            public static int Name(string[] a)
            {
                AttachConsole(-1);
                try
                {
                    string title = null, pattern = FileNaming.DefaultPattern, folder = null;
                    DateTime now = DateTime.Now;
                    bool strip = true;
                    var conflict = ConflictMode.AutoNumber;
                    for (int i = 1; i < a.Length; i++)
                    {
                        switch (a[i].ToLowerInvariant())
                        {
                            case "--title": title = a[++i]; break;
                            case "--pattern": pattern = a[++i]; break;
                            case "--folder": folder = a[++i]; break;
                            case "--now": now = DateTime.Parse(a[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                            case "--keep-app": strip = false; break;
                            case "--conflict":
                                string c = a[++i].ToLowerInvariant();
                                conflict = c == "overwrite" ? ConflictMode.Overwrite : c == "ask" ? ConflictMode.Ask : ConflictMode.AutoNumber;
                                break;
                            default: throw new ArgumentException("알 수 없는 옵션: " + a[i]);
                        }
                    }
                    if (folder == null)
                    {
                        Out(FileNaming.Expand(pattern, title, now, strip, 1) + ".pdf");
                        return 0;
                    }
                    string resolved = FileNaming.ResolveFolder(folder, now);
                    if (resolved == null) { Out("INVALID_FOLDER"); return 2; }
                    string name = FileNaming.HasCounter(pattern)
                        ? FileNaming.ExpandWithCounter(resolved, pattern, title, now, strip)
                        : FileNaming.Expand(pattern, title, now, strip, 1);
                    bool exists;
                    string path = FileNaming.BuildPath(resolved, name, conflict, out exists);
                    Out((exists ? "EXISTS " : "") + path);
                    return 0;
                }
                catch (Exception ex)
                {
                    Out("ERROR " + ex.Message);
                    return 1;
                }
            }

            // GoniPdfPrinter.exe encrypt <입력.pdf> <출력.pdf> [--open 암호] [--owner 암호]
            //   [--print none|low|high] [--edit 0-4] [--copy] [--no-access] [--aes128] [--no-meta]
            public static int Encrypt(string[] a)
            {
                AttachConsole(-1);
                try
                {
                    if (a.Length < 3) throw new ArgumentException("사용법: encrypt <입력.pdf> <출력.pdf> [옵션]");
                    var o = new SecurityOptions();
                    for (int i = 3; i < a.Length; i++)
                    {
                        switch (a[i].ToLowerInvariant())
                        {
                            case "--open": o.OpenPassword = a[++i]; break;
                            case "--owner": o.PermissionPassword = a[++i]; o.RestrictPermissions = true; break;
                            case "--print":
                                string p = a[++i].ToLowerInvariant();
                                o.Print = p == "high" ? PrintPermission.HighResolution : p == "low" ? PrintPermission.LowResolution : PrintPermission.None;
                                break;
                            case "--edit": o.Edit = (EditPermission)int.Parse(a[++i]); break;
                            case "--copy": o.AllowCopy = true; break;
                            case "--no-access": o.AllowAccessibility = false; break;
                            case "--aes128": o.Level = EncryptionLevel.Aes128; break;
                            case "--no-meta": o.EncryptMetadata = false; break;
                            default: throw new ArgumentException("알 수 없는 옵션: " + a[i]);
                        }
                    }
                    File.WriteAllBytes(a[2], PdfEncryptor.Encrypt(File.ReadAllBytes(a[1]), o));
                    Console.WriteLine("OK " + a[2]);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("ERROR " + ex.Message);
                    return 1;
                }
            }
        }
    }

    /// <summary>트레이 상주 앱: 프린터 포트(명명된 파이프)로 들어온 PDF를 받아 처리</summary>
    sealed class TrayApp : ApplicationContext
    {
        readonly NotifyIcon _tray;
        readonly PipeListener _listener;
        readonly Control _ui;
        readonly Queue<byte[]> _jobs = new Queue<byte[]>();
        bool _busy;
        string _lastSaved;

        public TrayApp()
        {
            _ui = new Control();
            _ui.CreateControl();
            var h = _ui.Handle; // UI 스레드 핸들 확보

            var menu = new ContextMenuStrip();
            menu.Items.Add("기본 보안 설정...", null, (o, e) => ShowSettings(null));
            menu.Items.Add("저장 폴더 열기", null, (o, e) => OpenFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, (o, e) => ExitThread());

            _tray = new NotifyIcon
            {
                Icon = AppIcon.Get(),
                Text = "Goni PDF 가상 프린터",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.DoubleClick += (o, e) => ShowSettings(null);
            _tray.BalloonTipClicked += (o, e) =>
            {
                if (_lastSaved != null && File.Exists(_lastSaved))
                    Process.Start("explorer.exe", "/select,\"" + _lastSaved + "\"");
            };

            _listener = new PipeListener(Program.PipeName);
            _listener.JobReceived += data => _ui.BeginInvoke((Action)(() => Enqueue(data)));
            _listener.Error += msg => _ui.BeginInvoke((Action)(() =>
                _tray.ShowBalloonTip(5000, "Goni PDF", msg, ToolTipIcon.Error)));
            _listener.Start();
        }

        void Enqueue(byte[] data)
        {
            _jobs.Enqueue(data);
            if (_busy) return;
            _busy = true;
            try
            {
                while (_jobs.Count > 0)
                {
                    string saved = ProcessJob(_jobs.Dequeue(), _tray);
                    if (saved != null) _lastSaved = saved;
                }
            }
            finally { _busy = false; }
        }

        /// <summary>인쇄 작업 1건 처리. 저장된 경로 또는 null.</summary>
        public static string ProcessJob(byte[] pdf, NotifyIcon tray)
        {
            if (pdf.Length < 5 || pdf[0] != '%' || pdf[1] != 'P' || pdf[2] != 'D' || pdf[3] != 'F')
            {
                Notify(tray, "PDF가 아닌 인쇄 데이터를 받았습니다. 프린터 드라이버가 'Microsoft Print To PDF'인지 확인하세요.", true);
                return null;
            }

            string title = null;
            try { title = new PdfDocument(pdf).GetTitle(); } catch { }

            var settings = AppSettings.Load();
            DateTime now = DateTime.Now;
            string path = null;
            bool showDialog = settings.SkipDialog == false || settings.ToSecurityOptions().Validate() != null
                              || (settings.RequireOpenPassword && settings.OpenPassword.Length == 0);

            while (true)
            {
                if (showDialog)
                {
                    using (var f = new SecurityForm(settings, true, title))
                    {
                        if (f.ShowDialog() != DialogResult.OK) return null;
                        settings = f.Result;
                        path = f.OutputPath;
                    }
                }
                else
                {
                    path = AutoPath(settings, title, now, tray);
                    if (path == null) { showDialog = true; continue; }
                }

                try
                {
                    byte[] output = PdfEncryptor.Encrypt(pdf, settings.ToSecurityOptions());
                    FileNaming.WriteAtomic(path, output);
                    break;
                }
                catch (Exception ex)
                {
                    string nl = Environment.NewLine;
                    MessageBox.Show("PDF 저장 중 오류가 발생했습니다." + nl + nl + path + nl + nl + ex.Message, "Goni PDF",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    showDialog = true; // 다시 설정 창으로
                }
            }

            settings.SkipEncryptionOnce = false;
            settings.LastFolder = Path.GetDirectoryName(path);
            try { settings.Save(); } catch { }

            Notify(tray, "저장했습니다: " + path, false);
            if (settings.OpenAfterSave)
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
            }
            return path;
        }

        /// <summary>창 없이 저장할 경로. 사용자 확인이 필요하면 null.</summary>
        static string AutoPath(AppSettings settings, string title, DateTime now, NotifyIcon tray)
        {
            string folder = settings.ResolveDefaultFolder(now);
            string err = FileNaming.EnsureWritable(folder);
            if (err != null)
            {
                // 기본 폴더(예: 분리된 USB/네트워크 드라이브)를 쓸 수 없으면 문서 폴더로
                string fallback = FileNaming.DefaultFolder;
                if (FileNaming.EnsureWritable(fallback) != null) return null;
                Notify(tray, "저장 폴더를 사용할 수 없어 문서 폴더에 저장합니다." + Environment.NewLine + folder + " - " + err, true);
                folder = fallback;
            }
            string name = FileNaming.HasCounter(settings.FileNamePattern)
                ? FileNaming.ExpandWithCounter(folder, settings.FileNamePattern, title, now, settings.StripAppName)
                : FileNaming.Expand(settings.FileNamePattern, title, now, settings.StripAppName, 1);
            bool exists;
            string path = FileNaming.BuildPath(folder, name, settings.Conflict, out exists);
            return exists ? null : path; // "묻기" 모드에서 같은 이름이 있으면 창을 띄움
        }

        static void Notify(NotifyIcon tray, string msg, bool error)
        {
            if (tray != null) tray.ShowBalloonTip(4000, "Goni PDF", msg, error ? ToolTipIcon.Error : ToolTipIcon.Info);
            else if (error) MessageBox.Show(msg, "Goni PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static bool _settingsOpen;

        public static void ShowSettings(IWin32Window owner)
        {
            if (_settingsOpen) return;
            _settingsOpen = true;
            try
            {
                using (var f = new SecurityForm(AppSettings.Load(), false, null))
                {
                    if (f.ShowDialog(owner) == DialogResult.OK)
                    {
                        try { f.Result.Save(); }
                        catch (Exception ex) { MessageBox.Show("설정을 저장하지 못했습니다.\n" + ex.Message, "Goni PDF"); }
                    }
                }
            }
            finally { _settingsOpen = false; }
        }

        static void OpenFolder()
        {
            var s = AppSettings.Load();
            string folder = FileNaming.ResolveFolder(s.LastFolder, DateTime.Now);
            if (folder == null || !Directory.Exists(folder)) folder = s.ResolveDefaultFolder(DateTime.Now);
            if (Directory.Exists(folder)) Process.Start("explorer.exe", "\"" + folder + "\"");
        }

        protected override void ExitThreadCore()
        {
            _listener.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            base.ExitThreadCore();
        }
    }

    /// <summary>
    /// 스풀러가 로컬 포트(\\.\pipe\GoniPdfPrinter)로 쓰는 인쇄 데이터를 수신.
    /// 연결 1건 = 인쇄 작업 1건 (스풀러가 작업 끝에 핸들을 닫음).
    /// </summary>
    sealed class PipeListener
    {
        readonly string _name;
        volatile bool _stop;
        Thread _thread;
        public event Action<byte[]> JobReceived;
        public event Action<string> Error;

        public PipeListener(string name) { _name = name; }

        public void Start()
        {
            _thread = new Thread(Loop) { IsBackground = true, Name = "PipeListener" };
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            try
            {
                // 대기 중인 WaitForConnection 해제
                using (var c = new NamedPipeClientStream(".", _name, PipeDirection.Out)) c.Connect(200);
            }
            catch { }
        }

        NamedPipeServerStream Create()
        {
            var sec = new PipeSecurity();
            sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            return new NamedPipeServerStream(_name, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.None, 1 << 16, 1 << 16, sec);
        }

        void Loop()
        {
            int failures = 0;
            while (!_stop)
            {
                NamedPipeServerStream server;
                try { server = Create(); failures = 0; }
                catch (Exception ex)
                {
                    if (++failures == 1 && Error != null) Error("프린터 포트를 열 수 없습니다: " + ex.Message);
                    Thread.Sleep(3000);
                    continue;
                }
                try { server.WaitForConnection(); }
                catch { server.Dispose(); continue; }
                if (_stop) { server.Dispose(); break; }
                var s = server;
                ThreadPool.QueueUserWorkItem(_ => Receive(s));
            }
        }

        void Receive(NamedPipeServerStream s)
        {
            try
            {
                using (s)
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    if (ms.Length > 0 && JobReceived != null) JobReceived(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                if (Error != null) Error("인쇄 데이터 수신 실패: " + ex.Message);
            }
        }
    }

    static class AppIcon
    {
        static Icon _icon;

        public static Icon Get()
        {
            if (_icon != null) return _icon;
            try
            {
                var asm = typeof(AppIcon).Assembly;
                _icon = Icon.ExtractAssociatedIcon(asm.Location);
            }
            catch { }
            if (_icon == null)
            {
                using (var bmp = new Bitmap(32, 32))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(200, 30, 40)), 2, 2, 28, 28);
                    g.DrawString("PDF", new Font("Arial", 8F, FontStyle.Bold), Brushes.White, 2, 9);
                    _icon = Icon.FromHandle(bmp.GetHicon());
                }
            }
            return _icon;
        }
    }
}
