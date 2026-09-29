using System.Net;
using System.Text.RegularExpressions;
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;

namespace MedicalTextExpander {
    public class TemplateItem {
        public string Shortcut { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public string Content { get; set; }

        public TemplateItem(string shortcut, string title, string category, string content) {
            Shortcut = shortcut;
            Title = title;
            Category = category;
            Content = content;
        }
    }

    public class Program {
        private const string MutexName = "Medical_Text_Expander_SingleInstance_Mutex";
        private const string EventName = "Medical_Text_Expander_ShowPalette_Event";

        [STAThread]
        public static void Main() {
            bool createdNew = false;
            Mutex mutex = null;

            try {
                mutex = new Mutex(true, MutexName, out createdNew);
            } catch (AbandonedMutexException) {
                createdNew = true;
            } catch {
                createdNew = false;
            }

            if (!createdNew) {
                // หากโปรแกรมเปิดทำงานอยู่แล้ว ให้ส่งสัญญาณปลุกอินสแตนซ์เดิม แล้วจบการทำงานทันที
                try {
                    using (EventWaitHandle activateEvent = EventWaitHandle.OpenExisting(EventName)) {
                        activateEvent.Set();
                    }
                } catch {}
                return;
            }

            try {
                using (EventWaitHandle activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName)) {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    ExpanderContext context = null;
                    try {
                        context = new ExpanderContext();
                    } catch (Exception exInit) {
                        try { File.WriteAllText(@"C:\PhisApp\Medical_Text_Expander\crash.log", exInit.ToString()); } catch {}
                        MessageBox.Show("ข้อผิดพลาดในการเริ่มต้นโปรแกรม:\n" + exInit.ToString(), "Medical Expander Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // Background thread สำหรับรอรับสัญญาณเมื่อผู้ใช้พยายามกดเปิดโปรแกรมซ้ำ
                    Thread listenerThread = new Thread(() => {
                        while (true) {
                            try {
                                if (activateEvent.WaitOne()) {
                                    context.ActivateFromOtherInstance();
                                }
                            } catch {
                                break;
                            }
                        }
                    });
                    listenerThread.IsBackground = true;
                    listenerThread.Start();

                    Application.Run(context);
                }
            } catch (Exception exApp) {
                try { File.WriteAllText(@"C:\PhisApp\Medical_Text_Expander\crash.log", exApp.ToString()); } catch {}
                MessageBox.Show("ข้อผิดพลาดของโปรแกรม:\n" + exApp.ToString(), "Medical Expander Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } finally {
                if (mutex != null) {
                    try {
                        mutex.ReleaseMutex();
                    } catch {}
                    mutex.Close();
                }
            }
        }
    }

    public class BedHistoryItem {
        public int BedNum { get; set; }
        public DateTime Timestamp { get; set; }
        public string Reason { get; set; }
        public string Content { get; set; }
        public int CharCount { get { return Content != null ? Content.Length : 0; } }
        public string DisplayText {
            get {
                int thaiYear = Timestamp.Year + 543;
                string dt = string.Format("{0:D2}/{1:D2}/{2} {3:D2}:{4:D2}:{5:D2}", 
                    Timestamp.Day, Timestamp.Month, thaiYear, Timestamp.Hour, Timestamp.Minute, Timestamp.Second);
                return string.Format("{0} [{1}] ({2} ตัวอักษร)", dt, Reason, CharCount);
            }
        }
    }

    
    // =========================================================================
    // ระบบตรวจสอบและอัปเดตโปรแกรมอัตโนมัติผ่าน GitHub (Auto-Updater)
    // =========================================================================
    public class UpdateInfo {
        public string Version { get; set; }
        public string ReleaseDate { get; set; }
        public string Changelog { get; set; }
        public string DownloadUrl { get; set; }
    }

    public static class AppUpdater {
        public const string CurrentVersion = "1.2.1";
        public const string DefaultGitHubRepo = "oatzilla/Medical_Text_Expander";

        public static void CheckForUpdatesAsync(string repo, bool isManual, Form parent = null, string token = null) {
            ThreadPool.QueueUserWorkItem(_ => {
                CheckForUpdatesInternal(repo, isManual, parent, token);
            });
        }

        private static void CheckForUpdatesInternal(string repo, bool isManual, Form parent, string token) {
            if (string.IsNullOrEmpty(repo)) repo = DefaultGitHubRepo;
            string url = string.Format("https://raw.githubusercontent.com/{0}/main/version.json", repo.Trim());

            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string json = "";
                using (WebClient client = new WebClient()) {
                    client.Encoding = Encoding.UTF8;
                    client.Headers["User-Agent"] = "MedicalTextExpander-AutoUpdater";
                    json = client.DownloadString(url);
                }

                UpdateInfo info = ParseVersionJson(json);
                if (info == null || string.IsNullOrEmpty(info.Version)) {
                    if (isManual) {
                        ShowMessage(parent, "ไม่สามารถอ่านข้อมูลเวอร์ชันจาก GitHub ได้ กรุณาตรวจสอบชื่อ Repository ในการตั้งค่า", "ตรวจสอบการอัปเดต", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }

                if (IsNewerVersion(info.Version, CurrentVersion)) {
                    Action promptAction = () => {
                        string msg = string.Format("🎉 พบการอัปเดตเวอร์ชันใหม่!\n\n" +
                                                   "เวอร์ชันปัจจุบัน: v{0}\n" +
                                                   "เวอร์ชันใหม่ล่าสุด: v{1} ({2})\n\n" +
                                                   "รายละเอียดการอัปเดต:\n{3}\n\n" +
                                                   "คุณต้องการดาวน์โหลดและติดตั้งอัปเดตเดี๋ยวนี้หรือไม่?",
                                                   CurrentVersion, info.Version, info.ReleaseDate ?? "", info.Changelog ?? "- ปรับปรุงประสิทธิภาพและการทำงาน");
                        
                        DialogResult dr = MessageBox.Show(parent, msg, "มีเวอร์ชันใหม่พร้อมให้อัปเดต", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (dr == DialogResult.Yes) {
                            DownloadAndApplyUpdate(info.DownloadUrl, parent, token);
                        }
                    };

                    if (parent != null && parent.InvokeRequired) {
                        parent.BeginInvoke(promptAction);
                    } else {
                        promptAction();
                    }
                } else {
                    if (isManual) {
                        ShowMessage(parent, string.Format("คุณกำลังใช้งานเวอร์ชันล่าสุดแล้ว (v{0})", CurrentVersion), "ตรวจสอบการอัปเดต", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            } catch (Exception ex) {
                if (isManual) {
                    ShowMessage(parent, "ไม่สามารถเชื่อมต่อกับ GitHub ได้:\n" + ex.Message + "\n\n(กรุณาตรวจสอบการเชื่อมต่ออินเทอร์เน็ตหรือชื่อ Repository ในการตั้งค่า)", "ข้อผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static bool IsNewerVersion(string remote, string current) {
            try {
                Version vRemote = new Version(NormalizeVersion(remote));
                Version vCurrent = new Version(NormalizeVersion(current));
                return vRemote > vCurrent;
            } catch {
                return string.Compare(remote, current, StringComparison.OrdinalIgnoreCase) > 0;
            }
        }

        private static string NormalizeVersion(string v) {
            v = v.TrimStart('v', 'V').Trim();
            string[] parts = v.Split('.');
            if (parts.Length == 1) return v + ".0.0";
            if (parts.Length == 2) return v + ".0";
            return v;
        }

        private static UpdateInfo ParseVersionJson(string json) {
            try {
                UpdateInfo info = new UpdateInfo();
                info.Version = ExtractJsonValue(json, "version");
                info.ReleaseDate = ExtractJsonValue(json, "releaseDate");
                info.Changelog = ExtractJsonValue(json, "changelog");
                info.DownloadUrl = ExtractJsonValue(json, "downloadUrl");
                return info;
            } catch {
                return null;
            }
        }

        private static string ExtractJsonValue(string json, string key) {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"(.*?)\"", RegexOptions.Singleline);
            if (m.Success) {
                return Regex.Unescape(m.Groups[1].Value);
            }
            return "";
        }

        private static void DownloadAndApplyUpdate(string downloadUrl, Form parent, string token) {
            if (string.IsNullOrEmpty(downloadUrl)) {
                ShowMessage(parent, "ไม่พบที่อยู่ดาวน์โหลดของไฟล์อัปเดต", "ข้อผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Form progressForm = new Form();
            progressForm.Text = "กำลังดาวน์โหลดอัปเดต...";
            progressForm.Size = new System.Drawing.Size(380, 140);
            progressForm.StartPosition = FormStartPosition.CenterScreen;
            progressForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            progressForm.MaximizeBox = false;
            progressForm.MinimizeBox = false;
            progressForm.ControlBox = false;
            progressForm.TopMost = true;

            Label lbl = new Label() {
                Text = "กำลังดาวน์โหลดไฟล์อัปเดตจาก GitHub กรุณารอสักครู่...",
                Location = new System.Drawing.Point(20, 20),
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 9f)
            };
            ProgressBar pb = new ProgressBar() {
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(325, 23),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };
            progressForm.Controls.Add(lbl);
            progressForm.Controls.Add(pb);
            progressForm.Show();

            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    string currentExePath = Application.ExecutablePath;
                    string currentDir = Path.GetDirectoryName(currentExePath);
                    string tempExePath = Path.Combine(currentDir, "Medical_Text_Expander_New.exe");
                    string updaterBatPath = Path.Combine(currentDir, "update_runner.bat");

                    using (WebClient client = new WebClient()) {
                        client.Headers["User-Agent"] = "MedicalTextExpander-AutoUpdater";
                        client.DownloadFile(downloadUrl, tempExePath);
                    }

                    FileInfo fi = new FileInfo(tempExePath);
                    if (fi.Length < 50000) {
                        throw new Exception("ไฟล์ที่ดาวน์โหลดมามีขนาดเล็กเกินไป (" + fi.Length + " bytes) อาจเป็นหน้าเว็บ Error ของ GitHub");
                    }

                    StringBuilder bat = new StringBuilder();
                    bat.AppendLine("@echo off");
                    bat.AppendLine("timeout /t 1 /nobreak >nul");
                    bat.AppendLine(string.Format("copy /y \"{0}\" \"{1}\" >nul", tempExePath, currentExePath));
                    bat.AppendLine(string.Format("del \"{0}\" >nul 2>&1", tempExePath));
                    bat.AppendLine(string.Format("start \"\" \"{0}\"", currentExePath));
                    bat.AppendLine("(goto) 2>nul & del \"%~f0\"");
                    File.WriteAllText(updaterBatPath, bat.ToString(), Encoding.Default);

                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = updaterBatPath;
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    Process.Start(psi);

                    Application.Exit();
                } catch (Exception ex) {
                    Action errAction = () => {
                        progressForm.Close();
                        ShowMessage(parent, "การดาวน์โหลดอัปเดตล้มเหลว:\n" + ex.Message, "ข้อผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    };
                    if (progressForm.InvokeRequired) {
                        progressForm.BeginInvoke(errAction);
                    } else {
                        errAction();
                    }
                }
            });
        }

        private static void ShowMessage(Form parent, string text, string title, MessageBoxButtons btn, MessageBoxIcon icon) {
            Action act = () => MessageBox.Show(parent, text, title, btn, icon);
            if (parent != null && parent.InvokeRequired) {
                parent.BeginInvoke(act);
            } else {
                act();
            }
        }
    }

    public class BedNotesManager {
        private string localDir;
        private string sharedDir;
        private string historyDir;
        private FileSystemWatcher sharedWatcher;
        private Dictionary<int, string> cache = new Dictionary<int, string>();
        private object syncLock = new object();
        public event Action<int, string> OnBedChanged;

        // Optimized: Background network share status caching to avoid UI thread blocking
        private volatile bool isSharedActiveCached = false;
        private System.Threading.Timer networkStatusTimer;

        public BedNotesManager(string localPath, string sharedPath) {
            localDir = localPath;
            sharedDir = sharedPath;
            historyDir = Path.Combine(localDir, "history");
            EnsureDirectories();
            CheckSharedDirectoryStatus();
            LoadAll();
            SetupWatcher();

            // Periodic background check of network status (every 20s) without blocking UI thread
            networkStatusTimer = new System.Threading.Timer(_ => CheckSharedDirectoryStatus(), null, 15000, 20000);
        }

        private void CheckSharedDirectoryStatus() {
            try {
                if (string.IsNullOrEmpty(sharedDir)) {
                    isSharedActiveCached = false;
                } else {
                    isSharedActiveCached = Directory.Exists(sharedDir);
                }
            } catch {
                isSharedActiveCached = false;
            }
        }

        public void UpdateSharedPath(string newSharedPath) {
            sharedDir = newSharedPath;
            ThreadPool.QueueUserWorkItem(_ => {
                CheckSharedDirectoryStatus();
                EnsureDirectories();
                SetupWatcher();
                SyncFromShared();
            });
        }

        private void EnsureDirectories() {
            try {
                if (!Directory.Exists(localDir)) {
                    Directory.CreateDirectory(localDir);
                }
                if (!Directory.Exists(historyDir)) {
                    Directory.CreateDirectory(historyDir);
                }
            } catch {}
            if (!string.IsNullOrEmpty(sharedDir) && isSharedActiveCached) {
                try {
                    if (!Directory.Exists(sharedDir)) {
                        Directory.CreateDirectory(sharedDir);
                    }
                } catch {}
            }
        }

        public static readonly Encoding SafeUtf8 = new UTF8Encoding(true);

        public static string ReadFileSafe(string path) {
            if (!File.Exists(path)) return "";
            try {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0) return "";
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) {
                    return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                }
                try {
                    UTF8Encoding strictUtf8 = new UTF8Encoding(false, true);
                    return strictUtf8.GetString(bytes);
                } catch {
                    try {
                        return Encoding.GetEncoding(874).GetString(bytes);
                    } catch {
                        return Encoding.Default.GetString(bytes);
                    }
                }
            } catch {
                return "";
            }
        }

        public static void WriteFileSafe(string path, string content) {
            try {
                File.WriteAllText(path, content ?? "", SafeUtf8);
            } catch {}
        }

        public void LoadAll() {
            lock (syncLock) {
                cache.Clear();
                SyncFromShared();

                for (int i = 1; i <= 30; i++) {
                    string file = GetLocalFilePath(i);
                    if (File.Exists(file)) {
                        cache[i] = ReadFileSafe(file);
                    } else {
                        cache[i] = "";
                    }
                }
            }
        }

        public void SyncFromShared() {
            if (string.IsNullOrEmpty(sharedDir) || !Directory.Exists(sharedDir)) return;
            try {
                for (int i = 1; i <= 30; i++) {
                    string sharedFile = GetSharedFilePath(i);
                    string localFile = GetLocalFilePath(i);
                    if (File.Exists(sharedFile)) {
                        bool shouldCopy = false;
                        if (!File.Exists(localFile)) {
                            shouldCopy = true;
                        } else {
                            DateTime sharedTime = File.GetLastWriteTimeUtc(sharedFile);
                            DateTime localTime = File.GetLastWriteTimeUtc(localFile);
                            if (sharedTime > localTime) {
                                shouldCopy = true;
                            }
                        }
                        if (shouldCopy) {
                            try { File.Copy(sharedFile, localFile, true); } catch {}
                        }
                    }
                }
            } catch {}
        }

        private void SetupWatcher() {
            if (sharedWatcher != null) {
                try {
                    sharedWatcher.EnableRaisingEvents = false;
                    sharedWatcher.Dispose();
                } catch {}
                sharedWatcher = null;
            }

            if (!string.IsNullOrEmpty(sharedDir) && Directory.Exists(sharedDir)) {
                try {
                    sharedWatcher = new FileSystemWatcher(sharedDir, "bed_*.txt");
                    sharedWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                    sharedWatcher.Changed += (s, e) => HandleFileChanged(e.FullPath);
                    sharedWatcher.Created += (s, e) => HandleFileChanged(e.FullPath);
                    sharedWatcher.EnableRaisingEvents = true;
                } catch {}
            }
        }

        private void HandleFileChanged(string fullPath) {
            Thread.Sleep(300); // Wait for file write to complete
            int bedNum = ExtractBedNumber(Path.GetFileName(fullPath));
            if (bedNum >= 1 && bedNum <= 30) {
                string content = ReadFileSafe(fullPath);
                string localFile = GetLocalFilePath(bedNum);
                WriteFileSafe(localFile, content);
                lock (syncLock) {
                    cache[bedNum] = content;
                }
                if (OnBedChanged != null) {
                    OnBedChanged(bedNum, content);
                }
            }
        }

        private int ExtractBedNumber(string fileName) {
            try {
                string name = Path.GetFileNameWithoutExtension(fileName).ToLower();
                if (name.StartsWith("bed_")) {
                    int n;
                    if (int.TryParse(name.Substring(4), out n)) {
                        return n;
                    }
                }
            } catch {}
            return -1;
        }

        public string GetBedNote(int bedNum) {
            lock (syncLock) {
                string val;
                if (cache.TryGetValue(bedNum, out val)) {
                    return val;
                }
                return "";
            }
        }

        public void SaveBedNote(int bedNum, string content) {
            lock (syncLock) {
                cache[bedNum] = content;
            }

            // 1. Save local cache (UTF-8 with BOM) - Fast local disk I/O (< 1ms)
            string localFile = GetLocalFilePath(bedNum);
            WriteFileSafe(localFile, content);

            // 2. Save to network share asynchronously in background thread
            // Never freeze the UI thread waiting for LAN/SMB!
            string sDir = sharedDir;
            if (isSharedActiveCached && !string.IsNullOrEmpty(sDir)) {
                ThreadPool.QueueUserWorkItem(_ => {
                    try {
                        string sharedFile = GetSharedFilePath(bedNum);
                        WriteFileSafe(sharedFile, content);
                    } catch {}
                });
            }
        }

        public void ClearBedNote(int bedNum) {
            string cur = GetBedNote(bedNum);
            if (!string.IsNullOrEmpty(cur) && !string.IsNullOrEmpty(cur.Trim())) {
                SaveHistorySnapshot(bedNum, "ก่อนล้างข้อมูลเตียง (Clear)", cur);
            }
            SaveBedNote(bedNum, "");
        }

        public void SaveHistorySnapshot(int bedNum, string reason, string content) {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(content.Trim())) return;
            try {
                if (!Directory.Exists(historyDir)) Directory.CreateDirectory(historyDir);
                string histFile = Path.Combine(historyDir, string.Format("bed_{0:D2}_history.txt", bedNum));
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== SNAPSHOT_START ===");
                sb.AppendLine("Timestamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("Reason=" + (reason ?? "บันทึกข้อมูล"));
                sb.AppendLine("=== CONTENT ===");
                sb.AppendLine(content.TrimEnd());
                sb.AppendLine("=== SNAPSHOT_END ===");
                File.AppendAllText(histFile, sb.ToString(), SafeUtf8);
            } catch {}
        }

        public List<BedHistoryItem> GetBedHistory(int bedNum) {
            List<BedHistoryItem> list = new List<BedHistoryItem>();
            string histFile = Path.Combine(historyDir, string.Format("bed_{0:D2}_history.txt", bedNum));
            if (!File.Exists(histFile)) return list;

            try {
                string text = ReadFileSafe(histFile);
                string[] blocks = text.Split(new string[] { "=== SNAPSHOT_START ===" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string block in blocks) {
                    int endIdx = block.IndexOf("=== SNAPSHOT_END ===");
                    string b = (endIdx >= 0 ? block.Substring(0, endIdx) : block).Trim();
                    if (string.IsNullOrEmpty(b)) continue;

                    DateTime ts = DateTime.Now;
                    string reason = "บันทึกข้อมูล";
                    string content = "";

                    using (StringReader sr = new StringReader(b)) {
                        string line;
                        bool inContent = false;
                        StringBuilder contentSb = new StringBuilder();
                        while ((line = sr.ReadLine()) != null) {
                            if (!inContent) {
                                if (line.StartsWith("Timestamp=")) {
                                    DateTime dt;
                                    if (DateTime.TryParse(line.Substring(10).Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt)) {
                                        ts = dt;
                                    }
                                } else if (line.StartsWith("Reason=")) {
                                    reason = line.Substring(7).Trim();
                                } else if (line.StartsWith("=== CONTENT ===")) {
                                    inContent = true;
                                }
                            } else {
                                contentSb.AppendLine(line);
                            }
                        }
                        content = contentSb.ToString().TrimEnd();
                    }

                    if (!string.IsNullOrEmpty(content)) {
                        BedHistoryItem item = new BedHistoryItem();
                        item.BedNum = bedNum;
                        item.Timestamp = ts;
                        item.Reason = reason;
                        item.Content = content;
                        list.Add(item);
                    }
                }
            } catch {}

            list.Reverse(); // Newest first
            return list;
        }

        public string GetLocalFilePath(int bedNum) {
            return Path.Combine(localDir, string.Format("bed_{0:D2}.txt", bedNum));
        }

        public string GetSharedFilePath(int bedNum) {
            return Path.Combine(sharedDir, string.Format("bed_{0:D2}.txt", bedNum));
        }

        public bool HasNote(int bedNum) {
            lock (syncLock) {
                string s;
                if (cache.TryGetValue(bedNum, out s)) {
                    return !string.IsNullOrWhiteSpace(s);
                }
                return false;
            }
        }

        public string GetPreview(int bedNum) {
            string note = GetBedNote(bedNum).Trim();
            if (string.IsNullOrEmpty(note)) return "(เตียงว่าง - ยังไม่มีข้อมูล)";
            string[] lines = note.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return "(เตียงว่าง)";
            if (lines.Length == 1) return lines[0];
            return lines[0] + " | " + lines[1];
        }

        public string LocalDir { get { return localDir; } }
        public string SharedDir { get { return sharedDir; } }
        public bool IsSharedActive { get { return isSharedActiveCached; } }
    }

    public class ExpanderContext : ApplicationContext {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private PaletteForm paletteForm;
        private BedNotesForm bedNotesForm;
        private ClinicalCalculatorForm calculatorForm;
        public BedNotesForm BedNotesFormInstance { get { return bedNotesForm; } }
        public ClinicalCalculatorForm CalculatorFormInstance { get { return calculatorForm; } }
        private WardReminderManager reminderManager;
        public WardReminderManager ReminderManager { get { return reminderManager; } }
        private WardReminderStickyForm stickyReminderForm;
        public WardReminderStickyForm StickyReminderFormInstance { get { return stickyReminderForm; } }
        private BedHistoryViewerForm historyViewerForm;
        private IntPtr hookId = IntPtr.Zero;
        private LowLevelKeyboardProc hookProc;
        private SynchronizationContext syncContext;

        private StringBuilder typedBuffer = new StringBuilder();
        private List<TemplateItem> templates = new List<TemplateItem>();
        private string appBaseDir;
        private string localConfigPath;
        private string settingsIniPath;
        private string sharedConfigPath = "";
        private string localBedNotesDir;
        private string sharedBedNotesDir = "";
        private string gitHubRepo = AppUpdater.DefaultGitHubRepo;
        private string gitHubToken = "";
        public string GetGitHubToken() { return gitHubToken; }
        public void SetGitHubToken(string token) { gitHubToken = token; SaveConfigFile(); }
        public string GetGitHubRepo() { return string.IsNullOrEmpty(gitHubRepo) ? AppUpdater.DefaultGitHubRepo : gitHubRepo; }
        public void SetGitHubRepo(string repo) { gitHubRepo = repo; SaveConfigFile(); }
        private FileSystemWatcher watcher = null;
        private string iconPath;
        private bool isEnabled = true;
        private float currentFontSize = 13.0f;
        public float CurrentFontSize { get { return currentFontSize; } set { currentFontSize = value; } }

        private BedNotesManager bedNotesManager;
        public BedNotesManager BedNotesManager { get { return bedNotesManager; } }

        // Win32 Keyboard Hook
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const byte VK_BACK = 0x08;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_V = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public ExpanderContext() {
            syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            InitializePaths();
            LoadSettings();

            bedNotesManager = new BedNotesManager(localBedNotesDir, sharedBedNotesDir);
            reminderManager = new WardReminderManager(appBaseDir, localBedNotesDir);
            reminderManager.OnReminderDue += HandleReminderDue;

            LoadTemplates();
            // Silent background update check 8 seconds after startup
            System.Threading.Timer updateCheckTimer = new System.Threading.Timer(_ => {
                AppUpdater.CheckForUpdatesAsync(GetGitHubRepo(), false, null, GetGitHubToken());
            }, null, 8000, Timeout.Infinite);
            InitializeTray();

            paletteForm = new PaletteForm(this);
            bedNotesForm = new BedNotesForm(this, bedNotesManager);
            stickyReminderForm = new WardReminderStickyForm(this, reminderManager);

            hookProc = HookCallback;
            using (Process curProc = Process.GetCurrentProcess())
            using (ProcessModule curMod = curProc.MainModule) {
                hookId = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(curMod.ModuleName), 0);
            }
        }

        private void InitializePaths() {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string candidateDir = baseDir;

            if (!File.Exists(Path.Combine(candidateDir, "medical_templates.txt"))) {
                string subDir = @"C:\PhisApp\Medical_Text_Expander";
                if (File.Exists(Path.Combine(subDir, "medical_templates.txt"))) {
                    candidateDir = subDir;
                } else if (File.Exists(@"C:\PhisApp\medical_templates.txt")) {
                    candidateDir = @"C:\PhisApp";
                }
            }

            appBaseDir = candidateDir;
            localConfigPath = Path.Combine(candidateDir, "medical_templates.txt");
            settingsIniPath = Path.Combine(candidateDir, "expander_settings.ini");
            iconPath = Path.Combine(candidateDir, "medical_expander_icon.ico");
            localBedNotesDir = Path.Combine(candidateDir, "bed_notes");

            try {
                if (!Directory.Exists(localBedNotesDir)) {
                    Directory.CreateDirectory(localBedNotesDir);
                }
            } catch {}
        }

        public void ActivateFromOtherInstance() {
            if (bedNotesForm != null && !bedNotesForm.IsDisposed && bedNotesForm.IsHandleCreated) {
                try {
                    bedNotesForm.BeginInvoke(new Action(() => {
                        ShowBedNotes();
                        ShowNotification("โปรแกรมเปิดทำงานอยู่แล้วที่ System Tray\n(แสดงหน้าต่างบันทึกข้อมูลรายเตียง 1-30 ให้เรียบร้อย)");
                    }));
                    return;
                } catch {}
            }
            ShowBedNotes();
        }

        private void InitializeTray() {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("🛏️ บันทึกข้อมูลผู้ป่วยรายเตียง (Bed Notes 1-30) (กด F7)", null, (s, e) => ShowBedNotes());
            trayMenu.Items.Add("📌 ตัวเตือนหัตถการค้างจอ (Ward Sticky Pad) (กด Alt+T)", null, (s, e) => ShowStickyReminders());
            trayMenu.Items.Add("🧮 คำนวณค่าวิกฤต & ABG/ยา (Clinical Calculators) (กด Alt+C)", null, (s, e) => ShowCalculator());
            trayMenu.Items.Add("📜 ดูประวัติข้อมูลเตียงย้อนหลัง (Bed History & Recovery)", null, (s, e) => ShowBedHistory(1));
            trayMenu.Items.Add("📋 เลือกเทมเพลตพยาบาล/แพทย์ (กด F8 หรือ Ctrl+Shift+M)", null, (s, e) => ShowPalette());
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("🚀 ตรวจสอบการอัปเดต (Check for Updates)", null, (s, e) => AppUpdater.CheckForUpdatesAsync(GetGitHubRepo(), true, null));
            trayMenu.Items.Add("🌐 เชื่อมต่อและซิงค์ข้อมูลในวอร์ด (Network Sync)", null, (s, e) => ShowSyncSettings());
            trayMenu.Items.Add("✏️ แก้ไขเทมเพลตข้อความ (Notepad)", null, (s, e) => EditTemplates());
            trayMenu.Items.Add("🔄 โหลดข้อมูลใหม่ทั้งหมดเดี๋ยวนี้", null, (s, e) => {
                LoadTemplates();
                if (bedNotesManager != null) bedNotesManager.LoadAll();
                if (bedNotesForm != null && !bedNotesForm.IsDisposed) bedNotesForm.RefreshAllBedButtons();
                if (stickyReminderForm != null && !stickyReminderForm.IsDisposed) stickyReminderForm.RefreshCards();
                ShowNotification("โหลดเทมเพลตและข้อมูลเตียงเรียบร้อยแล้ว");
            });
            trayMenu.Items.Add("-");
            ToolStripMenuItem itemEnable = new ToolStripMenuItem("เปิดใช้งานคีย์ลัดอัตโนมัติ", null, (s, e) => {
                isEnabled = !isEnabled;
                ((ToolStripMenuItem)s).Checked = isEnabled;
            });
            itemEnable.Checked = true;
            trayMenu.Items.Add(itemEnable);
            trayMenu.Items.Add("🚀 เริ่มพร้อมเปิดเครื่อง (Startup)", null, ToggleStartup);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("❌ ปิดโปรแกรม", null, (s, e) => Exit());

            Icon appIcon = null;
            if (File.Exists(iconPath)) {
                try { appIcon = new Icon(iconPath); } catch {}
            }
            if (appIcon == null) {
                appIcon = SystemIcons.Application;
            }

            trayIcon = new NotifyIcon();
            trayIcon.Text = "Medical: F7 เตียง | Alt+T เตือน | Alt+C คำนวณ | F8";
            trayIcon.Icon = appIcon;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => ShowBedNotes();

            string statusMsg = string.IsNullOrEmpty(sharedConfigPath) 
                ? "ระบบพร้อมทำงาน (โหมดข้อมูลในเครื่อง)" 
                : "เชื่อมต่อกับระบบแชร์ส่วนกลางของวอร์ดเรียบร้อยแล้ว";

            trayIcon.ShowBalloonTip(3000, "Medical & Nursing Text Expander", 
                statusMsg + "\n• กด F7: ข้อมูลรายเตียง 1-30\n• กด Alt+T: ตัวเตือนหัตถการค้างจอ (Sticky Pad)\n• กด Alt+C: คำนวณ SOS/ABG/ยา\n• กด F8: คลังเทมเพลต", 
                ToolTipIcon.Info);
        }

        private void HandleReminderDue(WardReminderItem item) {
            if (syncContext != null) {
                syncContext.Post(state => {
                    try {
                        System.Media.SystemSounds.Exclamation.Play();
                    } catch {}
                    string title = item.BedNum > 0 ? string.Format("⏰ เตือนหัตถการ เตียง {0}!", item.BedNum) : "⏰ เตือนหัตถการวอร์ด!";
                    string msg = string.Format("{0}\nเวลา: {1:D2}:{2:D2} น.", item.Title, item.DueTime.Hour, item.DueTime.Minute);
                    ShowNotification(title + "\n" + msg);
                    if (stickyReminderForm != null && !stickyReminderForm.IsDisposed) {
                        stickyReminderForm.EnsureVisibleAndFlash();
                    }
                }, null);
            }
        }

        public void ShowStickyReminders() {
            if (stickyReminderForm == null || stickyReminderForm.IsDisposed) {
                stickyReminderForm = new WardReminderStickyForm(this, reminderManager);
            }
            stickyReminderForm.ShowAndFocus();
        }

        public void ShowBedHistory(int bedNum = 1) {
            if (historyViewerForm == null || historyViewerForm.IsDisposed) {
                historyViewerForm = new BedHistoryViewerForm(this, bedNotesManager);
            }
            historyViewerForm.ShowAndFocus(bedNum);
        }

        public void LoadSettings() {
            if (File.Exists(settingsIniPath)) {
                try {
                    string[] lines = File.ReadAllLines(settingsIniPath, Encoding.UTF8);
                    foreach (string line in lines) {
                        string t = line.Trim();
                        if (t.StartsWith("SharedPath=", StringComparison.OrdinalIgnoreCase)) {
                            sharedConfigPath = t.Substring("SharedPath=".Length).Trim();
                        } else if (t.StartsWith("SharedBedNotesPath=", StringComparison.OrdinalIgnoreCase)) {
                            sharedBedNotesDir = t.Substring("SharedBedNotesPath=".Length).Trim();
                        } else if (t.StartsWith("GitHubRepo=", StringComparison.OrdinalIgnoreCase)) {
                            gitHubRepo = t.Substring("GitHubRepo=".Length).Trim();
                        } else if (t.StartsWith("GitHubToken=", StringComparison.OrdinalIgnoreCase)) {
                            gitHubToken = t.Substring("GitHubToken=".Length).Trim();
                        } else if (t.StartsWith("FontSize=", StringComparison.OrdinalIgnoreCase)) {
                            float f;
                            if (float.TryParse(t.Substring("FontSize=".Length).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) && f >= 9.0f && f <= 28.0f) {
                                CurrentFontSize = f;
                            }
                        }
                    }
                } catch {}
            }

            // Auto default sharedBedNotesDir if SharedPath is configured but SharedBedNotesPath is empty
            if (string.IsNullOrEmpty(sharedBedNotesDir) && !string.IsNullOrEmpty(sharedConfigPath)) {
                try {
                    string sharedDir = Path.GetDirectoryName(sharedConfigPath);
                    if (!string.IsNullOrEmpty(sharedDir)) {
                        sharedBedNotesDir = Path.Combine(sharedDir, "ward_bed_notes");
                    }
                } catch {}
            }
        }

        public void SaveSettings(string newSharedPath, string newSharedBedNotesPath) {
            sharedConfigPath = newSharedPath.Trim();
            sharedBedNotesDir = newSharedBedNotesPath.Trim();
            SaveConfigFile();
            SetupWatcher();
            LoadTemplates();
            if (bedNotesManager != null) {
                bedNotesManager.UpdateSharedPath(sharedBedNotesDir);
            }
        }

        public void SaveFontSize(float size) {
            CurrentFontSize = size;
            SaveConfigFile();
        }

        private void SaveConfigFile() {
            try {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("SharedPath=" + sharedConfigPath);
                sb.AppendLine("SharedBedNotesPath=" + sharedBedNotesDir);
                sb.AppendLine("FontSize=" + CurrentFontSize.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("GitHubRepo=" + (string.IsNullOrEmpty(gitHubRepo) ? AppUpdater.DefaultGitHubRepo : gitHubRepo));
                if (!string.IsNullOrEmpty(gitHubToken)) sb.AppendLine("GitHubToken=" + gitHubToken);
                File.WriteAllText(settingsIniPath, sb.ToString(), Encoding.UTF8);
            } catch {}
        }

        private void SetupWatcher() {
            if (watcher != null) {
                try {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                } catch {}
                watcher = null;
            }

            if (!string.IsNullOrEmpty(sharedConfigPath) && File.Exists(sharedConfigPath)) {
                try {
                    string dir = Path.GetDirectoryName(sharedConfigPath);
                    string file = Path.GetFileName(sharedConfigPath);
                    watcher = new FileSystemWatcher(dir, file);
                    watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                    watcher.Changed += (s, e) => {
                        System.Threading.Thread.Sleep(500); // Wait for writer to release
                        LoadTemplates();
                        ShowNotification("อัปเดตเทมเพลตจากส่วนกลางเรียบร้อยแล้ว");
                    };
                    watcher.EnableRaisingEvents = true;
                } catch {}
            }
        }

        public void LoadTemplates() {
            templates.Clear();
            string activeFile = localConfigPath;

            if (!string.IsNullOrEmpty(sharedConfigPath)) {
                try {
                    if (File.Exists(sharedConfigPath)) {
                        File.Copy(sharedConfigPath, localConfigPath, true);
                        activeFile = sharedConfigPath;
                    }
                } catch {
                    activeFile = localConfigPath;
                }
            }

            if (!File.Exists(activeFile)) {
                return;
            }

            try {
                string[] lines = File.ReadAllLines(activeFile, Encoding.UTF8);
                string curShortcut = "";
                string curTitle = "";
                string curCategory = "ทั่วไป";
                StringBuilder curContent = new StringBuilder();

                foreach (string line in lines) {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || string.IsNullOrEmpty(trimmed)) {
                        continue;
                    }
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) {
                        curCategory = trimmed.Substring(1, trimmed.Length - 2);
                        continue;
                    }
                    if (trimmed.StartsWith("---")) {
                        if (!string.IsNullOrEmpty(curShortcut) && curContent.Length > 0) {
                            templates.Add(new TemplateItem(curShortcut, curTitle, curCategory, curContent.ToString().TrimEnd()));
                        }
                        curShortcut = "";
                        curTitle = "";
                        curContent.Clear();
                        continue;
                    }

                    int eqIdx = line.IndexOf('=');
                    if (eqIdx > 0 && string.IsNullOrEmpty(curShortcut)) {
                        curShortcut = line.Substring(0, eqIdx).Trim();
                        curTitle = line.Substring(eqIdx + 1).Trim();
                    } else {
                        curContent.AppendLine(line);
                    }
                }

                if (!string.IsNullOrEmpty(curShortcut) && curContent.Length > 0) {
                    templates.Add(new TemplateItem(curShortcut, curTitle, curCategory, curContent.ToString().TrimEnd()));
                }
            } catch (Exception ex) {
                Debug.WriteLine("Template read error: " + ex.Message);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)) {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                bool isCtrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool isShift = (GetAsyncKeyState(0x10) & 0x8000) != 0;

                // F8 or Ctrl+Shift+M -> Open Medical Templates Palette
                if (key == Keys.F8 || (isCtrl && isShift && key == Keys.M)) {
                    ShowPalette();
                    return (IntPtr)1;
                }

                // F7 or Ctrl+Shift+B or Alt+B -> Open Ward Bed Notes (1-30)
                bool isAlt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                if (key == Keys.F7 || (isCtrl && isShift && key == Keys.B) || (isAlt && key == Keys.B)) {
                    ShowBedNotes();
                    return (IntPtr)1;
                }

                // Alt+C or Ctrl+Shift+C -> Open Clinical Calculators & Early Warning
                if ((isAlt && key == Keys.C) || (isCtrl && isShift && key == Keys.C)) {
                    ShowCalculator();
                    return (IntPtr)1;
                }

                // Alt+T or Ctrl+Shift+T -> Open/Toggle Ward Care & Task Reminders (Sticky Notepad)
                if ((isAlt && key == Keys.T) || (isCtrl && isShift && key == Keys.T)) {
                    ShowStickyReminders();
                    return (IntPtr)1;
                }

                if (isEnabled) {
                    ProcessKeystroke(key);
                }
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        private void ProcessKeystroke(Keys key) {
            char c = KeyToChar(key);

            if (key == Keys.Back) {
                if (typedBuffer.Length > 0) {
                    typedBuffer.Length--;
                }
                return;
            }

            if (key == Keys.Space || key == Keys.Tab || key == Keys.Enter || c == ' ') {
                string typed = typedBuffer.ToString().Trim();
                if (typed.StartsWith(".")) {
                    // 1. Check template match
                    TemplateItem match = templates.Find(t => t.Shortcut.Equals(typed, StringComparison.OrdinalIgnoreCase));
                    if (match != null) {
                        int eraseCount = typedBuffer.Length + (key == Keys.Space ? 1 : 0);
                        typedBuffer.Clear();

                        System.Threading.ThreadPool.QueueUserWorkItem(state => {
                            System.Threading.Thread.Sleep(30);
                            ExecutePaste(eraseCount, match.Content);
                        });
                        return;
                    }

                    // 2. Check bed note shortcut match: .b1 .. .b30 or .bed1 .. .bed30
                    int bedNum = ParseBedShortcut(typed);
                    if (bedNum >= 1 && bedNum <= 30 && bedNotesManager != null) {
                        string note = bedNotesManager.GetBedNote(bedNum);
                        int eraseCount = typedBuffer.Length + (key == Keys.Space ? 1 : 0);
                        typedBuffer.Clear();

                        if (!string.IsNullOrEmpty(note.Trim())) {
                            System.Threading.ThreadPool.QueueUserWorkItem(state => {
                                System.Threading.Thread.Sleep(30);
                                ExecutePaste(eraseCount, note);
                            });
                        } else {
                            ShowNotification(string.Format("เตียง {0} ยังไม่มีข้อมูลที่บันทึกไว้ (กด F7 เพื่อบันทึก)", bedNum));
                        }
                        return;
                    }
                }
                typedBuffer.Clear();
                return;
            }

            if (c != '\0') {
                if (c == '.') {
                    typedBuffer.Clear();
                    typedBuffer.Append('.');
                } else if (typedBuffer.Length > 0 && typedBuffer.Length < 30) {
                    typedBuffer.Append(c);
                }
            } else {
                if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Escape) {
                    typedBuffer.Clear();
                }
            }
        }

        private int ParseBedShortcut(string s) {
            string lower = s.ToLower();
            if (lower.StartsWith(".b")) {
                string numPart = "";
                if (lower.StartsWith(".bed")) {
                    numPart = lower.Substring(4);
                } else {
                    numPart = lower.Substring(2);
                }
                int n;
                if (int.TryParse(numPart, out n) && n >= 1 && n <= 30) {
                    return n;
                }
            }
            return -1;
        }

        private char KeyToChar(Keys key) {
            if (key >= Keys.A && key <= Keys.Z) {
                return (char)('a' + (key - Keys.A));
            }
            if (key >= Keys.D0 && key <= Keys.D9) {
                return (char)('0' + (key - Keys.D0));
            }
            if (key == Keys.OemPeriod || (int)key == 190) {
                return '.';
            }
            if (key == Keys.OemMinus || (int)key == 189) {
                return '-';
            }
            return '\0';
        }

        public void ExecutePaste(int eraseCount, string rawContent) {
            DateTime now = DateTime.Now;
            int thaiYear = now.Year + 543;
            string dateStr = string.Format("{0:D2}/{1:D2}/{2}", now.Day, now.Month, thaiYear);
            string timeStr = string.Format("{0:D2}:{1:D2}", now.Hour, now.Minute);

            string content = rawContent
                .Replace("{DATE}", dateStr)
                .Replace("{TIME}", timeStr)
                .Replace("{NOW}", dateStr + " " + timeStr);

            for (int i = 0; i < eraseCount; i++) {
                keybd_event(VK_BACK, 0, 0, 0);
                keybd_event(VK_BACK, 0, KEYEVENTF_KEYUP, 0);
                System.Threading.Thread.Sleep(5);
            }

            System.Threading.Thread.Sleep(20);

            IntPtr curWnd = GetForegroundWindow();
            MethodInvoker mi = new MethodInvoker(() => {
                try {
                    Clipboard.SetDataObject(content, true, 5, 50);
                } catch {}
            });

            if (Application.OpenForms.Count > 0) {
                Application.OpenForms[0].Invoke(mi);
            } else {
                System.Threading.Thread sta = new System.Threading.Thread(() => {
                    try { Clipboard.SetDataObject(content, true, 5, 50); } catch {}
                });
                sta.SetApartmentState(System.Threading.ApartmentState.STA);
                sta.Start();
                sta.Join();
            }

            System.Threading.Thread.Sleep(25);

            keybd_event(VK_CONTROL, 0, 0, 0);
            keybd_event(VK_V, 0, 0, 0);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
        }

        public void ShowPalette() {
            if (paletteForm == null || paletteForm.IsDisposed) {
                paletteForm = new PaletteForm(this);
            }
            paletteForm.ShowAndFocus();
        }

        public void ShowBedNotes(int bedNumber = -1) {
            if (bedNotesForm == null || bedNotesForm.IsDisposed) {
                bedNotesForm = new BedNotesForm(this, bedNotesManager);
            }
            bedNotesForm.ShowAndFocus(bedNumber);
        }

        public void ShowCalculator(int targetBed = -1) {
            if (calculatorForm == null || calculatorForm.IsDisposed) {
                calculatorForm = new ClinicalCalculatorForm(this);
            }
            int bedToUse = targetBed;
            if (bedToUse <= 0 && bedNotesForm != null && !bedNotesForm.IsDisposed) {
                bedToUse = bedNotesForm.CurrentBed;
            }
            if (bedToUse <= 0) bedToUse = 1;
            calculatorForm.ShowAndFocus(bedToUse);
        }

        public void InsertToBedNote(int bedNum, string snippet) {
            if (bedNum < 1 || bedNum > 30 || string.IsNullOrEmpty(snippet)) return;

            if (bedNotesForm != null && !bedNotesForm.IsDisposed) {
                if (bedNotesForm.CurrentBed != bedNum) {
                    bedNotesForm.SelectBed(bedNum);
                }
                bedNotesForm.InsertSnippetExternal(snippet);
                bedNotesForm.ShowAndFocus(bedNum);
            } else {
                string cur = bedNotesManager != null ? bedNotesManager.GetBedNote(bedNum) : "";
                string updated = string.IsNullOrEmpty(cur) ? snippet : cur + "\r\n\r\n" + snippet;
                if (bedNotesManager != null) {
                    bedNotesManager.SaveBedNote(bedNum, updated);
                }
                ShowBedNotes(bedNum);
            }
            ShowNotification(string.Format("บันทึกผลการคำนวณลงเตียง {0} เรียบร้อยแล้ว", bedNum));
        }

        public void ShowSyncSettings() {
            SyncSettingsForm form = new SyncSettingsForm(this, sharedConfigPath, sharedBedNotesDir);
            form.ShowDialog();
        }

        public void EditTemplates() {
            string fileToEdit = (!string.IsNullOrEmpty(sharedConfigPath) && File.Exists(sharedConfigPath)) 
                ? sharedConfigPath 
                : localConfigPath;

            try {
                Process.Start("notepad.exe", fileToEdit);
            } catch (Exception ex) {
                MessageBox.Show("ไม่สามารถเปิด Notepad ได้: " + ex.Message);
            }
        }

        public List<TemplateItem> GetTemplates() {
            return templates;
        }

        public string GetSharedConfigPath() {
            return sharedConfigPath;
        }

        public string GetSharedBedNotesDir() {
            return sharedBedNotesDir;
        }

        private void ToggleStartup(object sender, EventArgs e) {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            string appName = "ePHIS_Medical_Expander";
            string exePath = Application.ExecutablePath;

            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true)) {
                    if (key != null) {
                        object existing = key.GetValue(appName);
                        if (existing != null) {
                            key.DeleteValue(appName, false);
                            item.Checked = false;
                            ShowNotification("ปิดการทำงานพร้อมเปิดเครื่องแล้ว");
                        } else {
                            key.SetValue(appName, "\"" + exePath + "\"");
                            item.Checked = true;
                            ShowNotification("ตั้งค่าให้โปรแกรมทำงานพร้อมเปิดเครื่องเรียบร้อยแล้ว");
                        }
                    }
                }
            } catch (Exception ex) {
                MessageBox.Show("ไม่สามารถตั้งค่า Startup ได้: " + ex.Message);
            }
        }

        public void ShowNotification(string msg) {
            if (trayIcon != null) {
                trayIcon.ShowBalloonTip(2500, "Medical Text Expander", msg, ToolTipIcon.Info);
            }
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                if (hookId != IntPtr.Zero) {
                    UnhookWindowsHookEx(hookId);
                    hookId = IntPtr.Zero;
                }
                if (watcher != null) {
                    try { watcher.Dispose(); } catch {}
                    watcher = null;
                }
                if (trayIcon != null) {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    trayIcon = null;
                }
                if (paletteForm != null && !paletteForm.IsDisposed) {
                    try { paletteForm.Dispose(); } catch {}
                    paletteForm = null;
                }
                if (bedNotesForm != null && !bedNotesForm.IsDisposed) {
                    try { bedNotesForm.Dispose(); } catch {}
                    bedNotesForm = null;
                }
                if (calculatorForm != null && !calculatorForm.IsDisposed) {
                    try { calculatorForm.Dispose(); } catch {}
                    calculatorForm = null;
                }
                if (stickyReminderForm != null && !stickyReminderForm.IsDisposed) {
                    try { stickyReminderForm.Dispose(); } catch {}
                    stickyReminderForm = null;
                }
                if (historyViewerForm != null && !historyViewerForm.IsDisposed) {
                    try { historyViewerForm.Dispose(); } catch {}
                    historyViewerForm = null;
                }
            }
            base.Dispose(disposing);
        }

        private void Exit() {
            Dispose(true);
            Application.Exit();
        }
    }

    public static class BedColorHelper {
        public struct BedTheme {
            public Color Primary;     // Strong badge color / selected header
            public Color SoftBg;      // Soft background for bed button with data
            public Color Border;      // Border color
            public Color TextDark;    // Contrast dark text for soft background
            public string ZoneName;   // Zone description
        }

        private static readonly BedTheme[] Themes = new BedTheme[] {
            // Bed 1, 11, 21 - Sky Blue
            new BedTheme {
                Primary = Color.FromArgb(2, 132, 199),
                SoftBg = Color.FromArgb(240, 249, 255),
                Border = Color.FromArgb(186, 230, 253),
                TextDark = Color.FromArgb(3, 105, 161),
                ZoneName = "ฟ้าคราม"
            },
            // Bed 2, 12, 22 - Emerald Green
            new BedTheme {
                Primary = Color.FromArgb(16, 185, 129),
                SoftBg = Color.FromArgb(236, 253, 245),
                Border = Color.FromArgb(167, 243, 208),
                TextDark = Color.FromArgb(4, 120, 87),
                ZoneName = "เขียวมรกต"
            },
            // Bed 3, 13, 23 - Royal Indigo
            new BedTheme {
                Primary = Color.FromArgb(99, 102, 241),
                SoftBg = Color.FromArgb(238, 242, 255),
                Border = Color.FromArgb(199, 210, 254),
                TextDark = Color.FromArgb(67, 56, 202),
                ZoneName = "ม่วงคราม"
            },
            // Bed 4, 14, 24 - Amber Gold
            new BedTheme {
                Primary = Color.FromArgb(217, 119, 6),
                SoftBg = Color.FromArgb(254, 243, 199),
                Border = Color.FromArgb(253, 230, 138),
                TextDark = Color.FromArgb(180, 83, 9),
                ZoneName = "ส้มทอง"
            },
            // Bed 5, 15, 25 - Rose Crimson
            new BedTheme {
                Primary = Color.FromArgb(225, 29, 72),
                SoftBg = Color.FromArgb(255, 241, 242),
                Border = Color.FromArgb(254, 205, 211),
                TextDark = Color.FromArgb(190, 18, 60),
                ZoneName = "ชมพูกุหลาบ"
            },
            // Bed 6, 16, 26 - Violet Purple
            new BedTheme {
                Primary = Color.FromArgb(147, 51, 234),
                SoftBg = Color.FromArgb(250, 245, 255),
                Border = Color.FromArgb(233, 213, 255),
                TextDark = Color.FromArgb(126, 34, 206),
                ZoneName = "ม่วงสดใส"
            },
            // Bed 7, 17, 27 - Cyan Turquoise
            new BedTheme {
                Primary = Color.FromArgb(14, 165, 233),
                SoftBg = Color.FromArgb(240, 253, 250),
                Border = Color.FromArgb(153, 246, 228),
                TextDark = Color.FromArgb(15, 118, 110),
                ZoneName = "ฟ้าเทอร์ควอยซ์"
            },
            // Bed 8, 18, 28 - Coral Tangerine
            new BedTheme {
                Primary = Color.FromArgb(234, 88, 12),
                SoftBg = Color.FromArgb(255, 247, 237),
                Border = Color.FromArgb(254, 215, 170),
                TextDark = Color.FromArgb(194, 65, 12),
                ZoneName = "ส้มคอรัล"
            },
            // Bed 9, 19, 29 - Forest Teal
            new BedTheme {
                Primary = Color.FromArgb(13, 148, 136),
                SoftBg = Color.FromArgb(240, 253, 250),
                Border = Color.FromArgb(153, 246, 228),
                TextDark = Color.FromArgb(17, 94, 89),
                ZoneName = "เขียวหัวเป็ด"
            },
            // Bed 10, 20, 30 - Warm Fuchsia
            new BedTheme {
                Primary = Color.FromArgb(192, 38, 211),
                SoftBg = Color.FromArgb(253, 244, 255),
                Border = Color.FromArgb(245, 208, 254),
                TextDark = Color.FromArgb(134, 25, 143),
                ZoneName = "ชมพูฟูเชีย"
            }
        };

        public static BedTheme GetTheme(int bedNum) {
            if (bedNum <= 0) {
                // General / Ward
                return new BedTheme {
                    Primary = Color.FromArgb(71, 85, 105),
                    SoftBg = Color.FromArgb(241, 245, 249),
                    Border = Color.FromArgb(203, 213, 225),
                    TextDark = Color.FromArgb(30, 41, 59),
                    ZoneName = "ทั่วไป"
                };
            }
            int index = (bedNum - 1) % Themes.Length;
            return Themes[index];
        }
    }

    public class BedNotesForm : Form {
        private ExpanderContext context;
        private BedNotesManager manager;
        private int currentBed = 1;
        public int CurrentBed { get { return currentBed; } }
        private Button[] bedButtons = new Button[31];
        private TextBox txtSearchBed;
        private TextBox txtNote;
        private Label lblBedTitle;
        private Label lblAutoSave;
        private Label lblCharCount;
        private Label lblNetworkStatus;
        private System.Windows.Forms.Timer autoSaveTimer;
        private System.Windows.Forms.Timer reminderBlinkTimer;
        private bool isBlinkPhase = false;
        private bool isDirty = false;
        private bool isSuppressingEvents = false;
        private IntPtr lastActiveWindow = IntPtr.Zero;
        private float currentFontSize = 13.0f;
        private ToolTip bedToolTip;

        // Reusable static fonts to prevent GDI resource leaks and GC stutter
        private static readonly Font FontBedBold8 = new Font("Segoe UI", 8f, FontStyle.Bold);
        private static readonly Font FontBedBold85 = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontBedRegular8 = new Font("Segoe UI", 8f, FontStyle.Regular);

        private SplitContainer split;
        private Panel pnlTop;
        private Panel pnlBottom;
        private Panel pnlLeftHeader;
        private FlowLayoutPanel flowBeds;
        private Panel pnlNoteHeader;
        private FlowLayoutPanel pnlQuickButtons;

        private Label lblAppTitle;
        private Button btnCalc;
        private Button btnGoToPalette;
        private Button btnZoomOut;
        private Button btnZoomIn;
        private Button btnCheckUpdate;

        private Button btnPaste;
        private Button btnCopy;
        private Button btnInsertTime;
        private Button btnHistory;
        private Button btnClear;
        private Button btnSyncSettings;
        private Button btnClose;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public BedNotesForm(ExpanderContext ctx, BedNotesManager mgr) {
            context = ctx;
            manager = mgr;
            currentFontSize = context.CurrentFontSize;
            InitializeUI();
            IntPtr forceHandle = this.Handle;

            if (manager != null) {
                manager.OnBedChanged += Manager_OnBedChanged;
            }
        }

        private void InitializeUI() {
            this.Text = "🛏️ บันทึกข้อมูลผู้ป่วยรายเตียง (Bed Notes 1-30) - ใช้ซ้ำใน Nurse Note & ซิงค์ทั้งวอร์ด (F7)";
            this.Size = new Size(960, 680);
            this.MinimumSize = new Size(640, 440);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.Font = new Font("Segoe UI", 10f);
            this.BackColor = Color.FromArgb(248, 250, 252);

            string iconPath = @"C:\PhisApp\Medical_Text_Expander\medical_expander_icon.ico";
            if (!File.Exists(iconPath)) iconPath = @"C:\PhisApp\medical_expander_icon.ico";
            if (File.Exists(iconPath)) {
                try { this.Icon = new Icon(iconPath); } catch {}
            }

            bedToolTip = new ToolTip();
            bedToolTip.InitialDelay = 300;
            bedToolTip.AutoPopDelay = 8000;

            // Top Header Panel
            pnlTop = new Panel();
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Height = 56;
            pnlTop.BackColor = Color.FromArgb(13, 148, 136); // Medical Teal

            lblAppTitle = new Label();
            lblAppTitle.Text = "🛏️ ข้อมูลผู้ป่วยรายเตียง (Ward Bed Notes 1-30)";
            lblAppTitle.ForeColor = Color.White;
            lblAppTitle.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            lblAppTitle.Location = new Point(14, 8);
            lblAppTitle.AutoSize = true;
            pnlTop.Controls.Add(lblAppTitle);

            lblNetworkStatus = new Label();
            lblNetworkStatus.Text = manager.IsSharedActive 
                ? "🌐 เชื่อมต่อกับโฟลเดอร์ส่วนกลางของวอร์ดเรียบร้อย (ซิงค์ทุกเครื่อง)" 
                : "💻 โหมดบันทึกในเครื่องนี้ (ยังไม่ได้เชื่อมต่อโฟลเดอร์ส่วนกลาง)";
            lblNetworkStatus.ForeColor = Color.FromArgb(204, 251, 241);
            lblNetworkStatus.Font = new Font("Segoe UI", 9f);
            lblNetworkStatus.Location = new Point(16, 32);
            lblNetworkStatus.AutoSize = true;
            pnlTop.Controls.Add(lblNetworkStatus);

            btnCalc = new Button();
            btnCalc.Text = "🧮 คำนวณ SOS/ยา (Alt+C)";
            btnCalc.Size = new Size(185, 34);
            btnCalc.BackColor = Color.FromArgb(245, 158, 11);
            btnCalc.ForeColor = Color.White;
            btnCalc.FlatStyle = FlatStyle.Flat;
            btnCalc.FlatAppearance.BorderSize = 0;
            btnCalc.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnCalc.Cursor = Cursors.Hand;
            btnCalc.Click += (s, e) => context.ShowCalculator(currentBed);
            pnlTop.Controls.Add(btnCalc);

            btnGoToPalette = new Button();
            btnGoToPalette.Text = "📋 คลังเทมเพลต (F8)";
            btnGoToPalette.Size = new Size(150, 34);
            btnGoToPalette.BackColor = Color.FromArgb(15, 118, 110);
            btnGoToPalette.ForeColor = Color.White;
            btnGoToPalette.FlatStyle = FlatStyle.Flat;
            btnGoToPalette.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnGoToPalette.Cursor = Cursors.Hand;
            btnGoToPalette.Click += (s, e) => {
                FlushSave();
                this.Hide();
                context.ShowPalette();
            };
            pnlTop.Controls.Add(btnGoToPalette);

            btnZoomOut = new Button();
            btnZoomOut.Text = "A -";
            btnZoomOut.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnZoomOut.Size = new Size(38, 34);
            btnZoomOut.BackColor = Color.FromArgb(240, 240, 240);
            btnZoomOut.FlatStyle = FlatStyle.Flat;
            btnZoomOut.Cursor = Cursors.Hand;
            btnZoomOut.Click += (s, e) => AdjustFontSize(-1.5f);
            pnlTop.Controls.Add(btnZoomOut);

            btnZoomIn = new Button();
            btnZoomIn.Text = "A +";
            btnZoomIn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnZoomIn.Size = new Size(38, 34);
            btnZoomIn.BackColor = Color.FromArgb(240, 240, 240);
            btnZoomIn.FlatStyle = FlatStyle.Flat;
            btnZoomIn.Cursor = Cursors.Hand;
            btnZoomIn.Click += (s, e) => AdjustFontSize(1.5f);
            pnlTop.Controls.Add(btnZoomIn);

            btnCheckUpdate = new Button();
            btnCheckUpdate.Text = "🚀 อัปเดต";
            btnCheckUpdate.Name = "btnCheckUpdate";
            btnCheckUpdate.Size = new Size(82, 34);
            btnCheckUpdate.BackColor = Color.FromArgb(224, 231, 255);
            btnCheckUpdate.ForeColor = Color.FromArgb(67, 56, 202);
            btnCheckUpdate.FlatStyle = FlatStyle.Flat;
            btnCheckUpdate.FlatAppearance.BorderSize = 0;
            btnCheckUpdate.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnCheckUpdate.Cursor = Cursors.Hand;
            btnCheckUpdate.Click += (s, e) => AppUpdater.CheckForUpdatesAsync(context.GetGitHubRepo(), true, this, context.GetGitHubToken());
            pnlTop.Controls.Add(btnCheckUpdate);

            // Bottom Action Panel
            pnlBottom = new Panel();
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Height = 52;
            pnlBottom.BackColor = Color.FromArgb(238, 240, 246);

            btnPaste = new Button();
            btnPaste.Text = "📋 วางลงหน้าจอ e-PHIS (Ctrl+Enter)";
            btnPaste.Size = new Size(240, 34);
            btnPaste.BackColor = Color.FromArgb(13, 148, 136);
            btnPaste.ForeColor = Color.White;
            btnPaste.FlatStyle = FlatStyle.Flat;
            btnPaste.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnPaste.Cursor = Cursors.Hand;
            btnPaste.Click += (s, e) => PasteToActiveWindow();
            pnlBottom.Controls.Add(btnPaste);

            btnCopy = new Button();
            btnCopy.Text = "📋 คัดลอก (Copy)";
            btnCopy.Size = new Size(115, 34);
            btnCopy.BackColor = Color.FromArgb(225, 228, 238);
            btnCopy.FlatStyle = FlatStyle.Flat;
            btnCopy.Font = new Font("Segoe UI", 9f);
            btnCopy.Cursor = Cursors.Hand;
            btnCopy.Click += (s, e) => {
                FlushSave();
                if (!string.IsNullOrEmpty(txtNote.Text)) {
                    try { Clipboard.SetDataObject(txtNote.Text, true, 5, 50); } catch {}
                    context.ShowNotification(string.Format("คัดลอกข้อมูลเตียง {0} ไปยังคลิปบอร์ดแล้ว", currentBed));
                }
            };
            pnlBottom.Controls.Add(btnCopy);

            btnInsertTime = new Button();
            btnInsertTime.Text = "🕒 ใส่วันที่/เวลา";
            btnInsertTime.Size = new Size(110, 34);
            btnInsertTime.BackColor = Color.FromArgb(225, 228, 238);
            btnInsertTime.FlatStyle = FlatStyle.Flat;
            btnInsertTime.Font = new Font("Segoe UI", 9f);
            btnInsertTime.Cursor = Cursors.Hand;
            btnInsertTime.Click += (s, e) => InsertDateTimeAtCursor();
            pnlBottom.Controls.Add(btnInsertTime);

            btnHistory = new Button();
            btnHistory.Text = "📜 ประวัติเตียง";
            btnHistory.Size = new Size(110, 34);
            btnHistory.BackColor = Color.FromArgb(225, 228, 238);
            btnHistory.FlatStyle = FlatStyle.Flat;
            btnHistory.Font = new Font("Segoe UI", 9f);
            btnHistory.Cursor = Cursors.Hand;
            btnHistory.Click += (s, e) => context.ShowBedHistory(currentBed);
            pnlBottom.Controls.Add(btnHistory);

            btnClear = new Button();
            btnClear.Text = "🗑️ ล้างข้อมูลเตียงนี้";
            btnClear.Size = new Size(125, 34);
            btnClear.BackColor = Color.FromArgb(254, 226, 226);
            btnClear.ForeColor = Color.FromArgb(185, 28, 28);
            btnClear.FlatStyle = FlatStyle.Flat;
            btnClear.Font = new Font("Segoe UI", 9f);
            btnClear.Cursor = Cursors.Hand;
            btnClear.Click += (s, e) => ClearCurrentBed();
            pnlBottom.Controls.Add(btnClear);

            btnSyncSettings = new Button();
            btnSyncSettings.Text = "🌐 ตั้งค่าแชร์ในวอร์ด";
            btnSyncSettings.Size = new Size(125, 34);
            btnSyncSettings.BackColor = Color.FromArgb(225, 228, 238);
            btnSyncSettings.FlatStyle = FlatStyle.Flat;
            btnSyncSettings.Font = new Font("Segoe UI", 9f);
            btnSyncSettings.Cursor = Cursors.Hand;
            btnSyncSettings.Click += (s, e) => context.ShowSyncSettings();
            pnlBottom.Controls.Add(btnSyncSettings);



            btnClose = new Button();
            btnClose.Text = "ปิด (Esc)";
            btnClose.Size = new Size(80, 34);
            btnClose.BackColor = Color.FromArgb(225, 228, 238);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.Font = new Font("Segoe UI", 9f);
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => {
                FlushSave();
                this.Hide();
            };
            pnlBottom.Controls.Add(btnClose);

            // Split Container: Left for Bed List, Right for Notepad
            split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterWidth = 5;
            split.SplitterMoved += (s, e) => UpdateBedButtonSizes();

            // --- Panel 1: Left Beds Navigation ---
            pnlLeftHeader = new Panel();
            pnlLeftHeader.Dock = DockStyle.Top;
            pnlLeftHeader.Height = 52;
            pnlLeftHeader.BackColor = Color.FromArgb(241, 245, 249);

            Label lblListTitle = new Label();
            lblListTitle.Text = "หมายเลขเตียง 1 - 30:";
            lblListTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblListTitle.Location = new Point(8, 4);
            lblListTitle.AutoSize = true;
            pnlLeftHeader.Controls.Add(lblListTitle);

            txtSearchBed = new TextBox();
            txtSearchBed.Location = new Point(8, 24);
            txtSearchBed.Size = new Size(185, 24);
            txtSearchBed.Font = new Font("Segoe UI", 9f);
            txtSearchBed.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSearchBed.TextChanged += (s, e) => FilterBeds(txtSearchBed.Text);
            pnlLeftHeader.Controls.Add(txtSearchBed);

            flowBeds = new FlowLayoutPanel();
            flowBeds.Dock = DockStyle.Fill;
            flowBeds.AutoScroll = true;
            flowBeds.BackColor = Color.White;
            flowBeds.Padding = new Padding(3);
            flowBeds.ClientSizeChanged += (s, e) => UpdateBedButtonSizes();

            for (int i = 1; i <= 30; i++) {
                int bedNum = i;
                Button btn = new Button();
                btn.Size = new Size(90, 34);
                btn.Margin = new Padding(2);
                btn.Cursor = Cursors.Hand;
                btn.Tag = bedNum;
                btn.Click += (s, e) => SelectBed(bedNum);
                bedButtons[i] = btn;
                flowBeds.Controls.Add(btn);
            }

            split.Panel1.Controls.Add(flowBeds);
            split.Panel1.Controls.Add(pnlLeftHeader);

            // --- Panel 2: Right Notepad Area ---
            pnlNoteHeader = new Panel();
            pnlNoteHeader.Dock = DockStyle.Top;
            pnlNoteHeader.Height = 44;
            pnlNoteHeader.BackColor = Color.FromArgb(248, 250, 252);
            pnlNoteHeader.Padding = new Padding(8, 4, 8, 4);

            lblBedTitle = new Label();
            lblBedTitle.Text = "🛏️ ข้อมูลผู้ป่วย เตียง 1";
            lblBedTitle.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            lblBedTitle.ForeColor = Color.FromArgb(15, 23, 42);
            lblBedTitle.Location = new Point(8, 10);
            lblBedTitle.AutoSize = true;
            pnlNoteHeader.Controls.Add(lblBedTitle);

            lblAutoSave = new Label();
            lblAutoSave.Text = "💾 บันทึกอัตโนมัติแล้ว";
            lblAutoSave.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblAutoSave.ForeColor = Color.FromArgb(21, 128, 61);
            lblAutoSave.Location = new Point(220, 13);
            lblAutoSave.AutoSize = true;
            pnlNoteHeader.Controls.Add(lblAutoSave);

            lblCharCount = new Label();
            lblCharCount.Text = "0 ตัวอักษร | 0 บรรทัด";
            lblCharCount.Font = new Font("Segoe UI", 8.5f);
            lblCharCount.ForeColor = Color.FromArgb(100, 116, 139);
            lblCharCount.Location = new Point(480, 14);
            lblCharCount.AutoSize = true;
            pnlNoteHeader.Controls.Add(lblCharCount);

            // --- Quick Templates Toolbar ---
            pnlQuickButtons = new FlowLayoutPanel();
            pnlQuickButtons.Dock = DockStyle.Top;
            pnlQuickButtons.AutoSize = true;
            pnlQuickButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pnlQuickButtons.WrapContents = true;
            pnlQuickButtons.BackColor = Color.FromArgb(241, 245, 249);
            pnlQuickButtons.Padding = new Padding(5, 2, 5, 2);

            Label lblQuick = new Label();
            lblQuick.Text = "⚡ คีย์ด่วน:";
            lblQuick.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblQuick.ForeColor = Color.FromArgb(15, 118, 110);
            lblQuick.Margin = new Padding(0, 4, 3, 0);
            lblQuick.AutoSize = true;
            pnlQuickButtons.Controls.Add(lblQuick);

            AddQuickButton(pnlQuickButtons, "🩺 V/S สัญญาณชีพ", "V/S: BP.../... mmHg, PR... /min, RR... /min, SpO2... %");
            AddQuickButton(pnlQuickButtons, "😊 รู้สึกตัวดี (Alert)", "ผู้ป่วยรู้สึกตัวดี ถามตอบรู้เรื่อง ไม่มีเหนื่อยหอบ");
            AddQuickButton(pnlQuickButtons, "🫁 On O2", "On O2 cannula ... LPM, SpO2 ...%");
            AddQuickButton(pnlQuickButtons, "💉 On IV", "On IV ... rate ... ml/hr, IV site ดี phlebitis gr.0");
            AddQuickButton(pnlQuickButtons, "🚽 Foley", "Retained Foley cath, ปัสสาวะสีเหลืองใส ... ml");
            AddQuickButton(pnlQuickButtons, "😣 Pain สกอร์", "ประเมิน Pain score = .../10, ได้รับยาแก้ปวด... อาการปวดทุเลาเหลือ .../10");
            AddQuickButton(pnlQuickButtons, "🚫 NPO งดน้ำ-อาหาร", "NPO งดน้ำและอาหารตั้งแต่เวลา... น. เพื่อเตรียมตรวจ/ผ่าตัด");
            AddQuickButton(pnlQuickButtons, "👨‍⚕️ แพทย์ Round", "แพทย์... เข้าตรวจ เยี่ยมอาการ แผนการรักษา: ...");
            AddQuickButton(pnlQuickButtons, "📝 DAR", "Focus: ...\r\nData: ...\r\nAction: ...\r\nResponse: ...");

            Button btnQuickCalc = new Button();
            btnQuickCalc.Text = "🧮 คำนวณ SOS/ยา";
            btnQuickCalc.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnQuickCalc.BackColor = Color.FromArgb(254, 243, 199);
            btnQuickCalc.ForeColor = Color.FromArgb(180, 83, 9);
            btnQuickCalc.FlatStyle = FlatStyle.Flat;
            btnQuickCalc.FlatAppearance.BorderColor = Color.FromArgb(251, 191, 36);
            btnQuickCalc.Height = 25;
            btnQuickCalc.AutoSize = true;
            btnQuickCalc.Margin = new Padding(2, 1, 2, 1);
            btnQuickCalc.Cursor = Cursors.Hand;
            btnQuickCalc.Click += (s, e) => context.ShowCalculator(currentBed);
            pnlQuickButtons.Controls.Add(btnQuickCalc);

            Button btnStickyPad = new Button();
            btnStickyPad.Text = "📌 ตัวเตือนค้างจอ (Alt+T)";
            btnStickyPad.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnStickyPad.BackColor = Color.FromArgb(254, 240, 138); // Yellow note
            btnStickyPad.ForeColor = Color.FromArgb(146, 64, 14);
            btnStickyPad.FlatStyle = FlatStyle.Flat;
            btnStickyPad.FlatAppearance.BorderColor = Color.FromArgb(245, 158, 11);
            btnStickyPad.Height = 25;
            btnStickyPad.AutoSize = true;
            btnStickyPad.Margin = new Padding(2, 1, 2, 1);
            btnStickyPad.Cursor = Cursors.Hand;
            btnStickyPad.Click += (s, e) => context.ShowStickyReminders();
            pnlQuickButtons.Controls.Add(btnStickyPad);

            Button btnQuickRemind = new Button();
            btnQuickRemind.Text = "⏰ +ตั้งเตือนเตียงนี้ ▼";
            btnQuickRemind.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnQuickRemind.BackColor = Color.FromArgb(254, 243, 199);
            btnQuickRemind.ForeColor = Color.FromArgb(180, 83, 9);
            btnQuickRemind.FlatStyle = FlatStyle.Flat;
            btnQuickRemind.FlatAppearance.BorderColor = Color.FromArgb(251, 191, 36);
            btnQuickRemind.Height = 25;
            btnQuickRemind.AutoSize = true;
            btnQuickRemind.Margin = new Padding(2, 1, 2, 1);
            btnQuickRemind.Cursor = Cursors.Hand;
            btnQuickRemind.Click += (s, e) => ShowQuickRemindDropdown(btnQuickRemind);
            pnlQuickButtons.Controls.Add(btnQuickRemind);

            Button btnBedHistory = new Button();
            btnBedHistory.Text = "📜 ประวัติเตียง";
            btnBedHistory.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnBedHistory.BackColor = Color.FromArgb(241, 245, 249);
            btnBedHistory.ForeColor = Color.FromArgb(51, 65, 85);
            btnBedHistory.FlatStyle = FlatStyle.Flat;
            btnBedHistory.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnBedHistory.Height = 25;
            btnBedHistory.AutoSize = true;
            btnBedHistory.Margin = new Padding(2, 1, 2, 1);
            btnBedHistory.Cursor = Cursors.Hand;
            btnBedHistory.Click += (s, e) => context.ShowBedHistory(currentBed);
            pnlQuickButtons.Controls.Add(btnBedHistory);

            Button btnAllTemplates = new Button();
            btnAllTemplates.Text = "📋 คลังเทมเพลตทั้งหมด ▼";
            btnAllTemplates.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnAllTemplates.BackColor = Color.FromArgb(204, 251, 241);
            btnAllTemplates.ForeColor = Color.FromArgb(15, 118, 110);
            btnAllTemplates.FlatStyle = FlatStyle.Flat;
            btnAllTemplates.FlatAppearance.BorderColor = Color.FromArgb(45, 212, 191);
            btnAllTemplates.Height = 25;
            btnAllTemplates.AutoSize = true;
            btnAllTemplates.Margin = new Padding(2, 1, 2, 1);
            btnAllTemplates.Cursor = Cursors.Hand;
            btnAllTemplates.Click += (s, e) => ShowTemplatesDropdown(btnAllTemplates);
            pnlQuickButtons.Controls.Add(btnAllTemplates);

            txtNote = new TextBox();
            txtNote.Dock = DockStyle.Fill;
            txtNote.Multiline = true;
            txtNote.ScrollBars = ScrollBars.Vertical;
            txtNote.WordWrap = true;
            txtNote.BackColor = Color.White;
            txtNote.ForeColor = Color.FromArgb(15, 23, 42);
            txtNote.TextChanged += TxtNote_TextChanged;
            txtNote.KeyDown += TxtNote_KeyDown;
            txtNote.MouseWheel += (s, e) => {
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control) {
                    if (e.Delta > 0) AdjustFontSize(1.0f);
                    else if (e.Delta < 0) AdjustFontSize(-1.0f);
                }
            };

            split.Panel2.Controls.Add(txtNote);
            split.Panel2.Controls.Add(pnlQuickButtons);
            split.Panel2.Controls.Add(pnlNoteHeader);

            pnlNoteHeader.SendToBack();
            pnlQuickButtons.SendToBack();
            txtNote.BringToFront();

            // Docking order
            this.Controls.Add(split);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlTop);

            pnlTop.SendToBack();
            pnlBottom.SendToBack();
            split.BringToFront();

            // Auto-save timer (debounce 450ms)
            autoSaveTimer = new System.Windows.Forms.Timer();
            autoSaveTimer.Interval = 450;
            autoSaveTimer.Tick += (s, e) => {
                autoSaveTimer.Stop();
                FlushSave();
            };

            // Reminder alert & blink timer (1000ms)
            reminderBlinkTimer = new System.Windows.Forms.Timer();
            reminderBlinkTimer.Interval = 1000;
            reminderBlinkTimer.Tick += (s, e) => {
                if (!this.Visible) return;
                isBlinkPhase = !isBlinkPhase;
                RefreshAllBedButtons();
            };
            reminderBlinkTimer.Start();

            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape) {
                    FlushSave();
                    this.Hide();
                } else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)) {
                    AdjustFontSize(1.0f);
                    e.Handled = true;
                } else if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)) {
                    AdjustFontSize(-1.0f);
                    e.Handled = true;
                } else if (e.KeyCode == Keys.PageUp) {
                    if (currentBed > 1) SelectBed(currentBed - 1);
                    e.Handled = true;
                } else if (e.KeyCode == Keys.PageDown) {
                    if (currentBed < 30) SelectBed(currentBed + 1);
                    e.Handled = true;
                }
            };

            this.FormClosing += (s, e) => {
                FlushSave();
                if (e.CloseReason == CloseReason.UserClosing) {
                    e.Cancel = true;
                    this.Hide();
                }
            };

            ApplyFontSize();
            SelectBed(1);

            SetSafeSplitterDistance(205);
            UpdateBedButtonSizes();
            RepositionTopControls();
            RepositionBottomControls();
            RepositionNoteHeaderControls();
        }

        private void SetSafeSplitterDistance(int dist) {
            if (split == null) return;
            try {
                int max = split.ClientSize.Width - 100;
                int min = 80;
                if (max > min) {
                    if (dist > max) dist = max;
                    if (dist < min) dist = min;
                    split.SplitterDistance = dist;
                }
            } catch {}
        }

        private void UpdateBedButtonSizes() {
            if (flowBeds == null) return;
            int availableW = flowBeds.ClientSize.Width - 8;
            if (availableW < 90) availableW = 90;

            int cols = 2;
            if (availableW >= 500) cols = 5;
            else if (availableW >= 380) cols = 4;
            else if (availableW >= 260) cols = 3;
            else cols = 2;

            int spacing = 3;
            int btnW = (availableW - (spacing * (cols + 1))) / cols;
            if (btnW < 65) btnW = 65;
            int btnH = 34;

            for (int i = 1; i <= 30; i++) {
                Button btn = bedButtons[i];
                if (btn != null) {
                    btn.Size = new Size(btnW, btnH);
                    btn.Margin = new Padding(2);
                }
            }
        }

        private void RepositionTopControls() {
            if (pnlTop == null) return;
            int w = pnlTop.ClientSize.Width;
            bool isNarrow = (w < 820);

            if (isNarrow) {
                lblAppTitle.Text = "🛏️ ข้อมูลผู้ป่วยเตียง 1-30";
                lblAppTitle.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
                btnCalc.Text = "🧮 SOS/ยา (Alt+C)";
                btnCalc.Size = new Size(130, 34);
                btnGoToPalette.Text = "📋 เทมเพลต (F8)";
                btnGoToPalette.Size = new Size(115, 34);
            } else {
                lblAppTitle.Text = "🛏️ ข้อมูลผู้ป่วยรายเตียง (Ward Bed Notes 1-30)";
                lblAppTitle.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
                btnCalc.Text = "🧮 คำนวณ SOS/ยา (Alt+C)";
                btnCalc.Size = new Size(185, 34);
                btnGoToPalette.Text = "📋 คลังเทมเพลต (F8)";
                btnGoToPalette.Size = new Size(150, 34);
            }

            int rx = w - 10;
            if (btnZoomIn != null) {
                btnZoomIn.Location = new Point(rx - btnZoomIn.Width, 11);
                rx -= (btnZoomIn.Width + 4);
            }
            if (btnZoomOut != null) {
                btnZoomOut.Location = new Point(rx - btnZoomOut.Width, 11);
                rx -= (btnZoomOut.Width + 6);
            }
            if (btnCheckUpdate != null) {
                btnCheckUpdate.Location = new Point(rx - btnCheckUpdate.Width, 11);
                rx -= (btnCheckUpdate.Width + 6);
            }
            if (btnGoToPalette != null) {
                btnGoToPalette.Location = new Point(rx - btnGoToPalette.Width, 11);
                rx -= (btnGoToPalette.Width + 6);
            }
            if (btnCalc != null) {
                btnCalc.Location = new Point(rx - btnCalc.Width, 11);
            }
        }

        private void RepositionBottomControls() {
            if (pnlBottom == null) return;
            int w = pnlBottom.ClientSize.Width;
            bool isVeryNarrow = (w < 720);
            bool isNarrow = (w < 920);

            if (isVeryNarrow) {
                btnPaste.Text = "📋 วาง";
                btnPaste.Size = new Size(80, 34);
                btnCopy.Text = "คัดลอก";
                btnCopy.Size = new Size(65, 34);
                btnInsertTime.Text = "🕒";
                btnInsertTime.Size = new Size(42, 34);
                btnHistory.Text = "📜 ประวัติ";
                btnHistory.Size = new Size(72, 34);
                btnClear.Text = "🗑️";
                btnClear.Size = new Size(45, 34);
                btnSyncSettings.Text = "🌐";
                btnSyncSettings.Size = new Size(45, 34);
                btnClose.Text = "ปิด";
                btnClose.Size = new Size(50, 34);
            } else if (isNarrow) {
                btnPaste.Text = "📋 วาง e-PHIS (Ctrl+Enter)";
                btnPaste.Size = new Size(180, 34);
                btnCopy.Text = "📋 คัดลอก";
                btnCopy.Size = new Size(85, 34);
                btnInsertTime.Text = "🕒 เวลา";
                btnInsertTime.Size = new Size(70, 34);
                btnHistory.Text = "📜 ประวัติ";
                btnHistory.Size = new Size(85, 34);
                btnClear.Text = "🗑️ ล้าง";
                btnClear.Size = new Size(68, 34);
                btnSyncSettings.Text = "🌐 แชร์วอร์ด";
                btnSyncSettings.Size = new Size(90, 34);
                btnClose.Text = "ปิด";
                btnClose.Size = new Size(55, 34);
            } else {
                btnPaste.Text = "📋 วางลงหน้าจอ e-PHIS (Ctrl+Enter)";
                btnPaste.Size = new Size(240, 34);
                btnCopy.Text = "📋 คัดลอก (Copy)";
                btnCopy.Size = new Size(115, 34);
                btnInsertTime.Text = "🕒 ใส่วันที่/เวลา";
                btnInsertTime.Size = new Size(110, 34);
                btnHistory.Text = "📜 ประวัติเตียงย้อนหลัง";
                btnHistory.Size = new Size(140, 34);
                btnClear.Text = "🗑️ ล้างข้อมูลเตียงนี้";
                btnClear.Size = new Size(125, 34);
                btnSyncSettings.Text = "🌐 ตั้งค่าแชร์ในวอร์ด";
                btnSyncSettings.Size = new Size(125, 34);
                btnClose.Text = "ปิด (Esc)";
                btnClose.Size = new Size(80, 34);
            }

            int lx = 10;
            btnPaste.Location = new Point(lx, 9);
            lx += btnPaste.Width + 5;

            btnCopy.Location = new Point(lx, 9);
            lx += btnCopy.Width + 5;

            btnInsertTime.Location = new Point(lx, 9);
            lx += btnInsertTime.Width + 5;

            btnHistory.Location = new Point(lx, 9);
            lx += btnHistory.Width + 5;

            btnClear.Location = new Point(lx, 9);
            lx += btnClear.Width + 5;

            btnSyncSettings.Location = new Point(lx, 9);


            btnClose.Location = new System.Drawing.Point(w - btnClose.Width - 10, 9);
        }

        private void RepositionNoteHeaderControls() {
            if (pnlNoteHeader == null) return;
            int w = pnlNoteHeader.ClientSize.Width;
            if (lblCharCount != null) {
                lblCharCount.Location = new Point(w - lblCharCount.Width - 12, 14);
            }
            if (lblAutoSave != null && lblBedTitle != null) {
                int left = lblBedTitle.Right + 14;
                lblAutoSave.Location = new Point(left, 13);
                if (lblCharCount != null && lblAutoSave.Right > lblCharCount.Left - 8) {
                    if (lblAutoSave.Text.Contains("บันทึกอัตโนมัติแล้ว")) {
                        lblAutoSave.Text = "💾 บันทึกแล้ว";
                    }
                }
            }
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            if (split != null && this.ClientSize.Width <= 1050) {
                if (split.SplitterDistance > 215) {
                    SetSafeSplitterDistance(205);
                }
            }
            UpdateBedButtonSizes();
            RepositionTopControls();
            RepositionBottomControls();
            RepositionNoteHeaderControls();
        }

        private void ApplyFontSize() {
            try {
                Font f;
                try {
                    f = new Font("Leelawadee UI", currentFontSize, FontStyle.Regular);
                } catch {
                    f = new Font("Segoe UI", currentFontSize, FontStyle.Regular);
                }
                txtNote.Font = f;
            } catch {}
        }

        private void AdjustFontSize(float delta) {
            float newSize = currentFontSize + delta;
            if (newSize < 9.5f) newSize = 9.5f;
            if (newSize > 26.0f) newSize = 26.0f;
            currentFontSize = newSize;
            ApplyFontSize();
            context.SaveFontSize(currentFontSize);
        }

        public void SelectBed(int bedNum) {
            if (bedNum < 1 || bedNum > 30) return;

            if (isDirty) {
                FlushSave();
                if (!string.IsNullOrEmpty(txtNote.Text) && txtNote.Text.Trim().Length >= 10) {
                    manager.SaveHistorySnapshot(currentBed, "บันทึกอัตโนมัติก่อนเปลี่ยนเตียง", txtNote.Text);
                }
            }

            currentBed = bedNum;
            BedColorHelper.BedTheme curTheme = BedColorHelper.GetTheme(currentBed);
            lblBedTitle.Text = string.Format("🛏️ ข้อมูลผู้ป่วย เตียง {0:D2} ({1})", currentBed, curTheme.ZoneName);
            lblBedTitle.ForeColor = curTheme.Primary;

            isSuppressingEvents = true;
            txtNote.Text = manager.GetBedNote(currentBed);
            isSuppressingEvents = false;

            isDirty = false;
            lblAutoSave.Text = "พร้อมใช้งาน (พิมพ์แล้วบันทึกอัตโนมัติทันที)";
            lblAutoSave.ForeColor = Color.FromArgb(71, 85, 105);

            UpdateCharCount();
            RefreshAllBedButtons();
            txtNote.Focus();
        }

public void RefreshAllBedButtons() {
            for (int i = 1; i <= 30; i++) {
                Button btn = bedButtons[i];
                if (btn == null) continue;

                bool hasData = manager.HasNote(i);
                bool isSelected = (i == currentBed);
                BedColorHelper.BedTheme theme = BedColorHelper.GetTheme(i);

                WardReminderItem urgent = null;
                if (context != null && context.ReminderManager != null) {
                    urgent = context.ReminderManager.GetMostUrgentReminder(i);
                }

                if (urgent != null) {
                    TimeSpan diff = urgent.DueTime - DateTime.Now;
                    if (diff.TotalSeconds <= 0) {
                        // ถึงเวลาแล้ว / เกินกำหนด -> กระพริบเตือนสีแดง (Blinking Red Alert)
                        if (isBlinkPhase) {
                            btn.BackColor = Color.FromArgb(239, 68, 68); // Bright red
                            btn.ForeColor = Color.White;
                            btn.FlatStyle = FlatStyle.Flat;
                            btn.FlatAppearance.BorderColor = Color.FromArgb(185, 28, 28);
                            btn.FlatAppearance.BorderSize = 2;
                            if (btn.Font != FontBedBold8) btn.Font = FontBedBold8;
                            btn.Text = string.Format("เตียง {0:D2}\n🚨 ถึงเวลา!", i);
                        } else {
                            btn.BackColor = Color.FromArgb(254, 202, 202); // Soft red
                            btn.ForeColor = Color.FromArgb(185, 28, 28);
                            btn.FlatStyle = FlatStyle.Flat;
                            btn.FlatAppearance.BorderColor = Color.FromArgb(239, 68, 68);
                            btn.FlatAppearance.BorderSize = 2;
                            if (btn.Font != FontBedBold8) btn.Font = FontBedBold8;
                            btn.Text = string.Format("เตียง {0:D2}\n⏱ ถึงเวลา!", i);
                        }
                    } else if (diff.TotalMinutes <= 15) {
                        // ใกล้ถึงเวลาแล้ว (ภายใน 15 นาที) -> สีเหลืองส้มแจ้งเตือน (Amber Warning Alert)
                        int mins = (int)Math.Max(1, Math.Ceiling(diff.TotalMinutes));
                        if (mins <= 5 && isBlinkPhase) {
                            btn.BackColor = Color.FromArgb(254, 215, 170); // Pulse warning when <= 5 min
                        } else {
                            btn.BackColor = Color.FromArgb(254, 243, 199);
                        }
                        btn.ForeColor = Color.FromArgb(180, 83, 9);
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.FlatAppearance.BorderColor = Color.FromArgb(245, 158, 11);
                        btn.FlatAppearance.BorderSize = 2;
                        if (btn.Font != FontBedBold8) btn.Font = FontBedBold8;
                        btn.Text = string.Format("เตียง {0:D2}\n⚠️ อีก {1}น.", i, mins);
                    } else {
                        // มีการตั้งเตือนล่วงหน้า (> 15 นาที)
                        if (isSelected) {
                            btn.BackColor = theme.Primary;
                            btn.ForeColor = Color.White;
                            btn.FlatStyle = FlatStyle.Flat;
                            btn.FlatAppearance.BorderColor = theme.TextDark;
                            btn.FlatAppearance.BorderSize = 2;
                            if (btn.Font != FontBedBold8) btn.Font = FontBedBold8;
                            btn.Text = string.Format("เตียง {0:D2}\n⏱ {1:D2}:{2:D2}", i, urgent.DueTime.Hour, urgent.DueTime.Minute);
                        } else {
                            btn.BackColor = theme.SoftBg;
                            btn.ForeColor = theme.TextDark;
                            btn.FlatStyle = FlatStyle.Flat;
                            btn.FlatAppearance.BorderColor = theme.Border;
                            btn.FlatAppearance.BorderSize = 1;
                            if (btn.Font != FontBedBold8) btn.Font = FontBedBold8;
                            btn.Text = string.Format("เตียง {0:D2}\n⏱ {1:D2}:{2:D2}", i, urgent.DueTime.Hour, urgent.DueTime.Minute);
                        }
                    }
                } else {
                    // ไม่มีเตือนค้าง -> แสดงสีธีมประจำเตียงตามปกติ (Distinct Bed Colors)
                    if (isSelected) {
                        btn.BackColor = theme.Primary;
                        btn.ForeColor = Color.White;
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.FlatAppearance.BorderSize = 2;
                        btn.FlatAppearance.BorderColor = theme.TextDark;
                        if (btn.Font != FontBedBold85) btn.Font = FontBedBold85;
                        btn.Text = string.Format("เตียง {0:D2}\n● เลือก", i);
                    } else if (hasData) {
                        btn.BackColor = theme.SoftBg;
                        btn.ForeColor = theme.TextDark;
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.FlatAppearance.BorderSize = 1;
                        btn.FlatAppearance.BorderColor = theme.Border;
                        if (btn.Font != FontBedBold8) btn.Font = FontBedBold8;
                        btn.Text = string.Format("เตียง {0:D2}\n● มีข้อมูล", i);
                    } else {
                        btn.BackColor = Color.FromArgb(248, 250, 252);
                        btn.ForeColor = Color.FromArgb(148, 163, 184);
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.FlatAppearance.BorderSize = 1;
                        btn.FlatAppearance.BorderColor = theme.Border;
                        if (btn.Font != FontBedRegular8) btn.Font = FontBedRegular8;
                        btn.Text = string.Format("เตียง {0:D2}\n(ว่าง)", i);
                    }
                }

                if (bedToolTip != null) {
                    string remInfo = (urgent != null) 
                        ? string.Format("\n⏱ ตัวเตือนหัตถการ: {0} ({1})", urgent.Title, urgent.RemainingText) 
                        : "";
                    string tipText = string.Format("เตียง {0:D2} ({1}): {2}{3}\n(คีย์ลัดใน e-PHIS: พิมพ์ .b{0})", 
                        i, theme.ZoneName, manager.GetPreview(i), remInfo);
                    if (bedToolTip.GetToolTip(btn) != tipText) {
                        bedToolTip.SetToolTip(btn, tipText);
                    }
                }
            }

            lblNetworkStatus.Text = manager.IsSharedActive 
                ? "🌐 เชื่อมต่อกับโฟลเดอร์ส่วนกลางของวอร์ดเรียบร้อย (ซิงค์ทุกเครื่อง)" 
                : "💻 โหมดบันทึกในเครื่องนี้ (ยังไม่ได้เชื่อมต่อโฟลเดอร์ส่วนกลาง)";
        }

        private void FilterBeds(string query) {
            string q = query.Trim().ToLower();
            for (int i = 1; i <= 30; i++) {
                Button btn = bedButtons[i];
                if (btn == null) continue;

                if (string.IsNullOrEmpty(q)) {
                    btn.Visible = true;
                } else {
                    string bedStr = "เตียง " + i;
                    string note = manager.GetBedNote(i).ToLower();
                    bool match = (i.ToString() == q || bedStr.Contains(q) || note.Contains(q));
                    btn.Visible = match;
                }
            }
        }

        private void TxtNote_TextChanged(object sender, EventArgs e) {
            if (isSuppressingEvents) return;

            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();

            lblAutoSave.Text = "✏️ กำลังพิมพ์ (บันทึกอัตโนมัติใน 0.5 วินาที)...";
            lblAutoSave.ForeColor = Color.FromArgb(217, 119, 6);

            UpdateCharCount();
        }

        private void UpdateCharCount() {
            string text = txtNote.Text;
            int chars = text.Length;
            int lines = text.Split(new char[] { '\n' }).Length;
            lblCharCount.Text = string.Format("{0} ตัวอักษร | {1} บรรทัด", chars, lines);
            RepositionNoteHeaderControls();
        }

        private void TxtNote_KeyDown(object sender, KeyEventArgs e) {
            if (e.Control && e.KeyCode == Keys.Enter) {
                PasteToActiveWindow();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        public void FlushSave() {
            if (isDirty) {
                autoSaveTimer.Stop();
                manager.SaveBedNote(currentBed, txtNote.Text);
                isDirty = false;
                lblAutoSave.Text = "💾 บันทึกอัตโนมัติแล้ว (" + DateTime.Now.ToString("HH:mm:ss") + " น.)";
                lblAutoSave.ForeColor = Color.FromArgb(21, 128, 61);
                RefreshAllBedButtons();
            }
        }

        private void AddQuickButton(FlowLayoutPanel pnl, string title, string snippet) {
            Button btn = new Button();
            btn.Text = title;
            btn.Font = new Font("Segoe UI", 8f);
            btn.BackColor = Color.White;
            btn.ForeColor = Color.FromArgb(30, 41, 59);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(224, 242, 254);
            btn.Height = 25;
            btn.AutoSize = true;
            btn.Margin = new Padding(2, 1, 2, 1);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => InsertSnippetAtCursor(snippet);
            pnl.Controls.Add(btn);
        }

        private void ShowTemplatesDropdown(Button anchor) {
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.Font = new Font("Segoe UI", 9.5f);
            List<TemplateItem> tpls = context.GetTemplates();

            Dictionary<string, List<TemplateItem>> groups = new Dictionary<string, List<TemplateItem>>();
            foreach (TemplateItem t in tpls) {
                string cat = string.IsNullOrEmpty(t.Category) ? "ทั่วไป" : t.Category;
                if (!groups.ContainsKey(cat)) {
                    groups[cat] = new List<TemplateItem>();
                }
                groups[cat].Add(t);
            }

            foreach (KeyValuePair<string, List<TemplateItem>> kvp in groups) {
                ToolStripMenuItem catMenu = new ToolStripMenuItem("📁 " + kvp.Key);
                foreach (TemplateItem item in kvp.Value) {
                    TemplateItem target = item;
                    ToolStripMenuItem subItem = new ToolStripMenuItem(string.Format("{0} ({1})", target.Title, target.Shortcut));
                    subItem.Click += (s, e) => InsertSnippetAtCursor(target.Content);
                    catMenu.DropDownItems.Add(subItem);
                }
                cms.Items.Add(catMenu);
            }

            cms.Show(anchor, new Point(0, anchor.Height));
        }

        private void ShowQuickRemindDropdown(Button anchor) {
            ContextMenuStrip cms = new ContextMenuStrip();
            cms.Font = new Font("Segoe UI", 9.5f);

            cms.Items.Add(string.Format("⏰ ตั้งเตือนด่วนสำหรับ 'เตียง {0}':", currentBed)).Enabled = false;
            cms.Items.Add("-");

            if (context.ReminderManager != null) {
                List<WardReminderItem> bedReminders = new List<WardReminderItem>();
                foreach (WardReminderItem it in context.ReminderManager.GetAll()) {
                    if (it.BedNum == currentBed && !it.IsCompleted) {
                        bedReminders.Add(it);
                    }
                }
                if (bedReminders.Count > 0) {
                    ToolStripMenuItem mnuExisting = new ToolStripMenuItem(string.Format("📝 รายการที่รอเตือนของเตียงนี้ ({0}) ▼", bedReminders.Count));
                    mnuExisting.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                    foreach (WardReminderItem it in bedReminders) {
                        WardReminderItem closureItem = it;
                        string itemText = string.Format("✏️ {0} ({1:D2}:{2:D2} น. - {3})", 
                            closureItem.Title, closureItem.DueTime.Hour, closureItem.DueTime.Minute, closureItem.RemainingText);
                        mnuExisting.DropDownItems.Add(itemText, null, (s, e) => {
                            if (context.StickyReminderFormInstance != null) {
                                context.StickyReminderFormInstance.OpenEditDialog(closureItem);
                            }
                        });
                    }
                    cms.Items.Add(mnuExisting);
                    cms.Items.Add("-");
                }
            }

            cms.Items.Add("[+30 นาที] วัด V/S และประเมิน SOS ซ้ำ", null, (s, e) => {
                if (context.ReminderManager != null) {
                    context.ReminderManager.Add(currentBed, "วัด V/S และประเมิน SOS ซ้ำ", DateTime.Now.AddMinutes(30));
                    context.ShowNotification(string.Format("ตั้งเตือนเตียง {0}: วัด V/S และ SOS ซ้ำ (อีก 30 นาที)", currentBed));
                    context.ShowStickyReminders();
                }
            });

            cms.Items.Add("[+1 ชั่วโมง] โทรตามผล X-ray / Lab ด่วน", null, (s, e) => {
                if (context.ReminderManager != null) {
                    context.ReminderManager.Add(currentBed, "โทรตามผล X-ray / Lab ด่วน", DateTime.Now.AddMinutes(60));
                    context.ShowNotification(string.Format("ตั้งเตือนเตียง {0}: โทรตามผล X-ray / Lab (อีก 1 ชม.)", currentBed));
                    context.ShowStickyReminders();
                }
            });

            cms.Items.Add("[11:30 น.] เจาะ DTX ก่อนอาหาร", null, (s, e) => {
                DateTime now = DateTime.Now;
                DateTime target = new DateTime(now.Year, now.Month, now.Day, 11, 30, 0);
                if (target <= now) target = target.AddDays(1);
                if (context.ReminderManager != null) {
                    context.ReminderManager.Add(currentBed, "เจาะ DTX ก่อนอาหาร", target);
                    context.ShowNotification(string.Format("ตั้งเตือนเตียง {0}: เจาะ DTX ก่อนอาหาร (11:30 น.)", currentBed));
                    context.ShowStickyReminders();
                }
            });

            cms.Items.Add("[14:00 น.] เตรียมเปลี่ยนถุงน้ำเกลือ (IV)", null, (s, e) => {
                DateTime now = DateTime.Now;
                DateTime target = new DateTime(now.Year, now.Month, now.Day, 14, 0, 0);
                if (target <= now) target = target.AddDays(1);
                if (context.ReminderManager != null) {
                    context.ReminderManager.Add(currentBed, "เตรียมเปลี่ยนถุงน้ำเกลือ (IV)", target);
                    context.ShowNotification(string.Format("ตั้งเตือนเตียง {0}: เตรียมเปลี่ยนถุงน้ำเกลือ (IV) (14:00 น.)", currentBed));
                    context.ShowStickyReminders();
                }
            });

            cms.Items.Add("-");
            cms.Items.Add("➕ กำหนดเวลาและหัตถการเอง...", null, (s, e) => {
                if (context.StickyReminderFormInstance != null) {
                    context.StickyReminderFormInstance.OpenAddDialog(currentBed);
                }
            });
            cms.Items.Add("📌 เปิดดูแผ่นโน้ตเตือนค้างหน้าจอ (Sticky Pad)...", null, (s, e) => {
                context.ShowStickyReminders();
            });

            cms.Show(anchor, new Point(0, anchor.Height));
        }

        private void InsertSnippetAtCursor(string snippet) {
            if (string.IsNullOrEmpty(snippet)) return;
            int selStart = txtNote.SelectionStart;
            string curText = txtNote.Text;

            if (selStart > 0 && curText.Length > 0) {
                char prevChar = curText[selStart - 1];
                if (prevChar != '\n' && prevChar != ' ' && !snippet.StartsWith("\r\n") && !snippet.StartsWith(" ")) {
                    snippet = "\r\n" + snippet;
                }
            }

            txtNote.SelectedText = snippet;
            txtNote.Focus();

            int dotIndex = txtNote.Text.IndexOf("...", selStart);
            if (dotIndex >= 0) {
                txtNote.SelectionStart = dotIndex;
                txtNote.SelectionLength = 3;
            }
        }

        public void InsertSnippetExternal(string snippet) {
            if (this.InvokeRequired) {
                this.BeginInvoke(new Action(() => InsertSnippetExternal(snippet)));
                return;
            }
            InsertSnippetAtCursor(snippet);
        }

        private void InsertDateTimeAtCursor() {
            DateTime now = DateTime.Now;
            int thaiYear = now.Year + 543;
            string dtStr = string.Format("{0:D2}/{1:D2}/{2} {3:D2}:{4:D2} น.", now.Day, now.Month, thaiYear, now.Hour, now.Minute);
            InsertSnippetAtCursor(dtStr);
        }

        private void ClearCurrentBed() {
            DialogResult dr = MessageBox.Show(
                string.Format("คุณต้องการล้างข้อมูลผู้ป่วยของ 'เตียง {0}' หรือไม่?\n\n💡 ระบบจะสำรองข้อมูลปัจจุบันไว้ใน 'ประวัติเตียงย้อนหลัง' อัตโนมัติ หากเผลอลบผิด สามารถเปิดกู้คืนได้ 100%", currentBed),
                "ยืนยันการล้างข้อมูลเตียง",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (dr == DialogResult.Yes) {
                isSuppressingEvents = true;
                txtNote.Text = "";
                isSuppressingEvents = false;
                manager.ClearBedNote(currentBed);
                isDirty = false;
                lblAutoSave.Text = "🗑️ ล้างข้อมูลเตียงนี้แล้ว (สำรองในประวัติย้อนหลังเรียบร้อย กดดูได้ตลอด)";
                lblAutoSave.ForeColor = Color.FromArgb(71, 85, 105);
                UpdateCharCount();
                RefreshAllBedButtons();
            }
        }

        private void PasteToActiveWindow() {
            FlushSave();
            string content = txtNote.Text;
            if (string.IsNullOrEmpty(content.Trim())) {
                MessageBox.Show("เตียงนี้ยังไม่มีข้อมูลบันทึก กรุณาพิมพ์ข้อความก่อนนำไปวาง", "เตียงว่าง", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            this.Hide();

            System.Threading.ThreadPool.QueueUserWorkItem(state => {
                System.Threading.Thread.Sleep(80);
                if (lastActiveWindow != IntPtr.Zero) {
                    SetForegroundWindow(lastActiveWindow);
                    System.Threading.Thread.Sleep(50);
                }
                context.ExecutePaste(0, content);
            });
        }

        private void Manager_OnBedChanged(int bedNum, string content) {
            if (this.InvokeRequired) {
                this.BeginInvoke(new Action(() => Manager_OnBedChanged(bedNum, content)));
                return;
            }

            if (bedNum == currentBed) {
                if (!isDirty) {
                    isSuppressingEvents = true;
                    txtNote.Text = content;
                    isSuppressingEvents = false;
                    lblAutoSave.Text = "🔄 อัปเดตข้อมูลจากเครื่องอื่นในวอร์ดแล้ว (" + DateTime.Now.ToString("HH:mm:ss") + " น.)";
                    lblAutoSave.ForeColor = Color.FromArgb(37, 99, 235);
                    UpdateCharCount();
                } else {
                    lblAutoSave.Text = "⚠️ มีการแก้ไขเตียงนี้จากเครื่องอื่นในวอร์ด";
                    lblAutoSave.ForeColor = Color.Crimson;
                }
            }

            RefreshAllBedButtons();
        }

        public void ShowAndFocus(int targetBed = -1) {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != this.Handle) {
                lastActiveWindow = fg;
            }

            if (targetBed >= 1 && targetBed <= 30) {
                SelectBed(targetBed);
            } else {
                RefreshAllBedButtons();
            }

            this.Show();
            if (this.WindowState == FormWindowState.Minimized) {
                this.WindowState = FormWindowState.Normal;
            }
            this.BringToFront();
            this.Activate();
            txtNote.Focus();

            UpdateBedButtonSizes();
            RepositionTopControls();
            RepositionBottomControls();
            RepositionNoteHeaderControls();
        }
    }

    public class PaletteForm : Form {
        private ExpanderContext context;
        private TextBox txtSearch;
        private ListView lstTemplates;
        private TextBox txtPreview;
        private Button btnPaste;
        private Button btnEdit;
        private Button btnSync;
        private Button btnClose;
        private Button btnZoomIn;
        private Button btnZoomOut;
        private Button btnBedNotes;
        private Label lblFontSize;
        private float currentFontSize = 13.0f;
        private IntPtr lastActiveWindow = IntPtr.Zero;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public PaletteForm(ExpanderContext ctx) {
            context = ctx;
            InitializeUI();
        }

        private void InitializeUI() {
            this.Text = "คลังเทมเพลตบันทึกทางการพยาบาลและการแพทย์ (F8 เพื่อเปิด)";
            this.Size = new Size(940, 640);
            this.MinimumSize = new Size(760, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            this.BackColor = Color.FromArgb(246, 247, 250);

            currentFontSize = context.CurrentFontSize;

            string iconPath = @"C:\PhisApp\Medical_Text_Expander\medical_expander_icon.ico";
            if (!File.Exists(iconPath)) iconPath = @"C:\PhisApp\medical_expander_icon.ico";
            if (File.Exists(iconPath)) {
                try { this.Icon = new Icon(iconPath); } catch {}
            }

            // Top Search Bar & Font Zoom Controls
            Panel pnlTop = new Panel();
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Height = 58;
            pnlTop.BackColor = Color.FromArgb(14, 116, 144);

            Label lblSearch = new Label();
            lblSearch.Text = "🔍 ค้นหา:";
            lblSearch.ForeColor = Color.White;
            lblSearch.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            lblSearch.Location = new Point(14, 16);
            lblSearch.AutoSize = true;
            pnlTop.Controls.Add(lblSearch);

            txtSearch = new TextBox();
            txtSearch.Location = new Point(95, 13);
            txtSearch.Size = new Size(540, 31);
            txtSearch.Font = new Font("Segoe UI", 12f);
            txtSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSearch.TextChanged += TxtSearch_TextChanged;
            txtSearch.KeyDown += TxtSearch_KeyDown;
            pnlTop.Controls.Add(txtSearch);

            lblFontSize = new Label();
            lblFontSize.Text = string.Format("ขนาด: {0:0} pt", currentFontSize);
            lblFontSize.ForeColor = Color.White;
            lblFontSize.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblFontSize.AutoSize = true;
            lblFontSize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblFontSize.Location = new Point(648, 18);
            pnlTop.Controls.Add(lblFontSize);

            btnZoomOut = new Button();
            btnZoomOut.Text = "A -";
            btnZoomOut.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            btnZoomOut.Size = new Size(42, 33);
            btnZoomOut.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnZoomOut.Location = new Point(745, 12);
            btnZoomOut.BackColor = Color.FromArgb(240, 240, 240);
            btnZoomOut.FlatStyle = FlatStyle.Flat;
            btnZoomOut.Cursor = Cursors.Hand;
            btnZoomOut.Click += (s, e) => AdjustFontSize(-1.5f);
            pnlTop.Controls.Add(btnZoomOut);

            btnZoomIn = new Button();
            btnZoomIn.Text = "A +";
            btnZoomIn.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            btnZoomIn.Size = new Size(42, 33);
            btnZoomIn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnZoomIn.Location = new Point(793, 12);
            btnZoomIn.BackColor = Color.FromArgb(240, 240, 240);
            btnZoomIn.FlatStyle = FlatStyle.Flat;
            btnZoomIn.Cursor = Cursors.Hand;
            btnZoomIn.Click += (s, e) => AdjustFontSize(1.5f);
            pnlTop.Controls.Add(btnZoomIn);

            // Bottom Panel
            Panel pnlBottom = new Panel();
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Height = 54;
            pnlBottom.BackColor = Color.FromArgb(238, 240, 246);

            btnPaste = new Button();
            btnPaste.Text = "📋 วางลงหน้าจอ e-PHIS (Enter)";
            btnPaste.Location = new Point(14, 9);
            btnPaste.Size = new Size(230, 36);
            btnPaste.BackColor = Color.FromArgb(13, 148, 136);
            btnPaste.ForeColor = Color.White;
            btnPaste.FlatStyle = FlatStyle.Flat;
            btnPaste.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnPaste.Cursor = Cursors.Hand;
            btnPaste.Click += (s, e) => PasteSelected();
            pnlBottom.Controls.Add(btnPaste);

            btnBedNotes = new Button();
            btnBedNotes.Text = "🛏️ ข้อมูลรายเตียง (F7)";
            btnBedNotes.Location = new Point(252, 9);
            btnBedNotes.Size = new Size(165, 36);
            btnBedNotes.BackColor = Color.FromArgb(13, 148, 136);
            btnBedNotes.ForeColor = Color.White;
            btnBedNotes.FlatStyle = FlatStyle.Flat;
            btnBedNotes.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnBedNotes.Cursor = Cursors.Hand;
            btnBedNotes.Click += (s, e) => {
                this.Hide();
                context.ShowBedNotes();
            };
            pnlBottom.Controls.Add(btnBedNotes);

            btnEdit = new Button();
            btnEdit.Text = "✏️ แก้ไขเทมเพลต";
            btnEdit.Location = new Point(425, 9);
            btnEdit.Size = new Size(125, 36);
            btnEdit.BackColor = Color.FromArgb(225, 228, 238);
            btnEdit.FlatStyle = FlatStyle.Flat;
            btnEdit.Font = new Font("Segoe UI", 9.5f);
            btnEdit.Cursor = Cursors.Hand;
            btnEdit.Click += (s, e) => context.EditTemplates();
            pnlBottom.Controls.Add(btnEdit);

            btnSync = new Button();
            btnSync.Text = "🌐 ซิงค์วอร์ด";
            btnSync.Location = new Point(558, 9);
            btnSync.Size = new Size(95, 36);
            btnSync.BackColor = Color.FromArgb(225, 228, 238);
            btnSync.FlatStyle = FlatStyle.Flat;
            btnSync.Font = new Font("Segoe UI", 9.5f);
            btnSync.Cursor = Cursors.Hand;
            btnSync.Click += (s, e) => context.ShowSyncSettings();
            pnlBottom.Controls.Add(btnSync);

            Button btnCalc = new Button();
            btnCalc.Text = "🧮 คำนวณ SOS/ยา (Alt+C)";
            btnCalc.Location = new Point(660, 9);
            btnCalc.Size = new Size(165, 36);
            btnCalc.BackColor = Color.FromArgb(254, 243, 199);
            btnCalc.ForeColor = Color.FromArgb(180, 83, 9);
            btnCalc.FlatStyle = FlatStyle.Flat;
            btnCalc.FlatAppearance.BorderColor = Color.FromArgb(251, 191, 36);
            btnCalc.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnCalc.Cursor = Cursors.Hand;
            btnCalc.Click += (s, e) => {
                this.Hide();
                context.ShowCalculator();
            };
            pnlBottom.Controls.Add(btnCalc);

            btnClose = new Button();
            btnClose.Text = "ปิด (Esc)";
            btnClose.Location = new Point(830, 9);
            btnClose.Size = new Size(85, 36);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.BackColor = Color.FromArgb(225, 228, 238);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.Font = new Font("Segoe UI", 9.5f);
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => this.Hide();
            pnlBottom.Controls.Add(btnClose);

            // Split Container
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.SplitterDistance = 270;

            // ListView
            lstTemplates = new ListView();
            lstTemplates.Dock = DockStyle.Fill;
            lstTemplates.View = View.Details;
            lstTemplates.FullRowSelect = true;
            lstTemplates.GridLines = true;
            lstTemplates.Columns.Add("คีย์ลัด", 115);
            lstTemplates.Columns.Add("หมวดหมู่การพยาบาล", 260);
            lstTemplates.Columns.Add("ข้อวินิจฉัย / ชื่อเทมเพลต", 510);
            lstTemplates.SelectedIndexChanged += LstTemplates_SelectedIndexChanged;
            lstTemplates.DoubleClick += (s, e) => PasteSelected();
            lstTemplates.MouseWheel += (s, e) => {
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control) {
                    if (e.Delta > 0) AdjustFontSize(1.0f);
                    else if (e.Delta < 0) AdjustFontSize(-1.0f);
                }
            };
            split.Panel1.Controls.Add(lstTemplates);

            // Preview Box
            txtPreview = new TextBox();
            txtPreview.Dock = DockStyle.Fill;
            txtPreview.Multiline = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;
            txtPreview.ReadOnly = true;
            txtPreview.BackColor = Color.White;
            txtPreview.MouseWheel += (s, e) => {
                if ((Control.ModifierKeys & Keys.Control) == Keys.Control) {
                    if (e.Delta > 0) AdjustFontSize(1.0f);
                    else if (e.Delta < 0) AdjustFontSize(-1.0f);
                }
            };
            split.Panel2.Controls.Add(txtPreview);

            ApplyFontSize();

            this.Controls.Add(split);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlTop);

            pnlTop.SendToBack();
            pnlBottom.SendToBack();
            split.BringToFront();

            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape) {
                    this.Hide();
                } else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)) {
                    AdjustFontSize(1.0f);
                    e.Handled = true;
                } else if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)) {
                    AdjustFontSize(-1.0f);
                    e.Handled = true;
                }
            };
        }

        private void ApplyFontSize() {
            try {
                Font fList = new Font("Segoe UI", currentFontSize, FontStyle.Regular);
                lstTemplates.Font = fList;

                Font fPrev;
                try {
                    fPrev = new Font("Leelawadee UI", currentFontSize + 0.5f, FontStyle.Regular);
                } catch {
                    fPrev = new Font("Segoe UI", currentFontSize + 0.5f, FontStyle.Regular);
                }
                txtPreview.Font = fPrev;

                if (lblFontSize != null) {
                    lblFontSize.Text = string.Format("ขนาด: {0:0} pt", currentFontSize);
                }
            } catch {}
        }

        private void AdjustFontSize(float delta) {
            float newSize = currentFontSize + delta;
            if (newSize < 9.5f) newSize = 9.5f;
            if (newSize > 26.0f) newSize = 26.0f;
            currentFontSize = newSize;
            ApplyFontSize();
            context.SaveFontSize(currentFontSize);
        }

        public void ShowAndFocus() {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != this.Handle) {
                lastActiveWindow = fg;
            }
            RefreshList("");
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
            txtSearch.Focus();
            txtSearch.SelectAll();
        }

        private void RefreshList(string filter) {
            lstTemplates.Items.Clear();
            List<TemplateItem> items = context.GetTemplates();
            string query = filter.Trim().ToLower();

            foreach (TemplateItem item in items) {
                if (string.IsNullOrEmpty(query) || 
                    item.Shortcut.ToLower().Contains(query) || 
                    item.Title.ToLower().Contains(query) || 
                    item.Category.ToLower().Contains(query) ||
                    item.Content.ToLower().Contains(query)) {

                    ListViewItem lvi = new ListViewItem(item.Shortcut);
                    lvi.SubItems.Add(item.Category);
                    lvi.SubItems.Add(item.Title);
                    lvi.Tag = item;
                    lstTemplates.Items.Add(lvi);
                }
            }

            if (lstTemplates.Items.Count > 0) {
                lstTemplates.Items[0].Selected = true;
            } else {
                txtPreview.Text = "";
            }
        }

        private void TxtSearch_TextChanged(object sender, EventArgs e) {
            RefreshList(txtSearch.Text);
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.Down) {
                if (lstTemplates.Items.Count > 0) {
                    lstTemplates.Focus();
                }
                e.Handled = true;
            } else if (e.KeyCode == Keys.Enter) {
                PasteSelected();
                e.Handled = true;
            }
        }

        private void LstTemplates_SelectedIndexChanged(object sender, EventArgs e) {
            if (lstTemplates.SelectedItems.Count > 0) {
                TemplateItem item = lstTemplates.SelectedItems[0].Tag as TemplateItem;
                if (item != null) {
                    txtPreview.Text = item.Content;
                }
            }
        }

        private void PasteSelected() {
            if (lstTemplates.SelectedItems.Count == 0) return;
            TemplateItem item = lstTemplates.SelectedItems[0].Tag as TemplateItem;
            if (item == null) return;

            string content = item.Content;
            this.Hide();

            System.Threading.ThreadPool.QueueUserWorkItem(state => {
                System.Threading.Thread.Sleep(80);
                if (lastActiveWindow != IntPtr.Zero) {
                    SetForegroundWindow(lastActiveWindow);
                    System.Threading.Thread.Sleep(50);
                }
                context.ExecutePaste(0, content);
            });
        }
    }

    public class SyncSettingsForm : Form {
        private ExpanderContext context;
        private TextBox txtSharedPath;
        private TextBox txtSharedBedNotes;
        private TextBox txtGitHubRepo;
        private Label lblStatus;
        private Button btnBrowsePath;
        private Button btnBrowseBedNotes;
        private Button btnTest;
        private Button btnSave;
        private Button btnClear;

        public SyncSettingsForm(ExpanderContext ctx, string currentTemplatePath, string currentBedNotesPath) {
            context = ctx;
            InitializeUI(currentTemplatePath, currentBedNotesPath);
        }

        private void InitializeUI(string currentTemplatePath, string currentBedNotesPath) {
            this.Text = "การเชื่อมต่อข้อมูลส่วนกลางของวอร์ด (Network Shared Sync)";
            this.Size = new System.Drawing.Size(620, 440);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(246, 247, 250);
            this.Font = new Font("Segoe UI", 10f);

            string iconPath = @"C:\PhisApp\Medical_Text_Expander\medical_expander_icon.ico";
            if (!File.Exists(iconPath)) iconPath = @"C:\PhisApp\medical_expander_icon.ico";
            if (File.Exists(iconPath)) {
                try { this.Icon = new Icon(iconPath); } catch {}
            }

            Label lblDesc = new Label();
            lblDesc.Text = "คุณสามารถแชร์โฟลเดอร์ส่วนกลางในวงแลนของวอร์ด เพื่อให้ทุกเครื่องในหอผู้ป่วย\n" +
                           "ใช้ชุดเทมเพลต และข้อมูลบันทึกรายเตียง 1-30 ชุดเดียวกันแบบเรียลไทม์:";
            lblDesc.Location = new Point(20, 14);
            lblDesc.Size = new Size(560, 45);
            lblDesc.ForeColor = Color.FromArgb(50, 50, 50);
            this.Controls.Add(lblDesc);

            // 1. Templates Path
            Label lblPath = new Label();
            lblPath.Text = "1. ที่อยู่ไฟล์เทมเพลตส่วนกลาง (medical_templates.txt):";
            lblPath.Location = new Point(20, 68);
            lblPath.AutoSize = true;
            lblPath.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            this.Controls.Add(lblPath);

            txtSharedPath = new TextBox();
            txtSharedPath.Location = new Point(22, 95);
            txtSharedPath.Size = new Size(450, 29);
            txtSharedPath.Font = new Font("Segoe UI", 10f);
            txtSharedPath.Text = currentTemplatePath;
            this.Controls.Add(txtSharedPath);

            btnBrowsePath = new Button();
            btnBrowsePath.Text = "เลือกไฟล์...";
            btnBrowsePath.Location = new Point(480, 93);
            btnBrowsePath.Size = new Size(100, 32);
            btnBrowsePath.Click += (s, e) => {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                ofd.Title = "เลือกไฟล์เทมเพลตส่วนกลาง (medical_templates.txt)";
                if (ofd.ShowDialog() == DialogResult.OK) {
                    txtSharedPath.Text = ofd.FileName;
                    if (string.IsNullOrEmpty(txtSharedBedNotes.Text)) {
                        string dir = Path.GetDirectoryName(ofd.FileName);
                        txtSharedBedNotes.Text = Path.Combine(dir, "ward_bed_notes");
                    }
                }
            };
            this.Controls.Add(btnBrowsePath);

            // 2. Bed Notes Path
            Label lblBedPath = new Label();
            lblBedPath.Text = "2. ที่อยู่โฟลเดอร์ข้อมูลรายเตียง 1-30 (ward_bed_notes):";
            lblBedPath.Location = new Point(20, 138);
            lblBedPath.AutoSize = true;
            lblBedPath.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            this.Controls.Add(lblBedPath);

            txtSharedBedNotes = new TextBox();
            txtSharedBedNotes.Location = new Point(22, 165);
            txtSharedBedNotes.Size = new Size(450, 29);
            txtSharedBedNotes.Font = new Font("Segoe UI", 10f);
            txtSharedBedNotes.Text = currentBedNotesPath;
            this.Controls.Add(txtSharedBedNotes);

            btnBrowseBedNotes = new Button();
            btnBrowseBedNotes.Text = "เลือกโฟลเดอร์...";
            btnBrowseBedNotes.Location = new Point(480, 163);
            btnBrowseBedNotes.Size = new Size(100, 32);
            btnBrowseBedNotes.Click += (s, e) => {
                FolderBrowserDialog fbd = new FolderBrowserDialog();
                fbd.Description = "เลือกโฟลเดอร์สำหรับแชร์ข้อมูลเตียงผู้ป่วยในวอร์ด (ward_bed_notes)";
                if (fbd.ShowDialog() == DialogResult.OK) {
                    txtSharedBedNotes.Text = fbd.SelectedPath;
                }
            };
            this.Controls.Add(btnBrowseBedNotes);

            // 3. GitHub Auto-Update Repository
            Label lblGit = new Label();
            lblGit.Text = "3. ชื่อ GitHub Repository สำหรับตรวจเช็คอัปเดตอัตโนมัติ:";
            lblGit.Location = new Point(20, 208);
            lblGit.AutoSize = true;
            lblGit.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            this.Controls.Add(lblGit);

            txtGitHubRepo = new TextBox();
            txtGitHubRepo.Location = new Point(22, 235);
            txtGitHubRepo.Size = new Size(340, 29);
            txtGitHubRepo.Font = new Font("Segoe UI", 10f);
            txtGitHubRepo.Text = context.GetGitHubRepo();
            this.Controls.Add(txtGitHubRepo);

            Button btnCheckNow = new Button();
            btnCheckNow.Text = "🚀 ตรวจสอบอัปเดตเดี๋ยวนี้";
            btnCheckNow.Location = new Point(370, 233);
            btnCheckNow.Size = new Size(210, 32);
            btnCheckNow.BackColor = Color.FromArgb(224, 231, 255);
            btnCheckNow.ForeColor = Color.FromArgb(67, 56, 202);
            btnCheckNow.FlatStyle = FlatStyle.Flat;
            btnCheckNow.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnCheckNow.Cursor = Cursors.Hand;
            btnCheckNow.Click += (s, e) => {
                AppUpdater.CheckForUpdatesAsync(txtGitHubRepo.Text.Trim(), true, this, context.GetGitHubToken());
            };
            this.Controls.Add(btnCheckNow);

            lblStatus = new Label();
            lblStatus.Location = new Point(22, 275);
            lblStatus.Size = new Size(560, 40);
            lblStatus.Text = string.IsNullOrEmpty(currentTemplatePath) 
                ? "สถานะ: กำลังใช้งานข้อมูลเฉพาะในเครื่องนี้" 
                : "สถานะ: เชื่อมต่อข้อมูลส่วนกลางของวอร์ดอยู่";
            lblStatus.ForeColor = Color.FromArgb(100, 100, 100);
            this.Controls.Add(lblStatus);

            btnTest = new Button();
            btnTest.Text = "🔍 ทดสอบการเชื่อมต่อ";
            btnTest.Location = new System.Drawing.Point(22, 330);
            btnTest.Size = new Size(160, 38);
            btnTest.Click += (s, e) => {
                string pTpl = txtSharedPath.Text.Trim();
                string pBed = txtSharedBedNotes.Text.Trim();

                bool tplOk = string.IsNullOrEmpty(pTpl) || File.Exists(pTpl);
                bool bedOk = string.IsNullOrEmpty(pBed) || Directory.Exists(pBed);

                if (tplOk && bedOk && (!string.IsNullOrEmpty(pTpl) || !string.IsNullOrEmpty(pBed))) {
                    lblStatus.Text = "✅ เชื่อมต่อสำเร็จ! เข้าถึงไฟล์เทมเพลตและโฟลเดอร์เตียงได้ปกติ";
                    lblStatus.ForeColor = Color.DarkGreen;
                } else {
                    lblStatus.Text = "❌ ไม่สามารถเข้าถึงที่อยู่ส่วนกลางที่ระบุได้ กรุณาตรวจสอบสิทธิ์ของวงแลน";
                    lblStatus.ForeColor = Color.Red;
                }
            };
            this.Controls.Add(btnTest);

            btnSave = new Button();
            btnSave.Text = "💾 บันทึกและเชื่อมต่อ";
            btnSave.Location = new System.Drawing.Point(190, 330);
            btnSave.Size = new Size(185, 38);
            btnSave.BackColor = Color.FromArgb(13, 148, 136);
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnSave.Click += (s, e) => {
                string pTpl = txtSharedPath.Text.Trim();
                string pBed = txtSharedBedNotes.Text.Trim();
                context.SaveSettings(pTpl, pBed);
                context.SetGitHubRepo(txtGitHubRepo.Text.Trim());
                MessageBox.Show("บันทึกการตั้งค่าเรียบร้อยแล้ว ทุกเครื่องที่ตั้งชี้มาที่โฟลเดอร์นี้จะได้รับเทมเพลตและข้อมูลเตียงชุดเดียวกันอัตโนมัติ", "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            };
            this.Controls.Add(btnSave);

            btnClear = new Button();
            btnClear.Text = "ใช้ในเครื่องนี้เท่านั้น";
            btnClear.Location = new System.Drawing.Point(385, 330);
            btnClear.Size = new Size(175, 38);
            btnClear.Click += (s, e) => {
                txtSharedPath.Text = "";
                txtSharedBedNotes.Text = "";
                context.SaveSettings("", "");
                MessageBox.Show("ยกเลิกการเชื่อมต่อส่วนกลางแล้ว ระบบจะกลับมาใช้ข้อมูลในเครื่องนี้", "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            };
            this.Controls.Add(btnClear);
        }
    }

    // =========================================================================
    // ประวัติเตียงย้อนหลัง (Bed History Viewer & Recovery)
    // =========================================================================
    public class BedHistoryViewerForm : Form {
        private ExpanderContext context;
        private BedNotesManager manager;
        private int currentBed = 1;
        private ComboBox cmbBed;
        private ListBox lstSnapshots;
        private TextBox txtPreview;
        private Label lblDetails;
        private Label lblStatus;
        private Button btnRestore;
        private Button btnCopy;
        private Button btnClose;
        private List<BedHistoryItem> currentList = new List<BedHistoryItem>();

        public BedHistoryViewerForm(ExpanderContext ctx, BedNotesManager mgr) {
            context = ctx;
            manager = mgr;
            InitializeUI();
        }

        private void InitializeUI() {
            this.Text = "📜 ประวัติข้อมูลเตียงย้อนหลังและการกู้คืน (Bed History & Recovery)";
            this.Size = new Size(860, 600);
            this.MinimumSize = new Size(700, 480);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.5f);
            this.BackColor = Color.FromArgb(248, 250, 252);

            string iconPath = @"C:\PhisApp\Medical_Text_Expander\medical_expander_icon.ico";
            if (!File.Exists(iconPath)) iconPath = @"C:\PhisApp\medical_expander_icon.ico";
            if (File.Exists(iconPath)) {
                try { this.Icon = new Icon(iconPath); } catch {}
            }

            // Top Panel
            Panel pnlTop = new Panel();
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Height = 56;
            pnlTop.BackColor = Color.FromArgb(15, 118, 110); // Teal dark

            Label lblTitle = new Label();
            lblTitle.Text = "📜 ประวัติข้อมูลผู้ป่วยย้อนหลัง (ป้องกันข้อมูลสูญหาย 100%)";
            lblTitle.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.Location = new Point(14, 8);
            lblTitle.AutoSize = true;
            pnlTop.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "เลือกเตียงเพื่อดูเวอร์ชันที่เคยบันทึกไว้ หรือกู้คืนข้อมูลที่เผลอล้าง/ลบผิดกลับมาได้ทันที";
            lblSub.Font = new Font("Segoe UI", 8.5f);
            lblSub.ForeColor = Color.FromArgb(204, 251, 241);
            lblSub.Location = new Point(16, 31);
            lblSub.AutoSize = true;
            pnlTop.Controls.Add(lblSub);

            Label lblBedSelect = new Label();
            lblBedSelect.Text = "เลือกเตียง:";
            lblBedSelect.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblBedSelect.ForeColor = Color.White;
            lblBedSelect.Location = new Point(560, 16);
            lblBedSelect.AutoSize = true;
            lblBedSelect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pnlTop.Controls.Add(lblBedSelect);

            cmbBed = new ComboBox();
            cmbBed.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbBed.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            for (int i = 1; i <= 30; i++) {
                cmbBed.Items.Add(string.Format("เตียง {0}", i));
            }
            cmbBed.Location = new Point(635, 13);
            cmbBed.Size = new Size(110, 28);
            cmbBed.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cmbBed.SelectedIndexChanged += (s, e) => {
                LoadHistoryForBed(cmbBed.SelectedIndex + 1);
            };
            pnlTop.Controls.Add(cmbBed);

            Button btnRefresh = new Button();
            btnRefresh.Text = "🔄 โหลดใหม่";
            btnRefresh.Size = new Size(85, 30);
            btnRefresh.Location = new Point(755, 12);
            btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRefresh.BackColor = Color.FromArgb(204, 251, 241);
            btnRefresh.ForeColor = Color.FromArgb(15, 118, 110);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnRefresh.Click += (s, e) => LoadHistoryForBed(currentBed);
            pnlTop.Controls.Add(btnRefresh);

            this.Controls.Add(pnlTop);

            // Bottom Panel
            Panel pnlBottom = new Panel();
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Height = 52;
            pnlBottom.BackColor = Color.FromArgb(241, 245, 249);
            pnlBottom.Padding = new Padding(12, 8, 12, 8);

            lblStatus = new Label();
            lblStatus.Text = "พร้อมใช้งาน";
            lblStatus.Font = new Font("Segoe UI", 9f);
            lblStatus.ForeColor = Color.FromArgb(71, 85, 105);
            lblStatus.Location = new Point(14, 16);
            lblStatus.AutoSize = true;
            pnlBottom.Controls.Add(lblStatus);

            btnRestore = new Button();
            btnRestore.Text = "📥 กู้คืนข้อมูลมายังเตียงนี้ (Restore)";
            btnRestore.Size = new Size(220, 36);
            btnRestore.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnRestore.Location = new Point(330, 8);
            btnRestore.BackColor = Color.FromArgb(13, 148, 136);
            btnRestore.ForeColor = Color.White;
            btnRestore.FlatStyle = FlatStyle.Flat;
            btnRestore.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnRestore.Cursor = Cursors.Hand;
            btnRestore.Click += (s, e) => RestoreSelectedSnapshot();
            pnlBottom.Controls.Add(btnRestore);

            btnCopy = new Button();
            btnCopy.Text = "📋 คัดลอกข้อความ (Copy)";
            btnCopy.Size = new Size(160, 36);
            btnCopy.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCopy.Location = new Point(560, 8);
            btnCopy.BackColor = Color.FromArgb(226, 232, 240);
            btnCopy.FlatStyle = FlatStyle.Flat;
            btnCopy.Font = new Font("Segoe UI", 9f);
            btnCopy.Cursor = Cursors.Hand;
            btnCopy.Click += (s, e) => CopySelectedSnapshot();
            pnlBottom.Controls.Add(btnCopy);

            btnClose = new Button();
            btnClose.Text = "ปิด (Esc)";
            btnClose.Size = new Size(95, 36);
            btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnClose.Location = new Point(730, 8);
            btnClose.BackColor = Color.FromArgb(226, 232, 240);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.Font = new Font("Segoe UI", 9f);
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => this.Close();
            pnlBottom.Controls.Add(btnClose);

            this.Controls.Add(pnlBottom);

            // Split Center
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterDistance = 330;
            split.Panel1MinSize = 240;
            split.Panel2MinSize = 300;

            // Panel 1: List of snapshots
            Panel pnlListHeader = new Panel();
            pnlListHeader.Dock = DockStyle.Top;
            pnlListHeader.Height = 32;
            pnlListHeader.BackColor = Color.FromArgb(226, 232, 240);

            Label lblListHeader = new Label();
            lblListHeader.Text = "🕒 ประวัติและเวอร์ชันย้อนหลัง (ใหม่สุดอยู่บน):";
            lblListHeader.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblListHeader.ForeColor = Color.FromArgb(30, 41, 59);
            lblListHeader.Location = new Point(8, 7);
            lblListHeader.AutoSize = true;
            pnlListHeader.Controls.Add(lblListHeader);

            lstSnapshots = new ListBox();
            lstSnapshots.Dock = DockStyle.Fill;
            lstSnapshots.Font = new Font("Segoe UI", 9f);
            lstSnapshots.ItemHeight = 22;
            lstSnapshots.SelectedIndexChanged += (s, e) => ShowSelectedSnapshot();

            split.Panel1.Controls.Add(lstSnapshots);
            split.Panel1.Controls.Add(pnlListHeader);

            // Panel 2: Preview
            Panel pnlPreviewHeader = new Panel();
            pnlPreviewHeader.Dock = DockStyle.Top;
            pnlPreviewHeader.Height = 44;
            pnlPreviewHeader.BackColor = Color.FromArgb(241, 245, 249);
            pnlPreviewHeader.Padding = new Padding(8, 6, 8, 4);

            lblDetails = new Label();
            lblDetails.Text = "คลิกเลือกรายการทางซ้ายเพื่อดูเนื้อหาข้อความ";
            lblDetails.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblDetails.ForeColor = Color.FromArgb(15, 23, 42);
            lblDetails.Dock = DockStyle.Fill;
            pnlPreviewHeader.Controls.Add(lblDetails);

            txtPreview = new TextBox();
            txtPreview.Dock = DockStyle.Fill;
            txtPreview.Multiline = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;
            txtPreview.ReadOnly = true;
            txtPreview.BackColor = Color.White;
            txtPreview.ForeColor = Color.FromArgb(15, 23, 42);
            txtPreview.Font = new Font("Segoe UI", 10.5f);

            split.Panel2.Controls.Add(txtPreview);
            split.Panel2.Controls.Add(pnlPreviewHeader);

            this.Controls.Add(split);
            split.BringToFront();

            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape) this.Close();
            };
        }

        public void ShowAndFocus(int bedNum = 1) {
            if (bedNum < 1 || bedNum > 30) bedNum = 1;
            currentBed = bedNum;
            cmbBed.SelectedIndex = currentBed - 1;
            LoadHistoryForBed(currentBed);
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }

        private void LoadHistoryForBed(int bedNum) {
            currentBed = bedNum;
            lstSnapshots.Items.Clear();
            txtPreview.Text = "";
            lblDetails.Text = string.Format("ประวัติของ เตียง {0}:", currentBed);

            if (manager == null) return;
            currentList = manager.GetBedHistory(bedNum);

            if (currentList.Count == 0) {
                lstSnapshots.Items.Add("(ยังไม่มีประวัติย้อนหลังของเตียงนี้)");
                lblStatus.Text = string.Format("เตียง {0}: ไม่พบประวัติสำรอง (ระบบจะเริ่มบันทึกประวัติทันทีเมื่อมีการล้างเตียงหรือแก้ไขข้อมูล)", bedNum);
                lblStatus.ForeColor = Color.FromArgb(100, 116, 139);
                btnRestore.Enabled = false;
                btnCopy.Enabled = false;
            } else {
                foreach (BedHistoryItem item in currentList) {
                    lstSnapshots.Items.Add(item.DisplayText);
                }
                lstSnapshots.SelectedIndex = 0;
                lblStatus.Text = string.Format("เตียง {0}: พบประวัติย้อนหลัง {1} รายการ", bedNum, currentList.Count);
                lblStatus.ForeColor = Color.FromArgb(15, 118, 110);
                btnRestore.Enabled = true;
                btnCopy.Enabled = true;
            }
        }

        private void ShowSelectedSnapshot() {
            int idx = lstSnapshots.SelectedIndex;
            if (idx >= 0 && idx < currentList.Count) {
                BedHistoryItem item = currentList[idx];
                txtPreview.Text = item.Content;
                lblDetails.Text = string.Format("📌 บันทึกเมื่อ: {0:dd/MM/yyyy HH:mm:ss} | สาเหตุ: {1} | ขนาด: {2} ตัวอักษร", 
                    item.Timestamp, item.Reason, item.CharCount);
                btnRestore.Enabled = true;
                btnCopy.Enabled = true;
            } else {
                txtPreview.Text = "";
                btnRestore.Enabled = false;
                btnCopy.Enabled = false;
            }
        }

        private void RestoreSelectedSnapshot() {
            int idx = lstSnapshots.SelectedIndex;
            if (idx < 0 || idx >= currentList.Count) return;

            BedHistoryItem item = currentList[idx];
            DialogResult dr = MessageBox.Show(
                string.Format("ต้องการกู้คืนข้อมูลเวอร์ชัน '{0}'\nกลับมาเป็นข้อมูลปัจจุบันของ 'เตียง {1}' หรือไม่?\n\n(ข้อมูลปัจจุบันจะถูกบันทึกสำรองไว้ในประวัติก่อนกู้คืน เพื่อป้องกันข้อมูลสูญหาย)",
                    item.Timestamp.ToString("dd/MM/yyyy HH:mm:ss"), currentBed),
                "ยืนยันการกู้คืนข้อมูล",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (dr == DialogResult.Yes) {
                string currentNote = manager.GetBedNote(currentBed);
                if (!string.IsNullOrEmpty(currentNote)) {
                    manager.SaveHistorySnapshot(currentBed, "ก่อนกู้คืนข้อมูลย้อนหลัง", currentNote);
                }

                manager.SaveBedNote(currentBed, item.Content);
                context.ShowBedNotes(currentBed);
                context.ShowNotification(string.Format("กู้คืนข้อมูลเตียง {0} สำเร็จเรียบร้อยแล้ว", currentBed));
                LoadHistoryForBed(currentBed);
            }
        }

        private void CopySelectedSnapshot() {
            int idx = lstSnapshots.SelectedIndex;
            if (idx < 0 || idx >= currentList.Count) return;

            BedHistoryItem item = currentList[idx];
            try {
                Clipboard.SetDataObject(item.Content, true, 5, 50);
                context.ShowNotification(string.Format("คัดลอกข้อมูลประวัติเตียง {0} เรียบร้อยแล้ว", currentBed));
            } catch (Exception ex) {
                MessageBox.Show("ไม่สามารถคัดลอกได้: " + ex.Message);
            }
        }
    }

    // =========================================================================
    // ระบบตัวเตือนเวลาทำหัตถการรายเตียง (Ward Care & Task Reminders)
    // =========================================================================
    public class WardReminderItem {
        public string Id { get; set; }
        public int BedNum { get; set; }
        public string Title { get; set; }
        public DateTime DueTime { get; set; }
        public DateTime CreatedTime { get; set; }
        public bool IsCompleted { get; set; }
        public bool HasAlerted { get; set; }

        public WardReminderItem() {
            Id = Guid.NewGuid().ToString("N");
            CreatedTime = DateTime.Now;
            DueTime = DateTime.Now.AddMinutes(30);
            IsCompleted = false;
            HasAlerted = false;
        }

        public string BedText {
            get {
                return BedNum > 0 ? string.Format("เตียง {0:D2}", BedNum) : "ทั่วไป";
            }
        }

        public string RemainingText {
            get {
                if (IsCompleted) return "เสร็จสิ้นแล้ว";
                TimeSpan diff = DueTime - DateTime.Now;
                if (diff.TotalSeconds <= 0) {
                    int overMin = (int)Math.Abs(diff.TotalMinutes);
                    return overMin == 0 ? "🚨 ถึงเวลาแล้ว!" : string.Format("⚠️ เลยเวลา {0} นาที", overMin);
                }
                if (diff.TotalHours >= 1) {
                    return string.Format("⏳ อีก {0} ชม. {1} นาที", (int)diff.TotalHours, diff.Minutes);
                }
                int min = (int)Math.Ceiling(diff.TotalMinutes);
                if (min <= 1) return "⏳ อีกไม่ถึง 1 นาที";
                return string.Format("⏳ อีก {0} นาที", min);
            }
        }

        public bool IsDue {
            get {
                return !IsCompleted && DateTime.Now >= DueTime;
            }
        }
    }

    public class WardReminderManager {
        private string filePath;
        private List<WardReminderItem> reminders = new List<WardReminderItem>();
        private object syncLock = new object();
        private System.Windows.Forms.Timer checkTimer;
        public event Action<WardReminderItem> OnReminderDue;
        public event Action OnRemindersChanged;

        public WardReminderManager(string baseDir, string localBedNotesDir) {
            filePath = Path.Combine(baseDir, "ward_reminders.txt");
            Load();

            checkTimer = new System.Windows.Forms.Timer();
            checkTimer.Interval = 4000;
            checkTimer.Tick += (s, e) => CheckDueItems();
            checkTimer.Start();
        }

        public void Load() {
            lock (syncLock) {
                reminders.Clear();
                if (File.Exists(filePath)) {
                    try {
                        string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                        foreach (string line in lines) {
                            string t = line.Trim();
                            if (string.IsNullOrEmpty(t) || t.StartsWith("#")) continue;
                            string[] parts = t.Split(new char[] { '|' }, 6);
                            if (parts.Length >= 6) {
                                WardReminderItem item = new WardReminderItem();
                                item.Id = parts[0];
                                int b; if (int.TryParse(parts[1], out b)) item.BedNum = b;
                                DateTime dt;
                                if (DateTime.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt)) {
                                    item.DueTime = dt;
                                }
                                item.IsCompleted = (parts[3] == "1" || parts[3].ToLower() == "true");
                                item.HasAlerted = (parts[4] == "1" || parts[4].ToLower() == "true");
                                item.Title = parts[5];
                                reminders.Add(item);
                            }
                        }
                    } catch {}
                }
            }
        }

        public void Save() {
            lock (syncLock) {
                try {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("# Ward Reminders: Id|BedNum|DueTime|IsCompleted|HasAlerted|Title");
                    foreach (WardReminderItem item in reminders) {
                        sb.AppendLine(string.Format("{0}|{1}|{2}|{3}|{4}|{5}",
                            item.Id,
                            item.BedNum,
                            item.DueTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                            item.IsCompleted ? "1" : "0",
                            item.HasAlerted ? "1" : "0",
                            (item.Title ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", "/")));
                    }
                    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                } catch {}
            }
        }

        public List<WardReminderItem> GetActive() {
            lock (syncLock) {
                List<WardReminderItem> list = new List<WardReminderItem>();
                foreach (WardReminderItem it in reminders) {
                    if (!it.IsCompleted) list.Add(it);
                }
                list.Sort((a, b) => a.DueTime.CompareTo(b.DueTime));
                return list;
            }
        }

        public List<WardReminderItem> GetAll() {
            lock (syncLock) {
                List<WardReminderItem> list = new List<WardReminderItem>(reminders);
                list.Sort((a, b) => {
                    if (a.IsCompleted != b.IsCompleted) return a.IsCompleted.CompareTo(b.IsCompleted);
                    return a.DueTime.CompareTo(b.DueTime);
                });
                return list;
            }
        }

        public void Add(int bedNum, string title, DateTime dueTime) {
            WardReminderItem item = new WardReminderItem();
            item.BedNum = bedNum;
            item.Title = title;
            item.DueTime = new DateTime(dueTime.Year, dueTime.Month, dueTime.Day, dueTime.Hour, dueTime.Minute, 0);
            item.IsCompleted = false;
            item.HasAlerted = (DateTime.Now >= item.DueTime);
            lock (syncLock) {
                reminders.Add(item);
            }
            Save();
            if (OnRemindersChanged != null) OnRemindersChanged();
        }

        public void Update(string id, int bedNum, string title, DateTime dueTime) {
            lock (syncLock) {
                foreach (WardReminderItem it in reminders) {
                    if (it.Id == id) {
                        it.BedNum = bedNum;
                        it.Title = title;
                        it.DueTime = new DateTime(dueTime.Year, dueTime.Month, dueTime.Day, dueTime.Hour, dueTime.Minute, 0);
                        if (DateTime.Now < it.DueTime) {
                            it.HasAlerted = false;
                        }
                        break;
                    }
                }
            }
            Save();
            if (OnRemindersChanged != null) OnRemindersChanged();
        }

        public void Postpone(string id, int additionalMinutes) {
            lock (syncLock) {
                foreach (WardReminderItem it in reminders) {
                    if (it.Id == id) {
                        DateTime baseTime = it.DueTime > DateTime.Now ? it.DueTime : DateTime.Now;
                        DateTime target = baseTime.AddMinutes(additionalMinutes);
                        it.DueTime = new DateTime(target.Year, target.Month, target.Day, target.Hour, target.Minute, 0);
                        it.HasAlerted = false;
                        it.IsCompleted = false;
                        break;
                    }
                }
            }
            Save();
            if (OnRemindersChanged != null) OnRemindersChanged();
        }

        public void MarkCompleted(string id, bool completed) {
            lock (syncLock) {
                foreach (WardReminderItem it in reminders) {
                    if (it.Id == id) {
                        it.IsCompleted = completed;
                        break;
                    }
                }
            }
            Save();
            if (OnRemindersChanged != null) OnRemindersChanged();
        }

        public void Delete(string id) {
            lock (syncLock) {
                reminders.RemoveAll(it => it.Id == id);
            }
            Save();
            if (OnRemindersChanged != null) OnRemindersChanged();
        }

        public void ClearCompleted() {
            lock (syncLock) {
                reminders.RemoveAll(it => it.IsCompleted);
            }
            Save();
            if (OnRemindersChanged != null) OnRemindersChanged();
        }

        public WardReminderItem GetMostUrgentReminder(int bedNum) {
            lock (syncLock) {
                WardReminderItem mostUrgent = null;
                foreach (WardReminderItem it in reminders) {
                    if (it.BedNum == bedNum && !it.IsCompleted) {
                        if (mostUrgent == null || it.DueTime < mostUrgent.DueTime) {
                            mostUrgent = it;
                        }
                    }
                }
                return mostUrgent;
            }
        }

        private void CheckDueItems() {
            List<WardReminderItem> dueToAlert = new List<WardReminderItem>();
            lock (syncLock) {
                DateTime now = DateTime.Now;
                foreach (WardReminderItem it in reminders) {
                    if (!it.IsCompleted && !it.HasAlerted && now >= it.DueTime) {
                        it.HasAlerted = true;
                        dueToAlert.Add(it);
                    }
                }
            }
            if (dueToAlert.Count > 0) {
                Save();
                if (OnRemindersChanged != null) OnRemindersChanged();
                foreach (WardReminderItem it in dueToAlert) {
                    if (OnReminderDue != null) OnReminderDue(it);
                }
            }
        }
    }

    public class AddReminderDialog : Form {
        private WardReminderItem editingItem;
        private ComboBox cmbBed;
        private TextBox txtTitle;
        private RadioButton radRelative;
        private RadioButton radExact;
        private NumericUpDown numMinutes;
        private DateTimePicker dtExactTime;

        public int SelectedBed { get; private set; }
        public string ReminderTitle { get; private set; }
        public DateTime DueTime { get; private set; }
        public bool IsDeleted { get; private set; }

        public AddReminderDialog(WardReminderItem itemToEdit, int defaultBed = 1, string defaultTitle = "", int defaultMinutes = 30) {
            editingItem = itemToEdit;
            InitializeDialog(defaultBed, defaultTitle, defaultMinutes);
        }

        public AddReminderDialog(int defaultBed = 1, string defaultTitle = "", int defaultMinutes = 30) {
            editingItem = null;
            InitializeDialog(defaultBed, defaultTitle, defaultMinutes);
        }

        private void InitializeDialog(int defaultBed, string defaultTitle, int defaultMinutes) {
            bool isEditing = (editingItem != null);
            this.Text = isEditing ? "✏️ แก้ไขการแจ้งเตือนหัตถการ (Edit Bed Reminder)" : "⏰ ตั้งเตือนเวลาทำหัตถการรายเตียง (Set Bed Reminder)";
            this.Size = new Size(620, 625);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9.5f);
            this.BackColor = Color.FromArgb(248, 250, 252);

            string iconPath = @"C:\PhisApp\Medical_Text_Expander\medical_expander_icon.ico";
            if (!File.Exists(iconPath)) iconPath = @"C:\PhisApp\medical_expander_icon.ico";
            if (File.Exists(iconPath)) {
                try { this.Icon = new Icon(iconPath); } catch {}
            }

            Panel pnlTop = new Panel();
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Height = 46;
            pnlTop.BackColor = isEditing ? Color.FromArgb(13, 148, 136) : Color.FromArgb(245, 158, 11);

            Label lblTop = new Label();
            lblTop.Text = isEditing 
                ? (editingItem.BedNum > 0 ? string.Format("✏️ แก้ไขการแจ้งเตือนหัตถการ เตียง {0:D2}", editingItem.BedNum) : "✏️ แก้ไขการแจ้งเตือนหัตถการทั่วไป")
                : "⏰ ตั้งเตือนเวลาตรวจซ้ำ / ทำหัตถการประจำเตียง";
            lblTop.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            lblTop.ForeColor = Color.White;
            lblTop.Location = new Point(14, 11);
            lblTop.AutoSize = true;
            pnlTop.Controls.Add(lblTop);
            this.Controls.Add(pnlTop);

            int y = 54;

            Label lblBed = new Label();
            lblBed.Text = "หมายเลขเตียง:";
            lblBed.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblBed.Location = new Point(16, y);
            lblBed.AutoSize = true;
            this.Controls.Add(lblBed);

            cmbBed = new ComboBox();
            cmbBed.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbBed.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            for (int i = 1; i <= 30; i++) {
                cmbBed.Items.Add(string.Format("เตียง {0:D2}", i));
            }
            cmbBed.Items.Add("ทั่วไป (วอร์ด)");

            int bedToSelect = isEditing ? editingItem.BedNum : defaultBed;
            if (bedToSelect >= 1 && bedToSelect <= 30) cmbBed.SelectedIndex = bedToSelect - 1;
            else cmbBed.SelectedIndex = 30;
            cmbBed.Location = new Point(120, y - 3);
            cmbBed.Size = new Size(150, 28);
            this.Controls.Add(cmbBed);

            y += 34;
            Label lblPresets = new Label();
            lblPresets.Text = "📋 ปุ่มลัดชื่อหัตถการ (คลิกเพื่อใส่ข้อความด่วน โดยไม่ต้องพิมพ์ซ้ำ):";
            lblPresets.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblPresets.ForeColor = Color.FromArgb(71, 85, 105);
            lblPresets.Location = new Point(16, y);
            lblPresets.AutoSize = true;
            this.Controls.Add(lblPresets);

            FlowLayoutPanel pnlPresets = new FlowLayoutPanel();
            pnlPresets.Location = new Point(16, y + 20);
            pnlPresets.Size = new Size(572, 60);
            pnlPresets.WrapContents = true;

            // Pure procedure shortcut buttons without time
            AddProcedurePresetButton(pnlPresets, "🩺 วัด V/S & SOS ซ้ำ", "วัด V/S และประเมิน SOS ซ้ำ");
            AddProcedurePresetButton(pnlPresets, "📞 โทรตามผล X-ray / Lab ด่วน", "โทรตามผล X-ray / Lab ด่วน");
            AddProcedurePresetButton(pnlPresets, "🩸 เจาะ DTX ก่อนอาหาร", "เจาะ DTX ก่อนอาหาร");
            AddProcedurePresetButton(pnlPresets, "💧 เตรียมเปลี่ยนถุง IV", "เตรียมเปลี่ยนถุงน้ำเกลือ (IV)");
            AddProcedurePresetButton(pnlPresets, "🩹 ตรวจแผล / ให้ยา ATB", "ตรวจแผล / ให้ยา ATB");
            AddProcedurePresetButton(pnlPresets, "🩸 สังเกตอาการหลังให้เลือด", "สังเกตอาการหลังให้เลือด");
            AddProcedurePresetButton(pnlPresets, "🧪 วัด I/O & ปัสสาวะ", "วัด I/O และสังเกตปัสสาวะ");
            AddProcedurePresetButton(pnlPresets, "💊 ประเมินอาการหลังให้ยา", "ประเมินอาการหลังให้ยา");

            this.Controls.Add(pnlPresets);

            y += 84;
            Label lblTitle = new Label();
            lblTitle.Text = "📝 รายละเอียดงาน / คำสั่งหัตถการที่ต้องทำ (ช่องพิมพ์เด่นชัด):";
            lblTitle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(15, 23, 42);
            lblTitle.Location = new Point(16, y);
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            txtTitle = new TextBox();
            txtTitle.Multiline = true;
            txtTitle.ScrollBars = ScrollBars.Vertical;
            txtTitle.Font = new Font("Segoe UI", 11f);
            txtTitle.BackColor = Color.White;
            txtTitle.ForeColor = Color.FromArgb(15, 23, 42);
            txtTitle.Location = new Point(16, y + 23);
            txtTitle.Size = new Size(572, 68);
            if (isEditing) {
                txtTitle.Text = editingItem.Title;
            } else {
                txtTitle.Text = string.IsNullOrEmpty(defaultTitle) ? "วัด V/S และประเมิน SOS ซ้ำ" : defaultTitle;
            }
            this.Controls.Add(txtTitle);

            y += 98;
            GroupBox grpTime = new GroupBox();
            grpTime.Text = "⏰ กำหนดเวลาเตือน (เลือกเวลาได้ง่าย ไม่มีวินาที)";
            grpTime.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            grpTime.Location = new Point(16, y);
            grpTime.Size = new Size(572, 240);

            // Row 1: Exact Clock Time & Minute Chips
            radExact = new RadioButton();
            radExact.Text = "🕒 ระบุเวลาตรง:";
            radExact.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            radExact.Location = new Point(12, 22);
            radExact.AutoSize = true;
            grpTime.Controls.Add(radExact);

            dtExactTime = new DateTimePicker();
            dtExactTime.Format = DateTimePickerFormat.Custom;
            dtExactTime.CustomFormat = "HH:mm";
            dtExactTime.ShowUpDown = true;
            dtExactTime.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            dtExactTime.Location = new Point(115, 19);
            dtExactTime.Size = new Size(80, 26);
            if (isEditing) {
                dtExactTime.Value = new DateTime(editingItem.DueTime.Year, editingItem.DueTime.Month, editingItem.DueTime.Day, editingItem.DueTime.Hour, editingItem.DueTime.Minute, 0);
                radExact.Checked = true;
            } else {
                dtExactTime.Value = DateTime.Now.AddMinutes(30);
                radExact.Checked = true;
            }
            grpTime.Controls.Add(dtExactTime);

            FlowLayoutPanel pnlMinChips = new FlowLayoutPanel();
            pnlMinChips.Location = new Point(202, 18);
            pnlMinChips.Size = new Size(362, 28);
            pnlMinChips.WrapContents = false;
            pnlMinChips.Margin = new Padding(0);

            AddExactMinuteChip(pnlMinChips, ":00 ตรง", 0);
            AddExactMinuteChip(pnlMinChips, ":15 น.", 15);
            AddExactMinuteChip(pnlMinChips, ":30 น.", 30);
            AddExactMinuteChip(pnlMinChips, ":45 น.", 45);
            AddNowChip(pnlMinChips, "ตอนนี้");
            AddMinuteStepChip(pnlMinChips, "+15น.", 15);
            AddMinuteStepChip(pnlMinChips, "+30น.", 30);
            grpTime.Controls.Add(pnlMinChips);

            // Row 2: Clock Section Label
            Label lblClockSection = new Label();
            lblClockSection.Text = "🕒 เลือกรอบเวลาตามเข็มนาฬิกา (ทุก 1 ชั่วโมง ทั้ง 24 ชั่วโมง):";
            lblClockSection.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblClockSection.ForeColor = Color.FromArgb(71, 85, 105);
            lblClockSection.Location = new Point(12, 52);
            lblClockSection.AutoSize = true;
            grpTime.Controls.Add(lblClockSection);

            // Shift 1: ☀️ Morning shift (08:00 - 15:00)
            Label lblShiftM = new Label();
            lblShiftM.Text = "☀️ เช้า:";
            lblShiftM.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            lblShiftM.ForeColor = Color.FromArgb(180, 83, 9);
            lblShiftM.Location = new Point(12, 73);
            lblShiftM.Size = new Size(52, 25);
            lblShiftM.TextAlign = ContentAlignment.MiddleRight;
            grpTime.Controls.Add(lblShiftM);

            FlowLayoutPanel pnlShiftM = new FlowLayoutPanel();
            pnlShiftM.Location = new Point(68, 71);
            pnlShiftM.Size = new Size(495, 27);
            pnlShiftM.WrapContents = false;
            pnlShiftM.Margin = new Padding(0);
            for (int h = 8; h <= 15; h++) {
                AddHourButton(pnlShiftM, h);
            }
            grpTime.Controls.Add(pnlShiftM);

            // Shift 2: 🌅 Afternoon shift (16:00 - 23:00)
            Label lblShiftA = new Label();
            lblShiftA.Text = "🌅 บ่าย:";
            lblShiftA.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            lblShiftA.ForeColor = Color.FromArgb(109, 40, 217);
            lblShiftA.Location = new Point(12, 102);
            lblShiftA.Size = new Size(52, 25);
            lblShiftA.TextAlign = ContentAlignment.MiddleRight;
            grpTime.Controls.Add(lblShiftA);

            FlowLayoutPanel pnlShiftA = new FlowLayoutPanel();
            pnlShiftA.Location = new Point(68, 100);
            pnlShiftA.Size = new Size(495, 27);
            pnlShiftA.WrapContents = false;
            pnlShiftA.Margin = new Padding(0);
            for (int h = 16; h <= 23; h++) {
                AddHourButton(pnlShiftA, h);
            }
            grpTime.Controls.Add(pnlShiftA);

            // Shift 3: 🌙 Night shift (00:00 - 07:00)
            Label lblShiftN = new Label();
            lblShiftN.Text = "🌙 ดึก:";
            lblShiftN.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            lblShiftN.ForeColor = Color.FromArgb(30, 58, 138);
            lblShiftN.Location = new Point(12, 131);
            lblShiftN.Size = new Size(52, 25);
            lblShiftN.TextAlign = ContentAlignment.MiddleRight;
            grpTime.Controls.Add(lblShiftN);

            FlowLayoutPanel pnlShiftN = new FlowLayoutPanel();
            pnlShiftN.Location = new Point(68, 129);
            pnlShiftN.Size = new Size(495, 27);
            pnlShiftN.WrapContents = false;
            pnlShiftN.Margin = new Padding(0);
            for (int h = 0; h <= 7; h++) {
                AddHourButton(pnlShiftN, h);
            }
            grpTime.Controls.Add(pnlShiftN);

            // Row 5: Relative countdown
            radRelative = new RadioButton();
            radRelative.Text = "⏱️ หรือนับถอยหลังอีก:";
            radRelative.Font = new Font("Segoe UI", 9f);
            radRelative.Location = new Point(12, 166);
            radRelative.AutoSize = true;
            grpTime.Controls.Add(radRelative);

            numMinutes = new NumericUpDown();
            numMinutes.Minimum = 1;
            numMinutes.Maximum = 1440;
            numMinutes.Value = defaultMinutes > 0 ? defaultMinutes : 30;
            numMinutes.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            numMinutes.Location = new Point(155, 164);
            numMinutes.Size = new Size(60, 26);
            grpTime.Controls.Add(numMinutes);

            Label lblMinUnit = new Label();
            lblMinUnit.Text = "นาที";
            lblMinUnit.Font = new Font("Segoe UI", 9f);
            lblMinUnit.Location = new Point(218, 167);
            lblMinUnit.AutoSize = true;
            grpTime.Controls.Add(lblMinUnit);

            FlowLayoutPanel pnlRelChips = new FlowLayoutPanel();
            pnlRelChips.Location = new Point(252, 162);
            pnlRelChips.Size = new Size(310, 28);
            pnlRelChips.WrapContents = false;
            pnlRelChips.Margin = new Padding(0);

            AddRelativeMinuteChip(pnlRelChips, "+15น.", 15);
            AddRelativeMinuteChip(pnlRelChips, "+30น.", 30);
            AddRelativeMinuteChip(pnlRelChips, "+45น.", 45);
            AddRelativeMinuteChip(pnlRelChips, "+1ชม.", 60);
            AddRelativeMinuteChip(pnlRelChips, "+2ชม.", 120);
            AddRelativeMinuteChip(pnlRelChips, "+4ชม.", 240);
            grpTime.Controls.Add(pnlRelChips);

            // Bottom tip label
            Label lblHint = new Label();
            lblHint.Text = "💡 คลิกปุ่มชั่วโมงเพื่อตั้งเวลารอบนั้นๆ ทันที และสามารถกด [:15] [:30] [:45] เพื่อปรับเศษนาทีได้สะดวกรวดเร็ว";
            lblHint.Font = new Font("Segoe UI", 8f, FontStyle.Italic);
            lblHint.ForeColor = Color.FromArgb(100, 116, 139);
            lblHint.Location = new Point(12, 202);
            lblHint.AutoSize = true;
            grpTime.Controls.Add(lblHint);

            radRelative.CheckedChanged += (s, e) => {
                numMinutes.Enabled = radRelative.Checked;
                dtExactTime.Enabled = !radRelative.Checked;
            };
            radExact.CheckedChanged += (s, e) => {
                numMinutes.Enabled = radRelative.Checked;
                dtExactTime.Enabled = !radRelative.Checked;
            };
            numMinutes.Enabled = radRelative.Checked;
            dtExactTime.Enabled = radExact.Checked;

            this.Controls.Add(grpTime);

            y += 250;
            if (isEditing) {
                Button btnDeleteInDlg = new Button();
                btnDeleteInDlg.Text = "🗑️ ลบรายการนี้";
                btnDeleteInDlg.Font = new Font("Segoe UI", 9.5f);
                btnDeleteInDlg.BackColor = Color.FromArgb(254, 242, 242);
                btnDeleteInDlg.ForeColor = Color.FromArgb(220, 38, 38);
                btnDeleteInDlg.FlatStyle = FlatStyle.Flat;
                btnDeleteInDlg.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);
                btnDeleteInDlg.Location = new Point(16, y);
                btnDeleteInDlg.Size = new Size(130, 38);
                btnDeleteInDlg.Cursor = Cursors.Hand;
                btnDeleteInDlg.Click += (s, e) => {
                    if (MessageBox.Show("ต้องการลบการแจ้งเตือนหัตถการนี้ใช่หรือไม่?", "ยืนยันการลบ", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                        IsDeleted = true;
                        this.DialogResult = DialogResult.OK;
                        this.Close();
                    }
                };
                this.Controls.Add(btnDeleteInDlg);
            }

            Button btnSave = new Button();
            btnSave.Text = isEditing ? "💾 บันทึกการแก้ไข (Save)" : "✅ บันทึกตัวเตือน (Add Reminder)";
            btnSave.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnSave.BackColor = isEditing ? Color.FromArgb(13, 148, 136) : Color.FromArgb(245, 158, 11);
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.Location = isEditing ? new Point(160, y) : new Point(150, y);
            btnSave.Size = isEditing ? new Size(230, 38) : new Size(240, 38);
            btnSave.Cursor = Cursors.Hand;
            btnSave.Click += (s, e) => {
                string title = txtTitle.Text.Trim();
                if (string.IsNullOrEmpty(title)) {
                    MessageBox.Show("กรุณาระบุรายละเอียดหัตถการ", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SelectedBed = cmbBed.SelectedIndex == 30 ? 0 : cmbBed.SelectedIndex + 1;
                ReminderTitle = title;
                if (radRelative.Checked) {
                    DateTime calc = DateTime.Now.AddMinutes((double)numMinutes.Value);
                    DueTime = new DateTime(calc.Year, calc.Month, calc.Day, calc.Hour, calc.Minute, 0);
                } else {
                    DateTime t = dtExactTime.Value;
                    DateTime target = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, t.Hour, t.Minute, 0);
                    if (target <= DateTime.Now && !isEditing) {
                        target = target.AddDays(1);
                    }
                    DueTime = target;
                }
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(btnSave);

            Button btnCancel = new Button();
            btnCancel.Text = "ยกเลิก";
            btnCancel.Font = new Font("Segoe UI", 9.5f);
            btnCancel.BackColor = Color.FromArgb(226, 232, 240);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.Location = isEditing ? new Point(402, y) : new Point(405, y);
            btnCancel.Size = new Size(95, 38);
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += (s, e) => this.Close();
            this.Controls.Add(btnCancel);
        }

        private void AddProcedurePresetButton(FlowLayoutPanel pnl, string label, string textToSet) {
            Button btn = new Button();
            btn.Text = label;
            btn.Font = new Font("Segoe UI", 8.5f);
            btn.BackColor = Color.White;
            btn.ForeColor = Color.FromArgb(30, 41, 59);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.Height = 26;
            btn.AutoSize = true;
            btn.Margin = new Padding(2);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => {
                txtTitle.Text = textToSet;
                txtTitle.Focus();
                txtTitle.Select(txtTitle.Text.Length, 0);
            };
            pnl.Controls.Add(btn);
        }

        private void AddHourButton(FlowLayoutPanel pnl, int hour) {
            Button btn = new Button();
            btn.Text = string.Format("{0:D2}:00", hour);
            btn.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btn.BackColor = Color.White;
            btn.ForeColor = Color.FromArgb(30, 41, 59);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.Size = new Size(56, 25);
            btn.Margin = new Padding(2, 0, 2, 0);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => {
                radExact.Checked = true;
                DateTime now = DateTime.Now;
                dtExactTime.Value = new DateTime(now.Year, now.Month, now.Day, hour, 0, 0);
            };
            pnl.Controls.Add(btn);
        }

        private void AddExactMinuteChip(FlowLayoutPanel pnl, string label, int minute) {
            Button btn = new Button();
            btn.Text = label;
            btn.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btn.BackColor = Color.FromArgb(241, 245, 249);
            btn.ForeColor = Color.FromArgb(30, 41, 59);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.Height = 25;
            btn.AutoSize = true;
            btn.Margin = new Padding(1, 0, 2, 0);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => {
                radExact.Checked = true;
                DateTime curr = dtExactTime.Value;
                dtExactTime.Value = new DateTime(curr.Year, curr.Month, curr.Day, curr.Hour, minute, 0);
            };
            pnl.Controls.Add(btn);
        }

        private void AddNowChip(FlowLayoutPanel pnl, string label) {
            Button btn = new Button();
            btn.Text = label;
            btn.Font = new Font("Segoe UI", 8f);
            btn.BackColor = Color.FromArgb(254, 243, 199);
            btn.ForeColor = Color.FromArgb(146, 64, 14);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(252, 211, 77);
            btn.Height = 25;
            btn.AutoSize = true;
            btn.Margin = new Padding(1, 0, 2, 0);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => {
                radExact.Checked = true;
                DateTime now = DateTime.Now;
                dtExactTime.Value = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            };
            pnl.Controls.Add(btn);
        }

        private void AddMinuteStepChip(FlowLayoutPanel pnl, string label, int stepMinutes) {
            Button btn = new Button();
            btn.Text = label;
            btn.Font = new Font("Segoe UI", 8f);
            btn.BackColor = Color.White;
            btn.ForeColor = Color.FromArgb(15, 23, 42);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.Height = 25;
            btn.AutoSize = true;
            btn.Margin = new Padding(1, 0, 2, 0);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => {
                radExact.Checked = true;
                DateTime curr = dtExactTime.Value.AddMinutes(stepMinutes);
                dtExactTime.Value = new DateTime(curr.Year, curr.Month, curr.Day, curr.Hour, curr.Minute, 0);
            };
            pnl.Controls.Add(btn);
        }

        private void AddRelativeMinuteChip(FlowLayoutPanel pnl, string label, int minutes) {
            Button btn = new Button();
            btn.Text = label;
            btn.Font = new Font("Segoe UI", 8f);
            btn.BackColor = Color.White;
            btn.ForeColor = Color.FromArgb(15, 23, 42);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.Height = 25;
            btn.AutoSize = true;
            btn.Margin = new Padding(1, 0, 2, 0);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => {
                radRelative.Checked = true;
                numMinutes.Value = minutes;
            };
            pnl.Controls.Add(btn);
        }
    }

    public class WardReminderStickyForm : Form {
        private ExpanderContext context;
        private WardReminderManager manager;
        private FlowLayoutPanel flowList;
        private System.Windows.Forms.Timer refreshTimer;
        private Button btnPin;
        private ComboBox cmbFilterBed;
        private Label lblCount;
        private bool isPinned = true;
        private int lastFormWidth = 0;
        private bool isBlinkPhase = false;

        // Reusable static fonts to prevent GDI leaks during countdown updates
        private static readonly Font FontStickyBold85 = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        private static readonly Font FontStickyRegular8 = new Font("Segoe UI", 8f, FontStyle.Regular);

        public WardReminderStickyForm(ExpanderContext ctx, WardReminderManager mgr) {
            context = ctx;
            manager = mgr;
            InitializeUI();

            if (manager != null) {
                manager.OnRemindersChanged += () => {
                    if (this.InvokeRequired) {
                        try { this.BeginInvoke(new Action(RefreshCards)); } catch {}
                    } else {
                        RefreshCards();
                    }
                };
            }
        }

        private void InitializeUI() {
            this.Text = "📌 ตัวเตือนหัตถการรายเตียง (Ward Sticky Notepad) (Alt+T)";
            this.Size = new Size(395, 520);
            this.MinimumSize = new Size(310, 320);
            this.Font = new Font("Segoe UI", 9f);
            this.BackColor = Color.FromArgb(254, 252, 232);
            this.TopMost = true;
            this.ShowInTaskbar = false;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Math.Max(wa.Left + 20, wa.Right - 400), wa.Top + 60);

            string iconPath = @"C:\PhisApp\Medical_Text_Expander\medical_expander_icon.ico";
            if (!File.Exists(iconPath)) iconPath = @"C:\PhisApp\medical_expander_icon.ico";
            if (File.Exists(iconPath)) {
                try { this.Icon = new Icon(iconPath); } catch {}
            }

            Panel pnlHeader = new Panel();
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 44;
            pnlHeader.BackColor = Color.FromArgb(245, 158, 11);

            Label lblTitle = new Label();
            lblTitle.Text = "📌 Ward Task Reminders";
            lblTitle.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.Location = new Point(10, 11);
            lblTitle.AutoSize = true;
            pnlHeader.Controls.Add(lblTitle);

            btnPin = new Button();
            btnPin.Text = "📌 ค้างจอ: เปิด";
            btnPin.Size = new Size(95, 28);
            btnPin.Location = new Point(205, 8);
            btnPin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnPin.BackColor = Color.FromArgb(254, 243, 199);
            btnPin.ForeColor = Color.FromArgb(180, 83, 9);
            btnPin.FlatStyle = FlatStyle.Flat;
            btnPin.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnPin.Click += (s, e) => {
                isPinned = !isPinned;
                this.TopMost = isPinned;
                btnPin.Text = isPinned ? "📌 ค้างจอ: เปิด" : "📌 ค้างจอ: ปิด";
                btnPin.BackColor = isPinned ? Color.FromArgb(254, 243, 199) : Color.FromArgb(226, 232, 240);
                btnPin.ForeColor = isPinned ? Color.FromArgb(180, 83, 9) : Color.FromArgb(71, 85, 105);
            };
            pnlHeader.Controls.Add(btnPin);

            Button btnMin = new Button();
            btnMin.Text = "✕";
            btnMin.Size = new Size(32, 28);
            btnMin.Location = new Point(335, 8);
            btnMin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMin.BackColor = Color.FromArgb(217, 119, 6);
            btnMin.ForeColor = Color.White;
            btnMin.FlatStyle = FlatStyle.Flat;
            btnMin.FlatAppearance.BorderSize = 0;
            btnMin.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnMin.Click += (s, e) => this.Hide();
            pnlHeader.Controls.Add(btnMin);

            this.Controls.Add(pnlHeader);

            Panel pnlActions = new Panel();
            pnlActions.Dock = DockStyle.Top;
            pnlActions.Height = 40;
            pnlActions.BackColor = Color.FromArgb(254, 249, 195);
            pnlActions.Padding = new Padding(6, 4, 6, 4);

            Button btnAdd = new Button();
            btnAdd.Text = "+ เพิ่มเตือนด่วน";
            btnAdd.Size = new Size(110, 30);
            btnAdd.Location = new Point(6, 5);
            btnAdd.BackColor = Color.FromArgb(13, 148, 136);
            btnAdd.ForeColor = Color.White;
            btnAdd.FlatStyle = FlatStyle.Flat;
            btnAdd.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnAdd.Cursor = Cursors.Hand;
            btnAdd.Click += (s, e) => OpenAddDialog();
            pnlActions.Controls.Add(btnAdd);

            cmbFilterBed = new ComboBox();
            cmbFilterBed.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFilterBed.Font = new Font("Segoe UI", 8.5f);
            cmbFilterBed.Items.Add("ทุกเตียง");
            for (int i = 1; i <= 30; i++) cmbFilterBed.Items.Add(string.Format("เตียง {0:D2}", i));
            cmbFilterBed.Items.Add("ทั่วไป");
            cmbFilterBed.SelectedIndex = 0;
            cmbFilterBed.Location = new Point(122, 6);
            cmbFilterBed.Size = new Size(85, 26);
            cmbFilterBed.SelectedIndexChanged += (s, e) => RefreshCards();
            pnlActions.Controls.Add(cmbFilterBed);

            Button btnClearDone = new Button();
            btnClearDone.Text = "ล้างที่เสร็จ";
            btnClearDone.Size = new Size(72, 30);
            btnClearDone.Location = new Point(212, 5);
            btnClearDone.BackColor = Color.FromArgb(241, 245, 249);
            btnClearDone.FlatStyle = FlatStyle.Flat;
            btnClearDone.Font = new Font("Segoe UI", 8f);
            btnClearDone.Click += (s, e) => {
                if (manager != null) manager.ClearCompleted();
            };
            pnlActions.Controls.Add(btnClearDone);

            lblCount = new Label();
            lblCount.Text = "0 รายการ";
            lblCount.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            lblCount.ForeColor = Color.FromArgb(120, 53, 15);
            lblCount.Location = new Point(290, 11);
            lblCount.AutoSize = true;
            pnlActions.Controls.Add(lblCount);

            this.Controls.Add(pnlActions);

            Panel pnlBottomPresets = new Panel();
            pnlBottomPresets.Dock = DockStyle.Bottom;
            pnlBottomPresets.Height = 36;
            pnlBottomPresets.BackColor = Color.FromArgb(254, 240, 138);

            FlowLayoutPanel flowQuick = new FlowLayoutPanel();
            flowQuick.Dock = DockStyle.Fill;
            flowQuick.WrapContents = false;
            flowQuick.AutoScroll = true;
            flowQuick.Padding = new Padding(4, 3, 4, 3);

            AddQuickBottomButton(flowQuick, "+30น. V/S", 30, "วัด V/S และประเมิน SOS ซ้ำ");
            AddQuickBottomButton(flowQuick, "+1ชม. Lab/X-ray", 60, "โทรตามผล X-ray / Lab ด่วน");
            AddQuickBottomButton(flowQuick, "11:30 DTX", 11, 30, "เจาะ DTX ก่อนอาหาร");
            AddQuickBottomButton(flowQuick, "14:00 IV", 14, 0, "เตรียมเปลี่ยนถุงน้ำเกลือ (IV)");

            pnlBottomPresets.Controls.Add(flowQuick);
            this.Controls.Add(pnlBottomPresets);

            flowList = new FlowLayoutPanel();
            flowList.Dock = DockStyle.Fill;
            flowList.AutoScroll = true;
            flowList.FlowDirection = FlowDirection.TopDown;
            flowList.WrapContents = false;
            flowList.BackColor = Color.FromArgb(254, 252, 232);
            flowList.Padding = new Padding(6);
            this.Controls.Add(flowList);
            flowList.BringToFront();

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 1000;
            refreshTimer.Tick += (s, e) => UpdateCountdowns();
            refreshTimer.Start();

            this.FormClosing += (s, e) => {
                if (e.CloseReason == CloseReason.UserClosing) {
                    e.Cancel = true;
                    this.Hide();
                }
            };

            this.Resize += (s, e) => {
                if (flowList != null && Math.Abs(this.ClientSize.Width - lastFormWidth) >= 8) {
                    lastFormWidth = this.ClientSize.Width;
                    RefreshCards();
                }
            };

            RefreshCards();
        }

        private void AddQuickBottomButton(FlowLayoutPanel pnl, string label, int minutes, string title) {
            Button btn = new Button();
            btn.Text = label;
            btn.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btn.BackColor = Color.White;
            btn.ForeColor = Color.FromArgb(120, 53, 15);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(245, 158, 11);
            btn.Height = 24;
            btn.AutoSize = true;
            btn.Margin = new Padding(2);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => {
                int activeBed = context.BedNotesFormInstance != null ? context.BedNotesFormInstance.CurrentBed : 1;
                if (manager != null) {
                    manager.Add(activeBed, title, DateTime.Now.AddMinutes(minutes));
                    context.ShowNotification(string.Format("ตั้งเตือนเตียง {0}: {1} (อีก {2} นาที)", activeBed, title, minutes));
                }
            };
            pnl.Controls.Add(btn);
        }

        private void AddQuickBottomButton(FlowLayoutPanel pnl, string label, int hour, int min, string title) {
            Button btn = new Button();
            btn.Text = label;
            btn.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btn.BackColor = Color.White;
            btn.ForeColor = Color.FromArgb(120, 53, 15);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(245, 158, 11);
            btn.Height = 24;
            btn.AutoSize = true;
            btn.Margin = new Padding(2);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => {
                int activeBed = context.BedNotesFormInstance != null ? context.BedNotesFormInstance.CurrentBed : 1;
                DateTime now = DateTime.Now;
                DateTime target = new DateTime(now.Year, now.Month, now.Day, hour, min, 0);
                if (target <= now) target = target.AddDays(1);
                if (manager != null) {
                    manager.Add(activeBed, title, target);
                    context.ShowNotification(string.Format("ตั้งเตือนเตียง {0}: {1} (เวลา {2:D2}:{3:D2} น.)", activeBed, title, hour, min));
                }
            };
            pnl.Controls.Add(btn);
        }

        public void OpenAddDialog(int targetBed = -1) {
            int bed = targetBed;
            if (bed <= 0 && context.BedNotesFormInstance != null) {
                bed = context.BedNotesFormInstance.CurrentBed;
            }
            if (bed <= 0) bed = 1;

            using (AddReminderDialog dlg = new AddReminderDialog(bed)) {
                if (dlg.ShowDialog() == DialogResult.OK) {
                    if (manager != null) {
                        manager.Add(dlg.SelectedBed, dlg.ReminderTitle, dlg.DueTime);
                        context.ShowNotification(string.Format("บันทึกตัวเตือนเตียง {0} เรียบร้อยแล้ว ({1:D2}:{2:D2} น.)", 
                            dlg.SelectedBed > 0 ? dlg.SelectedBed.ToString() : "ทั่วไป",
                            dlg.DueTime.Hour,
                            dlg.DueTime.Minute));
                    }
                }
            }
        }

        public void OpenEditDialog(WardReminderItem item) {
            if (item == null) return;
            using (AddReminderDialog dlg = new AddReminderDialog(item)) {
                if (dlg.ShowDialog() == DialogResult.OK) {
                    if (dlg.IsDeleted) {
                        if (manager != null) {
                            manager.Delete(item.Id);
                            context.ShowNotification(string.Format("ลบการแจ้งเตือนเรียบร้อยแล้ว"));
                        }
                    } else if (manager != null) {
                        manager.Update(item.Id, dlg.SelectedBed, dlg.ReminderTitle, dlg.DueTime);
                        context.ShowNotification(string.Format("แก้ไขตัวเตือนเตียง {0}: {1} ({2:D2}:{3:D2} น.)", 
                            dlg.SelectedBed > 0 ? dlg.SelectedBed.ToString() : "ทั่วไป",
                            dlg.ReminderTitle,
                            dlg.DueTime.Hour,
                            dlg.DueTime.Minute));
                    }
                }
            }
        }

        public void RefreshCards() {
            flowList.SuspendLayout();
            flowList.Controls.Clear();

            if (manager == null) {
                flowList.ResumeLayout();
                return;
            }

            int filterIndex = cmbFilterBed != null ? cmbFilterBed.SelectedIndex : 0;
            int filterBed = -1;
            if (filterIndex > 0 && filterIndex <= 30) filterBed = filterIndex;
            else if (filterIndex == 31) filterBed = 0;

            List<WardReminderItem> all = manager.GetAll();
            int activeCount = 0;

            foreach (WardReminderItem item in all) {
                if (filterBed != -1 && item.BedNum != filterBed) continue;
                if (!item.IsCompleted) activeCount++;

                Panel card = CreateReminderCard(item);
                flowList.Controls.Add(card);
            }

            if (lblCount != null) {
                lblCount.Text = string.Format("{0} รายการที่ต้องทำ", activeCount);
            }

            if (flowList.Controls.Count == 0) {
                Label lblEmpty = new Label();
                lblEmpty.Text = "🎉 ไม่มีรายการเตือนหัตถการค้าง\n(กดปุ่ม '+ เพิ่มเตือนด่วน' ด้านบนเพื่อตั้งเตือน)";
                lblEmpty.Font = new Font("Segoe UI", 9.5f, FontStyle.Italic);
                lblEmpty.ForeColor = Color.FromArgb(148, 163, 184);
                lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
                lblEmpty.Size = new Size(flowList.ClientSize.Width - 20, 100);
                lblEmpty.Margin = new Padding(10, 20, 10, 10);
                flowList.Controls.Add(lblEmpty);
            }

            flowList.ResumeLayout();
        }

        private Panel CreateReminderCard(WardReminderItem item) {
            int cardW = flowList.ClientSize.Width - 18;
            if (cardW < 270) cardW = 270;

            Panel card = new Panel();
            card.Size = new Size(cardW, 68);
            card.Margin = new Padding(2, 3, 2, 3);
            card.Tag = item;

            TimeSpan diff = item.DueTime - DateTime.Now;
            bool isDue = item.IsDue;
            bool isNearDue = (!item.IsCompleted && !isDue && diff.TotalMinutes > 0 && diff.TotalMinutes <= 15);
            BedColorHelper.BedTheme theme = BedColorHelper.GetTheme(item.BedNum);

            if (item.IsCompleted) {
                card.BackColor = Color.FromArgb(241, 245, 249);
            } else if (isDue) {
                card.BackColor = Color.FromArgb(254, 202, 202); // Red alert
            } else if (isNearDue) {
                card.BackColor = Color.FromArgb(254, 243, 199); // Amber warning
            } else {
                card.BackColor = Color.White;
            }

            CheckBox chk = new CheckBox();
            chk.Location = new Point(8, 8);
            chk.Size = new Size(20, 20);
            chk.Checked = item.IsCompleted;
            chk.CheckedChanged += (s, e) => {
                if (manager != null) manager.MarkCompleted(item.Id, chk.Checked);
            };
            card.Controls.Add(chk);

            Label lblBed = new Label();
            lblBed.Name = "lblBed";
            lblBed.Text = item.BedText;
            lblBed.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblBed.Location = new Point(30, 8);
            lblBed.Size = new Size(58, 20);
            lblBed.TextAlign = ContentAlignment.MiddleCenter;
            if (item.IsCompleted) {
                lblBed.BackColor = Color.FromArgb(148, 163, 184);
                lblBed.ForeColor = Color.White;
            } else {
                lblBed.BackColor = theme.Primary;
                lblBed.ForeColor = Color.White;
            }
            card.Controls.Add(lblBed);

            ToolTip tt = new ToolTip();

            int btnDelW = 24;
            int btnEditW = 26;
            int btnH = 24;
            int btnY = 6;
            int rightPad = 8;

            // Button Delete (✕) - Clear red styling
            Button btnDel = new Button();
            btnDel.Text = "✕";
            btnDel.Size = new Size(btnDelW, btnH);
            btnDel.Location = new Point(cardW - rightPad - btnDelW, btnY);
            btnDel.BackColor = Color.FromArgb(254, 242, 242);
            btnDel.ForeColor = Color.FromArgb(220, 38, 38);
            btnDel.FlatStyle = FlatStyle.Flat;
            btnDel.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);
            btnDel.FlatAppearance.BorderSize = 1;
            btnDel.FlatAppearance.MouseOverBackColor = Color.FromArgb(254, 202, 202);
            btnDel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnDel.Cursor = Cursors.Hand;
            tt.SetToolTip(btnDel, "ลบการเตือนนี้");
            btnDel.Click += (s, e) => {
                if (manager != null) manager.Delete(item.Id);
            };
            card.Controls.Add(btnDel);

            // Button Edit (✏️) - Clear slate styling
            Button btnEdit = new Button();
            btnEdit.Text = "✏️";
            btnEdit.Size = new Size(btnEditW, btnH);
            btnEdit.Location = new Point(cardW - rightPad - btnDelW - 4 - btnEditW, btnY);
            btnEdit.BackColor = Color.FromArgb(241, 245, 249);
            btnEdit.ForeColor = Color.FromArgb(51, 65, 85);
            btnEdit.FlatStyle = FlatStyle.Flat;
            btnEdit.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnEdit.FlatAppearance.BorderSize = 1;
            btnEdit.FlatAppearance.MouseOverBackColor = Color.FromArgb(226, 232, 240);
            btnEdit.Font = new Font("Segoe UI", 8f);
            btnEdit.Cursor = Cursors.Hand;
            tt.SetToolTip(btnEdit, "แก้ไขหัตถการหรือเวลาเตือน");
            btnEdit.Click += (s, e) => OpenEditDialog(item);
            card.Controls.Add(btnEdit);

            // Dynamically reposition buttons on card resize
            card.Resize += (s, e) => {
                int w = card.ClientSize.Width;
                btnDel.Location = new Point(w - rightPad - btnDelW, btnY);
                btnEdit.Location = new Point(w - rightPad - btnDelW - 4 - btnEditW, btnY);
            };

            btnDel.BringToFront();
            btnEdit.BringToFront();

            // Title Label (Multi-line auto-wrap, bounded by buttons on the right)
            int titleStartX = 92;
            int buttonsTotalW = rightPad + btnDelW + 4 + btnEditW + 6;
            int titleW = cardW - titleStartX - buttonsTotalW;
            if (titleW < 100) titleW = 100;

            Label lblTitle = new Label();
            lblTitle.Text = item.Title ?? "";
            lblTitle.Font = new Font("Segoe UI", 9f, item.IsCompleted ? FontStyle.Strikeout : FontStyle.Bold);
            lblTitle.ForeColor = item.IsCompleted ? Color.FromArgb(148, 163, 184) : Color.FromArgb(15, 23, 42);
            lblTitle.Location = new Point(titleStartX, 7);

            Size titleSize = TextRenderer.MeasureText(
                string.IsNullOrEmpty(item.Title) ? " " : item.Title,
                lblTitle.Font,
                new Size(titleW, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
            );
            int titleH = Math.Max(20, titleSize.Height);
            lblTitle.Size = new Size(titleW, titleH);
            lblTitle.AutoEllipsis = false;
            tt.SetToolTip(lblTitle, item.Title);
            card.Controls.Add(lblTitle);

            // Time Label without seconds, placed safely below Title
            int timeY = Math.Max(32, 7 + titleH + 4);

            Label lblTime = new Label();
            lblTime.Name = "lblTime";
            lblTime.Location = new Point(30, timeY);
            lblTime.Size = new Size(cardW - 40, 18);

            if (isDue) {
                lblTime.Text = string.Format("{0} ({1:D2}:{2:D2} น.)", item.RemainingText, item.DueTime.Hour, item.DueTime.Minute);
                lblTime.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                lblTime.ForeColor = Color.FromArgb(185, 28, 28);
            } else if (isNearDue) {
                lblTime.Text = string.Format("⚠️ {0} ({1:D2}:{2:D2} น.)", item.RemainingText, item.DueTime.Hour, item.DueTime.Minute);
                lblTime.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                lblTime.ForeColor = Color.FromArgb(180, 83, 9);
            } else {
                lblTime.Text = string.Format("{0} ({1:D2}:{2:D2} น.)", item.RemainingText, item.DueTime.Hour, item.DueTime.Minute);
                lblTime.Font = new Font("Segoe UI", 8f, FontStyle.Regular);
                lblTime.ForeColor = Color.FromArgb(71, 85, 105);
            }
            card.Controls.Add(lblTime);

            int cardH = timeY + 18 + 7;
            if (cardH < 62) cardH = 62;
            card.Height = cardH;

            card.Paint += (s, e) => {
                // Left colored accent bar matching the bed's distinct theme
                using (Brush b = new SolidBrush(item.IsCompleted ? Color.FromArgb(148, 163, 184) : theme.Primary)) {
                    e.Graphics.FillRectangle(b, 0, 0, 4, card.Height);
                }
                TimeSpan curDiff = item.DueTime - DateTime.Now;
                Color borderCol;
                if (item.IsCompleted) borderCol = Color.FromArgb(226, 232, 240);
                else if (curDiff.TotalSeconds <= 0) borderCol = isBlinkPhase ? Color.FromArgb(239, 68, 68) : Color.FromArgb(248, 113, 113);
                else if (curDiff.TotalMinutes <= 15) borderCol = Color.FromArgb(245, 158, 11);
                else borderCol = Color.FromArgb(226, 232, 240);

                using (Pen p = new Pen(borderCol, 1)) {
                    e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
                }
            };

            // Context Menu on right click
            ContextMenuStrip cmsCard = new ContextMenuStrip();
            cmsCard.Font = new Font("Segoe UI", 9f);
            cmsCard.Items.Add("✏️ แก้ไขการแจ้งเตือนนี้ (Edit)...", null, (s, e) => OpenEditDialog(item));
            cmsCard.Items.Add("-");
            cmsCard.Items.Add("⏱️ เลื่อนเวลาอีก +15 นาที", null, (s, e) => {
                if (manager != null) manager.Postpone(item.Id, 15);
            });
            cmsCard.Items.Add("⏱️ เลื่อนเวลาอีก +30 นาที", null, (s, e) => {
                if (manager != null) manager.Postpone(item.Id, 30);
            });
            cmsCard.Items.Add("⏱️ เลื่อนเวลาอีก +1 ชั่วโมง", null, (s, e) => {
                if (manager != null) manager.Postpone(item.Id, 60);
            });
            cmsCard.Items.Add("-");
            if (item.BedNum >= 1 && item.BedNum <= 30) {
                cmsCard.Items.Add(string.Format("🛏️ เปิดข้อมูลเตียง {0:D2} (F7)", item.BedNum), null, (s, e) => context.ShowBedNotes(item.BedNum));
            }
            cmsCard.Items.Add(item.IsCompleted ? "↩️ ทำเครื่องหมายว่ายังไม่เสร็จ" : "✔️ ทำเสร็จแล้ว (Mark Complete)", null, (s, e) => {
                if (manager != null) manager.MarkCompleted(item.Id, !item.IsCompleted);
            });
            cmsCard.Items.Add("❌ ลบการแจ้งเตือนนี้", null, (s, e) => {
                if (manager != null) manager.Delete(item.Id);
            });

            card.ContextMenuStrip = cmsCard;
            lblTitle.ContextMenuStrip = cmsCard;
            lblBed.ContextMenuStrip = cmsCard;
            lblTime.ContextMenuStrip = cmsCard;

            EventHandler openBed = (s, e) => {
                if (item.BedNum >= 1 && item.BedNum <= 30) {
                    context.ShowBedNotes(item.BedNum);
                }
            };
            card.DoubleClick += openBed;
            lblTitle.DoubleClick += openBed;
            lblBed.DoubleClick += openBed;
            lblTime.DoubleClick += openBed;

            return card;
        }

        private void UpdateCountdowns() {
            if (!this.Visible) return;
            isBlinkPhase = !isBlinkPhase;
            foreach (Control c in flowList.Controls) {
                Panel card = c as Panel;
                if (card != null && card.Tag is WardReminderItem) {
                    WardReminderItem item = (WardReminderItem)card.Tag;
                    if (item.IsCompleted) continue;

                    TimeSpan diff = item.DueTime - DateTime.Now;
                    BedColorHelper.BedTheme theme = BedColorHelper.GetTheme(item.BedNum);
                    Control[] matches = card.Controls.Find("lblTime", false);
                    Control[] bedMatches = card.Controls.Find("lblBed", false);
                    Label lblTime = matches.Length > 0 ? matches[0] as Label : null;
                    Label lblBed = bedMatches.Length > 0 ? bedMatches[0] as Label : null;

if (diff.TotalSeconds <= 0) {
                        // ถึงเวลาแล้ว / เกินกำหนด -> กระพริบเตือนสีแดง (Blinking Red Alert)
                        if (isBlinkPhase) {
                            card.BackColor = Color.FromArgb(254, 202, 202);
                            if (lblBed != null) lblBed.BackColor = Color.FromArgb(220, 38, 38);
                        } else {
                            card.BackColor = Color.FromArgb(255, 241, 242);
                            if (lblBed != null) lblBed.BackColor = theme.Primary;
                        }
                        if (lblTime != null) {
                            lblTime.Text = string.Format("{0} ({1:D2}:{2:D2} น.)", item.RemainingText, item.DueTime.Hour, item.DueTime.Minute);
                            lblTime.ForeColor = Color.FromArgb(185, 28, 28);
                            if (lblTime.Font != FontStickyBold85) lblTime.Font = FontStickyBold85;
                        }
                        card.Invalidate();
                    } else if (diff.TotalMinutes <= 15) {
                        // ใกล้ถึงเวลาแล้ว (ภายใน 15 นาที) -> สีเหลืองส้มแจ้งเตือน (Amber Warning Alert)
                        if (diff.TotalMinutes <= 5 && isBlinkPhase) {
                            card.BackColor = Color.FromArgb(254, 215, 170); // Pulse warning when <= 5 min
                        } else {
                            card.BackColor = Color.FromArgb(254, 243, 199);
                        }
                        if (lblBed != null) lblBed.BackColor = theme.Primary;
                        if (lblTime != null) {
                            lblTime.Text = string.Format("⚠️ {0} ({1:D2}:{2:D2} น.)", item.RemainingText, item.DueTime.Hour, item.DueTime.Minute);
                            lblTime.ForeColor = Color.FromArgb(180, 83, 9);
                            if (lblTime.Font != FontStickyBold85) lblTime.Font = FontStickyBold85;
                        }
                        card.Invalidate();
                    } else {
                        // ปกติ (> 15 นาที)
                        card.BackColor = Color.White;
                        if (lblBed != null) lblBed.BackColor = theme.Primary;
                        if (lblTime != null) {
                            lblTime.Text = string.Format("{0} ({1:D2}:{2:D2} น.)", item.RemainingText, item.DueTime.Hour, item.DueTime.Minute);
                            lblTime.ForeColor = Color.FromArgb(71, 85, 105);
                            if (lblTime.Font != FontStickyRegular8) lblTime.Font = FontStickyRegular8;
                        }
                        card.Invalidate();
                    }
                }
            }
        }

        public void EnsureVisibleAndFlash() {
            if (this.WindowState == FormWindowState.Minimized) {
                this.WindowState = FormWindowState.Normal;
            }
            this.Show();
            this.BringToFront();
            RefreshCards();
        }

        public void ShowAndFocus() {
            if (this.WindowState == FormWindowState.Minimized) {
                this.WindowState = FormWindowState.Normal;
            }
            this.Show();
            this.BringToFront();
            this.Activate();
            RefreshCards();
        }
    }

    public class ClinicalCalculatorForm : Form {
        private ExpanderContext context;
        private int currentBed = 1;
        private IntPtr lastActiveWindow = IntPtr.Zero;

        // Top UI
        private ComboBox cmbBedSelector;
        private TabControl tabControl;

        // Tab 1: SOS Score
        private NumericUpDown numSosTemp;
        private NumericUpDown numSosSBP;
        private NumericUpDown numSosDBP;
        private NumericUpDown numSosHR;
        private NumericUpDown numSosRR;
        private NumericUpDown numSosSpO2;
        private ComboBox cmbSosAVPU;
        private ComboBox cmbSosUrine;
        private CheckBox chkSosO2;
        private Label lblSosScoreBig;
        private Label lblSosBadge;
        private Label lblSosBreakdown;
        private Label lblSosAdvice;
        private TextBox txtSosPreview;
        private Button btnSosInsert;

        // Tab 2: MAP & Shock Index
        private NumericUpDown numMapSBP;
        private NumericUpDown numMapDBP;
        private NumericUpDown numMapHR;
        private Label lblMapValueBig;
        private Label lblMapBadge;
        private Label lblMapAdvice;
        private Label lblSiValueBig;
        private Label lblPpValue;
        private TextBox txtMapPreview;
        private Button btnMapInsert;

        // Tab 3: IV Drop Rate
        private ComboBox cmbIvFluid;
        private NumericUpDown numIvVolume;
        private NumericUpDown numIvHours;
        private NumericUpDown numIvMinutes;
        private ComboBox cmbIvDropFactor;
        private Label lblIvRateBig;
        private Label lblIvDropsBig;
        private Label lblIvInterval;
        private Label lblIvFinishTime;
        private TextBox txtIvPreview;
        private Button btnIvInsert;

        // Tab 4: Inotrope & Drug Drip
        private NumericUpDown numDripWeight;
        private ComboBox cmbDrugPreset;
        private RadioButton radModeDoseToRate;
        private RadioButton radModeRateToDose;
        private NumericUpDown numDose;
        private Label lblDoseUnit;
        private NumericUpDown numPumpRate;
        private Label lblDripResultBig;
        private Label lblDripSubtitle;
        private Label lblDripSafetyAdvice;
        private TextBox txtDripPreview;
        private Button btnDripInsert;

        // Tab 5: Renal Function (CrCl) & BMI
        private RadioButton radGenderMale;
        private RadioButton radGenderFemale;
        private NumericUpDown numRenalAge;
        private NumericUpDown numRenalWeight;
        private NumericUpDown numRenalHeight;
        private NumericUpDown numRenalCr;
        private Label lblCrClBig;
        private Label lblCkdStageBadge;
        private Label lblRenalAdvice;
        private Label lblBmiBig;
        private Label lblBmiBadge;
        private Label lblIbw;
        private TextBox txtRenalPreview;
        private Button btnRenalInsert;

        // Tab 6: Arterial Blood Gas (ABG) Interpreter
        private NumericUpDown numAbgPH;
        private NumericUpDown numAbgPaCO2;
        private NumericUpDown numAbgHCO3;
        private NumericUpDown numAbgPaO2;
        private NumericUpDown numAbgFiO2;
        private Label lblAbgDiagnosisBig;
        private Label lblAbgOxygenationBadge;
        private Label lblAbgExpectedComp;
        private Label lblAbgAdvice;
        private TextBox txtAbgPreview;
        private Button btnAbgInsert;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public ClinicalCalculatorForm(ExpanderContext ctx) {
            context = ctx;
            InitializeUI();
        }

        private void InitializeUI() {
            this.Text = "🧮 เครื่องมือคำนวณและแจ้งเตือนค่าวิกฤตทางการพยาบาล (Clinical Calculators & Early Warning) (Alt+C)";
            this.Size = new Size(960, 710);
            this.MinimumSize = new Size(880, 640);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.BackColor = Color.FromArgb(248, 250, 252);

            string iconPath = @"C:\PhisApp\Medical_Text_Expander\medical_expander_icon.ico";
            if (!File.Exists(iconPath)) iconPath = @"C:\PhisApp\medical_expander_icon.ico";
            if (File.Exists(iconPath)) {
                try { this.Icon = new Icon(iconPath); } catch {}
            }

            // --- Top Banner Panel ---
            Panel pnlTopBanner = new Panel();
            pnlTopBanner.Dock = DockStyle.Top;
            pnlTopBanner.Height = 60;
            pnlTopBanner.BackColor = Color.FromArgb(13, 148, 136); // Teal

            Label lblTitle = new Label();
            lblTitle.Text = "🧮 ระบบคำนวณและแจ้งเตือนค่าวิกฤตทางการพยาบาล (Clinical Calculators)";
            lblTitle.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.Location = new Point(14, 8);
            lblTitle.AutoSize = true;
            pnlTopBanner.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "ประเมินสัญญาณเตือนวิกฤต SOS, ความดันเฉลี่ย MAP, อัตราสารน้ำ IV, ยาดริปฉุกเฉิน และไต CrCl/BMI";
            lblSub.Font = new Font("Segoe UI", 9f);
            lblSub.ForeColor = Color.FromArgb(204, 251, 241);
            lblSub.Location = new Point(16, 33);
            lblSub.AutoSize = true;
            pnlTopBanner.Controls.Add(lblSub);

            Label lblTarget = new Label();
            lblTarget.Text = "เตียงเป้าหมาย:";
            lblTarget.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblTarget.ForeColor = Color.White;
            lblTarget.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblTarget.Location = new Point(690, 18);
            lblTarget.AutoSize = true;
            pnlTopBanner.Controls.Add(lblTarget);

            cmbBedSelector = new ComboBox();
            cmbBedSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbBedSelector.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            cmbBedSelector.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cmbBedSelector.Location = new Point(788, 14);
            cmbBedSelector.Size = new Size(138, 31);
            for (int i = 1; i <= 30; i++) {
                cmbBedSelector.Items.Add(string.Format("🛏️ เตียง {0}", i));
            }
            cmbBedSelector.SelectedIndex = 0;
            cmbBedSelector.SelectedIndexChanged += (s, e) => {
                ChangeTargetBed(cmbBedSelector.SelectedIndex + 1);
            };
            pnlTopBanner.Controls.Add(cmbBedSelector);

            this.Controls.Add(pnlTopBanner);

            // --- Main TabControl ---
            tabControl = new TabControl();
            tabControl.Dock = DockStyle.Fill;
            tabControl.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            tabControl.Padding = new Point(14, 8);

            TabPage tabSOS = new TabPage("🚨 SOS Score (วิกฤต/MEWS)");
            tabSOS.BackColor = Color.FromArgb(248, 250, 252);
            BuildSosTab(tabSOS);
            tabControl.TabPages.Add(tabSOS);

            TabPage tabMAP = new TabPage("💓 MAP & Shock Index");
            tabMAP.BackColor = Color.FromArgb(248, 250, 252);
            BuildMapTab(tabMAP);
            tabControl.TabPages.Add(tabMAP);

            TabPage tabIV = new TabPage("💧 IV Infusion & Drop Rate");
            tabIV.BackColor = Color.FromArgb(248, 250, 252);
            BuildIvTab(tabIV);
            tabControl.TabPages.Add(tabIV);

            TabPage tabDrips = new TabPage("💉 Inotrope & ยาดริปฉุกเฉิน");
            tabDrips.BackColor = Color.FromArgb(248, 250, 252);
            BuildDripsTab(tabDrips);
            tabControl.TabPages.Add(tabDrips);

            TabPage tabRenal = new TabPage("🫘 CrCl (ไต) & BMI");
            tabRenal.BackColor = Color.FromArgb(248, 250, 252);
            BuildRenalTab(tabRenal);
            tabControl.TabPages.Add(tabRenal);

            TabPage tabABG = new TabPage("🫁 แปลผลก๊าซในเลือด (ABG)");
            tabABG.BackColor = Color.FromArgb(248, 250, 252);
            BuildAbgTab(tabABG);
            tabControl.TabPages.Add(tabABG);

            this.Controls.Add(tabControl);
            tabControl.BringToFront();

            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape) {
                    this.Hide();
                }
            };

            this.FormClosing += (s, e) => {
                if (e.CloseReason == CloseReason.UserClosing) {
                    e.Cancel = true;
                    this.Hide();
                }
            };
        }

        private void ChangeTargetBed(int bedNum) {
            if (bedNum < 1 || bedNum > 30) return;
            currentBed = bedNum;
            string bedText = string.Format("🛏️ บันทึกลงเตียง {0}", currentBed);
            if (btnSosInsert != null) btnSosInsert.Text = bedText;
            if (btnMapInsert != null) btnMapInsert.Text = bedText;
            if (btnIvInsert != null) btnIvInsert.Text = bedText;
            if (btnDripInsert != null) btnDripInsert.Text = bedText;
            if (btnRenalInsert != null) btnRenalInsert.Text = bedText;
            if (btnAbgInsert != null) btnAbgInsert.Text = bedText;
        }

        private string GetTimestampHeader() {
            DateTime now = DateTime.Now;
            int thaiYear = now.Year + 543;
            return string.Format("{0:D2}/{1:D2}/{2} {3:D2}:{4:D2} น.", now.Day, now.Month, thaiYear, now.Hour, now.Minute);
        }

        // ==========================================
        // TAB 1: SOS SCORE (MEWS)
        // ==========================================
        private void BuildSosTab(TabPage page) {
            // Left Input Panel
            Panel pnlLeft = new Panel();
            pnlLeft.Dock = DockStyle.Left;
            pnlLeft.Width = 440;
            pnlLeft.Padding = new Padding(14);
            pnlLeft.AutoScroll = true;

            int y = 10;
            Label lblHeader = new Label();
            lblHeader.Text = "พารามิเตอร์สัญญาณชีพ (Vital Signs):";
            lblHeader.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            lblHeader.ForeColor = Color.FromArgb(15, 23, 42);
            lblHeader.Location = new Point(14, y);
            lblHeader.AutoSize = true;
            pnlLeft.Controls.Add(lblHeader);

            y += 32;
            pnlLeft.Controls.Add(CreateLbl("อุณหภูมิกาย BT (°C):", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(ปกติ 36.5 - 37.4)", 230, y, false));
            y += 24;
            numSosTemp = CreateNum(36.8m, 32.0m, 43.0m, 0.1m, 1, 14, y, 160, (s, e) => RecalcSos());
            pnlLeft.Controls.Add(numSosTemp);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ความดัน Systolic BP (mmHg):", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(ปกติ 101 - 180)", 230, y, false));
            y += 24;
            numSosSBP = CreateNum(120m, 40m, 260m, 1m, 0, 14, y, 160, (s, e) => RecalcSos());
            pnlLeft.Controls.Add(numSosSBP);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ความดัน Diastolic BP (mmHg):", 14, y, true));
            y += 24;
            numSosDBP = CreateNum(80m, 20m, 180m, 1m, 0, 14, y, 160, (s, e) => RecalcSos());
            pnlLeft.Controls.Add(numSosDBP);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ชีพจร Pulse Rate / HR (bpm):", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(ปกติ 51 - 100)", 230, y, false));
            y += 24;
            numSosHR = CreateNum(78m, 20m, 240m, 1m, 0, 14, y, 160, (s, e) => RecalcSos());
            pnlLeft.Controls.Add(numSosHR);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("อัตราการหายใจ RR (ครั้ง/นาที):", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(ปกติ 9 - 20)", 230, y, false));
            y += 24;
            numSosRR = CreateNum(18m, 4m, 60m, 1m, 0, 14, y, 160, (s, e) => RecalcSos());
            pnlLeft.Controls.Add(numSosRR);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ความอิ่มตัวออกซิเจน SpO2 (%):", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(ปกติ >= 95%)", 230, y, false));
            y += 24;
            numSosSpO2 = CreateNum(98m, 50m, 100m, 1m, 0, 14, y, 160, (s, e) => RecalcSos());
            pnlLeft.Controls.Add(numSosSpO2);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ระดับความรู้สึกตัว (AVPU Scale):", 14, y, true));
            y += 24;
            cmbSosAVPU = new ComboBox();
            cmbSosAVPU.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSosAVPU.Font = new Font("Segoe UI", 10f);
            cmbSosAVPU.Location = new Point(14, y);
            cmbSosAVPU.Size = new Size(390, 29);
            cmbSosAVPU.Items.Add("A - รู้สึกตัวดี ถามตอบรู้เรื่อง (Alert) [0 คะแนน]");
            cmbSosAVPU.Items.Add("V - ตอบสนองต่อเสียงเรียก (Voice) [1 คะแนน]");
            cmbSosAVPU.Items.Add("P - ตอบสนองต่อความเจ็บปวด (Pain) [2 คะแนน]");
            cmbSosAVPU.Items.Add("U - หมดสติ ไม่ตอบสนอง (Unresponsive) [3 คะแนน]");
            cmbSosAVPU.SelectedIndex = 0;
            cmbSosAVPU.SelectedIndexChanged += (s, e) => RecalcSos();
            pnlLeft.Controls.Add(cmbSosAVPU);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ปริมาณปัสสาวะ (Urine Output):", 14, y, true));
            y += 24;
            cmbSosUrine = new ComboBox();
            cmbSosUrine.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSosUrine.Font = new Font("Segoe UI", 10f);
            cmbSosUrine.Location = new Point(14, y);
            cmbSosUrine.Size = new Size(390, 29);
            cmbSosUrine.Items.Add("ปกติ: ออกดี > 30 ml/hr [0 คะแนน]");
            cmbSosUrine.Items.Add("ลดลง: 20 - 30 ml/hr [1 คะแนน]");
            cmbSosUrine.Items.Add("วิกฤต: < 20 ml/hr หรือไม่ออก 2 ชม. [2 คะแนน]");
            cmbSosUrine.SelectedIndex = 0;
            cmbSosUrine.SelectedIndexChanged += (s, e) => RecalcSos();
            pnlLeft.Controls.Add(cmbSosUrine);

            y += 36;
            chkSosO2 = new CheckBox();
            chkSosO2.Text = "ผู้ป่วยได้รับการ On ออกซิเจน (Oxygen therapy)";
            chkSosO2.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            chkSosO2.Location = new Point(14, y);
            chkSosO2.Size = new Size(390, 26);
            chkSosO2.CheckedChanged += (s, e) => RecalcSos();
            pnlLeft.Controls.Add(chkSosO2);

            page.Controls.Add(pnlLeft);

            // Right Result & Preview Panel
            Panel pnlRight = new Panel();
            pnlRight.Dock = DockStyle.Fill;
            pnlRight.Padding = new Padding(12);
            pnlRight.BackColor = Color.White;

            // Result Card
            Panel pnlCard = new Panel();
            pnlCard.Dock = DockStyle.Top;
            pnlCard.Height = 160;
            pnlCard.BackColor = Color.FromArgb(240, 253, 250);
            pnlCard.BorderStyle = BorderStyle.FixedSingle;
            pnlCard.Padding = new Padding(12);

            lblSosScoreBig = new Label();
            lblSosScoreBig.Text = "SOS Score: 0";
            lblSosScoreBig.Font = new Font("Segoe UI", 22f, FontStyle.Bold);
            lblSosScoreBig.ForeColor = Color.FromArgb(15, 118, 110);
            lblSosScoreBig.Location = new Point(12, 10);
            lblSosScoreBig.AutoSize = true;
            pnlCard.Controls.Add(lblSosScoreBig);

            lblSosBadge = new Label();
            lblSosBadge.Text = "🟢 สัญญาณชีพปกติ / เสี่ยงต่ำ (Low Risk)";
            lblSosBadge.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            lblSosBadge.ForeColor = Color.FromArgb(22, 101, 52);
            lblSosBadge.Location = new Point(16, 52);
            lblSosBadge.AutoSize = true;
            pnlCard.Controls.Add(lblSosBadge);

            lblSosBreakdown = new Label();
            lblSosBreakdown.Text = "BT: 0 | SBP: 0 | HR: 0 | RR: 0 | SpO2: 0 | AVPU: 0 | Urine: 0";
            lblSosBreakdown.Font = new Font("Segoe UI", 9f);
            lblSosBreakdown.ForeColor = Color.FromArgb(71, 85, 105);
            lblSosBreakdown.Location = new Point(16, 78);
            lblSosBreakdown.AutoSize = true;
            pnlCard.Controls.Add(lblSosBreakdown);

            lblSosAdvice = new Label();
            lblSosAdvice.Text = "แนวทางการพยาบาล: ติดตามและบันทึกสัญญาณชีพตามรอบปกติทุก 4 ชั่วโมง";
            lblSosAdvice.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblSosAdvice.ForeColor = Color.FromArgb(30, 41, 59);
            lblSosAdvice.Location = new Point(16, 104);
            lblSosAdvice.Size = new Size(460, 46);
            pnlCard.Controls.Add(lblSosAdvice);

            pnlRight.Controls.Add(pnlCard);

            // Action Buttons Panel
            Panel pnlActions = new Panel();
            pnlActions.Dock = DockStyle.Bottom;
            pnlActions.Height = 52;
            pnlActions.BackColor = Color.FromArgb(241, 245, 249);
            pnlActions.Padding = new Padding(6);

            btnSosInsert = CreateActionButton("🛏️ บันทึกลงเตียง 1", Color.FromArgb(13, 148, 136), Color.White, (s, e) => {
                context.InsertToBedNote(currentBed, txtSosPreview.Text);
            });
            pnlActions.Controls.Add(btnSosInsert);

            Button btnSosCopy = CreateActionButton("📋 คัดลอก (Copy)", Color.FromArgb(226, 232, 240), Color.FromArgb(15, 23, 42), (s, e) => {
                CopyText(txtSosPreview.Text);
            });
            btnSosCopy.Left = btnSosInsert.Right + 8;
            pnlActions.Controls.Add(btnSosCopy);

            Button btnSosPaste = CreateActionButton("🚀 วางลง e-PHIS ทันที", Color.FromArgb(15, 118, 110), Color.White, (s, e) => {
                PasteDirectly(txtSosPreview.Text);
            });
            btnSosPaste.Left = btnSosCopy.Right + 8;
            pnlActions.Controls.Add(btnSosPaste);

            pnlRight.Controls.Add(pnlActions);

            // Preview Text Box
            Label lblPreviewTitle = new Label();
            lblPreviewTitle.Text = "📝 ตัวอย่างข้อความบันทึก Nurse Note (แก้ไขก่อนแทรกได้):";
            lblPreviewTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblPreviewTitle.Location = new Point(12, 172);
            lblPreviewTitle.AutoSize = true;
            pnlRight.Controls.Add(lblPreviewTitle);

            txtSosPreview = new TextBox();
            txtSosPreview.Multiline = true;
            txtSosPreview.ScrollBars = ScrollBars.Vertical;
            txtSosPreview.Font = new Font("Leelawadee UI", 9.5f);
            txtSosPreview.Location = new Point(12, 198);
            txtSosPreview.Size = new Size(pnlRight.Width - 24, 230);
            txtSosPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            pnlRight.Controls.Add(txtSosPreview);

            page.Controls.Add(pnlRight);
            pnlRight.BringToFront();

            RecalcSos();
        }

        private void RecalcSos() {
            if (numSosTemp == null) return;

            decimal bt = numSosTemp.Value;
            decimal sbp = numSosSBP.Value;
            decimal dbp = numSosDBP.Value;
            decimal hr = numSosHR.Value;
            decimal rr = numSosRR.Value;
            decimal spo2 = numSosSpO2.Value;
            int avpuIdx = cmbSosAVPU != null ? cmbSosAVPU.SelectedIndex : 0;
            if (avpuIdx < 0) avpuIdx = 0;
            int urineIdx = cmbSosUrine != null ? cmbSosUrine.SelectedIndex : 0;
            if (urineIdx < 0) urineIdx = 0;
            bool onO2 = chkSosO2 != null && chkSosO2.Checked;

            int btScore = 0;
            if (bt < 35.0m) btScore = 3;
            else if (bt <= 36.0m) btScore = 1;
            else if (bt <= 38.0m) btScore = 0;
            else if (bt <= 38.4m) btScore = 1;
            else btScore = 2;

            int sbpScore = 0;
            if (sbp <= 80m) sbpScore = 3;
            else if (sbp <= 90m) sbpScore = 2;
            else if (sbp <= 100m) sbpScore = 1;
            else if (sbp <= 180m) sbpScore = 0;
            else if (sbp <= 199m) sbpScore = 1;
            else sbpScore = 2;

            int hrScore = 0;
            if (hr <= 40m) hrScore = 3;
            else if (hr <= 50m) hrScore = 1;
            else if (hr <= 100m) hrScore = 0;
            else if (hr <= 110m) hrScore = 1;
            else if (hr <= 129m) hrScore = 2;
            else hrScore = 3;

            int rrScore = 0;
            if (rr <= 8m) rrScore = 3;
            else if (rr <= 20m) rrScore = 0;
            else if (rr <= 25m) rrScore = 2;
            else rrScore = 3;

            int spo2Score = 0;
            if (spo2 >= 95m) spo2Score = 0;
            else if (spo2 >= 90m) spo2Score = 1;
            else if (spo2 >= 85m) spo2Score = 2;
            else spo2Score = 3;

            int avpuScore = avpuIdx;
            int urineScore = urineIdx;

            int totalScore = btScore + sbpScore + hrScore + rrScore + spo2Score + avpuScore + urineScore;
            bool hasItem3 = (btScore == 3 || sbpScore == 3 || hrScore == 3 || rrScore == 3 || spo2Score == 3 || avpuScore == 3);

            string riskTitle;
            Color badgeColor;
            string adviceText;

            if (totalScore >= 4 || hasItem3) {
                riskTitle = "🔴 วิกฤต / เสี่ยงสูงมาก (Code SOS / High Risk)";
                badgeColor = Color.FromArgb(220, 38, 38);
                adviceText = "แนวทางการพยาบาล: รายงานแพทย์ทันที (RRT Call), เตรียม Emergency Cart, Monitor V/S ทุก 15-30 นาที, เตรียม O2 และเปิดเส้น IV Fluid";
            } else if (totalScore >= 2) {
                riskTitle = "🟡 เฝ้าระวัง / เสี่ยงปานกลาง (Medium Risk)";
                badgeColor = Color.FromArgb(217, 119, 6);
                adviceText = "แนวทางการพยาบาล: วัด V/S ทุก 1-2 ชั่วโมง, ประเมินอาการซ้ำ, แจ้ง In-charge nurse, หากอาการไม่ดีขึ้นให้รายงานแพทย์";
            } else {
                riskTitle = "🟢 สัญญาณชีพปกติ / เสี่ยงต่ำ (Low Risk)";
                badgeColor = Color.FromArgb(22, 101, 52);
                adviceText = "แนวทางการพยาบาล: ติดตามและบันทึกสัญญาณชีพตามรอบปกติของหอผู้ป่วยทุก 4 ชั่วโมง";
            }

            lblSosScoreBig.Text = string.Format("SOS Score: {0} คะแนน", totalScore);
            lblSosScoreBig.ForeColor = badgeColor;
            lblSosBadge.Text = riskTitle;
            lblSosBadge.ForeColor = badgeColor;
            lblSosBreakdown.Text = string.Format("BT: {0} | SBP: {1} | HR: {2} | RR: {3} | SpO2: {4} | AVPU: {5} | ปัสสาวะ: {6}",
                btScore, sbpScore, hrScore, rrScore, spo2Score, avpuScore, urineScore);
            lblSosAdvice.Text = adviceText;

            string avpuDesc = avpuIdx == 0 ? "Alert (รู้สึกตัวดี)" : (avpuIdx == 1 ? "Voice (เรียกตอบ)" : (avpuIdx == 2 ? "Pain (เจ็บตอบ)" : "Unresponsive (หมดสติ)"));
            string urineDesc = urineIdx == 0 ? ">30 ml/hr (ปกติ)" : (urineIdx == 1 ? "20-30 ml/hr" : "<20 ml/hr (ออกน้อย/ไม่ออก)");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[ประเมินสัญญาณเตือนวิกฤต SOS Score]");
            sb.AppendLine(string.Format("- เวลา: {0}", GetTimestampHeader()));
            sb.AppendLine(string.Format("- V/S: BT {0:F1}°C, BP {1}/{2} mmHg, PR {3} bpm, RR {4} /min, SpO2 {5}%{6}", 
                bt, sbp, dbp, hr, rr, spo2, onO2 ? " (On O2)" : " (Room air)"));
            sb.AppendLine(string.Format("- การประเมิน: AVPU: {0}, ปัสสาวะ: {1}", avpuDesc, urineDesc));
            sb.AppendLine(string.Format("- รวม SOS Score = {0} คะแนน [{1}]", totalScore, riskTitle));
            sb.AppendLine(string.Format("- การพยาบาล: {0}", adviceText.Replace("แนวทางการพยาบาล: ", "")));

            txtSosPreview.Text = sb.ToString();
        }

        // ==========================================
        // TAB 2: MAP & SHOCK INDEX
        // ==========================================
        private void BuildMapTab(TabPage page) {
            Panel pnlLeft = new Panel();
            pnlLeft.Dock = DockStyle.Left;
            pnlLeft.Width = 440;
            pnlLeft.Padding = new Padding(14);

            int y = 14;
            pnlLeft.Controls.Add(CreateLbl("ค่าความดันและสัญญาณชีพ:", 14, y, true));

            y += 32;
            pnlLeft.Controls.Add(CreateLbl("Systolic BP (ตัวบน, mmHg):", 14, y, true));
            y += 24;
            numMapSBP = CreateNum(110m, 40m, 260m, 1m, 0, 14, y, 160, (s, e) => RecalcMap());
            pnlLeft.Controls.Add(numMapSBP);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("Diastolic BP (ตัวล่าง, mmHg):", 14, y, true));
            y += 24;
            numMapDBP = CreateNum(70m, 20m, 180m, 1m, 0, 14, y, 160, (s, e) => RecalcMap());
            pnlLeft.Controls.Add(numMapDBP);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("Heart Rate / ชีพจร (bpm):", 14, y, true));
            y += 24;
            numMapHR = CreateNum(78m, 20m, 240m, 1m, 0, 14, y, 160, (s, e) => RecalcMap());
            pnlLeft.Controls.Add(numMapHR);

            y += 44;
            Label lblInfo = new Label();
            lblInfo.Text = "💡 เกณฑ์ทางคลินิกที่สำคัญ:\n" +
                           "• MAP < 65 mmHg: เลือดไปเลี้ยงอวัยวะสำคัญไม่พอ (เสี่ยงช็อก/ไตวาย)\n" +
                           "• Shock Index > 0.9: สงสัยภาวะช็อกแอบแฝง (Occult Shock)\n" +
                           "• Pulse Pressure < 30 mmHg: Narrow pulse pressure (Hypovolemia)";
            lblInfo.Font = new Font("Segoe UI", 9f);
            lblInfo.ForeColor = Color.FromArgb(71, 85, 105);
            lblInfo.Location = new Point(14, y);
            lblInfo.Size = new Size(410, 80);
            pnlLeft.Controls.Add(lblInfo);

            page.Controls.Add(pnlLeft);

            // Right Panel
            Panel pnlRight = new Panel();
            pnlRight.Dock = DockStyle.Fill;
            pnlRight.Padding = new Padding(12);
            pnlRight.BackColor = Color.White;

            Panel pnlCard = new Panel();
            pnlCard.Dock = DockStyle.Top;
            pnlCard.Height = 175;
            pnlCard.BackColor = Color.FromArgb(240, 253, 250);
            pnlCard.BorderStyle = BorderStyle.FixedSingle;
            pnlCard.Padding = new Padding(12);

            lblMapValueBig = new Label();
            lblMapValueBig.Text = "MAP = 83.3 mmHg";
            lblMapValueBig.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
            lblMapValueBig.ForeColor = Color.FromArgb(15, 118, 110);
            lblMapValueBig.Location = new Point(12, 10);
            lblMapValueBig.AutoSize = true;
            pnlCard.Controls.Add(lblMapValueBig);

            lblMapBadge = new Label();
            lblMapBadge.Text = "🟢 ปกติ: MAP >= 65 mmHg (เพียงพอต่อการกำซาบเลือด)";
            lblMapBadge.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            lblMapBadge.ForeColor = Color.FromArgb(22, 101, 52);
            lblMapBadge.Location = new Point(14, 52);
            lblMapBadge.AutoSize = true;
            pnlCard.Controls.Add(lblMapBadge);

            lblSiValueBig = new Label();
            lblSiValueBig.Text = "Shock Index: 0.71";
            lblSiValueBig.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            lblSiValueBig.ForeColor = Color.FromArgb(30, 41, 59);
            lblSiValueBig.Location = new Point(14, 78);
            lblSiValueBig.AutoSize = true;
            pnlCard.Controls.Add(lblSiValueBig);

            lblPpValue = new Label();
            lblPpValue.Text = "Pulse Pressure: 40 mmHg";
            lblPpValue.Font = new Font("Segoe UI", 9.5f);
            lblPpValue.ForeColor = Color.FromArgb(71, 85, 105);
            lblPpValue.Location = new Point(230, 78);
            lblPpValue.AutoSize = true;
            pnlCard.Controls.Add(lblPpValue);

            lblMapAdvice = new Label();
            lblMapAdvice.Text = "การพยาบาล: การไหลเวียนเลือดไปเลี้ยงสมอง ไต และหัวใจเพียงพอ ติดตาม V/S ตามปกติ";
            lblMapAdvice.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblMapAdvice.ForeColor = Color.FromArgb(30, 41, 59);
            lblMapAdvice.Location = new Point(14, 108);
            lblMapAdvice.Size = new Size(460, 56);
            pnlCard.Controls.Add(lblMapAdvice);

            pnlRight.Controls.Add(pnlCard);

            // Action Buttons
            Panel pnlActions = new Panel();
            pnlActions.Dock = DockStyle.Bottom;
            pnlActions.Height = 52;
            pnlActions.BackColor = Color.FromArgb(241, 245, 249);
            pnlActions.Padding = new Padding(6);

            btnMapInsert = CreateActionButton("🛏️ บันทึกลงเตียง 1", Color.FromArgb(13, 148, 136), Color.White, (s, e) => {
                context.InsertToBedNote(currentBed, txtMapPreview.Text);
            });
            pnlActions.Controls.Add(btnMapInsert);

            Button btnMapCopy = CreateActionButton("📋 คัดลอก (Copy)", Color.FromArgb(226, 232, 240), Color.FromArgb(15, 23, 42), (s, e) => {
                CopyText(txtMapPreview.Text);
            });
            btnMapCopy.Left = btnMapInsert.Right + 8;
            pnlActions.Controls.Add(btnMapCopy);

            Button btnMapPaste = CreateActionButton("🚀 วางลง e-PHIS ทันที", Color.FromArgb(15, 118, 110), Color.White, (s, e) => {
                PasteDirectly(txtMapPreview.Text);
            });
            btnMapPaste.Left = btnMapCopy.Right + 8;
            pnlActions.Controls.Add(btnMapPaste);

            pnlRight.Controls.Add(pnlActions);

            Label lblPreviewTitle = new Label();
            lblPreviewTitle.Text = "📝 ตัวอย่างข้อความบันทึก Nurse Note:";
            lblPreviewTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblPreviewTitle.Location = new Point(12, 186);
            lblPreviewTitle.AutoSize = true;
            pnlRight.Controls.Add(lblPreviewTitle);

            txtMapPreview = new TextBox();
            txtMapPreview.Multiline = true;
            txtMapPreview.ScrollBars = ScrollBars.Vertical;
            txtMapPreview.Font = new Font("Leelawadee UI", 9.5f);
            txtMapPreview.Location = new Point(12, 212);
            txtMapPreview.Size = new Size(pnlRight.Width - 24, 215);
            txtMapPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            pnlRight.Controls.Add(txtMapPreview);

            page.Controls.Add(pnlRight);
            pnlRight.BringToFront();

            RecalcMap();
        }

        private void RecalcMap() {
            if (numMapSBP == null) return;

            decimal sbp = numMapSBP.Value;
            decimal dbp = numMapDBP.Value;
            decimal hr = numMapHR.Value;

            decimal map = (sbp + 2.0m * dbp) / 3.0m;
            decimal pp = sbp - dbp;
            decimal si = sbp > 0 ? (hr / sbp) : 0m;

            string mapStatus;
            Color mapColor;
            string advice;

            if (map < 65.0m) {
                mapStatus = "🔴 วิกฤต: MAP < 65 mmHg (อวัยวะสำคัญขาดเลือด)";
                mapColor = Color.FromArgb(220, 38, 38);
                advice = "แนวทางการพยาบาล: ความดันเฉลี่ยไม่พอเลี้ยงอวัยวะสำคัญ เสี่ยงไตวายเฉียบพลัน/ช็อก รายงานแพทย์ทันที ให้สารน้ำ/ยากระตุ้นความดันตามแผนการรักษา";
            } else if (map > 105.0m) {
                mapStatus = "🟡 สูงกว่าปกติ (Hypertension)";
                mapColor = Color.FromArgb(217, 119, 6);
                advice = "แนวทางการพยาบาล: ความดันเฉลี่ยสูง เฝ้าระวังอาการปวดศีรษะ ตามัว คลื่นไส้ อาการทางสมอง";
            } else {
                mapStatus = "🟢 ปกติ: MAP 65 - 105 mmHg (การไหลเวียนเลือดเพียงพอ)";
                mapColor = Color.FromArgb(22, 101, 52);
                advice = "แนวทางการพยาบาล: การกำซาบเลือดของอวัยวะสำคัญอยู่ในเกณฑ์ปกติ ติดตามสัญญาณชีพตามรอบ";
            }

            string siStatus = si > 0.9m ? "🔴 วิกฤต (>0.9 เสี่ยงภาวะช็อกแอบแฝง)" : (si >= 0.7m ? "🟡 เฝ้าระวัง (0.7-0.9)" : "🟢 ปกติ (0.5-0.7)");

            lblMapValueBig.Text = string.Format("MAP = {0:F1} mmHg", map);
            lblMapValueBig.ForeColor = mapColor;
            lblMapBadge.Text = mapStatus;
            lblMapBadge.ForeColor = mapColor;
            lblSiValueBig.Text = string.Format("Shock Index: {0:F2} [{1}]", si, siStatus);
            lblPpValue.Text = string.Format("Pulse Pressure: {0} mmHg", pp);
            lblMapAdvice.Text = advice;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[ประเมิน MAP & Shock Index]");
            sb.AppendLine(string.Format("- เวลา: {0}", GetTimestampHeader()));
            sb.AppendLine(string.Format("- สัญญาณชีพ: BP {0}/{1} mmHg, PR {2} bpm", sbp, dbp, hr));
            sb.AppendLine(string.Format("- Mean Arterial Pressure (MAP) = {0:F1} mmHg [{1}]", map, mapStatus));
            sb.AppendLine(string.Format("- Pulse Pressure = {0} mmHg, Shock Index (HR/SBP) = {1:F2} [{2}]", pp, si, siStatus));
            sb.AppendLine(string.Format("- การพยาบาล: {0}", advice.Replace("แนวทางการพยาบาล: ", "")));

            txtMapPreview.Text = sb.ToString();
        }

        // ==========================================
        // TAB 3: IV INFUSION & DROP RATE
        // ==========================================
        private void BuildIvTab(TabPage page) {
            Panel pnlLeft = new Panel();
            pnlLeft.Dock = DockStyle.Left;
            pnlLeft.Width = 440;
            pnlLeft.Padding = new Padding(14);

            int y = 14;
            pnlLeft.Controls.Add(CreateLbl("คำนวณอัตราสารน้ำทางหลอดเลือดดำ:", 14, y, true));

            y += 32;
            pnlLeft.Controls.Add(CreateLbl("ชนิดสารน้ำ (IV Fluid):", 14, y, true));
            y += 24;
            cmbIvFluid = new ComboBox();
            cmbIvFluid.Font = new Font("Segoe UI", 10f);
            cmbIvFluid.Location = new Point(14, y);
            cmbIvFluid.Size = new Size(390, 29);
            cmbIvFluid.Items.Add("0.9% NSS (Normal Saline)");
            cmbIvFluid.Items.Add("5% D/NSS");
            cmbIvFluid.Items.Add("5% D/NSS/2");
            cmbIvFluid.Items.Add("Acetar (Acetated Ringer's)");
            cmbIvFluid.Items.Add("5% D/W");
            cmbIvFluid.Items.Add("Ringer's Lactate (LRS)");
            cmbIvFluid.Items.Add("10% D/W");
            cmbIvFluid.Items.Add("0.45% NSS");
            cmbIvFluid.SelectedIndex = 0;
            cmbIvFluid.SelectedIndexChanged += (s, e) => RecalcIv();
            cmbIvFluid.TextChanged += (s, e) => RecalcIv();
            pnlLeft.Controls.Add(cmbIvFluid);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ปริมาตรสารน้ำทั้งหมด (Total Volume, ml):", 14, y, true));
            y += 24;
            numIvVolume = CreateNum(1000m, 10m, 5000m, 50m, 0, 14, y, 160, (s, e) => RecalcIv());
            pnlLeft.Controls.Add(numIvVolume);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ระยะเวลาที่ต้องการให้หมด (ชั่วโมง : นาที):", 14, y, true));
            y += 24;
            numIvHours = CreateNum(8m, 0m, 72m, 1m, 0, 14, y, 110, (s, e) => RecalcIv());
            pnlLeft.Controls.Add(numIvHours);
            pnlLeft.Controls.Add(CreateLbl("ชั่วโมง", 130, y + 4, false));

            numIvMinutes = CreateNum(0m, 0m, 55m, 5m, 0, 190, y, 90, (s, e) => RecalcIv());
            pnlLeft.Controls.Add(numIvMinutes);
            pnlLeft.Controls.Add(CreateLbl("นาที", 286, y + 4, false));

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ขนาดชุดให้สารน้ำ (Drop Factor):", 14, y, true));
            y += 24;
            cmbIvDropFactor = new ComboBox();
            cmbIvDropFactor.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbIvDropFactor.Font = new Font("Segoe UI", 10f);
            cmbIvDropFactor.Location = new Point(14, y);
            cmbIvDropFactor.Size = new Size(390, 29);
            cmbIvDropFactor.Items.Add("20 gtt/ml (ชุด Macro ผู้ใหญ่มาตรฐาน)");
            cmbIvDropFactor.Items.Add("15 gtt/ml (ชุดให้เลือด / Macro 15)");
            cmbIvDropFactor.Items.Add("60 gtt/ml (ชุด Microdrip เด็ก/คุมละเอียด)");
            cmbIvDropFactor.Items.Add("10 gtt/ml (ชุดหยดใหญ่พิเศษ)");
            cmbIvDropFactor.SelectedIndex = 0;
            cmbIvDropFactor.SelectedIndexChanged += (s, e) => RecalcIv();
            pnlLeft.Controls.Add(cmbIvDropFactor);

            page.Controls.Add(pnlLeft);

            // Right Panel
            Panel pnlRight = new Panel();
            pnlRight.Dock = DockStyle.Fill;
            pnlRight.Padding = new Padding(12);
            pnlRight.BackColor = Color.White;

            Panel pnlCard = new Panel();
            pnlCard.Dock = DockStyle.Top;
            pnlCard.Height = 165;
            pnlCard.BackColor = Color.FromArgb(240, 253, 250);
            pnlCard.BorderStyle = BorderStyle.FixedSingle;
            pnlCard.Padding = new Padding(12);

            lblIvRateBig = new Label();
            lblIvRateBig.Text = "อัตราการให้: 125.0 ml/hr";
            lblIvRateBig.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
            lblIvRateBig.ForeColor = Color.FromArgb(15, 118, 110);
            lblIvRateBig.Location = new Point(12, 10);
            lblIvRateBig.AutoSize = true;
            pnlCard.Controls.Add(lblIvRateBig);

            lblIvDropsBig = new Label();
            lblIvDropsBig.Text = "💧 อัตราหยด: 42 หยด/นาที (gtt/min)";
            lblIvDropsBig.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            lblIvDropsBig.ForeColor = Color.FromArgb(14, 116, 144);
            lblIvDropsBig.Location = new Point(14, 52);
            lblIvDropsBig.AutoSize = true;
            pnlCard.Controls.Add(lblIvDropsBig);

            lblIvInterval = new Label();
            lblIvInterval.Text = "⏱️ ความเร็วหยด: 1 หยด ทุกๆ 1.4 วินาที";
            lblIvInterval.Font = new Font("Segoe UI", 10f);
            lblIvInterval.ForeColor = Color.FromArgb(71, 85, 105);
            lblIvInterval.Location = new Point(14, 82);
            lblIvInterval.AutoSize = true;
            pnlCard.Controls.Add(lblIvInterval);

            lblIvFinishTime = new Label();
            lblIvFinishTime.Text = "🕒 เวลาคาดว่าจะหมดถุง: 17:30 น. (อีก 8 ชม. 0 นาที)";
            lblIvFinishTime.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            lblIvFinishTime.ForeColor = Color.FromArgb(15, 23, 42);
            lblIvFinishTime.Location = new Point(14, 112);
            lblIvFinishTime.AutoSize = true;
            pnlCard.Controls.Add(lblIvFinishTime);

            pnlRight.Controls.Add(pnlCard);

            // Action Buttons
            Panel pnlActions = new Panel();
            pnlActions.Dock = DockStyle.Bottom;
            pnlActions.Height = 52;
            pnlActions.BackColor = Color.FromArgb(241, 245, 249);
            pnlActions.Padding = new Padding(6);

            btnIvInsert = CreateActionButton("🛏️ บันทึกลงเตียง 1", Color.FromArgb(13, 148, 136), Color.White, (s, e) => {
                context.InsertToBedNote(currentBed, txtIvPreview.Text);
            });
            pnlActions.Controls.Add(btnIvInsert);

            Button btnIvCopy = CreateActionButton("📋 คัดลอก (Copy)", Color.FromArgb(226, 232, 240), Color.FromArgb(15, 23, 42), (s, e) => {
                CopyText(txtIvPreview.Text);
            });
            btnIvCopy.Left = btnIvInsert.Right + 8;
            pnlActions.Controls.Add(btnIvCopy);

            Button btnIvPaste = CreateActionButton("🚀 วางลง e-PHIS ทันที", Color.FromArgb(15, 118, 110), Color.White, (s, e) => {
                PasteDirectly(txtIvPreview.Text);
            });
            btnIvPaste.Left = btnIvCopy.Right + 8;
            pnlActions.Controls.Add(btnIvPaste);

            pnlRight.Controls.Add(pnlActions);

            Label lblPreviewTitle = new Label();
            lblPreviewTitle.Text = "📝 ตัวอย่างข้อความบันทึก Nurse Note:";
            lblPreviewTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblPreviewTitle.Location = new Point(12, 180);
            lblPreviewTitle.AutoSize = true;
            pnlRight.Controls.Add(lblPreviewTitle);

            txtIvPreview = new TextBox();
            txtIvPreview.Multiline = true;
            txtIvPreview.ScrollBars = ScrollBars.Vertical;
            txtIvPreview.Font = new Font("Leelawadee UI", 9.5f);
            txtIvPreview.Location = new Point(12, 206);
            txtIvPreview.Size = new Size(pnlRight.Width - 24, 220);
            txtIvPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            pnlRight.Controls.Add(txtIvPreview);

            page.Controls.Add(pnlRight);
            pnlRight.BringToFront();

            RecalcIv();
        }

        private void RecalcIv() {
            if (numIvVolume == null) return;

            string fluid = cmbIvFluid != null ? cmbIvFluid.Text.Trim() : "IV Fluid";
            if (string.IsNullOrEmpty(fluid)) fluid = "IV Fluid";

            decimal vol = numIvVolume.Value;
            decimal hrs = numIvHours.Value;
            decimal mins = numIvMinutes.Value;
            decimal totalHrs = hrs + (mins / 60.0m);
            if (totalHrs <= 0) totalHrs = 1.0m;

            decimal factor = 20m;
            int factorIdx = cmbIvDropFactor != null ? cmbIvDropFactor.SelectedIndex : 0;
            if (factorIdx == 1) factor = 15m;
            else if (factorIdx == 2) factor = 60m;
            else if (factorIdx == 3) factor = 10m;

            decimal rateMlHr = vol / totalHrs;
            decimal dropRate = (rateMlHr * factor) / 60.0m;
            decimal secPerDrop = dropRate > 0 ? (60.0m / dropRate) : 0m;

            DateTime finish = DateTime.Now.AddHours((double)totalHrs);
            int thaiYear = finish.Year + 543;
            string finishStr = string.Format("{0:D2}:{1:D2} น.", finish.Hour, finish.Minute);

            lblIvRateBig.Text = string.Format("อัตราการให้: {0:F1} ml/hr", rateMlHr);
            lblIvDropsBig.Text = string.Format("💧 อัตราหยด: {0:F0} หยด/นาที (gtt/min)", dropRate);
            lblIvInterval.Text = string.Format("⏱️ ความเร็วหยด: 1 หยด ทุกๆ {0:F1} วินาที", secPerDrop);
            lblIvFinishTime.Text = string.Format("🕒 เวลาคาดว่าจะหมดถุง: {0} (อีก {1:F0} ชม. {2} นาที)", finishStr, Math.Floor(totalHrs), (int)mins);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[คำนวณอัตราสารน้ำ IV Infusion]");
            sb.AppendLine(string.Format("- สารน้ำ: {0} จำนวน {1:F0} ml", fluid, vol));
            sb.AppendLine(string.Format("- แผนการให้: ให้หมดใน {0:F0} ชม. {1} นาที (ชุดให้ {2:F0} gtt/ml)", Math.Floor(totalHrs), mins, factor));
            sb.AppendLine(string.Format("- อัตราการให้ (Rate): {0:F1} ml/hr", rateMlHr));
            sb.AppendLine(string.Format("- อัตราหยด: {0:F0} หยด/นาที (gtt/min) [ความเร็ว 1 หยด ทุก {1:F1} วินาที]", dropRate, secPerDrop));
            sb.AppendLine(string.Format("- คาดว่าสารน้ำจะหมดเวลา: {0}", finishStr));
            sb.AppendLine("- การพยาบาล: ตรวจสอบ IV site ไม่บวมแดง รั่ว หรืออักเสบ phlebitis gr.0");

            txtIvPreview.Text = sb.ToString();
        }

        // ==========================================
        // TAB 4: INOTROPE & DRUG DRIP
        // ==========================================
        private void BuildDripsTab(TabPage page) {
            Panel pnlLeft = new Panel();
            pnlLeft.Dock = DockStyle.Left;
            pnlLeft.Width = 440;
            pnlLeft.Padding = new Padding(14);
            pnlLeft.AutoScroll = true;

            int y = 10;
            pnlLeft.Controls.Add(CreateLbl("สูตรยาและการคำนวณดริปยาฉุกเฉิน:", 14, y, true));

            y += 30;
            pnlLeft.Controls.Add(CreateLbl("เลือกรายการยาฉุกเฉินมาตรฐาน (Drug Preset):", 14, y, true));
            y += 24;
            cmbDrugPreset = new ComboBox();
            cmbDrugPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDrugPreset.Font = new Font("Segoe UI", 9.5f);
            cmbDrugPreset.Location = new Point(14, y);
            cmbDrugPreset.Size = new Size(390, 29);
            cmbDrugPreset.Items.Add("Levophed (Norepinephrine) [4 mg / 100 ml D5W = 40 mcg/ml]");
            cmbDrugPreset.Items.Add("Levophed (Norepinephrine) [4 mg / 250 ml D5W = 16 mcg/ml]");
            cmbDrugPreset.Items.Add("Levophed (Norepinephrine) [8 mg / 100 ml D5W = 80 mcg/ml]");
            cmbDrugPreset.Items.Add("Dopamine [250 mg / 250 ml D5W = 1,000 mcg/ml]");
            cmbDrugPreset.Items.Add("Dobutamine [250 mg / 250 ml D5W = 1,000 mcg/ml]");
            cmbDrugPreset.Items.Add("Nicardipine [10 mg / 100 ml NSS = 0.1 mg/ml]");
            cmbDrugPreset.Items.Add("Regular Insulin (RI) [100 units / 100 ml NSS = 1 unit/ml]");
            cmbDrugPreset.Items.Add("Cordarone (Amiodarone) [900 mg / 500 ml D5W = 1.8 mg/ml]");
            cmbDrugPreset.Items.Add("กำหนดสูตรยาเอง (Custom Drug Mix)");
            cmbDrugPreset.SelectedIndex = 0;
            cmbDrugPreset.SelectedIndexChanged += (s, e) => OnDrugPresetChanged();
            pnlLeft.Controls.Add(cmbDrugPreset);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("น้ำหนักตัวผู้ป่วย (Patient Weight, kg):", 14, y, true));
            y += 24;
            numDripWeight = CreateNum(60m, 10m, 250m, 1m, 1, 14, y, 160, (s, e) => RecalcDrip());
            pnlLeft.Controls.Add(numDripWeight);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("โหมดการคำนวณ:", 14, y, true));
            y += 24;
            radModeDoseToRate = new RadioButton();
            radModeDoseToRate.Text = "คำนวณ Rate ปั๊ม (ml/hr) จากขนาดยา";
            radModeDoseToRate.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            radModeDoseToRate.Location = new Point(14, y);
            radModeDoseToRate.Size = new Size(390, 24);
            radModeDoseToRate.Checked = true;
            radModeDoseToRate.CheckedChanged += (s, e) => OnModeChanged();
            pnlLeft.Controls.Add(radModeDoseToRate);

            y += 26;
            radModeRateToDose = new RadioButton();
            radModeRateToDose.Text = "คำนวณ ขนาดยาที่ได้รับ จาก Rate ปั๊ม (Reverse)";
            radModeRateToDose.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            radModeRateToDose.Location = new Point(14, y);
            radModeRateToDose.Size = new Size(390, 24);
            radModeRateToDose.CheckedChanged += (s, e) => OnModeChanged();
            pnlLeft.Controls.Add(radModeRateToDose);

            y += 34;
            pnlLeft.Controls.Add(CreateLbl("ขนาดยาที่ต้องการ (Desired Dose):", 14, y, true));
            y += 24;
            numDose = CreateNum(0.1m, 0.001m, 1000m, 0.05m, 3, 14, y, 160, (s, e) => {
                if (radModeDoseToRate.Checked) RecalcDrip();
            });
            pnlLeft.Controls.Add(numDose);

            lblDoseUnit = new Label();
            lblDoseUnit.Text = "mcg/kg/min";
            lblDoseUnit.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            lblDoseUnit.Location = new Point(185, y + 4);
            lblDoseUnit.AutoSize = true;
            pnlLeft.Controls.Add(lblDoseUnit);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("อัตราปั๊มสารน้ำ (Infusion Pump Rate, ml/hr):", 14, y, true));
            y += 24;
            numPumpRate = CreateNum(9.0m, 0.1m, 999m, 0.5m, 1, 14, y, 160, (s, e) => {
                if (radModeRateToDose.Checked) RecalcDrip();
            });
            numPumpRate.Enabled = false;
            pnlLeft.Controls.Add(numPumpRate);

            page.Controls.Add(pnlLeft);

            // Right Panel
            Panel pnlRight = new Panel();
            pnlRight.Dock = DockStyle.Fill;
            pnlRight.Padding = new Padding(12);
            pnlRight.BackColor = Color.White;

            Panel pnlCard = new Panel();
            pnlCard.Dock = DockStyle.Top;
            pnlCard.Height = 165;
            pnlCard.BackColor = Color.FromArgb(240, 253, 250);
            pnlCard.BorderStyle = BorderStyle.FixedSingle;
            pnlCard.Padding = new Padding(12);

            lblDripResultBig = new Label();
            lblDripResultBig.Text = "Infusion Rate = 9.0 ml/hr";
            lblDripResultBig.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
            lblDripResultBig.ForeColor = Color.FromArgb(15, 118, 110);
            lblDripResultBig.Location = new Point(12, 10);
            lblDripResultBig.AutoSize = true;
            pnlCard.Controls.Add(lblDripResultBig);

            lblDripSubtitle = new Label();
            lblDripSubtitle.Text = "ยา: Levophed (40 mcg/ml) | ขนาดยา: 0.100 mcg/kg/min";
            lblDripSubtitle.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            lblDripSubtitle.ForeColor = Color.FromArgb(14, 116, 144);
            lblDripSubtitle.Location = new Point(14, 52);
            lblDripSubtitle.AutoSize = true;
            pnlCard.Controls.Add(lblDripSubtitle);

            lblDripSafetyAdvice = new Label();
            lblDripSafetyAdvice.Text = "⚠️ ข้อควรระวังทางการพยาบาล:\n" +
                                       "• บริหารยาผ่าน Infusion Pump เท่านั้น | วัด BP/HR ทุก 15 นาที\n" +
                                       "• แนะนำให้ผ่าน Central Line ป้องกัน Peripheral Extravasation & Necrosis";
            lblDripSafetyAdvice.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblDripSafetyAdvice.ForeColor = Color.FromArgb(185, 28, 28);
            lblDripSafetyAdvice.Location = new Point(14, 86);
            lblDripSafetyAdvice.Size = new Size(460, 65);
            pnlCard.Controls.Add(lblDripSafetyAdvice);

            pnlRight.Controls.Add(pnlCard);

            // Action Buttons
            Panel pnlActions = new Panel();
            pnlActions.Dock = DockStyle.Bottom;
            pnlActions.Height = 52;
            pnlActions.BackColor = Color.FromArgb(241, 245, 249);
            pnlActions.Padding = new Padding(6);

            btnDripInsert = CreateActionButton("🛏️ บันทึกลงเตียง 1", Color.FromArgb(13, 148, 136), Color.White, (s, e) => {
                context.InsertToBedNote(currentBed, txtDripPreview.Text);
            });
            pnlActions.Controls.Add(btnDripInsert);

            Button btnDripCopy = CreateActionButton("📋 คัดลอก (Copy)", Color.FromArgb(226, 232, 240), Color.FromArgb(15, 23, 42), (s, e) => {
                CopyText(txtDripPreview.Text);
            });
            btnDripCopy.Left = btnDripInsert.Right + 8;
            pnlActions.Controls.Add(btnDripCopy);

            Button btnDripPaste = CreateActionButton("🚀 วางลง e-PHIS ทันที", Color.FromArgb(15, 118, 110), Color.White, (s, e) => {
                PasteDirectly(txtDripPreview.Text);
            });
            btnDripPaste.Left = btnDripCopy.Right + 8;
            pnlActions.Controls.Add(btnDripPaste);

            pnlRight.Controls.Add(pnlActions);

            Label lblPreviewTitle = new Label();
            lblPreviewTitle.Text = "📝 ตัวอย่างข้อความบันทึก Nurse Note:";
            lblPreviewTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblPreviewTitle.Location = new Point(12, 180);
            lblPreviewTitle.AutoSize = true;
            pnlRight.Controls.Add(lblPreviewTitle);

            txtDripPreview = new TextBox();
            txtDripPreview.Multiline = true;
            txtDripPreview.ScrollBars = ScrollBars.Vertical;
            txtDripPreview.Font = new Font("Leelawadee UI", 9.5f);
            txtDripPreview.Location = new Point(12, 206);
            txtDripPreview.Size = new Size(pnlRight.Width - 24, 220);
            txtDripPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            pnlRight.Controls.Add(txtDripPreview);

            page.Controls.Add(pnlRight);
            pnlRight.BringToFront();

            OnDrugPresetChanged();
        }

        private void OnModeChanged() {
            if (radModeDoseToRate.Checked) {
                numDose.Enabled = true;
                numPumpRate.Enabled = false;
            } else {
                numDose.Enabled = false;
                numPumpRate.Enabled = true;
            }
            RecalcDrip();
        }

        private void OnDrugPresetChanged() {
            int idx = cmbDrugPreset.SelectedIndex;
            if (idx == 0) { // Levophed 4mg in 100ml
                lblDoseUnit.Text = "mcg/kg/min";
                numDose.Value = 0.1m;
            } else if (idx == 1) { // Levophed 4mg in 250ml
                lblDoseUnit.Text = "mcg/kg/min";
                numDose.Value = 0.1m;
            } else if (idx == 2) { // Levophed 8mg in 100ml
                lblDoseUnit.Text = "mcg/kg/min";
                numDose.Value = 0.1m;
            } else if (idx == 3) { // Dopamine 250mg in 250ml
                lblDoseUnit.Text = "mcg/kg/min";
                numDose.Value = 5.0m;
            } else if (idx == 4) { // Dobutamine 250mg in 250ml
                lblDoseUnit.Text = "mcg/kg/min";
                numDose.Value = 5.0m;
            } else if (idx == 5) { // Nicardipine 10mg in 100ml
                lblDoseUnit.Text = "mg/hr";
                numDose.Value = 5.0m;
            } else if (idx == 6) { // RI 100u in 100ml
                lblDoseUnit.Text = "units/hr";
                numDose.Value = 2.0m;
            } else if (idx == 7) { // Amiodarone 900mg in 500ml
                lblDoseUnit.Text = "mg/min";
                numDose.Value = 1.0m;
            } else {
                lblDoseUnit.Text = "mcg/kg/min";
            }
            RecalcDrip();
        }

        private void RecalcDrip() {
            if (numDripWeight == null) return;

            int idx = cmbDrugPreset.SelectedIndex;
            decimal weight = numDripWeight.Value;
            if (weight <= 0) weight = 60m;

            string drugName = "";
            string concDesc = "";
            decimal conc = 40m; // in mcg/ml or mg/ml
            bool isWeightBased = true;
            bool isPerMinute = true;
            string unit = lblDoseUnit.Text;

            if (idx == 0) {
                drugName = "Levophed (Norepinephrine)";
                concDesc = "4 mg in D5W 100 ml (40 mcg/ml)";
                conc = 40m; // mcg/ml
                isWeightBased = true;
                isPerMinute = true;
            } else if (idx == 1) {
                drugName = "Levophed (Norepinephrine)";
                concDesc = "4 mg in D5W 250 ml (16 mcg/ml)";
                conc = 16m;
                isWeightBased = true;
                isPerMinute = true;
            } else if (idx == 2) {
                drugName = "Levophed (Norepinephrine Double)";
                concDesc = "8 mg in D5W 100 ml (80 mcg/ml)";
                conc = 80m;
                isWeightBased = true;
                isPerMinute = true;
            } else if (idx == 3) {
                drugName = "Dopamine";
                concDesc = "250 mg in D5W 250 ml (1,000 mcg/ml)";
                conc = 1000m;
                isWeightBased = true;
                isPerMinute = true;
            } else if (idx == 4) {
                drugName = "Dobutamine";
                concDesc = "250 mg in D5W 250 ml (1,000 mcg/ml)";
                conc = 1000m;
                isWeightBased = true;
                isPerMinute = true;
            } else if (idx == 5) {
                drugName = "Nicardipine (Cardene)";
                concDesc = "10 mg in NSS 100 ml (0.1 mg/ml)";
                conc = 0.1m; // mg/ml
                isWeightBased = false;
                isPerMinute = false;
            } else if (idx == 6) {
                drugName = "Regular Insulin (RI)";
                concDesc = "100 units in NSS 100 ml (1 unit/ml)";
                conc = 1.0m; // unit/ml
                isWeightBased = false;
                isPerMinute = false;
            } else if (idx == 7) {
                drugName = "Cordarone (Amiodarone)";
                concDesc = "900 mg in D5W 500 ml (1.8 mg/ml)";
                conc = 1.8m; // mg/ml
                isWeightBased = false;
                isPerMinute = true;
            } else {
                drugName = "Custom Drug Mix";
                concDesc = "กำหนดเอง";
                conc = 40m;
                isWeightBased = true;
                isPerMinute = true;
            }

            decimal dose = numDose.Value;
            decimal pumpRate = numPumpRate.Value;

            if (radModeDoseToRate.Checked) {
                // Dose -> Rate
                if (isWeightBased) {
                    // dose in mcg/kg/min, conc in mcg/ml
                    // rate (ml/hr) = (dose * weight * 60) / conc
                    pumpRate = conc > 0 ? ((dose * weight * 60m) / conc) : 0m;
                } else if (isPerMinute) {
                    // dose in mg/min, conc in mg/ml
                    pumpRate = conc > 0 ? ((dose * 60m) / conc) : 0m;
                } else {
                    // dose in mg/hr or units/hr, conc in mg/ml or unit/ml
                    pumpRate = conc > 0 ? (dose / conc) : 0m;
                }

                if (pumpRate > 999m) pumpRate = 999m;
                if (pumpRate < 0.1m) pumpRate = 0.1m;
                numPumpRate.Value = Math.Round(pumpRate, 1);
            } else {
                // Rate -> Dose
                if (isWeightBased) {
                    // dose = (rate * conc) / (weight * 60)
                    dose = (weight > 0 && conc > 0) ? ((pumpRate * conc) / (weight * 60m)) : 0m;
                } else if (isPerMinute) {
                    dose = conc > 0 ? ((pumpRate * conc) / 60m) : 0m;
                } else {
                    dose = conc * pumpRate;
                }
                numDose.Value = Math.Round(dose, 3);
            }

            lblDripResultBig.Text = string.Format("Infusion Pump Rate = {0:F1} ml/hr", numPumpRate.Value);
            lblDripSubtitle.Text = string.Format("ยา: {0} ({1}) | ขนาดยา: {2:F3} {3}", drugName, concDesc, numDose.Value, unit);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[คำนวณและปรับอัตราดริปยาฉุกเฉิน (Inotrope/Vasopressor)]");
            sb.AppendLine(string.Format("- เวลา: {0}", GetTimestampHeader()));
            sb.AppendLine(string.Format("- ข้อมูล: น้ำหนักผู้ป่วย {0:F1} kg", weight));
            sb.AppendLine(string.Format("- รายการยา: {0} [{1}]", drugName, concDesc));
            sb.AppendLine(string.Format("- ขนาดยาที่ได้รับ: {0:F3} {1}", numDose.Value, unit));
            sb.AppendLine(string.Format("- อัตราการให้ (Infusion Pump Rate): {0:F1} ml/hr", numPumpRate.Value));
            sb.AppendLine("- การพยาบาล: บริหารยาผ่าน Infusion pump เท่านั้น, ติดตาม BP/HR ทุก 15 นาที, ตรวจสอบ IV site ป้องกัน extravasation");

            txtDripPreview.Text = sb.ToString();
        }

        // ==========================================
        // TAB 5: RENAL FUNCTION (CrCl) & BMI
        // ==========================================
        private void BuildRenalTab(TabPage page) {
            Panel pnlLeft = new Panel();
            pnlLeft.Dock = DockStyle.Left;
            pnlLeft.Width = 440;
            pnlLeft.Padding = new Padding(14);
            pnlLeft.AutoScroll = true;

            int y = 14;
            pnlLeft.Controls.Add(CreateLbl("ข้อมูลผู้ป่วยสำหรับการคำนวณการทำงานของไต:", 14, y, true));

            y += 30;
            pnlLeft.Controls.Add(CreateLbl("เพศ (Gender):", 14, y, true));
            y += 24;
            radGenderMale = new RadioButton();
            radGenderMale.Text = "ชาย (Male)";
            radGenderMale.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            radGenderMale.Location = new Point(14, y);
            radGenderMale.Size = new Size(120, 24);
            radGenderMale.Checked = true;
            radGenderMale.CheckedChanged += (s, e) => RecalcRenal();
            pnlLeft.Controls.Add(radGenderMale);

            radGenderFemale = new RadioButton();
            radGenderFemale.Text = "หญิง (Female) [CrCl x 0.85]";
            radGenderFemale.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            radGenderFemale.Location = new Point(140, y);
            radGenderFemale.Size = new Size(240, 24);
            radGenderFemale.CheckedChanged += (s, e) => RecalcRenal();
            pnlLeft.Controls.Add(radGenderFemale);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("อายุ (Age, ปี):", 14, y, true));
            y += 24;
            numRenalAge = CreateNum(65m, 1m, 120m, 1m, 0, 14, y, 160, (s, e) => RecalcRenal());
            pnlLeft.Controls.Add(numRenalAge);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("น้ำหนัก (Weight, kg):", 14, y, true));
            y += 24;
            numRenalWeight = CreateNum(60m, 10m, 250m, 1m, 1, 14, y, 160, (s, e) => RecalcRenal());
            pnlLeft.Controls.Add(numRenalWeight);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ส่วนสูง (Height, cm):", 14, y, true));
            y += 24;
            numRenalHeight = CreateNum(160m, 50m, 230m, 1m, 0, 14, y, 160, (s, e) => RecalcRenal());
            pnlLeft.Controls.Add(numRenalHeight);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("Serum Creatinine (mg/dL):", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(ค่าปกติ ~0.6 - 1.2)", 230, y, false));
            y += 24;
            numRenalCr = CreateNum(1.20m, 0.10m, 25.0m, 0.05m, 2, 14, y, 160, (s, e) => RecalcRenal());
            pnlLeft.Controls.Add(numRenalCr);

            page.Controls.Add(pnlLeft);

            // Right Panel
            Panel pnlRight = new Panel();
            pnlRight.Dock = DockStyle.Fill;
            pnlRight.Padding = new Padding(12);
            pnlRight.BackColor = Color.White;

            Panel pnlCard = new Panel();
            pnlCard.Dock = DockStyle.Top;
            pnlCard.Height = 175;
            pnlCard.BackColor = Color.FromArgb(240, 253, 250);
            pnlCard.BorderStyle = BorderStyle.FixedSingle;
            pnlCard.Padding = new Padding(12);

            lblCrClBig = new Label();
            lblCrClBig.Text = "CrCl = 52.1 ml/min";
            lblCrClBig.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
            lblCrClBig.ForeColor = Color.FromArgb(15, 118, 110);
            lblCrClBig.Location = new Point(12, 10);
            lblCrClBig.AutoSize = true;
            pnlCard.Controls.Add(lblCrClBig);

            lblCkdStageBadge = new Label();
            lblCkdStageBadge.Text = "🟡 CKD Stage 3 (ไตลดลงปานกลาง 30 - 59 ml/min)";
            lblCkdStageBadge.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            lblCkdStageBadge.ForeColor = Color.FromArgb(217, 119, 6);
            lblCkdStageBadge.Location = new Point(14, 52);
            lblCkdStageBadge.AutoSize = true;
            pnlCard.Controls.Add(lblCkdStageBadge);

            lblBmiBig = new Label();
            lblBmiBig.Text = "BMI: 23.44 kg/m²";
            lblBmiBig.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            lblBmiBig.ForeColor = Color.FromArgb(15, 23, 42);
            lblBmiBig.Location = new Point(14, 80);
            lblBmiBig.AutoSize = true;
            pnlCard.Controls.Add(lblBmiBig);

            lblBmiBadge = new Label();
            lblBmiBadge.Text = "(น้ำหนักเกินเกณฑ์เล็กน้อย / Overweight)";
            lblBmiBadge.Font = new Font("Segoe UI", 9.5f);
            lblBmiBadge.ForeColor = Color.FromArgb(71, 85, 105);
            lblBmiBadge.Location = new Point(160, 80);
            lblBmiBadge.AutoSize = true;
            pnlCard.Controls.Add(lblBmiBadge);

            lblIbw = new Label();
            lblIbw.Text = "น้ำหนักมาตรฐาน (IBW): 56.9 kg";
            lblIbw.Font = new Font("Segoe UI", 9f);
            lblIbw.ForeColor = Color.FromArgb(100, 116, 139);
            lblIbw.Location = new Point(14, 104);
            lblIbw.AutoSize = true;
            pnlCard.Controls.Add(lblIbw);

            lblRenalAdvice = new Label();
            lblRenalAdvice.Text = "⚠️ ข้อควรระวัง: ต้องปรับขนาดยาปฏิชีวนะและยาที่ขับทางไต (Enoxaparin, Colistin, Meropenem)";
            lblRenalAdvice.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            lblRenalAdvice.ForeColor = Color.FromArgb(185, 28, 28);
            lblRenalAdvice.Location = new Point(14, 126);
            lblRenalAdvice.Size = new Size(460, 42);
            pnlCard.Controls.Add(lblRenalAdvice);

            pnlRight.Controls.Add(pnlCard);

            // Action Buttons
            Panel pnlActions = new Panel();
            pnlActions.Dock = DockStyle.Bottom;
            pnlActions.Height = 52;
            pnlActions.BackColor = Color.FromArgb(241, 245, 249);
            pnlActions.Padding = new Padding(6);

            btnRenalInsert = CreateActionButton("🛏️ บันทึกลงเตียง 1", Color.FromArgb(13, 148, 136), Color.White, (s, e) => {
                context.InsertToBedNote(currentBed, txtRenalPreview.Text);
            });
            pnlActions.Controls.Add(btnRenalInsert);

            Button btnRenalCopy = CreateActionButton("📋 คัดลอก (Copy)", Color.FromArgb(226, 232, 240), Color.FromArgb(15, 23, 42), (s, e) => {
                CopyText(txtRenalPreview.Text);
            });
            btnRenalCopy.Left = btnRenalInsert.Right + 8;
            pnlActions.Controls.Add(btnRenalCopy);

            Button btnRenalPaste = CreateActionButton("🚀 วางลง e-PHIS ทันที", Color.FromArgb(15, 118, 110), Color.White, (s, e) => {
                PasteDirectly(txtRenalPreview.Text);
            });
            btnRenalPaste.Left = btnRenalCopy.Right + 8;
            pnlActions.Controls.Add(btnRenalPaste);

            pnlRight.Controls.Add(pnlActions);

            Label lblPreviewTitle = new Label();
            lblPreviewTitle.Text = "📝 ตัวอย่างข้อความบันทึก Nurse Note:";
            lblPreviewTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblPreviewTitle.Location = new Point(12, 190);
            lblPreviewTitle.AutoSize = true;
            pnlRight.Controls.Add(lblPreviewTitle);

            txtRenalPreview = new TextBox();
            txtRenalPreview.Multiline = true;
            txtRenalPreview.ScrollBars = ScrollBars.Vertical;
            txtRenalPreview.Font = new Font("Leelawadee UI", 9.5f);
            txtRenalPreview.Location = new Point(12, 216);
            txtRenalPreview.Size = new Size(pnlRight.Width - 24, 210);
            txtRenalPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            pnlRight.Controls.Add(txtRenalPreview);

            page.Controls.Add(pnlRight);
            pnlRight.BringToFront();

            RecalcRenal();
        }

        private void RecalcRenal() {
            if (numRenalCr == null) return;

            bool isMale = radGenderMale.Checked;
            decimal age = numRenalAge.Value;
            decimal weight = numRenalWeight.Value;
            decimal height = numRenalHeight.Value;
            decimal scr = numRenalCr.Value;
            if (scr <= 0) scr = 1.0m;

            // Cockcroft-Gault CrCl
            decimal crcl = ((140m - age) * weight) / (72m * scr);
            if (!isMale) crcl *= 0.85m;

            // BMI
            decimal heightM = height / 100m;
            decimal bmi = heightM > 0 ? (weight / (heightM * heightM)) : 0m;

            // IBW
            decimal ibw = isMale ? (50m + 0.91m * (height - 152.4m)) : (45.5m + 0.91m * (height - 152.4m));
            if (height < 152.4m) ibw = weight;

            string ckdTitle;
            Color ckdColor;
            string advice;

            if (crcl >= 90m) {
                ckdTitle = "🟢 CKD Stage 1 (การทำงานของไตปกติ >= 90 ml/min)";
                ckdColor = Color.FromArgb(22, 101, 52);
                advice = "การทำงานของไตปกติ ไม่จำเป็นต้องปรับขนาดยาไตขับทั่วไป";
            } else if (crcl >= 60m) {
                ckdTitle = "🟢 CKD Stage 2 (ไตลดลงเล็กน้อย 60 - 89 ml/min)";
                ckdColor = Color.FromArgb(22, 101, 52);
                advice = "ไตลดลงเล็กน้อย ติดตามค่าไตตามรอบการรักษา";
            } else if (crcl >= 30m) {
                ckdTitle = "🟡 CKD Stage 3 (ไตลดลงปานกลาง 30 - 59 ml/min)";
                ckdColor = Color.FromArgb(217, 119, 6);
                advice = "⚠️ ข้อควรระวัง: ต้องปรับขนาดยาที่ขับทางไต (เช่น LMWH/Enoxaparin, Colistin, Meropenem, Vancomycin, Digoxin)";
            } else if (crcl >= 15m) {
                ckdTitle = "🔴 CKD Stage 4 (ไตลดลงรุนแรง 15 - 29 ml/min)";
                ckdColor = Color.FromArgb(220, 38, 38);
                advice = "🔴 ไตเสื่อมขั้นรุนแรง ยาหลายชนิดสะสมในร่างกายและเป็นพิษ ตรวจสอบขนาดยาทุกรายการกับเภสัชกร/แพทย์";
            } else {
                ckdTitle = "🔴 CKD Stage 5 (ไตวายระยะสุดท้าย < 15 ml/min)";
                ckdColor = Color.FromArgb(220, 38, 38);
                advice = "🔴 ภาวะไตวายระยะสุดท้าย (ESRD) เฝ้าระวังน้ำเกิน เกลือแร่ผิดปกติ และพิษจากยาสะสม";
            }

            string bmiStatus;
            if (bmi < 18.5m) bmiStatus = "น้ำหนักต่ำกว่าเกณฑ์ (Underweight)";
            else if (bmi <= 22.9m) bmiStatus = "น้ำหนักปกติสมส่วน (Normal)";
            else if (bmi <= 24.9m) bmiStatus = "น้ำหนักเกินเกณฑ์/ท้วม (Overweight)";
            else if (bmi <= 29.9m) bmiStatus = "โรคอ้วนระดับ 1 (Obese class I)";
            else bmiStatus = "โรคอ้วนระดับ 2 (Obese class II)";

            lblCrClBig.Text = string.Format("CrCl = {0:F1} ml/min", crcl);
            lblCrClBig.ForeColor = ckdColor;
            lblCkdStageBadge.Text = ckdTitle;
            lblCkdStageBadge.ForeColor = ckdColor;
            lblBmiBig.Text = string.Format("BMI: {0:F2} kg/m²", bmi);
            lblBmiBadge.Text = string.Format("({0})", bmiStatus);
            lblIbw.Text = string.Format("น้ำหนักมาตรฐาน (IBW): {0:F1} kg", ibw);
            lblRenalAdvice.Text = advice;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[ประเมินการทำงานของไตและสัดส่วนร่างกาย]");
            sb.AppendLine(string.Format("- เวลา: {0}", GetTimestampHeader()));
            sb.AppendLine(string.Format("- ข้อมูล: เพศ{0}, อายุ {1} ปี, นน. {2:F1} kg, สส. {3} cm, Cr: {4:F2} mg/dL",
                isMale ? "ชาย" : "หญิง", age, weight, height, scr));
            sb.AppendLine(string.Format("- Creatinine Clearance (CrCl Cockcroft-Gault) = {0:F1} ml/min [{1}]", crcl, ckdTitle));
            sb.AppendLine(string.Format("- Body Mass Index (BMI) = {0:F2} kg/m² ({1}), IBW = {2:F1} kg", bmi, bmiStatus, ibw));
            sb.AppendLine(string.Format("- คำแนะนำ: {0}", advice));

            txtRenalPreview.Text = sb.ToString();
        }

        // ==========================================
        // TAB 6: ARTERIAL BLOOD GAS (ABG) INTERPRETER
        // ==========================================
        private void BuildAbgTab(TabPage page) {
            // Left Input Panel
            Panel pnlLeft = new Panel();
            pnlLeft.Dock = DockStyle.Left;
            pnlLeft.Width = 440;
            pnlLeft.Padding = new Padding(14);
            pnlLeft.AutoScroll = true;

            int y = 10;
            Label lblHeader = new Label();
            lblHeader.Text = "พารามิเตอร์ผลเจาะก๊าซในเลือด (Arterial Blood Gas):";
            lblHeader.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            lblHeader.ForeColor = Color.FromArgb(15, 23, 42);
            lblHeader.Location = new Point(14, y);
            lblHeader.AutoSize = true;
            pnlLeft.Controls.Add(lblHeader);

            y += 28;
            Label lblScenarios = new Label();
            lblScenarios.Text = "ตัวอย่างเคสทางคลินิกด่วน (Presets):";
            lblScenarios.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblScenarios.ForeColor = Color.FromArgb(71, 85, 105);
            lblScenarios.Location = new Point(14, y);
            lblScenarios.AutoSize = true;
            pnlLeft.Controls.Add(lblScenarios);

            y += 20;
            FlowLayoutPanel pnlPresets = new FlowLayoutPanel();
            pnlPresets.Location = new Point(14, y);
            pnlPresets.Size = new Size(410, 84);
            pnlPresets.WrapContents = true;

            AddAbgPresetBtn(pnlPresets, "ปกติ (Normal)", 7.40m, 40m, 24m, 95m, 21m);
            AddAbgPresetBtn(pnlPresets, "Met Acidosis (DKA/Sepsis)", 7.20m, 25m, 10m, 95m, 21m);
            AddAbgPresetBtn(pnlPresets, "Met Alkalosis (Vomiting)", 7.52m, 48m, 38m, 90m, 21m);
            AddAbgPresetBtn(pnlPresets, "Resp Acidosis (Acute)", 7.24m, 65m, 26m, 65m, 21m);
            AddAbgPresetBtn(pnlPresets, "Resp Acidosis (Compensated)", 7.36m, 62m, 35m, 70m, 21m);
            AddAbgPresetBtn(pnlPresets, "Resp Alkalosis (Hypervent)", 7.56m, 24m, 22m, 98m, 21m);
            AddAbgPresetBtn(pnlPresets, "Mixed Acidosis (Severe)", 7.10m, 58m, 16m, 50m, 100m);
            AddAbgPresetBtn(pnlPresets, "Severe ARDS / Hypoxemia", 7.32m, 48m, 24m, 52m, 80m);

            pnlLeft.Controls.Add(pnlPresets);

            y += 92;
            pnlLeft.Controls.Add(CreateLbl("ความเป็นกรด-ด่าง pH:", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(ค่าปกติ 7.35 - 7.45)", 210, y, false));
            y += 24;
            numAbgPH = CreateNum(7.40m, 6.80m, 7.80m, 0.01m, 2, 14, y, 160, (s, e) => RecalcAbg());
            pnlLeft.Controls.Add(numAbgPH);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ก๊าซคาร์บอนไดออกไซด์ PaCO2 (mmHg):", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(ค่าปกติ 35 - 45)", 265, y, false));
            y += 24;
            numAbgPaCO2 = CreateNum(40.0m, 10.0m, 150.0m, 1.0m, 1, 14, y, 160, (s, e) => RecalcAbg());
            pnlLeft.Controls.Add(numAbgPaCO2);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ไบคาร์บอเนต HCO3 (mEq/L):", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(ค่าปกติ 22 - 26)", 210, y, false));
            y += 24;
            numAbgHCO3 = CreateNum(24.0m, 2.0m, 60.0m, 1.0m, 1, 14, y, 160, (s, e) => RecalcAbg());
            pnlLeft.Controls.Add(numAbgHCO3);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("แรงดันออกซิเจน PaO2 (mmHg):", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(ค่าปกติ 80 - 100)", 235, y, false));
            y += 24;
            numAbgPaO2 = CreateNum(95.0m, 20.0m, 500.0m, 1.0m, 1, 14, y, 160, (s, e) => RecalcAbg());
            pnlLeft.Controls.Add(numAbgPaO2);

            y += 36;
            pnlLeft.Controls.Add(CreateLbl("ความเข้มข้นออกซิเจน FiO2 (%):", 14, y, true));
            pnlLeft.Controls.Add(CreateLbl("(Room air = 21%)", 235, y, false));
            y += 24;
            numAbgFiO2 = CreateNum(21.0m, 21.0m, 100.0m, 1.0m, 0, 14, y, 160, (s, e) => RecalcAbg());
            pnlLeft.Controls.Add(numAbgFiO2);

            page.Controls.Add(pnlLeft);

            // Right Result Panel
            Panel pnlRight = new Panel();
            pnlRight.Dock = DockStyle.Fill;
            pnlRight.Padding = new Padding(12);
            pnlRight.AutoScroll = true;

            lblAbgDiagnosisBig = new Label();
            lblAbgDiagnosisBig.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
            lblAbgDiagnosisBig.Location = new Point(12, 10);
            lblAbgDiagnosisBig.Size = new Size(pnlRight.Width - 24, 38);
            lblAbgDiagnosisBig.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblAbgDiagnosisBig.TextAlign = ContentAlignment.MiddleLeft;
            lblAbgDiagnosisBig.BackColor = Color.FromArgb(240, 253, 250);
            lblAbgDiagnosisBig.ForeColor = Color.FromArgb(13, 148, 136);
            lblAbgDiagnosisBig.Padding = new Padding(8, 0, 0, 0);
            pnlRight.Controls.Add(lblAbgDiagnosisBig);

            lblAbgOxygenationBadge = new Label();
            lblAbgOxygenationBadge.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblAbgOxygenationBadge.Location = new Point(12, 52);
            lblAbgOxygenationBadge.Size = new Size(pnlRight.Width - 24, 28);
            lblAbgOxygenationBadge.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblAbgOxygenationBadge.TextAlign = ContentAlignment.MiddleLeft;
            lblAbgOxygenationBadge.BackColor = Color.FromArgb(241, 245, 249);
            lblAbgOxygenationBadge.ForeColor = Color.FromArgb(30, 41, 59);
            lblAbgOxygenationBadge.Padding = new Padding(8, 0, 0, 0);
            pnlRight.Controls.Add(lblAbgOxygenationBadge);

            lblAbgExpectedComp = new Label();
            lblAbgExpectedComp.Font = new Font("Segoe UI", 9f);
            lblAbgExpectedComp.Location = new Point(12, 84);
            lblAbgExpectedComp.Size = new Size(pnlRight.Width - 24, 44);
            lblAbgExpectedComp.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblAbgExpectedComp.BackColor = Color.FromArgb(248, 250, 252);
            lblAbgExpectedComp.ForeColor = Color.FromArgb(15, 23, 42);
            lblAbgExpectedComp.BorderStyle = BorderStyle.FixedSingle;
            lblAbgExpectedComp.Padding = new Padding(6);
            pnlRight.Controls.Add(lblAbgExpectedComp);

            lblAbgAdvice = new Label();
            lblAbgAdvice.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblAbgAdvice.Location = new Point(12, 132);
            lblAbgAdvice.Size = new Size(pnlRight.Width - 24, 46);
            lblAbgAdvice.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblAbgAdvice.BackColor = Color.FromArgb(254, 243, 199);
            lblAbgAdvice.ForeColor = Color.FromArgb(146, 64, 14);
            lblAbgAdvice.BorderStyle = BorderStyle.FixedSingle;
            lblAbgAdvice.Padding = new Padding(6);
            pnlRight.Controls.Add(lblAbgAdvice);

            FlowLayoutPanel pnlActions = new FlowLayoutPanel();
            pnlActions.Location = new Point(12, 184);
            pnlActions.Size = new Size(pnlRight.Width - 24, 44);
            pnlActions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pnlActions.WrapContents = false;

            btnAbgInsert = CreateActionButton(string.Format("🛏️ บันทึกลงเตียง {0}", currentBed), Color.FromArgb(13, 148, 136), Color.White, (s, e) => {
                context.InsertToBedNote(currentBed, txtAbgPreview.Text);
            });
            pnlActions.Controls.Add(btnAbgInsert);

            Button btnCopy = CreateActionButton("📋 คัดลอก (Copy)", Color.FromArgb(226, 232, 240), Color.FromArgb(15, 23, 42), (s, e) => {
                CopyText(txtAbgPreview.Text);
            });
            pnlActions.Controls.Add(btnCopy);

            Button btnPasteDirect = CreateActionButton("📋 วาง e-PHIS ทันที", Color.FromArgb(204, 251, 241), Color.FromArgb(15, 118, 110), (s, e) => {
                PasteDirectly(txtAbgPreview.Text);
            });
            pnlActions.Controls.Add(btnPasteDirect);

            pnlRight.Controls.Add(pnlActions);

            Label lblPreviewTitle = new Label();
            lblPreviewTitle.Text = "📝 ตัวอย่างข้อความบันทึก Nurse Note (ABG):";
            lblPreviewTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblPreviewTitle.Location = new Point(12, 232);
            lblPreviewTitle.AutoSize = true;
            pnlRight.Controls.Add(lblPreviewTitle);

            txtAbgPreview = new TextBox();
            txtAbgPreview.Multiline = true;
            txtAbgPreview.ScrollBars = ScrollBars.Vertical;
            txtAbgPreview.Font = new Font("Leelawadee UI", 9.5f);
            txtAbgPreview.Location = new Point(12, 256);
            txtAbgPreview.Size = new Size(pnlRight.Width - 24, 180);
            txtAbgPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            pnlRight.Controls.Add(txtAbgPreview);

            page.Controls.Add(pnlRight);
            pnlRight.BringToFront();

            RecalcAbg();
        }

        private void AddAbgPresetBtn(FlowLayoutPanel pnl, string label, decimal ph, decimal paco2, decimal hco3, decimal pao2, decimal fio2) {
            Button btn = new Button();
            btn.Text = label;
            btn.Font = new Font("Segoe UI", 7.5f);
            btn.BackColor = Color.White;
            btn.ForeColor = Color.FromArgb(30, 41, 59);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.Height = 24;
            btn.AutoSize = true;
            btn.Margin = new Padding(2);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => {
                if (numAbgPH != null) numAbgPH.Value = ph;
                if (numAbgPaCO2 != null) numAbgPaCO2.Value = paco2;
                if (numAbgHCO3 != null) numAbgHCO3.Value = hco3;
                if (numAbgPaO2 != null) numAbgPaO2.Value = pao2;
                if (numAbgFiO2 != null) numAbgFiO2.Value = fio2;
                RecalcAbg();
            };
            pnl.Controls.Add(btn);
        }

        private void RecalcAbg() {
            if (numAbgPH == null) return;

            decimal ph = numAbgPH.Value;
            decimal paco2 = numAbgPaCO2.Value;
            decimal hco3 = numAbgHCO3.Value;
            decimal pao2 = numAbgPaO2.Value;
            decimal fio2 = numAbgFiO2.Value;

            string primaryDisorder = "";
            string compStatus = "";
            string compDetails = "";
            string advice = "";
            Color badgeBg = Color.FromArgb(240, 253, 250);
            Color badgeFg = Color.FromArgb(13, 148, 136);

            // Step 1: Acidemia vs Alkalemia vs Normal
            if (ph < 7.35m) {
                badgeBg = Color.FromArgb(254, 226, 226);
                badgeFg = Color.FromArgb(185, 28, 28);
                bool respAcid = (paco2 > 45.0m);
                bool metAcid = (hco3 < 22.0m);

                if (respAcid && metAcid) {
                    primaryDisorder = "Mixed Acidosis (Respiratory & Metabolic Acidosis)";
                    compStatus = "Combined Severe Acidosis (ไม่มีการชดเชย)";
                    compDetails = "พบทั้ง PaCO2 คั่งสูง (>45) และ HCO3 ลดต่ำ (<22) มักเกิดจาก Cardiac Arrest หรือ Severe Shock ร่วมกับหายใจล้มเหลว";
                    advice = "🚨 ภาวะวิกฤตฉุกเฉิน! รายงานแพทย์ทันที เตรียมช่วยหายใจ (Intubation/BVM) แก้ไข Hypoxemia และให้สารน้ำ/แก้ไขตามสาเหตุ";
                } else if (metAcid) {
                    primaryDisorder = "Metabolic Acidosis";
                    decimal expPaCO2 = (1.5m * hco3) + 8.0m;
                    decimal expLow = expPaCO2 - 2.0m;
                    decimal expHigh = expPaCO2 + 2.0m;

                    if (paco2 >= expLow && paco2 <= expHigh) {
                        compStatus = "with Appropriate Respiratory Compensation";
                        compDetails = string.Format("สูตร Winter's: Expected PaCO2 = {0:F1} - {1:F1} mmHg (ค่าจริง {2:F1} mmHg อยู่ในเกณฑ์ชดเชยสมบูรณ์)", expLow, expHigh, paco2);
                    } else if (paco2 > expHigh) {
                        compStatus = "with Concomitant Respiratory Acidosis";
                        compDetails = string.Format("สูตร Winter's: Expected PaCO2 = {0:F1} - {1:F1} mmHg (ค่าจริง {2:F1} mmHg สูงกว่าคาด -> มีภาวะกดการหายใจร่วมด้วย)", expLow, expHigh, paco2);
                    } else {
                        compStatus = "with Concomitant Respiratory Alkalosis";
                        compDetails = string.Format("สูตร Winter's: Expected PaCO2 = {0:F1} - {1:F1} mmHg (ค่าจริง {2:F1} mmHg ต่ำกว่าคาด -> มีภาวะ Hyperventilation ร่วมด้วย)", expLow, expHigh, paco2);
                    }
                    advice = "📌 ตรวจ DTX/Ketones (ระวัง DKA), เฝ้าระวัง K+ ในเลือด, ประเมิน Anion Gap, ติดตาม V/S และ SOS สกอร์ใกล้ชิด";
                } else if (respAcid) {
                    primaryDisorder = "Respiratory Acidosis";
                    if (hco3 >= 22.0m && hco3 <= 26.0m) {
                        compStatus = "Uncompensated (Acute Respiratory Acidosis)";
                        compDetails = "HCO3 ปกติ (22-26) เป็นระยะเฉียบพลัน ไตยังไม่ทันชดเชย";
                    } else if (hco3 > 26.0m) {
                        compStatus = "Partially Compensated (Renal Compensation)";
                        compDetails = string.Format("ไตชดเชยโดยการเพิ่ม HCO3 เป็น {0:F1} mEq/L", hco3);
                    }
                    advice = "📌 ประเมินทางเดินหายใจ (Airway), ดูดเสมหะ, ตรวจสอบตำแหน่งท่อช่วยหายใจ, ปรับเพิ่ม Minute Ventilation ตามคำสั่งแพทย์";
                } else {
                    primaryDisorder = "Unclassified Acidemia";
                    compStatus = "ประเมินติดตามซ้ำ";
                    compDetails = "pH ต่ำแต่ PaCO2 และ HCO3 ยังไม่ผิดปกติเด่นชัด";
                    advice = "ตรวจสอบความถูกต้องของตัวอย่างเลือดและตรวจติดตามอาการ";
                }
            } else if (ph > 7.45m) {
                badgeBg = Color.FromArgb(219, 234, 254);
                badgeFg = Color.FromArgb(29, 78, 216);
                bool respAlk = (paco2 < 35.0m);
                bool metAlk = (hco3 > 26.0m);

                if (respAlk && metAlk) {
                    primaryDisorder = "Mixed Alkalosis (Respiratory & Metabolic Alkalosis)";
                    compStatus = "Combined Alkalosis";
                    compDetails = "พบทั้ง PaCO2 ลดต่ำมาก และ HCO3 สูงเกินเกณฑ์";
                    advice = "📌 ระวังภาวะ Hypokalemia, Hypocalcemia (กล้ามเนื้อเกร็ง/ชัก), ตรวจสอบ Ventilator settings";
                } else if (metAlk) {
                    primaryDisorder = "Metabolic Alkalosis";
                    decimal expPaCO2 = (0.7m * hco3) + 21.0m;
                    decimal expLow = expPaCO2 - 2.0m;
                    decimal expHigh = expPaCO2 + 2.0m;
                    if (paco2 > 45.0m) {
                        compStatus = "with Partial Respiratory Compensation";
                        compDetails = string.Format("คาดหมาย PaCO2 ชดเชย = {0:F1} - {1:F1} mmHg (ค่าจริง {2:F1} mmHg)", expLow, expHigh, paco2);
                    } else {
                        compStatus = "Uncompensated";
                        compDetails = "ยังไม่มีการคั่งของ PaCO2 เพื่อชดเชยด่าง";
                    }
                    advice = "📌 ซักประวัติการสูญเสียกรด (NG tube suction, อาเจียนบ่อย), ได้รับยาขับปัสสาวะ Diuretics, ตรวจระดับเกลือแร่ K+, Cl-";
                } else if (respAlk) {
                    primaryDisorder = "Respiratory Alkalosis";
                    if (hco3 >= 22.0m && hco3 <= 26.0m) {
                        compStatus = "Uncompensated (Acute Hyperventilation)";
                        compDetails = "HCO3 ปกติ เกิดจากการหายใจเร็วเฉียบพลัน เช่น ความเจ็บปวด, วิตกกังวล, ไข้สูง, หรือ Hypoxemia";
                    } else if (hco3 < 22.0m) {
                        compStatus = "Partially Compensated";
                        compDetails = string.Format("ไตลดการดูดกลับ HCO3 เหลือ {0:F1} mEq/L เพื่อชดเชย", hco3);
                    }
                    advice = "📌 บรรเทาความเจ็บปวด/ความเครียด, ตรวจหาภาวะขาดออกซิเจนซ่อนเร้น (Hypoxemia), ปรับลดอัตราหายใจบนเครื่องช่วยหายใจ";
                } else {
                    primaryDisorder = "Unclassified Alkalemia";
                    compStatus = "ประเมินติดตามซ้ำ";
                    compDetails = "pH สูงแต่ PaCO2 และ HCO3 ยังไม่ผิดปกติเด่นชัด";
                    advice = "ตรวจติดตามอาการและประเมินผลเลือดซ้ำ";
                }
            } else {
                // Normal pH (7.35 - 7.45)
                bool paco2Abn = (paco2 < 35.0m || paco2 > 45.0m);
                bool hco3Abn = (hco3 < 22.0m || hco3 > 26.0m);

                if (!paco2Abn && !hco3Abn) {
                    primaryDisorder = "Normal Acid-Base Balance (สมดุลกรด-ด่างปกติ)";
                    compStatus = "ค่าก๊าซในเลือดอยู่ในเกณฑ์ปกติทุกตัว";
                    compDetails = "pH (7.35-7.45), PaCO2 (35-45) และ HCO3 (22-26) ปกติทั้งหมด";
                    advice = "✔️ สมดุลกรด-ด่างอยู่ในเกณฑ์ปกติ ติดตามสัญญาณชีพและอาการตามรอบการดูแล";
                    badgeBg = Color.FromArgb(240, 253, 250);
                    badgeFg = Color.FromArgb(13, 148, 136);
                } else {
                    badgeBg = Color.FromArgb(254, 243, 199);
                    badgeFg = Color.FromArgb(180, 83, 9);
                    if (ph < 7.40m) {
                        if (paco2 > 45.0m && hco3 > 26.0m) {
                            primaryDisorder = "Fully Compensated Respiratory Acidosis";
                            compStatus = "Fully Compensated (Chronic / COPD baseline)";
                            compDetails = string.Format("pH ปกติ ({0:F2}) โดยไตสะสม HCO3 สูงถึง {1:F1} mEq/L ชดเชยได้สมบูรณ์", ph, hco3);
                            advice = "📌 พบใน COPD เรื้อรัง ระวังการให้ออกซิเจนเข้มข้นสูงเกินไป (O2 induced CO2 narcosis)";
                        } else {
                            primaryDisorder = "Fully Compensated Metabolic Acidosis";
                            compStatus = "Fully Compensated";
                            compDetails = string.Format("pH ปกติ ({0:F2}) โดยการหายใจระบาย PaCO2 เหลือ {1:F1} mmHg ชดเชยได้สมบูรณ์", ph, paco2);
                            advice = "📌 ตรวจหาสาเหตุภาวะกรดเกินและติดตามสมดุลกรด-ด่างต่อเนื่อง";
                        }
                    } else {
                        if (paco2 < 35.0m && hco3 < 22.0m) {
                            primaryDisorder = "Fully Compensated Respiratory Alkalosis";
                            compStatus = "Fully Compensated (Chronic)";
                            compDetails = string.Format("pH ปกติ ({0:F2}) ไตขับ HCO3 เหลือ {1:F1} mEq/L ชดเชยได้สมบูรณ์", ph, hco3);
                            advice = "📌 ตรวจติดตามสาเหตุเรื้อรัง เช่น โรคตับ หรือโรคปอดระยะแรก";
                        } else {
                            primaryDisorder = "Fully Compensated Metabolic Alkalosis";
                            compStatus = "Fully Compensated";
                            compDetails = string.Format("pH ปกติ ({0:F2}) ร่างกายลดการหายใจจน PaCO2 คั่ง {1:F1} mmHg ชดเชยได้สมบูรณ์", ph, paco2);
                            advice = "📌 ตรวจระดับ Electrolyte (K+, Cl-) และปริมาณสารน้ำในร่างกาย";
                        }
                    }
                }
            }

            // Step 2: Oxygenation & P/F ratio
            decimal pfRatio = fio2 > 0 ? (pao2 / (fio2 / 100.0m)) : 0;
            string o2BadgeText = "";
            string o2Category = "";
            Color o2Bg = Color.FromArgb(241, 245, 249);
            Color o2Fg = Color.FromArgb(30, 41, 59);

            if (pao2 >= 80.0m) {
                o2Category = "Normal Oxygenation";
            } else if (pao2 >= 60.0m) {
                o2Category = "Mild Hypoxemia";
                o2Bg = Color.FromArgb(254, 243, 199);
                o2Fg = Color.FromArgb(180, 83, 9);
            } else if (pao2 >= 45.0m) {
                o2Category = "Moderate Hypoxemia";
                o2Bg = Color.FromArgb(254, 226, 226);
                o2Fg = Color.FromArgb(185, 28, 28);
            } else {
                o2Category = "🚨 Severe Hypoxemia (วิกฤตขาดออกซิเจนรุนแรง)";
                o2Bg = Color.FromArgb(254, 202, 202);
                o2Fg = Color.FromArgb(153, 27, 27);
            }

            string ardsInfo = "";
            if (fio2 > 21.0m) {
                if (pfRatio <= 100.0m) ardsInfo = string.Format(" | P/F ratio: {0:F0} (เข้าเกณฑ์ Severe ARDS)", pfRatio);
                else if (pfRatio <= 200.0m) ardsInfo = string.Format(" | P/F ratio: {0:F0} (เข้าเกณฑ์ Moderate ARDS)", pfRatio);
                else if (pfRatio <= 300.0m) ardsInfo = string.Format(" | P/F ratio: {0:F0} (เข้าเกณฑ์ Mild ARDS)", pfRatio);
                else ardsInfo = string.Format(" | P/F ratio: {0:F0} (ปกติ)", pfRatio);
            } else {
                ardsInfo = string.Format(" (Room air | P/F: {0:F0})", pfRatio);
            }

            o2BadgeText = string.Format("🫁 ออกซิเจน: {0} (PaO2 {1:F1} mmHg, FiO2 {2:F0}%){3}", o2Category, pao2, fio2, ardsInfo);

            // Update UI
            lblAbgDiagnosisBig.Text = string.Format("🫁 ผลวิเคราะห์: {0}", primaryDisorder);
            lblAbgDiagnosisBig.BackColor = badgeBg;
            lblAbgDiagnosisBig.ForeColor = badgeFg;

            lblAbgOxygenationBadge.Text = o2BadgeText;
            lblAbgOxygenationBadge.BackColor = o2Bg;
            lblAbgOxygenationBadge.ForeColor = o2Fg;

            lblAbgExpectedComp.Text = string.Format("🔍 ภาวะการชดเชย (Compensation):\n{0} • {1}", compStatus, compDetails);
            lblAbgAdvice.Text = string.Format("💡 ข้อเสนอแนะทางการพยาบาล: {0}", advice);

            // Generate Note
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("[ผลวิเคราะห์ก๊าซในเลือด (ABG Analysis) - {0}]", GetTimestampHeader()));
            sb.AppendLine(string.Format("- ค่าที่วัดได้: pH: {0:F2}, PaCO2: {1:F1} mmHg, HCO3: {2:F1} mEq/L", ph, paco2, hco3));
            sb.AppendLine(string.Format("- ออกซิเจน: PaO2: {0:F1} mmHg, FiO2: {1:F0}% (P/F ratio: {2:F0} - {3})", pao2, fio2, pfRatio, o2Category));
            sb.AppendLine(string.Format("- การแปลผลหลัก: {0}", primaryDisorder));
            sb.AppendLine(string.Format("- ภาวะการชดเชย: {0} ({1})", compStatus, compDetails));
            sb.AppendLine(string.Format("- แผนการพยาบาล/คำแนะนำ: {0}", advice));

            txtAbgPreview.Text = sb.ToString();
        }

        // ==========================================
        // UI HELPERS & ACTIONS
        // ==========================================
        private Label CreateLbl(string text, int x, int y, bool isBold) {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.Location = new Point(x, y);
            lbl.AutoSize = true;
            lbl.Font = new Font("Segoe UI", 9.5f, isBold ? FontStyle.Bold : FontStyle.Regular);
            lbl.ForeColor = isBold ? Color.FromArgb(15, 23, 42) : Color.FromArgb(100, 116, 139);
            return lbl;
        }

        private NumericUpDown CreateNum(decimal val, decimal min, decimal max, decimal step, int dec, int x, int y, int w, EventHandler onValChanged) {
            NumericUpDown num = new NumericUpDown();
            num.Location = new Point(x, y);
            num.Size = new Size(w, 29);
            num.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            num.Minimum = min;
            num.Maximum = max;
            num.DecimalPlaces = dec;
            num.Increment = step;
            num.Value = val;
            num.ValueChanged += onValChanged;
            return num;
        }

        private Button CreateActionButton(string text, Color bg, Color fg, EventHandler onClick) {
            Button btn = new Button();
            btn.Text = text;
            btn.BackColor = bg;
            btn.ForeColor = fg;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btn.Size = new Size(160, 38);
            btn.Cursor = Cursors.Hand;
            btn.Click += onClick;
            return btn;
        }

        private void CopyText(string text) {
            if (string.IsNullOrEmpty(text)) return;
            try {
                Clipboard.SetDataObject(text, true, 5, 50);
                context.ShowNotification("คัดลอกข้อความผลการคำนวณเรียบร้อยแล้ว");
            } catch (Exception ex) {
                MessageBox.Show("ไม่สามารถคัดลอกได้: " + ex.Message);
            }
        }

        private void PasteDirectly(string text) {
            if (string.IsNullOrEmpty(text)) return;
            this.Hide();

            System.Threading.ThreadPool.QueueUserWorkItem(state => {
                System.Threading.Thread.Sleep(80);
                if (lastActiveWindow != IntPtr.Zero) {
                    SetForegroundWindow(lastActiveWindow);
                    System.Threading.Thread.Sleep(50);
                }
                context.ExecutePaste(0, text);
            });
        }

        public void ShowAndFocus(int bedNum = 1) {
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != this.Handle) {
                lastActiveWindow = fg;
            }

            if (bedNum >= 1 && bedNum <= 30) {
                currentBed = bedNum;
                if (cmbBedSelector != null && cmbBedSelector.SelectedIndex != bedNum - 1) {
                    cmbBedSelector.SelectedIndex = bedNum - 1;
                }
                ChangeTargetBed(bedNum);
            }

            // Refresh calculations to update time
            RecalcSos();
            RecalcMap();
            RecalcIv();
            RecalcDrip();
            RecalcRenal();
            RecalcAbg();

            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }
    }
}
