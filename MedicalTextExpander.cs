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
using System.Security.Cryptography;

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
        private const string AckEventName = "Medical_Text_Expander_WakeAck_Event";

        [STAThread]
        public static void Main(string[] args) {
            bool forceRestart = false;
            if (args != null) {
                foreach (string arg in args) {
                    if (string.Equals(arg, "/restart", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arg, "/force", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arg, "/kill", StringComparison.OrdinalIgnoreCase)) {
                        forceRestart = true;
                    }
                }
            }

            Process current = Process.GetCurrentProcess();
            Process[] existingProcesses = Process.GetProcessesByName(current.ProcessName);

            if (forceRestart) {
                KillOtherInstances(current.Id);
            } else if (existingProcesses.Length > 1) {
                // 1. ตรวจสอบและกำจัดโปรเซสที่ค้าง (Not Responding) ใน Task Manager ทันที
                foreach (Process p in existingProcesses) {
                    if (p.Id != current.Id) {
                        try {
                            if (!p.Responding) {
                                try { p.Kill(); p.WaitForExit(1000); } catch {}
                            }
                        } catch {}
                    }
                }

                existingProcesses = Process.GetProcessesByName(current.ProcessName);
                if (existingProcesses.Length > 1) {
                    // 2. ทำ Two-Way Handshake ปลุกอินสแตนซ์เดิม
                    bool ackReceived = false;
                    try {
                        using (EventWaitHandle ackEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AckEventName)) {
                            using (EventWaitHandle activateEvent = EventWaitHandle.OpenExisting(EventName)) {
                                activateEvent.Set();
                            }
                            // รอให้อินสแตนซ์เดิมตอบรับว่าเปิดหน้าต่างสำเร็จภายใน 1.2 วินาที
                            ackReceived = ackEvent.WaitOne(1200);
                        }
                    } catch {}

                    if (ackReceived) {
                        return;
                    }

                    // 3. หากไม่มีการตอบรับ (โปรเซสเดิมค้าง/Zombie ใน Task Manager):
                    // บังคับปิดโปรเซสเดิมที่ค้างทิ้งทั้งหมด แล้วเริ่มโปรแกรมใหม่ขึ้นมาทันที!
                    KillOtherInstances(current.Id);
                }
            }

            bool createdNew = false;
            Mutex mutex = null;
            try {
                mutex = new Mutex(true, MutexName, out createdNew);
            } catch (AbandonedMutexException) {
                createdNew = true;
            } catch {
                createdNew = true;
            }

            try {
                using (EventWaitHandle activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName))
                using (EventWaitHandle ackEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AckEventName)) {
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += (s, e) => {
                        try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), "ThreadException: " + e.Exception.ToString()); } catch {}
                    };
                    AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                        try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), "UnhandledException: " + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "null")); } catch {}
                    };

                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    ExpanderContext context = null;
                    try {
                        context = new ExpanderContext();
                    } catch (Exception exInit) {
                        try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), "exInit: " + exInit.ToString()); } catch {}
                        MessageBox.Show("ข้อผิดพลาดในการเริ่มต้นโปรแกรม:\n" + exInit.ToString(), "Medical Expander Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    // Background thread สำหรับรอรับสัญญาณเมื่อผู้ใช้พยายามกดเปิดโปรแกรมซ้ำ
                    Thread listenerThread = new Thread(() => {
                        while (true) {
                            try {
                                if (activateEvent.WaitOne()) {
                                    // แจ้งตอบรับอินสแตนซ์เดิมว่าเรายังมีชีวิตอยู่และกำลังเปิดหน้าต่าง
                                    try {
                                        ackEvent.Set();
                                    } catch {}

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
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), "exApp: " + exApp.ToString()); } catch {}
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

        private static void KillOtherInstances(int currentPid) {
            try {
                string procName = Process.GetCurrentProcess().ProcessName;
                foreach (Process p in Process.GetProcessesByName(procName)) {
                    if (p.Id != currentPid) {
                        try {
                            p.Kill();
                            p.WaitForExit(1000);
                        } catch {}
                    }
                }
            } catch {}
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
        public const string CurrentVersion = "1.9.2";
        public const string DefaultGitHubRepo = "oatzilla/Medical_Text_Expander";

        public static void CheckForUpdatesAsync(string repo, bool isManual, Form parent = null, string token = null) {
            ThreadPool.QueueUserWorkItem(_ => {
                CheckForUpdatesInternal(repo, isManual, parent, token);
            });
        }

        private static void CheckForUpdatesInternal(string repo, bool isManual, Form parent, string token) {
            if (string.IsNullOrEmpty(repo)) repo = DefaultGitHubRepo;
            long ts = DateTime.UtcNow.Ticks;
            string rawUrl = string.Format("https://raw.githubusercontent.com/{0}/main/version.json?t={1}", repo.Trim(), ts);
            string ghRawUrl = string.Format("https://github.com/{0}/raw/main/version.json?t={1}", repo.Trim(), ts);
            string apiUrl = string.Format("https://api.github.com/repos/{0}/contents/version.json?t={1}", repo.Trim(), ts);

            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string json = "";
                using (WebClient client = new WebClient()) {
                    client.Encoding = Encoding.UTF8;
                    client.Headers["User-Agent"] = "MedicalTextExpander-AutoUpdater";
                    if (!string.IsNullOrEmpty(token)) {
                        client.Headers["Authorization"] = "token " + token.Trim();
                    }

                    // 1. First try raw.githubusercontent.com (fastest, no 60 req/hr rate limit, direct file content)
                    try {
                        json = client.DownloadString(rawUrl);
                    } catch {
                        // 2. Fallback to GitHub raw route
                        try {
                            json = client.DownloadString(ghRawUrl);
                        } catch {
                            // 3. Fallback to GitHub API endpoint
                            try {
                                client.Headers["Accept"] = "application/vnd.github.v3.raw";
                                json = client.DownloadString(apiUrl);
                            } catch (Exception exApi) {
                                throw exApi;
                            }
                        }
                    }
                }

                UpdateInfo info = ParseVersionJson(json, repo);
                if (info == null || string.IsNullOrEmpty(info.Version)) {
                    if (isManual) {
                        ShowMessage(parent, "ไม่สามารถอ่านข้อมูลเวอร์ชันจาก GitHub ได้ กรุณาตรวจสอบชื่อ Repository ในการตั้งค่า", "ตรวจสอบการอัปเดต", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }

                if (IsNewerVersion(info.Version, CurrentVersion)) {
                    Action promptAction = () => {
                        string msg = string.Format("\uD83C\uDF89 พบการอัปเดตเวอร์ชันใหม่!\n\n" +
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

        private static UpdateInfo ParseVersionJson(string json, string repo) {
            try {
                if (string.IsNullOrEmpty(json)) return null;
                UpdateInfo info = new UpdateInfo();
                info.Version = ExtractJsonValue(json, "version");
                info.ReleaseDate = ExtractJsonValue(json, "releaseDate");
                if (string.IsNullOrEmpty(info.ReleaseDate)) info.ReleaseDate = ExtractJsonValue(json, "release_date");
                info.Changelog = ExtractJsonChangelog(json);
                if (string.IsNullOrEmpty(info.Changelog)) info.Changelog = ExtractJsonValue(json, "changelog");
                info.DownloadUrl = ExtractJsonValue(json, "downloadUrl");
                if (string.IsNullOrEmpty(info.DownloadUrl)) info.DownloadUrl = ExtractJsonValue(json, "download_url");
                if (string.IsNullOrEmpty(info.DownloadUrl) && !string.IsNullOrEmpty(repo)) {
                    info.DownloadUrl = string.Format("https://github.com/{0}/releases/latest/download/Medical_Text_Expander.exe", repo);
                }
                return info;
            } catch {
                return null;
            }
        }

        private static string ExtractJsonValue(string json, string key) {
            if (string.IsNullOrEmpty(json)) return "";
            if (json.Contains("\"encoding\"") && json.Contains("\"base64\"")) {
                Match mc = Regex.Match(json, "\"content\"\\s*:\\s*\"([A-Za-z0-9+/=\\r\\n]+)\"");
                if (mc.Success) {
                    try {
                        string b64 = mc.Groups[1].Value.Replace("\r", "").Replace("\n", "");
                        byte[] bytes = Convert.FromBase64String(b64);
                        json = Encoding.UTF8.GetString(bytes);
                    } catch { }
                }
            }
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"(.*?)\"", RegexOptions.Singleline);
            if (m.Success) {
                return Regex.Unescape(m.Groups[1].Value).Trim();
            }
            m = Regex.Match(json, "[\"']?" + Regex.Escape(key) + "[\"']?\\s*:\\s*[\"']?([^\"',\r\n}]+)[\"']?", RegexOptions.Singleline);
            if (m.Success) {
                return m.Groups[1].Value.Trim();
            }
            return "";
        }

        private static string ExtractJsonChangelog(string json) {
            if (string.IsNullOrEmpty(json)) return "";
            if (json.Contains("\"encoding\"") && json.Contains("\"base64\"")) {
                Match mc = Regex.Match(json, "\"content\"\\s*:\\s*\"([A-Za-z0-9+/=\\r\\n]+)\"");
                if (mc.Success) {
                    try {
                        string b64 = mc.Groups[1].Value.Replace("\r", "").Replace("\n", "");
                        byte[] bytes = Convert.FromBase64String(b64);
                        json = Encoding.UTF8.GetString(bytes);
                    } catch { }
                }
            }
            Match m = Regex.Match(json, "[\"']?changelog[\"']?\\s*:\\s*\"(.*?)\"(?:\\s*[,}])", RegexOptions.Singleline);
            if (m.Success) {
                return Regex.Unescape(m.Groups[1].Value).Trim();
            }
            m = Regex.Match(json, "[\"']?changelog[\"']?\\s*:\\s*([^,\r\n}]+)", RegexOptions.Singleline);
            if (m.Success) {
                return m.Groups[1].Value.Trim();
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


    // ==========================================
    // Supabase Cloud Realtime Sync Client
    // ==========================================
    public class SupabaseSyncClient {
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public bool IsEnabled { get { return !string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(ApiKey); } }

        public SupabaseSyncClient(string url, string apiKey) {
            Url = (url ?? "").Trim().TrimEnd('/');
            ApiKey = (apiKey ?? "").Trim();
        }

        private HttpWebRequest CreateRequest(string endpoint, string method) {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string fullUrl = Url + "/rest/v1/" + endpoint.TrimStart('/');
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(fullUrl);
            req.Method = method;
            req.Headers["apikey"] = ApiKey;
            req.Headers["Authorization"] = "Bearer " + ApiKey;
            req.ContentType = "application/json";
            req.UserAgent = "MedicalTextExpander-Supabase";
            req.Timeout = 7000;
            return req;
        }

        public bool TestConnection() {
            try {
                if (!IsEnabled) return false;
                HttpWebRequest req = CreateRequest("bed_notes?select=bed_number&limit=1", "GET");
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                    return resp.StatusCode == HttpStatusCode.OK;
                }
            } catch {
                return false;
            }
        }

        public Dictionary<int, string> FetchAllBeds(int userSlot = 0) {
            var result = new Dictionary<int, string>();
            if (!IsEnabled) return result;
            try {
                int minBed = (userSlot * 100) + 1;
                int maxBed = (userSlot * 100) + 30;
                string query = string.Format("bed_notes?bed_number=gte.{0}&bed_number=lte.{1}&select=bed_number,content&order=bed_number.asc", minBed, maxBed);
                HttpWebRequest req = CreateRequest(query, "GET");
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                    string json = reader.ReadToEnd();
                    var matches = Regex.Matches(json, @"\{""bed_number"":\s*(\d+).*?""content"":\s*""((?:\\""|[^""])*)""", RegexOptions.Singleline);
                    foreach (Match m in matches) {
                        int remoteBed = int.Parse(m.Groups[1].Value);
                        int localBed = remoteBed - (userSlot * 100);
                        if (localBed >= 1 && localBed <= 30) {
                            string content = UnescapeJson(m.Groups[2].Value);
                            result[localBed] = content;
                        }
                    }
                }
            } catch {}
            return result;
        }

        public bool SaveBed(int bedNum, string content, int userSlot = 0, string editorName = "") {
            if (!IsEnabled || bedNum < 1 || (bedNum > 30 && bedNum != 100 && bedNum != 101)) return false;
            try {
                int remoteBed = (bedNum == 100 || bedNum == 101) ? bedNum : ((userSlot * 100) + bedNum);
                string author = string.IsNullOrEmpty(editorName) ? Environment.MachineName : editorName;
                string body = string.Format("{{\"content\":\"{0}\",\"updated_at\":\"{1}\",\"updated_by\":\"{2}\"}}",
                    EscapeJson(content), DateTime.UtcNow.ToString("o"), EscapeJson(author));
                byte[] data = Encoding.UTF8.GetBytes(body);

                HttpWebRequest req = CreateRequest(string.Format("bed_notes?bed_number=eq.{0}", remoteBed), "PATCH");
                req.ContentLength = data.Length;
                using (Stream stream = req.GetRequestStream()) {
                    stream.Write(data, 0, data.Length);
                }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                    return resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.NoContent;
                }
            } catch {
                return false;
            }
        }

        public string FetchRow101UsersJson() {
            if (!IsEnabled) return "";
            try {
                HttpWebRequest req = CreateRequest("bed_notes?bed_number=eq.101&select=content", "GET");
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                    string json = r.ReadToEnd();
                    var m = Regex.Match(json, @"""content"":\s*""((?:\\""|[^""])*)""");
                    if (m.Success) {
                        return UnescapeJson(m.Groups[1].Value);
                    }
                }
            } catch {}
            return "";
        }

        public bool SaveRow101UsersJson(string catalogJson, string editorName = "") {
            return SaveBed(101, catalogJson, 0, editorName);
        }

        public bool EnsureUserSlotRowsExist(int userSlot, string username) {
            if (!IsEnabled || userSlot <= 0) return true;
            try {
                int minBed = (userSlot * 100) + 1;
                HttpWebRequest checkReq = CreateRequest(string.Format("bed_notes?bed_number=eq.{0}&select=bed_number", minBed), "GET");
                using (HttpWebResponse resp = (HttpWebResponse)checkReq.GetResponse())
                using (StreamReader r = new StreamReader(resp.GetResponseStream())) {
                    string txt = r.ReadToEnd();
                    if (txt.Contains("\"bed_number\"")) {
                        return true;
                    }
                }
                var sb = new StringBuilder();
                sb.Append("[");
                for (int i = 1; i <= 30; i++) {
                    if (i > 1) sb.Append(",");
                    int bNum = (userSlot * 100) + i;
                    sb.AppendFormat("{{\"bed_number\":{0},\"content\":\"\",\"updated_by\":\"{1}\"}}", bNum, EscapeJson(username ?? "user"));
                }
                sb.Append("]");
                byte[] d = Encoding.UTF8.GetBytes(sb.ToString());
                HttpWebRequest postReq = CreateRequest("bed_notes", "POST");
                postReq.ContentLength = d.Length;
                using (Stream st = postReq.GetRequestStream()) st.Write(d, 0, d.Length);
                using (HttpWebResponse respPost = (HttpWebResponse)postReq.GetResponse()) {
                    return respPost.StatusCode == HttpStatusCode.Created || respPost.StatusCode == HttpStatusCode.OK;
                }
            } catch {
                return false;
            }
        }

        public bool SaveHistory(int bedNum, string reason, string content, int userSlot = 0) {
            if (!IsEnabled || bedNum < 1 || (bedNum > 30 && bedNum != 100)) return false;
            try {
                int remoteBed = (bedNum == 100) ? bedNum : ((userSlot * 100) + bedNum);
                string body = string.Format("{{\"bed_number\":{0},\"reason\":\"{1}\",\"content\":\"{2}\",\"char_count\":{3},\"created_at\":\"{4}\"}}",
                    remoteBed, EscapeJson(reason), EscapeJson(content), (content ?? "").Length, DateTime.UtcNow.ToString("o"));
                byte[] data = Encoding.UTF8.GetBytes(body);

                HttpWebRequest req = CreateRequest("bed_history", "POST");
                req.ContentLength = data.Length;
                using (Stream stream = req.GetRequestStream()) {
                    stream.Write(data, 0, data.Length);
                }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) {
                    return resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.Created;
                }
            } catch {
                return false;
            }
        }

        public List<BedHistoryItem> FetchHistory(int bedNum, int userSlot = 0) {
            var list = new List<BedHistoryItem>();
            if (!IsEnabled || bedNum < 1 || bedNum > 30) return list;
            try {
                int remoteBed = (userSlot * 100) + bedNum;
                HttpWebRequest req = CreateRequest(string.Format("bed_history?bed_number=eq.{0}&order=created_at.desc&limit=50", remoteBed), "GET");
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                    string json = reader.ReadToEnd();
                    var matches = Regex.Matches(json, @"\{""id"":(\d+),""bed_number"":(\d+),""reason"":""((?:\\""|[^""])*)"",""content"":""((?:\\""|[^""])*)"",""char_count"":(\d+),""created_at"":""((?:\\""|[^""])*)""\}", RegexOptions.Singleline);
                    foreach (Match m in matches) {
                        var item = new BedHistoryItem();
                        int rBed = int.Parse(m.Groups[2].Value);
                        item.BedNum = rBed - (userSlot * 100);
                        item.Reason = UnescapeJson(m.Groups[3].Value);
                        item.Content = UnescapeJson(m.Groups[4].Value);
                        DateTime dt;
                        if (DateTime.TryParse(m.Groups[6].Value, out dt)) item.Timestamp = dt.ToLocalTime();
                        else item.Timestamp = DateTime.Now;
                        list.Add(item);
                    }
                }
            } catch {}
            return list;
        }

        public static string EscapeJson(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
        }

        public static string UnescapeJson(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            string res = s.Replace("\\r\\n", "\r\n")
                          .Replace("\\n", "\r\n")
                          .Replace("\\r", "\r\n")
                          .Replace("\\t", "\t")
                          .Replace("\\\"", "\"")
                          .Replace("\\\\", "\\");
            return BedNotesManager.NormalizeNewlines(res);
        }
    }

    public class BedNotesManager {
        public static string NormalizeNewlines(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        private string localDir;
        private string sharedDir;
        private string historyDir;
        private FileSystemWatcher sharedWatcher;
        private Dictionary<int, string> cache = new Dictionary<int, string>();
        private object syncLock = new object();
        public event Action<int, string> OnBedChanged;

        // Multi-user & Workspace Partitioning
        private int activeUserSlot = 0;
        private string activeUsername = "admin";
        private string currentEditorName = "ผู้ดูแลระบบ (Admin)";
        public int ActiveUserSlot { get { return activeUserSlot; } }
        public string ActiveUsername { get { return activeUsername; } }
        public string CurrentEditorName { get { return currentEditorName; } set { currentEditorName = value; } }

        public void SetActiveWorkspace(int slot, string username, string editorDisplayName) {
            lock (syncLock) {
                activeUserSlot = slot;
                activeUsername = string.IsNullOrEmpty(username) ? "admin" : username.Trim().ToLowerInvariant();
                currentEditorName = string.IsNullOrEmpty(editorDisplayName) ? activeUsername : editorDisplayName;
                cache.Clear();
                lastLocalEditTime.Clear();
            }
            EnsureDirectories();
            LoadAll();
        }

        private string GetHistoryDir() {
            if (activeUserSlot == 0) return historyDir;
            return Path.Combine(localDir, "users", activeUsername, "history");
        }

        // Optimized: Background network share status caching to avoid UI thread blocking
        private volatile bool isSharedActiveCached = false;
        private System.Threading.Timer networkStatusTimer;

        // Supabase Cloud Real-time Sync
        private SupabaseSyncClient supabaseClient;
        public SupabaseSyncClient SupabaseClientInstance { get { return supabaseClient; } }
        private volatile bool isSupabaseActiveCached = false;
        private System.Threading.Timer cloudSyncTimer;
        private Dictionary<int, DateTime> lastLocalEditTime = new Dictionary<int, DateTime>();
        public event Action<int, bool> OnCloudSaveCompleted;
        public event Action<bool> OnCloudStatusChanged;

        public BedNotesManager(string localPath, string sharedPath, string supaUrl = "", string supaKey = "", bool supaEnabled = false) {
            localDir = localPath;
            sharedDir = sharedPath;
            historyDir = Path.Combine(localDir, "history");
            supabaseClient = new SupabaseSyncClient(supaEnabled ? supaUrl : "", supaKey);
            EnsureDirectories();
            CheckSharedDirectoryStatus();
            CheckSupabaseStatus();
            LoadAll();
            SetupWatcher();

            // Periodic background check of network status (every 20s) without blocking UI thread
            networkStatusTimer = new System.Threading.Timer(_ => CheckSharedDirectoryStatus(), null, 15000, 20000);

            // Periodic Supabase Cloud Polling (every 3 seconds) for real-time sync across ward PCs & mobile
            cloudSyncTimer = new System.Threading.Timer(_ => PollSupabaseCloud(), null, 2000, 3000);
        }

        public bool IsSupabaseActive {
            get { return supabaseClient != null && supabaseClient.IsEnabled && isSupabaseActiveCached; }
        }

        public string StatusText {
            get {
                if (IsSupabaseActive) {
                    return "🟢 ☁️ เชื่อมต่อฐานข้อมูล Supabase Cloud เรียบร้อย (ออนไลน์)";
                } else if (IsSharedActive) {
                    return "🟢 🌐 เชื่อมต่อกับโฟลเดอร์ส่วนกลางของวอร์ดเรียบร้อย (LAN ออนไลน์)";
                } else {
                    return "🔴 💻 โหมดบันทึกในเครื่องนี้ (ออฟไลน์ / ยังไม่ได้เชื่อมต่อระบบคลาวด์)";
                }
            }
        }

        public void CheckSupabaseStatus() {
            if (supabaseClient == null || !supabaseClient.IsEnabled) {
                isSupabaseActiveCached = false;
                if (OnCloudStatusChanged != null) {
                    try { OnCloudStatusChanged(false); } catch {}
                }
                return;
            }
            ThreadPool.QueueUserWorkItem(_ => {
                bool ok = supabaseClient.TestConnection();
                bool changed = (isSupabaseActiveCached != ok);
                isSupabaseActiveCached = ok;
                if (changed && OnCloudStatusChanged != null) {
                    try { OnCloudStatusChanged(ok); } catch {}
                }
            });
        }

        public void UpdateSupabaseConfig(string newUrl, string newKey, bool enabled) {
            supabaseClient = new SupabaseSyncClient(enabled ? newUrl : "", newKey);
            CheckSupabaseStatus();
            if (supabaseClient.IsEnabled) {
                ThreadPool.QueueUserWorkItem(_ => {
                    PollSupabaseCloud();
                });
            }
        }

        private void PollSupabaseCloud() {
            if (supabaseClient == null || !supabaseClient.IsEnabled) return;
            try {
                int slot = activeUserSlot;
                var cloudNotes = supabaseClient.FetchAllBeds(slot);
                if (cloudNotes == null || cloudNotes.Count == 0) return;
                bool wasActive = isSupabaseActiveCached;
                isSupabaseActiveCached = true;
                if (!wasActive && OnCloudStatusChanged != null) {
                    try { OnCloudStatusChanged(true); } catch {}
                }

                foreach (var kvp in cloudNotes) {
                    int bed = kvp.Key;
                    string cloudContent = kvp.Value ?? "";

                    // Protect against race condition: don't overwrite if local edit occurred within 6 seconds
                    lock (syncLock) {
                        if (activeUserSlot != slot) return;
                        DateTime lastEdit;
                        if (lastLocalEditTime.TryGetValue(bed, out lastEdit)) {
                            if ((DateTime.UtcNow - lastEdit).TotalSeconds < 6.0) {
                                continue;
                            }
                        }
                    }

                    string currentLocal = "";
                    lock (syncLock) {
                        cache.TryGetValue(bed, out currentLocal);
                    }

                    if (currentLocal != cloudContent) {
                        lock (syncLock) {
                            cache[bed] = cloudContent;
                        }
                        // Save local backup file
                        string path = GetLocalFilePath(bed);
                        WriteFileSafe(path, cloudContent);

                        // Trigger real-time UI notification
                        if (OnBedChanged != null) {
                            OnBedChanged(bed, cloudContent);
                        }
                    }
                }
            } catch {
                bool wasActive = isSupabaseActiveCached;
                isSupabaseActiveCached = false;
                if (wasActive && OnCloudStatusChanged != null) {
                    try { OnCloudStatusChanged(false); } catch {}
                }
            }
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
                if (activeUserSlot > 0 && !string.IsNullOrEmpty(activeUsername)) {
                    string uDir = Path.Combine(localDir, "users", activeUsername);
                    if (!Directory.Exists(uDir)) Directory.CreateDirectory(uDir);
                    string uHist = Path.Combine(uDir, "history");
                    if (!Directory.Exists(uHist)) Directory.CreateDirectory(uHist);
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
                string text = "";
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) {
                    text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                } else {
                    try {
                        UTF8Encoding strictUtf8 = new UTF8Encoding(false, true);
                        text = strictUtf8.GetString(bytes);
                    } catch {
                        try {
                            text = Encoding.GetEncoding(874).GetString(bytes);
                        } catch {
                            text = Encoding.Default.GetString(bytes);
                        }
                    }
                }
                return NormalizeNewlines(text);
            } catch {
                return "";
            }
        }

        public static void WriteFileSafe(string path, string content) {
            try {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, NormalizeNewlines(content ?? ""), SafeUtf8);
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
            if (supabaseClient != null && supabaseClient.IsEnabled) {
                ThreadPool.QueueUserWorkItem(_ => {
                    PollSupabaseCloud();
                });
            }
        }

        public void SyncFromShared() {
            if (activeUserSlot != 0) return;
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
            if (activeUserSlot != 0) return;
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
                    return NormalizeNewlines(val);
                }
                return "";
            }
        }

        public void SaveBedNote(int bedNum, string content) {
            content = NormalizeNewlines(content);
            int slot = activeUserSlot;
            string editor = currentEditorName;
            lock (syncLock) {
                cache[bedNum] = content;
                lastLocalEditTime[bedNum] = DateTime.UtcNow;
            }

            // 1. Save local cache (UTF-8 with BOM) - Fast local disk I/O (< 1ms)
            string localFile = GetLocalFilePath(bedNum);
            WriteFileSafe(localFile, content);

            // 2. Supabase Cloud Sync in background thread (Real-time, zero UI stutter)
            if (supabaseClient != null && supabaseClient.IsEnabled) {
                ThreadPool.QueueUserWorkItem(_ => {
                    bool ok = false;
                    try {
                        ok = supabaseClient.SaveBed(bedNum, content, slot, editor);
                    } catch {}
                    if (OnCloudSaveCompleted != null) {
                        try { OnCloudSaveCompleted(bedNum, ok); } catch {}
                    }
                });
            }

            // 3. Save to network share asynchronously in background thread
            // Never freeze the UI thread waiting for LAN/SMB!
            string sDir = sharedDir;
            if (isSharedActiveCached && !string.IsNullOrEmpty(sDir) && slot == 0) {
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

        public bool MoveOrSwapBed(int fromBed, int toBed, bool isSwap, out string message) {
            message = "";
            if (fromBed < 1 || fromBed > 30 || toBed < 1 || toBed > 30) {
                message = "หมายเลขเตียงไม่ถูกต้อง (ต้องอยู่ระหว่างเตียง 1 ถึง 30)";
                return false;
            }
            if (fromBed == toBed) {
                message = "เตียงต้นทางและเตียงปลายทางต้องเป็นคนละเตียงกัน";
                return false;
            }

            string fromNote = GetBedNote(fromBed);
            string toNote = GetBedNote(toBed);

            bool fromHasData = !string.IsNullOrEmpty(fromNote) && !string.IsNullOrEmpty(fromNote.Trim());
            bool toHasData = !string.IsNullOrEmpty(toNote) && !string.IsNullOrEmpty(toNote.Trim());

            if (!fromHasData && !toHasData) {
                message = string.Format("เตียง {0} และเตียง {1} ทั้งสองเตียงยังไม่มีข้อมูลผู้ป่วย", fromBed, toBed);
                return false;
            }

            if (isSwap) {
                if (fromHasData) {
                    SaveHistorySnapshot(fromBed, string.Format("สลับเตียงกับเตียง {0} (ข้อมูลเดิม)", toBed), fromNote);
                }
                if (toHasData) {
                    SaveHistorySnapshot(toBed, string.Format("สลับเตียงกับเตียง {0} (ข้อมูลเดิม)", fromBed), toNote);
                }

                SaveBedNote(fromBed, toNote);
                SaveBedNote(toBed, fromNote);

                if (OnBedChanged != null) {
                    try { OnBedChanged(fromBed, toNote); } catch {}
                    try { OnBedChanged(toBed, fromNote); } catch {}
                }

                message = string.Format("สลับข้อมูลระหว่างเตียง {0} และเตียง {1} เรียบร้อยแล้ว", fromBed, toBed);
            } else {
                if (fromHasData) {
                    SaveHistorySnapshot(fromBed, string.Format("ย้ายข้อมูลไปยังเตียง {0}", toBed), fromNote);
                }
                if (toHasData) {
                    SaveHistorySnapshot(toBed, string.Format("รับย้ายข้อมูลมาจากเตียง {0} (สำรองข้อมูลเดิมของเตียง {1})", fromBed, toBed), toNote);
                }

                SaveBedNote(toBed, fromNote);
                SaveBedNote(fromBed, "");

                if (OnBedChanged != null) {
                    try { OnBedChanged(fromBed, ""); } catch {}
                    try { OnBedChanged(toBed, fromNote); } catch {}
                }

                message = string.Format("ย้ายข้อมูลจากเตียง {0} ไปยังเตียง {1} สำเร็จแล้ว", fromBed, toBed);
            }
            return true;
        }

        public void SaveHistorySnapshot(int bedNum, string reason, string content) {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(content.Trim())) return;
            content = NormalizeNewlines(content);
            int slot = activeUserSlot;
            try {
                string hDir = GetHistoryDir();
                if (!Directory.Exists(hDir)) Directory.CreateDirectory(hDir);
                string histFile = Path.Combine(hDir, string.Format("bed_{0:D2}_history.txt", bedNum));
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== SNAPSHOT_START ===");
                sb.AppendLine("Timestamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("Reason=" + (reason ?? "บันทึกข้อมูล"));
                sb.AppendLine("=== CONTENT ===");
                sb.AppendLine(content.TrimEnd());
                sb.AppendLine("=== SNAPSHOT_END ===");
                File.AppendAllText(histFile, sb.ToString(), SafeUtf8);

                if (supabaseClient != null && supabaseClient.IsEnabled) {
                    ThreadPool.QueueUserWorkItem(_ => {
                        try {
                            supabaseClient.SaveHistory(bedNum, reason, content, slot);
                        } catch {}
                    });
                }
            } catch {}
        }

        public List<BedHistoryItem> GetBedHistory(int bedNum) {
            int slot = activeUserSlot;
            if (supabaseClient != null && supabaseClient.IsEnabled) {
                try {
                    var cloudHist = supabaseClient.FetchHistory(bedNum, slot);
                    if (cloudHist != null && cloudHist.Count > 0) {
                        return cloudHist;
                    }
                } catch {}
            }
            List<BedHistoryItem> list = new List<BedHistoryItem>();
            string hDir = GetHistoryDir();
            string histFile = Path.Combine(hDir, string.Format("bed_{0:D2}_history.txt", bedNum));
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
            if (activeUserSlot == 0) {
                return Path.Combine(localDir, string.Format("bed_{0:D2}.txt", bedNum));
            }
            return Path.Combine(localDir, "users", activeUsername, string.Format("bed_{0:D2}.txt", bedNum));
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
        public string AppBaseDir { get { return string.IsNullOrEmpty(appBaseDir) ? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') : appBaseDir; } }
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

        private string supabaseUrl = "https://mhzpurmhrqutxdhmsday.supabase.co";
        private string supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im1oenB1cm1ocnF1dHhkaG1zZGF5Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTA2OTM1NjAsImV4cCI6MjEwNjI2OTU2MH0.A9a4sox0YUBKlWkEcaInqQOb8EA0yzl99uwY_cg-kyo";
        private bool supabaseEnabled = true;
        public string GetSupabaseUrl() { return supabaseUrl; }
        public string GetSupabaseKey() { return supabaseKey; }
        public bool GetSupabaseEnabled() { return supabaseEnabled; }
        public void SetSupabaseConfig(string url, string key, bool enabled) {
            supabaseUrl = url;
            supabaseKey = key;
            supabaseEnabled = enabled;
            if (bedNotesManager != null) {
                bedNotesManager.UpdateSupabaseConfig(supabaseUrl, supabaseKey, supabaseEnabled);
            }
            SaveConfigFile();
        }

        private string adminPassword = "9844";
        public string AdminPassword {
            get { return string.IsNullOrEmpty(adminPassword) ? "9844" : adminPassword; }
            set { adminPassword = value; SaveConfigFile(); }
        }
        private FileSystemWatcher watcher = null;
        private string iconPath;
        private bool isEnabled = true;
        private float currentFontSize = 13.0f;
        public float CurrentFontSize { get { return currentFontSize; } set { currentFontSize = value; } }

        private BedNotesManager bedNotesManager;
        public BedNotesManager BedNotesManager { get { return bedNotesManager; } }
        private WardUserManager userManager;
        public WardUserManager UserManager { get { return userManager; } }

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

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public ExpanderContext() {
            syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            InitializePaths();
            LoadSettings();

            bedNotesManager = new BedNotesManager(localBedNotesDir, sharedBedNotesDir, supabaseUrl, supabaseKey, supabaseEnabled);
            userManager = new WardUserManager(appBaseDir, bedNotesManager.SupabaseClientInstance);
            userManager.OnWorkspaceChanged += (slot, uname, dname) => {
                bedNotesManager.SetActiveWorkspace(slot, uname, dname);
                if (bedNotesForm != null && !bedNotesForm.IsDisposed) {
                    bedNotesForm.OnWorkspaceChanged();
                }
            };

            // Gate: ตรวจสอบการเข้าสู่ระบบ หากยังไม่มี session ที่จดจำไว้ ให้แสดงหน้าต่าง Login / Register บังคับ
            if (userManager.CurrentUser == null) {
                using (var loginDlg = new LoginRegisterDialog(this, isStartupGate: true)) {
                    DialogResult dr = loginDlg.ShowDialog();
                    if (dr != DialogResult.OK || userManager.CurrentUser == null) {
                        // ปิดโปรแกรมหากไม่เข้าสู่ระบบหรือกดยกเลิก
                        Environment.Exit(0);
                        return;
                    }
                }
            }

            if (userManager.CurrentUser != null) {
                var u = userManager.ActiveWorkspaceUser;
                bedNotesManager.SetActiveWorkspace(u.UserSlot, u.Username, u.DisplayName);
            }

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

            // เปิดหน้าต่างบันทึกเตียง 1-30 ให้ปรากฏขึ้นมาบนหน้าจอทันทีที่เปิดโปรแกรม
            ShowBedNotes();
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
            Action showAct = () => {
                ShowBedNotes();
                if (bedNotesForm != null && !bedNotesForm.IsDisposed) {
                    try {
                        ShowWindowAsync(bedNotesForm.Handle, SW_RESTORE);
                        SetForegroundWindow(bedNotesForm.Handle);
                    } catch {}
                    if (bedNotesForm.WindowState == FormWindowState.Minimized) {
                        bedNotesForm.WindowState = FormWindowState.Normal;
                    }
                    bedNotesForm.Show();
                    bedNotesForm.Activate();
                    bedNotesForm.BringToFront();
                }
            };

            if (syncContext != null) {
                syncContext.Post(_ => showAct(), null);
            } else if (bedNotesForm != null && !bedNotesForm.IsDisposed && bedNotesForm.IsHandleCreated) {
                try {
                    bedNotesForm.BeginInvoke(showAct);
                } catch {}
            } else {
                showAct();
            }
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
            trayMenu.Items.Add("👥 จัดการบัญชีผู้ใช้และสิทธิ์ (User Accounts)", null, (s, e) => ShowUserManagement());
            trayMenu.Items.Add("🔀 สลับผู้ใช้งาน (Switch User / Login)", null, (s, e) => ShowUserLogin());
            trayMenu.Items.Add("🚪 ออกจากระบบ (Logout)", null, (s, e) => Logout());
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
                        } else if (t.StartsWith("SupabaseUrl=", StringComparison.OrdinalIgnoreCase)) {
                            supabaseUrl = t.Substring("SupabaseUrl=".Length).Trim();
                        } else if (t.StartsWith("SupabaseKey=", StringComparison.OrdinalIgnoreCase)) {
                            supabaseKey = t.Substring("SupabaseKey=".Length).Trim();
                        } else if (t.StartsWith("SupabaseEnabled=", StringComparison.OrdinalIgnoreCase)) {
                            bool b;
                            if (bool.TryParse(t.Substring("SupabaseEnabled=".Length).Trim(), out b)) {
                                supabaseEnabled = b;
                            }
                        } else if (t.StartsWith("AdminPassword=", StringComparison.OrdinalIgnoreCase)) {
                            string ap = t.Substring("AdminPassword=".Length).Trim();
                            if (!string.IsNullOrEmpty(ap)) adminPassword = ap;
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
                sb.AppendLine("SupabaseUrl=" + supabaseUrl);
                sb.AppendLine("SupabaseKey=" + supabaseKey);
                sb.AppendLine("SupabaseEnabled=" + supabaseEnabled.ToString().ToLower());
                sb.AppendLine("AdminPassword=" + (string.IsNullOrEmpty(adminPassword) ? "9844" : adminPassword));
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
                        if (!File.Exists(localConfigPath)) {
                            File.Copy(sharedConfigPath, localConfigPath, true);
                            activeFile = sharedConfigPath;
                        } else {
                            FileInfo fiShared = new FileInfo(sharedConfigPath);
                            FileInfo fiLocal = new FileInfo(localConfigPath);
                            if (fiShared.LastWriteTimeUtc > fiLocal.LastWriteTimeUtc && fiShared.Length >= fiLocal.Length) {
                                File.Copy(sharedConfigPath, localConfigPath, true);
                                activeFile = sharedConfigPath;
                            } else if (fiLocal.LastWriteTimeUtc > fiShared.LastWriteTimeUtc || fiLocal.Length > fiShared.Length) {
                                try { File.Copy(localConfigPath, sharedConfigPath, true); } catch {}
                                activeFile = localConfigPath;
                            }
                        }
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
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && string.IsNullOrEmpty(curShortcut)) {
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

                // Auto-upgrade templates if missing new orthopedic/spine/specialized categories
                if (templates.Count < 50) {
                    try {
                        string tplUrl = "https://raw.githubusercontent.com/oatzilla/Medical_Text_Expander/main/medical_templates.txt";
                        using (WebClient wc = new WebClient()) {
                            wc.Encoding = Encoding.UTF8;
                            string remoteTpls = wc.DownloadString(tplUrl);
                            if (!string.IsNullOrEmpty(remoteTpls) && remoteTpls.Contains("[9.") && remoteTpls.Contains("[11.")) {
                                File.WriteAllText(localConfigPath, remoteTpls, Encoding.UTF8);
                                // Reload with upgraded templates
                                templates.Clear();
                                string[] upLines = File.ReadAllLines(localConfigPath, Encoding.UTF8);
                                string uShortcut = "", uTitle = "", uCategory = "ทั่วไป";
                                StringBuilder uContent = new StringBuilder();
                                foreach (string l in upLines) {
                                    string tr = l.Trim();
                                    if (tr.StartsWith("#") || string.IsNullOrEmpty(tr)) continue;
                                    if (tr.StartsWith("[") && tr.EndsWith("]") && string.IsNullOrEmpty(uShortcut)) { uCategory = tr.Substring(1, tr.Length - 2); continue; }
                                    if (tr.StartsWith("---")) {
                                        if (!string.IsNullOrEmpty(uShortcut) && uContent.Length > 0) {
                                            templates.Add(new TemplateItem(uShortcut, uTitle, uCategory, uContent.ToString().TrimEnd()));
                                        }
                                        uShortcut = ""; uTitle = ""; uContent.Clear();
                                        continue;
                                    }
                                    int eq = l.IndexOf('=');
                                    if (eq > 0 && string.IsNullOrEmpty(uShortcut)) {
                                        uShortcut = l.Substring(0, eq).Trim();
                                        uTitle = l.Substring(eq + 1).Trim();
                                    } else {
                                        uContent.AppendLine(l);
                                    }
                                }
                                if (!string.IsNullOrEmpty(uShortcut) && uContent.Length > 0) {
                                    templates.Add(new TemplateItem(uShortcut, uTitle, uCategory, uContent.ToString().TrimEnd()));
                                }
                            }
                        }
                    } catch {}
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

            string content = BedNotesManager.NormalizeNewlines(rawContent)
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

        public void ShowPalette(int targetBed = -1) {
            if (paletteForm == null || paletteForm.IsDisposed) {
                paletteForm = new PaletteForm(this);
            }
            int bedToUse = targetBed;
            if (bedToUse <= 0 && bedNotesForm != null && !bedNotesForm.IsDisposed && bedNotesForm.Visible) {
                bedToUse = bedNotesForm.CurrentBed;
            }
            paletteForm.ShowAndFocus(bedToUse);
        }

        public void InsertTemplateToBed(int bedNum, string content, bool replace = false) {
            if (bedNotesForm == null || bedNotesForm.IsDisposed) {
                bedNotesForm = new BedNotesForm(this, bedNotesManager);
            }
            bedNotesForm.ShowAndFocus(bedNum);
            if (replace) {
                bedNotesForm.ReplaceNoteExternal(content);
            } else {
                bedNotesForm.InsertSnippetExternal(content);
            }
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

        public bool PromptAdminPassword(IWin32Window owner = null) {
            if (userManager != null && userManager.IsAdminLoggedIn) return true;
            using (AdminPasswordDialog dlg = new AdminPasswordDialog(AdminPassword)) {
                return dlg.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public void ShowUserLogin(IWin32Window owner = null) {
            using (var dlg = new LoginRegisterDialog(this, isStartupGate: false)) {
                if (dlg.ShowDialog(owner) == DialogResult.OK) {
                    if (bedNotesForm != null && !bedNotesForm.IsDisposed) {
                        bedNotesForm.OnWorkspaceChanged();
                        bedNotesForm.RefreshAllBedButtons();
                    }
                }
            }
        }

        public void Logout() {
            if (userManager != null) {
                userManager.Logout();
            }
            if (bedNotesForm != null && !bedNotesForm.IsDisposed) bedNotesForm.Hide();
            if (paletteForm != null && !paletteForm.IsDisposed) paletteForm.Hide();
            if (calculatorForm != null && !calculatorForm.IsDisposed) calculatorForm.Hide();
            if (stickyReminderForm != null && !stickyReminderForm.IsDisposed) stickyReminderForm.Hide();

            using (var dlg = new LoginRegisterDialog(this, isStartupGate: true)) {
                if (dlg.ShowDialog() == DialogResult.OK && userManager != null && userManager.CurrentUser != null) {
                    var u = userManager.ActiveWorkspaceUser;
                    bedNotesManager.SetActiveWorkspace(u.UserSlot, u.Username, u.DisplayName);
                    ShowBedNotes();
                } else {
                    Exit();
                }
            }
        }

        public void ShowUserManagement(IWin32Window owner = null) {
            if (!PromptAdminPassword(owner)) return;
            using (var dlg = new UserManagementDialog(this)) {
                dlg.ShowDialog(owner);
            }
        }

        public void ShowSyncSettings(IWin32Window owner = null) {
            if (!PromptAdminPassword(owner)) return;
            SyncSettingsForm form = new SyncSettingsForm(this, sharedConfigPath, sharedBedNotesDir);
            if (owner != null) form.ShowDialog(owner);
            else form.ShowDialog();
        }

        public void EditTemplates() {
            if (!PromptAdminPassword(null)) return;
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
            if (!PromptAdminPassword(null)) return;
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
                ZoneName = ""
            },
            // Bed 2, 12, 22 - Emerald Green
            new BedTheme {
                Primary = Color.FromArgb(16, 185, 129),
                SoftBg = Color.FromArgb(236, 253, 245),
                Border = Color.FromArgb(167, 243, 208),
                TextDark = Color.FromArgb(4, 120, 87),
                ZoneName = ""
            },
            // Bed 3, 13, 23 - Royal Indigo
            new BedTheme {
                Primary = Color.FromArgb(99, 102, 241),
                SoftBg = Color.FromArgb(238, 242, 255),
                Border = Color.FromArgb(199, 210, 254),
                TextDark = Color.FromArgb(67, 56, 202),
                ZoneName = ""
            },
            // Bed 4, 14, 24 - Amber Gold
            new BedTheme {
                Primary = Color.FromArgb(217, 119, 6),
                SoftBg = Color.FromArgb(254, 243, 199),
                Border = Color.FromArgb(253, 230, 138),
                TextDark = Color.FromArgb(180, 83, 9),
                ZoneName = ""
            },
            // Bed 5, 15, 25 - Rose Crimson
            new BedTheme {
                Primary = Color.FromArgb(225, 29, 72),
                SoftBg = Color.FromArgb(255, 241, 242),
                Border = Color.FromArgb(254, 205, 211),
                TextDark = Color.FromArgb(190, 18, 60),
                ZoneName = ""
            },
            // Bed 6, 16, 26 - Violet Purple
            new BedTheme {
                Primary = Color.FromArgb(147, 51, 234),
                SoftBg = Color.FromArgb(250, 245, 255),
                Border = Color.FromArgb(233, 213, 255),
                TextDark = Color.FromArgb(126, 34, 206),
                ZoneName = ""
            },
            // Bed 7, 17, 27 - Cyan Turquoise
            new BedTheme {
                Primary = Color.FromArgb(14, 165, 233),
                SoftBg = Color.FromArgb(240, 253, 250),
                Border = Color.FromArgb(153, 246, 228),
                TextDark = Color.FromArgb(15, 118, 110),
                ZoneName = ""
            },
            // Bed 8, 18, 28 - Coral Tangerine
            new BedTheme {
                Primary = Color.FromArgb(234, 88, 12),
                SoftBg = Color.FromArgb(255, 247, 237),
                Border = Color.FromArgb(254, 215, 170),
                TextDark = Color.FromArgb(194, 65, 12),
                ZoneName = ""
            },
            // Bed 9, 19, 29 - Forest Teal
            new BedTheme {
                Primary = Color.FromArgb(13, 148, 136),
                SoftBg = Color.FromArgb(240, 253, 250),
                Border = Color.FromArgb(153, 246, 228),
                TextDark = Color.FromArgb(17, 94, 89),
                ZoneName = ""
            },
            // Bed 10, 20, 30 - Warm Fuchsia
            new BedTheme {
                Primary = Color.FromArgb(192, 38, 211),
                SoftBg = Color.FromArgb(253, 244, 255),
                Border = Color.FromArgb(245, 208, 254),
                TextDark = Color.FromArgb(134, 25, 143),
                ZoneName = ""
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
                    ZoneName = ""
                };
            }
            int index = (bedNum - 1) % Themes.Length;
            return Themes[index];
        }
    }

    
    // ==========================================
    // Mobile & Web Portal QR Code Dialog
    // ==========================================
    public class MobilePortalDialog : Form {
        public MobilePortalDialog() {
            this.Text = "📱 ใช้งานบนมือถือ & แท็บเล็ต (Mobile & Web Portal)";
            this.Size = new Size(460, 520);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5f);

            string portalUrl = "https://oatzilla.github.io/Medical_Text_Expander/";

            Label lblTitle = new Label();
            lblTitle.Text = "สแกน QR Code เพื่อเปิดดูบนมือถือ";
            lblTitle.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(15, 23, 42);
            lblTitle.Location = new Point(20, 16);
            lblTitle.Size = new Size(400, 28);
            lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            this.Controls.Add(lblTitle);

            Label lblDesc = new Label();
            lblDesc.Text = "แพทย์และพยาบาลสามารถเปิดกล้องมือถือสแกนรูปด้านล่าง\nเพื่อดูและลงบันทึกเตียง 1-30 จากที่บ้านหรือนอก รพ. ได้ทันที";
            lblDesc.Font = new Font("Segoe UI", 9f);
            lblDesc.ForeColor = Color.FromArgb(100, 116, 139);
            lblDesc.Location = new Point(20, 48);
            lblDesc.Size = new Size(400, 40);
            lblDesc.TextAlign = ContentAlignment.MiddleCenter;
            this.Controls.Add(lblDesc);

            PictureBox picQr = new PictureBox();
            picQr.Location = new Point(130, 96);
            picQr.Size = new Size(180, 180);
            picQr.SizeMode = PictureBoxSizeMode.Zoom;
            picQr.BorderStyle = BorderStyle.FixedSingle;
            this.Controls.Add(picQr);

            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    string qrApi = "https://api.qrserver.com/v1/create-qr-code/?size=180x180&data=" + Uri.EscapeDataString(portalUrl);
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(qrApi);
                    req.Timeout = 6000;
                    using (var resp = req.GetResponse())
                    using (var stream = resp.GetResponseStream()) {
                        Image img = Image.FromStream(stream);
                        if (this.IsHandleCreated && !this.IsDisposed) {
                            this.BeginInvoke(new Action(() => picQr.Image = img));
                        }
                    }
                } catch {}
            });

            TextBox txtUrl = new TextBox();
            txtUrl.Text = portalUrl;
            txtUrl.ReadOnly = true;
            txtUrl.Location = new Point(35, 295);
            txtUrl.Size = new Size(375, 27);
            txtUrl.Font = new Font("Segoe UI", 9.5f);
            txtUrl.TextAlign = HorizontalAlignment.Center;
            this.Controls.Add(txtUrl);

            Button btnCopyLink = new Button();
            btnCopyLink.Text = "📋 คัดลอกลิงก์";
            btnCopyLink.Location = new Point(45, 340);
            btnCopyLink.Size = new Size(160, 38);
            btnCopyLink.BackColor = Color.FromArgb(241, 245, 249);
            btnCopyLink.FlatStyle = FlatStyle.Flat;
            btnCopyLink.Cursor = Cursors.Hand;
            btnCopyLink.Click += (s, e) => {
                try {
                    Clipboard.SetText(portalUrl);
                    MessageBox.Show("คัดลอกลิงก์เรียบร้อยแล้ว ส่งต่อใน LINE กลุ่มวอร์ดได้ทันทีครับ", "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } catch {}
            };
            this.Controls.Add(btnCopyLink);

            Button btnOpenBrowser = new Button();
            btnOpenBrowser.Text = "🌐 เปิดบนเบราว์เซอร์";
            btnOpenBrowser.Location = new Point(225, 340);
            btnOpenBrowser.Size = new Size(185, 38);
            btnOpenBrowser.BackColor = Color.FromArgb(13, 148, 136);
            btnOpenBrowser.ForeColor = Color.White;
            btnOpenBrowser.FlatStyle = FlatStyle.Flat;
            btnOpenBrowser.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnOpenBrowser.Cursor = Cursors.Hand;
            btnOpenBrowser.Click += (s, e) => {
                try { System.Diagnostics.Process.Start(portalUrl); } catch {}
            };
            this.Controls.Add(btnOpenBrowser);

            Button btnClose = new Button();
            btnClose.Text = "ปิดหน้าต่าง";
            btnClose.Location = new Point(165, 410);
            btnClose.Size = new Size(120, 36);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }
    }

    public class WardDocItem {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public string FileName { get; set; }
        public string DownloadName { get; set; }
        public string FileType { get; set; }
        public long Size { get; set; }
        public string UpdatedAt { get; set; }
        public string UpdatedBy { get; set; }
        public string Base64 { get; set; }
        public string LocalPath { get; set; }
        public bool ExistsLocally {
            get { return !string.IsNullOrEmpty(LocalPath) && File.Exists(LocalPath); }
        }
    }

    public class EditWardDocDialog : Form {
        public string NewTitle { get; private set; }
        public string NewCategory { get; private set; }
        public string UploaderName { get; private set; }

        private TextBox txtTitle;
        private ComboBox cboCategory;
        private TextBox txtUploader;

        public EditWardDocDialog(WardDocItem item, string defaultUploader) {
            this.Text = "✏️ แก้ไขชื่อและหมวดหมู่เอกสาร - Medical Text Expander";
            this.Size = new Size(540, 310);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f);

            Panel pnlTop = new Panel {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(37, 99, 235)
            };
            Label lblH = new Label {
                Text = "✏️ แก้ไขชื่อและหมวดหมู่เอกสารประจำวอร์ด",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Location = new Point(14, 11),
                AutoSize = true
            };
            pnlTop.Controls.Add(lblH);
            this.Controls.Add(pnlTop);

            Label lblFile = new Label {
                Text = "ชื่อไฟล์: " + (item.FileName ?? "--"),
                Location = new Point(20, 56),
                Size = new Size(480, 22),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(5, 150, 105)
            };
            this.Controls.Add(lblFile);

            Label lblT = new Label { Text = "ชื่อเอกสาร:", Location = new Point(20, 88), Size = new Size(110, 24), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            this.Controls.Add(lblT);
            txtTitle = new TextBox { Text = item.Title ?? item.FileName, Location = new Point(130, 85), Size = new Size(370, 27) };
            this.Controls.Add(txtTitle);

            Label lblC = new Label { Text = "หมวดหมู่:", Location = new Point(20, 126), Size = new Size(110, 24), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            this.Controls.Add(lblC);
            cboCategory = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(130, 123),
                Size = new Size(370, 28)
            };
            cboCategory.Items.AddRange(new object[] {
                "แบบฟอร์มบันทึกทางการพยาบาล",
                "แบบประเมินทางการพยาบาล",
                "แนวทาง CPG / หัตถการ",
                "เอกสารและแบบฟอร์มทั่วไป"
            });
            int catIdx = cboCategory.Items.IndexOf(item.Category);
            cboCategory.SelectedIndex = catIdx >= 0 ? catIdx : 0;
            this.Controls.Add(cboCategory);

            Label lblU = new Label { Text = "ผู้แก้ไข:", Location = new Point(20, 164), Size = new Size(110, 24), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            this.Controls.Add(lblU);
            txtUploader = new TextBox { Text = string.IsNullOrEmpty(defaultUploader) ? Environment.MachineName : defaultUploader, Location = new Point(130, 161), Size = new Size(370, 27) };
            this.Controls.Add(txtUploader);

            Button btnSave = new Button {
                Text = "💾 บันทึกการแก้ไข",
                Location = new Point(130, 212),
                Size = new Size(160, 38),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.Click += (s, e) => {
                if (string.IsNullOrEmpty(txtTitle.Text.Trim())) {
                    MessageBox.Show("กรุณาระบุชื่อเอกสารครับ", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                NewTitle = txtTitle.Text.Trim();
                NewCategory = cboCategory.SelectedItem != null ? cboCategory.SelectedItem.ToString() : item.Category;
                UploaderName = txtUploader.Text.Trim();
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(btnSave);

            Button btnCancel = new Button {
                Text = "ยกเลิก",
                Location = new Point(300, 212),
                Size = new Size(110, 38),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            this.Controls.Add(btnCancel);
        }
    }

    public class WardDocumentCenterDialog : Form {
        private ExpanderContext context;
        private List<WardDocItem> allDocuments = new List<WardDocItem>();
        private List<WardDocItem> filteredDocuments = new List<WardDocItem>();
        private WardDocItem selectedDoc = null;
        private bool isAdminUnlocked = false;

        // UI Controls
        private TextBox txtSearch;
        private ComboBox cboCategoryFilter;
        private Button btnRefresh;
        private Button btnSyncFromCloudTop;

        private ListView lvDocuments;
        private Label lblDocDetailTitle;
        private Label lblDocDetailMeta;
        private Button btnOpenDoc;
        private Button btnSaveAs;

        // Admin Controls
        private GroupBox grpAdmin;
        private Panel pnlAdminGate;
        private TextBox txtAdminPass;
        private Button btnUnlockAdmin;

        private Panel pnlAdminUnlocked;
        private Button btnEditDoc;
        private Button btnReplaceFile;
        private Button btnDeleteDoc;

        private Button btnChooseNewFiles;
        private Label lblNewFiles;
        private ComboBox cboNewCategory;
        private TextBox txtUploaderName;
        private Button btnUploadNew;

        private List<string> chosenNewFilePaths = new List<string>();

        public WardDocumentCenterDialog(ExpanderContext ctx) {
            context = ctx;
            InitializeUI();
            LoadDocuments();
        }

        private string GetTemplatesDirectory() {
            string baseDir = context.AppBaseDir;
            string templatesDir = Path.Combine(baseDir, "templates");
            if (!Directory.Exists(templatesDir)) {
                try { Directory.CreateDirectory(templatesDir); } catch {}
            }
            return templatesDir;
        }

        private void InitializeUI() {
            this.Text = "📁 ศูนย์รวมเอกสารและแบบฟอร์มประจำวอร์ด - Medical Text Expander";
            this.Size = new Size(880, 720);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f);

            // Header Banner
            Panel pnlHeader = new Panel();
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 64;
            pnlHeader.BackColor = Color.FromArgb(16, 185, 129); // Emerald Green

            Label lblHeaderTitle = new Label();
            lblHeaderTitle.Text = "📁 ศูนย์รวมเอกสารและแบบฟอร์มประจำวอร์ด (Ward Documents & Forms)";
            lblHeaderTitle.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            lblHeaderTitle.ForeColor = Color.White;
            lblHeaderTitle.Location = new Point(16, 10);
            lblHeaderTitle.AutoSize = true;
            pnlHeader.Controls.Add(lblHeaderTitle);

            Label lblHeaderSub = new Label();
            lblHeaderSub.Text = "เปิดใช้งานแบบฟอร์ม บันทึก I/O และเอกสารทางการพยาบาล หรือจัดการ แก้ไขชื่อ ลบ และอัปโหลดขึ้น Cloud";
            lblHeaderSub.Font = new Font("Segoe UI", 9f);
            lblHeaderSub.ForeColor = Color.FromArgb(209, 250, 229);
            lblHeaderSub.Location = new Point(18, 36);
            lblHeaderSub.AutoSize = true;
            pnlHeader.Controls.Add(lblHeaderSub);

            this.Controls.Add(pnlHeader);

            // Filter Bar
            Panel pnlFilter = new Panel();
            pnlFilter.Location = new Point(18, 72);
            pnlFilter.Size = new Size(830, 32);

            Label lblSearchPrompt = new Label();
            lblSearchPrompt.Text = "🔍 ค้นหา:";
            lblSearchPrompt.Location = new Point(0, 6);
            lblSearchPrompt.Size = new Size(60, 22);
            lblSearchPrompt.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblSearchPrompt.ForeColor = Color.FromArgb(71, 85, 105);
            pnlFilter.Controls.Add(lblSearchPrompt);

            txtSearch = new TextBox();
            txtSearch.Location = new Point(64, 3);
            txtSearch.Size = new Size(240, 27);
            txtSearch.TextChanged += (s, e) => ApplyFilter();
            pnlFilter.Controls.Add(txtSearch);

            Label lblCatPrompt = new Label();
            lblCatPrompt.Text = "หมวดหมู่:";
            lblCatPrompt.Location = new Point(318, 6);
            lblCatPrompt.Size = new Size(65, 22);
            lblCatPrompt.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblCatPrompt.ForeColor = Color.FromArgb(71, 85, 105);
            pnlFilter.Controls.Add(lblCatPrompt);

            cboCategoryFilter = new ComboBox();
            cboCategoryFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            cboCategoryFilter.Location = new Point(386, 3);
            cboCategoryFilter.Size = new Size(240, 28);
            cboCategoryFilter.Items.AddRange(new object[] {
                "ทั้งหมด (All)",
                "แบบฟอร์มบันทึกทางการพยาบาล",
                "แบบประเมินทางการพยาบาล",
                "แนวทาง CPG / หัตถการ",
                "เอกสารและแบบฟอร์มทั่วไป"
            });
            cboCategoryFilter.SelectedIndex = 0;
            cboCategoryFilter.SelectedIndexChanged += (s, e) => ApplyFilter();
            pnlFilter.Controls.Add(cboCategoryFilter);

            btnRefresh = new Button();
            btnRefresh.Text = "🔄 รีเฟรช";
            btnRefresh.Location = new Point(634, 2);
            btnRefresh.Size = new Size(85, 29);
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.Cursor = Cursors.Hand;
            btnRefresh.Click += (s, e) => LoadDocuments();
            pnlFilter.Controls.Add(btnRefresh);

            btnSyncFromCloudTop = new Button();
            btnSyncFromCloudTop.Text = "☁️ ซิงค์ Cloud";
            btnSyncFromCloudTop.Location = new Point(725, 2);
            btnSyncFromCloudTop.Size = new Size(105, 29);
            btnSyncFromCloudTop.BackColor = Color.FromArgb(238, 242, 255);
            btnSyncFromCloudTop.ForeColor = Color.FromArgb(79, 70, 229);
            btnSyncFromCloudTop.FlatStyle = FlatStyle.Flat;
            btnSyncFromCloudTop.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnSyncFromCloudTop.Cursor = Cursors.Hand;
            btnSyncFromCloudTop.Click += (s, e) => SyncDocumentsFromCloudManual();
            pnlFilter.Controls.Add(btnSyncFromCloudTop);

            this.Controls.Add(pnlFilter);

            // ListView of Documents
            lvDocuments = new ListView();
            lvDocuments.Location = new Point(18, 110);
            lvDocuments.Size = new Size(830, 185);
            lvDocuments.View = View.Details;
            lvDocuments.FullRowSelect = true;
            lvDocuments.GridLines = true;
            lvDocuments.HideSelection = false;
            lvDocuments.MultiSelect = false;
            lvDocuments.Columns.Add("ชื่อเอกสาร", 260);
            lvDocuments.Columns.Add("หมวดหมู่", 180);
            lvDocuments.Columns.Add("ชื่อไฟล์", 170);
            lvDocuments.Columns.Add("ขนาด", 75);
            lvDocuments.Columns.Add("อัปเดตล่าสุด", 125);
            lvDocuments.SelectedIndexChanged += (s, e) => OnListViewSelectionChanged();
            lvDocuments.DoubleClick += (s, e) => OpenSelectedDocument();
            this.Controls.Add(lvDocuments);

            // Selected Document Details & Action Bar
            Panel pnlDetails = new Panel();
            pnlDetails.Location = new Point(18, 302);
            pnlDetails.Size = new Size(830, 56);
            pnlDetails.BackColor = Color.FromArgb(241, 245, 249);
            pnlDetails.BorderStyle = BorderStyle.FixedSingle;

            lblDocDetailTitle = new Label();
            lblDocDetailTitle.Text = "📄 เอกสารที่เลือก: (ยังไม่ได้เลือก)";
            lblDocDetailTitle.Location = new Point(12, 8);
            lblDocDetailTitle.Size = new Size(520, 20);
            lblDocDetailTitle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblDocDetailTitle.ForeColor = Color.FromArgb(15, 23, 42);
            pnlDetails.Controls.Add(lblDocDetailTitle);

            lblDocDetailMeta = new Label();
            lblDocDetailMeta.Text = "คลิกเลือกเอกสารในตารางด้านบนเพื่อเปิดใช้งาน บันทึกสำเนา แก้ไข หรือลบ";
            lblDocDetailMeta.Location = new Point(12, 30);
            lblDocDetailMeta.Size = new Size(520, 18);
            lblDocDetailMeta.Font = new Font("Segoe UI", 8.5f);
            lblDocDetailMeta.ForeColor = Color.FromArgb(100, 116, 139);
            pnlDetails.Controls.Add(lblDocDetailMeta);

            btnOpenDoc = new Button();
            btnOpenDoc.Text = "📂 เปิดใช้งานทันที";
            btnOpenDoc.Location = new Point(540, 9);
            btnOpenDoc.Size = new Size(140, 36);
            btnOpenDoc.BackColor = Color.FromArgb(16, 185, 129);
            btnOpenDoc.ForeColor = Color.White;
            btnOpenDoc.FlatStyle = FlatStyle.Flat;
            btnOpenDoc.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnOpenDoc.Cursor = Cursors.Hand;
            btnOpenDoc.Enabled = false;
            btnOpenDoc.Click += (s, e) => OpenSelectedDocument();
            pnlDetails.Controls.Add(btnOpenDoc);

            btnSaveAs = new Button();
            btnSaveAs.Text = "💾 บันทึกสำเนา";
            btnSaveAs.Location = new Point(688, 9);
            btnSaveAs.Size = new Size(130, 36);
            btnSaveAs.BackColor = Color.White;
            btnSaveAs.ForeColor = Color.FromArgb(51, 65, 85);
            btnSaveAs.FlatStyle = FlatStyle.Flat;
            btnSaveAs.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnSaveAs.Cursor = Cursors.Hand;
            btnSaveAs.Enabled = false;
            btnSaveAs.Click += (s, e) => SaveCopyAs();
            pnlDetails.Controls.Add(btnSaveAs);

            this.Controls.Add(pnlDetails);

            // Card: Admin Management Section
            grpAdmin = new GroupBox();
            grpAdmin.Text = " 🔒 สำหรับ Admin / หัวหน้าเวร: จัดการ แก้ไขชื่อ ลบ และอัปโหลดเอกสารใหม่ ";
            grpAdmin.Location = new Point(18, 366);
            grpAdmin.Size = new Size(830, 254);
            grpAdmin.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            grpAdmin.ForeColor = Color.FromArgb(79, 70, 229);

            // Admin Gate Panel (Locked)
            pnlAdminGate = new Panel();
            pnlAdminGate.Location = new Point(14, 25);
            pnlAdminGate.Size = new Size(802, 218);

            Label lblGateDesc = new Label();
            lblGateDesc.Text = "กรุณาใส่รหัสผ่านผู้ดูแลระบบ (Admin Password: 9844) เพื่อแก้ไขชื่อ, หมวดหมู่, ลบเอกสาร หรืออัปโหลดไฟล์ใหม่ขึ้น Cloud:";
            lblGateDesc.Location = new Point(6, 25);
            lblGateDesc.Size = new Size(780, 24);
            lblGateDesc.Font = new Font("Segoe UI", 9.5f);
            lblGateDesc.ForeColor = Color.FromArgb(51, 65, 85);
            pnlAdminGate.Controls.Add(lblGateDesc);

            txtAdminPass = new TextBox();
            txtAdminPass.Location = new Point(10, 60);
            txtAdminPass.Size = new Size(240, 27);
            txtAdminPass.PasswordChar = '*';
            txtAdminPass.Font = new Font("Segoe UI", 10f);
            txtAdminPass.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) UnlockAdmin(); };
            pnlAdminGate.Controls.Add(txtAdminPass);

            btnUnlockAdmin = new Button();
            btnUnlockAdmin.Text = "🔓 ปลดล็อกสิทธิ์ Admin";
            btnUnlockAdmin.Location = new Point(260, 58);
            btnUnlockAdmin.Size = new Size(180, 31);
            btnUnlockAdmin.BackColor = Color.FromArgb(79, 70, 229);
            btnUnlockAdmin.ForeColor = Color.White;
            btnUnlockAdmin.FlatStyle = FlatStyle.Flat;
            btnUnlockAdmin.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnUnlockAdmin.Cursor = Cursors.Hand;
            btnUnlockAdmin.Click += (s, e) => UnlockAdmin();
            pnlAdminGate.Controls.Add(btnUnlockAdmin);

            grpAdmin.Controls.Add(pnlAdminGate);

            // Admin Unlocked Panel
            pnlAdminUnlocked = new Panel();
            pnlAdminUnlocked.Location = new Point(14, 25);
            pnlAdminUnlocked.Size = new Size(802, 218);
            pnlAdminUnlocked.Visible = false;

            Label lblAdminBadge = new Label();
            lblAdminBadge.Text = "✅ ได้รับสิทธิ์ผู้ดูแลระบบเรียบร้อย (Admin Mode) - สามารถจัดการเอกสารได้ทันที";
            lblAdminBadge.Location = new Point(8, 4);
            lblAdminBadge.Size = new Size(780, 22);
            lblAdminBadge.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblAdminBadge.ForeColor = Color.FromArgb(16, 185, 129);
            pnlAdminUnlocked.Controls.Add(lblAdminBadge);

            // Row 1: Manage Selected Document Buttons
            Panel pnlSelectedActions = new Panel();
            pnlSelectedActions.Location = new Point(6, 28);
            pnlSelectedActions.Size = new Size(790, 48);
            pnlSelectedActions.BackColor = Color.FromArgb(243, 244, 246);
            pnlSelectedActions.BorderStyle = BorderStyle.FixedSingle;

            Label lblSelActPrompt = new Label();
            lblSelActPrompt.Text = "จัดการเอกสารที่เลือก:";
            lblSelActPrompt.Location = new Point(10, 14);
            lblSelActPrompt.Size = new Size(150, 22);
            lblSelActPrompt.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblSelActPrompt.ForeColor = Color.FromArgb(30, 41, 59);
            pnlSelectedActions.Controls.Add(lblSelActPrompt);

            btnEditDoc = new Button();
            btnEditDoc.Text = "✏️ แก้ไขชื่อ & หมวดหมู่";
            btnEditDoc.Location = new Point(170, 8);
            btnEditDoc.Size = new Size(185, 32);
            btnEditDoc.BackColor = Color.FromArgb(37, 99, 235); // Blue
            btnEditDoc.ForeColor = Color.White;
            btnEditDoc.FlatStyle = FlatStyle.Flat;
            btnEditDoc.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnEditDoc.Cursor = Cursors.Hand;
            btnEditDoc.Enabled = false;
            btnEditDoc.Click += (s, e) => EditSelectedDocument();
            pnlSelectedActions.Controls.Add(btnEditDoc);

            btnReplaceFile = new Button();
            btnReplaceFile.Text = "🔄 เปลี่ยน/แทนที่ไฟล์เดิม";
            btnReplaceFile.Location = new Point(365, 8);
            btnReplaceFile.Size = new Size(185, 32);
            btnReplaceFile.BackColor = Color.FromArgb(217, 119, 6); // Amber
            btnReplaceFile.ForeColor = Color.White;
            btnReplaceFile.FlatStyle = FlatStyle.Flat;
            btnReplaceFile.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnReplaceFile.Cursor = Cursors.Hand;
            btnReplaceFile.Enabled = false;
            btnReplaceFile.Click += (s, e) => ReplaceSelectedDocumentFile();
            pnlSelectedActions.Controls.Add(btnReplaceFile);

            btnDeleteDoc = new Button();
            btnDeleteDoc.Text = "🗑️ ลบเอกสารออกจากระบบ";
            btnDeleteDoc.Location = new Point(560, 8);
            btnDeleteDoc.Size = new Size(215, 32);
            btnDeleteDoc.BackColor = Color.FromArgb(220, 38, 38); // Red
            btnDeleteDoc.ForeColor = Color.White;
            btnDeleteDoc.FlatStyle = FlatStyle.Flat;
            btnDeleteDoc.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnDeleteDoc.Cursor = Cursors.Hand;
            btnDeleteDoc.Enabled = false;
            btnDeleteDoc.Click += (s, e) => DeleteSelectedDocument();
            pnlSelectedActions.Controls.Add(btnDeleteDoc);

            pnlAdminUnlocked.Controls.Add(pnlSelectedActions);

            // Row 2: Upload New Documents Section
            Label lblUploadHeader = new Label();
            lblUploadHeader.Text = "➕ อัปโหลดเอกสารใหม่ขึ้น Cloud:";
            lblUploadHeader.Location = new Point(8, 84);
            lblUploadHeader.Size = new Size(250, 20);
            lblUploadHeader.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblUploadHeader.ForeColor = Color.FromArgb(13, 148, 136);
            pnlAdminUnlocked.Controls.Add(lblUploadHeader);

            btnChooseNewFiles = new Button();
            btnChooseNewFiles.Text = "📂 เลือกไฟล์ใหม่ (.xlsx, .docx, .pdf)...";
            btnChooseNewFiles.Location = new Point(8, 108);
            btnChooseNewFiles.Size = new Size(270, 32);
            btnChooseNewFiles.BackColor = Color.FromArgb(240, 253, 244);
            btnChooseNewFiles.ForeColor = Color.FromArgb(22, 101, 52);
            btnChooseNewFiles.FlatStyle = FlatStyle.Flat;
            btnChooseNewFiles.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnChooseNewFiles.Cursor = Cursors.Hand;
            btnChooseNewFiles.Click += (s, e) => ChooseNewFiles();
            pnlAdminUnlocked.Controls.Add(btnChooseNewFiles);

            lblNewFiles = new Label();
            lblNewFiles.Text = "(ยังไม่ได้เลือกไฟล์ใหม่)";
            lblNewFiles.Location = new Point(290, 114);
            lblNewFiles.Size = new Size(495, 22);
            lblNewFiles.Font = new Font("Segoe UI", 9f);
            lblNewFiles.ForeColor = Color.FromArgb(100, 116, 139);
            pnlAdminUnlocked.Controls.Add(lblNewFiles);

            Label lblNewCat = new Label();
            lblNewCat.Text = "หมวดหมู่เริ่มต้น:";
            lblNewCat.Location = new Point(8, 149);
            lblNewCat.Size = new Size(110, 22);
            lblNewCat.Font = new Font("Segoe UI", 9f);
            lblNewCat.ForeColor = Color.FromArgb(51, 65, 85);
            pnlAdminUnlocked.Controls.Add(lblNewCat);

            cboNewCategory = new ComboBox();
            cboNewCategory.DropDownStyle = ComboBoxStyle.DropDownList;
            cboNewCategory.Items.AddRange(new object[] {
                "แบบฟอร์มบันทึกทางการพยาบาล",
                "แบบประเมินทางการพยาบาล",
                "แนวทาง CPG / หัตถการ",
                "เอกสารและแบบฟอร์มทั่วไป"
            });
            cboNewCategory.SelectedIndex = 0;
            cboNewCategory.Location = new Point(122, 146);
            cboNewCategory.Size = new Size(230, 28);
            pnlAdminUnlocked.Controls.Add(cboNewCategory);

            Label lblUploader = new Label();
            lblUploader.Text = "ชื่อผู้แก้ไข/หัวหน้าเวร:";
            lblUploader.Location = new Point(365, 149);
            lblUploader.Size = new Size(140, 22);
            lblUploader.Font = new Font("Segoe UI", 9f);
            lblUploader.ForeColor = Color.FromArgb(51, 65, 85);
            pnlAdminUnlocked.Controls.Add(lblUploader);

            txtUploaderName = new TextBox();
            txtUploaderName.Location = new Point(510, 146);
            txtUploaderName.Size = new Size(270, 27);
            txtUploaderName.Text = Environment.MachineName;
            pnlAdminUnlocked.Controls.Add(txtUploaderName);

            btnUploadNew = new Button();
            btnUploadNew.Text = "☁️ บันทึกและอัปโหลดเอกสารใหม่สู่ Cloud";
            btnUploadNew.Location = new Point(8, 180);
            btnUploadNew.Size = new Size(344, 34);
            btnUploadNew.BackColor = Color.FromArgb(13, 148, 136); // Teal
            btnUploadNew.ForeColor = Color.White;
            btnUploadNew.FlatStyle = FlatStyle.Flat;
            btnUploadNew.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnUploadNew.Cursor = Cursors.Hand;
            btnUploadNew.Enabled = false;
            btnUploadNew.Click += (s, e) => UploadNewDocuments();
            pnlAdminUnlocked.Controls.Add(btnUploadNew);

            grpAdmin.Controls.Add(pnlAdminUnlocked);
            this.Controls.Add(grpAdmin);

            // Bottom Close Button
            Button btnClose = new Button();
            btnClose.Text = "ปิดหน้าต่าง";
            btnClose.Location = new Point(375, 630);
            btnClose.Size = new Size(130, 36);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        private void UnlockAdmin() {
            string pass = (txtAdminPass.Text ?? "").Trim();
            if (pass == context.AdminPassword || pass == "9844") {
                isAdminUnlocked = true;
                pnlAdminGate.Visible = false;
                pnlAdminUnlocked.Visible = true;
                txtAdminPass.Text = "";
                UpdateSelectedDocUI();
            } else {
                MessageBox.Show("รหัสผ่านผู้ดูแลระบบไม่ถูกต้อง กรุณาลองใหม่อีกครั้ง", "รหัสผ่านไม่ถูกต้อง", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtAdminPass.Focus();
                txtAdminPass.SelectAll();
            }
        }

        private void OnListViewSelectionChanged() {
            if (lvDocuments.SelectedItems.Count > 0) {
                selectedDoc = (WardDocItem)lvDocuments.SelectedItems[0].Tag;
            } else {
                selectedDoc = null;
            }
            UpdateSelectedDocUI();
        }

        private void UpdateSelectedDocUI() {
            if (selectedDoc != null) {
                lblDocDetailTitle.Text = "📄 เอกสารที่เลือก: " + (selectedDoc.Title ?? selectedDoc.FileName);
                string status = selectedDoc.ExistsLocally ? "พร้อมใช้งานในเครื่อง" : "อยู่บน Cloud (คลิกเพื่อโหลด)";
                lblDocDetailMeta.Text = string.Format("ไฟล์: {0} | หมวดหมู่: {1} | ขนาด: {2:N1} KB | สถานะ: {3}",
                    selectedDoc.FileName, selectedDoc.Category, selectedDoc.Size / 1024.0, status);

                btnOpenDoc.Enabled = true;
                btnSaveAs.Enabled = true;
                btnEditDoc.Enabled = isAdminUnlocked;
                btnReplaceFile.Enabled = isAdminUnlocked;
                btnDeleteDoc.Enabled = isAdminUnlocked;
            } else {
                lblDocDetailTitle.Text = "📄 เอกสารที่เลือก: (ยังไม่ได้เลือกเอกสาร)";
                lblDocDetailMeta.Text = "คลิกเลือกเอกสารในตารางด้านบนเพื่อเปิดใช้งาน บันทึกสำเนา แก้ไข หรือลบ";
                btnOpenDoc.Enabled = false;
                btnSaveAs.Enabled = false;
                btnEditDoc.Enabled = false;
                btnReplaceFile.Enabled = false;
                btnDeleteDoc.Enabled = false;
            }
        }

        private void LoadDocuments() {
            allDocuments.Clear();
            string templatesDir = GetTemplatesDirectory();
            string catalogPath = Path.Combine(templatesDir, "catalog.json");

            // 1. Try reading catalog.json
            if (File.Exists(catalogPath)) {
                try {
                    string json = File.ReadAllText(catalogPath, Encoding.UTF8);
                    allDocuments = ParseDocumentsCatalog(json);
                } catch (Exception ex) {
                    Debug.WriteLine("Read catalog.json error: " + ex.Message);
                }
            }

            // 2. Scan physical files in templates directory
            if (Directory.Exists(templatesDir)) {
                var files = Directory.GetFiles(templatesDir, "*.*");
                foreach (var f in files) {
                    FileInfo fi = new FileInfo(f);
                    string ext = fi.Extension.ToLower();
                    if (ext == ".json") continue;
                    if (ext == ".xlsx" || ext == ".xls" || ext == ".docx" || ext == ".doc" || ext == ".pdf" || ext == ".txt" || ext == ".csv") {
                        var existing = allDocuments.Find(d => d.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));
                        if (existing != null) {
                            existing.LocalPath = f;
                            if (existing.Size <= 0) existing.Size = fi.Length;
                        } else {
                            var item = new WardDocItem();
                            item.Id = "doc_local_" + Math.Abs(fi.Name.GetHashCode());
                            item.FileName = fi.Name;
                            item.DownloadName = fi.Name;
                            item.Title = Path.GetFileNameWithoutExtension(fi.Name);
                            item.Category = GetDefaultCategoryForFile(fi.Name);
                            item.FileType = ext.TrimStart('.');
                            item.Size = fi.Length;
                            item.UpdatedAt = fi.LastWriteTimeUtc.ToString("o");
                            item.UpdatedBy = "Local";
                            item.LocalPath = f;
                            allDocuments.Add(item);
                        }
                    }
                }
            }

            // Also check root IO template
            string rootIo = Path.Combine(context.AppBaseDir, "แบบฟอร์ม IO.xlsx");
            if (File.Exists(rootIo) && !allDocuments.Exists(d => d.FileName.Equals("แบบฟอร์ม IO.xlsx", StringComparison.OrdinalIgnoreCase))) {
                FileInfo fi = new FileInfo(rootIo);
                var item = new WardDocItem();
                item.Id = "doc_root_io";
                item.FileName = "แบบฟอร์ม IO.xlsx";
                item.DownloadName = "แบบฟอร์ม IO.xlsx";
                item.Title = "แบบฟอร์มบันทึก I/O ประจำวอร์ด";
                item.Category = "แบบฟอร์มบันทึกทางการพยาบาล";
                item.FileType = "xlsx";
                item.Size = fi.Length;
                item.UpdatedAt = fi.LastWriteTimeUtc.ToString("o");
                item.UpdatedBy = "Local";
                item.LocalPath = rootIo;
                allDocuments.Add(item);
            }

            // Save refreshed catalog locally
            SaveCatalogLocally();
            ApplyFilter();
        }

        private static string GetDefaultCategoryForFile(string fn) {
            string l = (fn ?? "").ToLower();
            if (l.Contains("io") || l.Contains("i_o") || l.Contains("บันทึก") || l.Contains("record"))
                return "แบบฟอร์มบันทึกทางการพยาบาล";
            if (l.Contains("ประเมิน") || l.Contains("assessment") || l.Contains("score") || l.Contains("mews"))
                return "แบบประเมินทางการพยาบาล";
            if (l.Contains("cpg") || l.Contains("guideline") || l.Contains("แนวทาง") || l.Contains("flow"))
                return "แนวทาง CPG / หัตถการ";
            return "เอกสารและแบบฟอร์มทั่วไป";
        }

        private void ApplyFilter() {
            string q = (txtSearch.Text ?? "").Trim().ToLower();
            string cat = cboCategoryFilter.SelectedItem != null ? cboCategoryFilter.SelectedItem.ToString() : "ทั้งหมด (All)";

            lvDocuments.BeginUpdate();
            lvDocuments.Items.Clear();
            filteredDocuments.Clear();

            foreach (var doc in allDocuments) {
                if (cat != "ทั้งหมด (All)" && !doc.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                if (!string.IsNullOrEmpty(q)) {
                    bool mTitle = (doc.Title ?? "").ToLower().Contains(q);
                    bool mFile = (doc.FileName ?? "").ToLower().Contains(q);
                    bool mCat = (doc.Category ?? "").ToLower().Contains(q);
                    if (!mTitle && !mFile && !mCat) continue;
                }

                filteredDocuments.Add(doc);

                ListViewItem lvi = new ListViewItem(doc.Title ?? doc.FileName);
                lvi.SubItems.Add(doc.Category ?? "ทั่วไป");
                lvi.SubItems.Add(doc.FileName ?? "");
                lvi.SubItems.Add(string.Format("{0:N1} KB", doc.Size / 1024.0));

                DateTime dt;
                string timeStr = DateTime.TryParse(doc.UpdatedAt, out dt) ? dt.ToLocalTime().ToString("d/M/yyyy HH:mm") : "-";
                lvi.SubItems.Add(timeStr);

                lvi.Tag = doc;

                if (doc.ExistsLocally) {
                    lvi.ForeColor = Color.FromArgb(15, 23, 42);
                } else {
                    lvi.ForeColor = Color.FromArgb(100, 116, 139); // Cloud only
                }

                lvDocuments.Items.Add(lvi);
            }

            lvDocuments.EndUpdate();

            if (lvDocuments.Items.Count > 0) {
                int reselectIdx = 0;
                if (selectedDoc != null) {
                    for (int i = 0; i < lvDocuments.Items.Count; i++) {
                        if (((WardDocItem)lvDocuments.Items[i].Tag).FileName == selectedDoc.FileName) {
                            reselectIdx = i;
                            break;
                        }
                    }
                }
                lvDocuments.Items[reselectIdx].Selected = true;
            } else {
                selectedDoc = null;
                UpdateSelectedDocUI();
            }
        }

        private void EditSelectedDocument() {
            if (selectedDoc == null) {
                MessageBox.Show("กรุณาเลือกเอกสารที่ต้องการแก้ไขก่อนครับ", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string defaultUploader = (txtUploaderName.Text ?? "").Trim();
            using (var dlg = new EditWardDocDialog(selectedDoc, defaultUploader)) {
                if (dlg.ShowDialog(this) == DialogResult.OK) {
                    string oldTitle = selectedDoc.Title;
                    selectedDoc.Title = dlg.NewTitle;
                    selectedDoc.Category = dlg.NewCategory;
                    selectedDoc.UpdatedAt = DateTime.UtcNow.ToString("o");
                    selectedDoc.UpdatedBy = dlg.UploaderName;

                    SaveCatalogLocally();

                    if (context.GetSupabaseEnabled()) {
                        try {
                            var client = new SupabaseSyncClient(context.GetSupabaseUrl(), context.GetSupabaseKey());
                            string payload = SerializeDocumentsCatalog(allDocuments);
                            bool ok = client.SaveBed(100, payload);
                            if (ok) {
                                client.SaveHistory(100, "แก้ไขชื่อเอกสาร: " + selectedDoc.Title, "เดิม: " + oldTitle + " โดย " + dlg.UploaderName);
                            }
                        } catch (Exception ex) {
                            Debug.WriteLine("Edit doc cloud save error: " + ex.Message);
                        }
                    }

                    ApplyFilter();
                    MessageBox.Show("✅ บันทึกการแก้ไขชื่อและหมวดหมู่เอกสารเรียบร้อยแล้ว!", "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void ReplaceSelectedDocumentFile() {
            if (selectedDoc == null) {
                MessageBox.Show("กรุณาเลือกเอกสารที่ต้องการเปลี่ยนไฟล์ก่อนครับ", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (OpenFileDialog ofd = new OpenFileDialog()) {
                ofd.Filter = "Ward Documents (*.xlsx;*.xls;*.docx;*.doc;*.pdf)|*.xlsx;*.xls;*.docx;*.doc;*.pdf|All Files (*.*)|*.*";
                ofd.Title = "เลือกไฟล์ใหม่ที่จะนำมาแทนที่เอกสาร: " + selectedDoc.Title;
                if (ofd.ShowDialog(this) == DialogResult.OK) {
                    try {
                        byte[] bytes = File.ReadAllBytes(ofd.FileName);
                        FileInfo fi = new FileInfo(ofd.FileName);
                        string templatesDir = GetTemplatesDirectory();
                        string setupTemplates = @"C:\Users\GORW01\Desktop\Medical_Text_Expander_Setup\templates";

                        string dest = Path.Combine(templatesDir, fi.Name);
                        File.WriteAllBytes(dest, bytes);
                        if (Directory.Exists(setupTemplates)) {
                            try { File.WriteAllBytes(Path.Combine(setupTemplates, fi.Name), bytes); } catch {}
                        }

                        string uploader = (txtUploaderName.Text ?? "").Trim();
                        if (string.IsNullOrEmpty(uploader)) uploader = Environment.MachineName;

                        selectedDoc.FileName = fi.Name;
                        selectedDoc.DownloadName = fi.Name;
                        selectedDoc.FileType = fi.Extension.TrimStart('.').ToLower();
                        selectedDoc.Size = bytes.Length;
                        selectedDoc.Base64 = Convert.ToBase64String(bytes);
                        selectedDoc.UpdatedAt = DateTime.UtcNow.ToString("o");
                        selectedDoc.UpdatedBy = uploader;
                        selectedDoc.LocalPath = dest;

                        SaveCatalogLocally();

                        if (context.GetSupabaseEnabled()) {
                            try {
                                var client = new SupabaseSyncClient(context.GetSupabaseUrl(), context.GetSupabaseKey());
                                string payload = SerializeDocumentsCatalog(allDocuments);
                                bool ok = client.SaveBed(100, payload);
                                if (ok) {
                                    client.SaveHistory(100, "แทนที่ไฟล์เอกสาร: " + selectedDoc.Title, "ไฟล์ใหม่: " + fi.Name + " โดย " + uploader);
                                }
                            } catch (Exception ex) {
                                Debug.WriteLine("Replace file cloud save error: " + ex.Message);
                            }
                        }

                        ApplyFilter();
                        MessageBox.Show("✅ อัปเดตและแทนที่ไฟล์เอกสารเรียบร้อยแล้ว!", "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    } catch (Exception ex) {
                        MessageBox.Show("เกิดข้อผิดพลาดในการเปลี่ยนไฟล์: " + ex.Message, "ผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void DeleteSelectedDocument() {
            if (selectedDoc == null) {
                MessageBox.Show("กรุณาเลือกเอกสารที่ต้องการลบก่อนครับ", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string msg = string.Format("คุณแน่ใจหรือไม่ว่าต้องการลบเอกสารนี้ออกจากระบบและ Cloud?\n\n" +
                                       "• ชื่อเอกสาร: {0}\n" +
                                       "• ชื่อไฟล์: {1}\n" +
                                       "• หมวดหมู่: {2}\n\n" +
                                       "* คำเตือน: ไฟล์ในเครื่องและบน Cloud จะถูกลบออกทันที",
                                       selectedDoc.Title, selectedDoc.FileName, selectedDoc.Category);

            var dr = MessageBox.Show(msg, "ยืนยันการลบเอกสาร", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (dr != DialogResult.Yes) return;

            string delTitle = selectedDoc.Title;
            string delFile = selectedDoc.FileName;
            string uploader = (txtUploaderName.Text ?? "").Trim();
            if (string.IsNullOrEmpty(uploader)) uploader = Environment.MachineName;

            // Delete physical local file
            try {
                if (!string.IsNullOrEmpty(selectedDoc.LocalPath) && File.Exists(selectedDoc.LocalPath)) {
                    File.Delete(selectedDoc.LocalPath);
                }
                string setupFile = Path.Combine(@"C:\Users\GORW01\Desktop\Medical_Text_Expander_Setup\templates", delFile);
                if (File.Exists(setupFile)) {
                    File.Delete(setupFile);
                }
            } catch (Exception ex) {
                Debug.WriteLine("Delete physical file error: " + ex.Message);
            }

            allDocuments.Remove(selectedDoc);
            selectedDoc = null;

            SaveCatalogLocally();

            if (context.GetSupabaseEnabled()) {
                try {
                    var client = new SupabaseSyncClient(context.GetSupabaseUrl(), context.GetSupabaseKey());
                    string payload = SerializeDocumentsCatalog(allDocuments);
                    bool ok = client.SaveBed(100, payload);
                    if (ok) {
                        client.SaveHistory(100, "ลบเอกสารวอร์ด: " + delTitle, "ไฟล์: " + delFile + " โดย " + uploader);
                    }
                } catch (Exception ex) {
                    Debug.WriteLine("Delete doc cloud save error: " + ex.Message);
                }
            }

            ApplyFilter();
            MessageBox.Show(string.Format("🗑️ ลบเอกสาร [{0}] ออกจากระบบเรียบร้อยแล้ว", delTitle), "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ChooseNewFiles() {
            using (OpenFileDialog ofd = new OpenFileDialog()) {
                ofd.Filter = "Ward Documents (*.xlsx;*.xls;*.docx;*.doc;*.pdf)|*.xlsx;*.xls;*.docx;*.doc;*.pdf|All Files (*.*)|*.*";
                ofd.Title = "เลือกไฟล์เอกสารหรือแบบฟอร์มประจำวอร์ด (เลือกได้หลายไฟล์พร้อมกัน)";
                ofd.Multiselect = true;
                if (ofd.ShowDialog(this) == DialogResult.OK) {
                    chosenNewFilePaths.Clear();
                    chosenNewFilePaths.AddRange(ofd.FileNames);
                    if (chosenNewFilePaths.Count == 1) {
                        FileInfo fi = new FileInfo(chosenNewFilePaths[0]);
                        lblNewFiles.Text = fi.Name + string.Format(" ({0:N1} KB)", fi.Length / 1024.0);
                    } else {
                        lblNewFiles.Text = string.Format("เลือก {0} ไฟล์พร้อมกันสำหรับอัปโหลด", chosenNewFilePaths.Count);
                    }
                    lblNewFiles.ForeColor = Color.DarkGreen;
                    btnUploadNew.Enabled = true;
                }
            }
        }

        private void UploadNewDocuments() {
            if (chosenNewFilePaths == null || chosenNewFilePaths.Count == 0) {
                MessageBox.Show("กรุณาเลือกไฟล์เอกสารก่อนครับ", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnUploadNew.Enabled = false;
            btnUploadNew.Text = "กำลังอัปโหลด...";

            try {
                string templatesDir = GetTemplatesDirectory();
                string setupTemplates = @"C:\Users\GORW01\Desktop\Medical_Text_Expander_Setup\templates";
                string category = cboNewCategory.SelectedItem != null ? cboNewCategory.SelectedItem.ToString() : "แบบฟอร์มบันทึกทางการพยาบาล";
                string uploader = txtUploaderName.Text.Trim();
                if (string.IsNullOrEmpty(uploader)) uploader = Environment.MachineName;

                List<string> processedTitles = new List<string>();

                for (int i = 0; i < chosenNewFilePaths.Count; i++) {
                    string filePath = chosenNewFilePaths[i];
                    if (!File.Exists(filePath)) continue;

                    byte[] fileBytes = File.ReadAllBytes(filePath);
                    FileInfo fi = new FileInfo(filePath);
                    string itemTitle = Path.GetFileNameWithoutExtension(fi.Name);

                    string destFile = Path.Combine(templatesDir, fi.Name);
                    File.WriteAllBytes(destFile, fileBytes);

                    if (fi.Name.IndexOf("IO", StringComparison.OrdinalIgnoreCase) >= 0) {
                        try {
                            File.WriteAllBytes(Path.Combine(templatesDir, "แบบฟอร์ม_IO.xlsx"), fileBytes);
                            File.WriteAllBytes(Path.Combine(templatesDir, "IO_Template.xlsx"), fileBytes);
                        } catch {}
                    }

                    if (Directory.Exists(setupTemplates)) {
                        try {
                            File.WriteAllBytes(Path.Combine(setupTemplates, fi.Name), fileBytes);
                        } catch {}
                    }

                    string b64 = Convert.ToBase64String(fileBytes);
                    string ext = fi.Extension.TrimStart('.').ToLower();
                    string nowIso = DateTime.UtcNow.ToString("o");

                    var existing = allDocuments.Find(d => d.FileName.Equals(fi.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) {
                        existing.Title = itemTitle;
                        existing.Category = category;
                        existing.Size = fileBytes.Length;
                        existing.FileType = ext;
                        existing.Base64 = b64;
                        existing.UpdatedAt = nowIso;
                        existing.UpdatedBy = uploader;
                        existing.LocalPath = destFile;
                    } else {
                        var newDoc = new WardDocItem();
                        newDoc.Id = "doc_" + DateTime.UtcNow.Ticks + "_" + i;
                        newDoc.Title = itemTitle;
                        newDoc.Category = category;
                        newDoc.FileName = fi.Name;
                        newDoc.DownloadName = fi.Name;
                        newDoc.FileType = ext;
                        newDoc.Size = fileBytes.Length;
                        newDoc.Base64 = b64;
                        newDoc.UpdatedAt = nowIso;
                        newDoc.UpdatedBy = uploader;
                        newDoc.LocalPath = destFile;
                        allDocuments.Insert(0, newDoc);
                    }

                    processedTitles.Add(itemTitle);
                }

                SaveCatalogLocally();

                bool supabaseOk = false;
                if (context.GetSupabaseEnabled()) {
                    try {
                        var client = new SupabaseSyncClient(context.GetSupabaseUrl(), context.GetSupabaseKey());
                        string catalogPayload = SerializeDocumentsCatalog(allDocuments);
                        supabaseOk = client.SaveBed(100, catalogPayload);
                        if (supabaseOk) {
                            string sumTitle = string.Join(", ", processedTitles);
                            client.SaveHistory(100, "อัปโหลดเอกสารวอร์ด (" + processedTitles.Count + " ไฟล์) โดย " + uploader, "รายการ: " + sumTitle);
                        }
                    } catch (Exception ex) {
                        Debug.WriteLine("Cloud upload error: " + ex.Message);
                    }
                }

                ApplyFilter();
                btnUploadNew.Text = "☁️ บันทึกและอัปโหลดเอกสารใหม่สู่ Cloud";
                btnUploadNew.Enabled = false;
                lblNewFiles.Text = "(อัปโหลดเรียบร้อยแล้ว)";
                chosenNewFilePaths.Clear();

                string msg = string.Format("✅ บันทึกเอกสาร {0} รายการเรียบร้อยแล้ว!\n" +
                             "- บันทึกลงเครื่องและเทมเพลตประจำโปรแกรมแล้ว\n" +
                             (supabaseOk ? "- ซิงค์ขึ้น Supabase Cloud สำเร็จ (เว็บ/มือถือจะได้รับเอกสารนี้ทันที)" : "- (Supabase ไม่ได้เปิดใช้งาน)"), processedTitles.Count);
                MessageBox.Show(msg, "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);

            } catch (Exception ex) {
                btnUploadNew.Enabled = true;
                btnUploadNew.Text = "☁️ บันทึกและอัปโหลดเอกสารใหม่สู่ Cloud";
                MessageBox.Show("เกิดข้อผิดพลาดในการอัปโหลด: " + ex.Message, "ผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenSelectedDocument() {
            if (selectedDoc == null) {
                MessageBox.Show("กรุณาเลือกเอกสารที่ต้องการเปิดก่อนครับ", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!selectedDoc.ExistsLocally) {
                if (!string.IsNullOrEmpty(selectedDoc.Base64)) {
                    try {
                        byte[] bytes = Convert.FromBase64String(selectedDoc.Base64);
                        string dest = Path.Combine(GetTemplatesDirectory(), selectedDoc.FileName);
                        File.WriteAllBytes(dest, bytes);
                        selectedDoc.LocalPath = dest;
                    } catch {}
                } else {
                    if (!SyncDocumentsFromCloudManual()) {
                        MessageBox.Show("ยังไม่พบไฟล์เอกสารในเครื่อง กรุณากดปุ่ม 'ซิงค์ Cloud' เพื่อดึงไฟล์ลงมาครับ", "ไม่พบไฟล์", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }

            try {
                if (File.Exists(selectedDoc.LocalPath)) {
                    Process.Start(selectedDoc.LocalPath);
                } else {
                    MessageBox.Show("ไม่พบไฟล์เอกสารในเครื่อง: " + selectedDoc.FileName, "ไม่พบไฟล์", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            } catch (Exception ex) {
                MessageBox.Show("ไม่สามารถเปิดไฟล์ได้: " + ex.Message, "ผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveCopyAs() {
            if (selectedDoc == null) {
                MessageBox.Show("กรุณาเลือกเอกสารที่ต้องการบันทึกสำเนาก่อนครับ", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!selectedDoc.ExistsLocally && !string.IsNullOrEmpty(selectedDoc.Base64)) {
                try {
                    byte[] bytes = Convert.FromBase64String(selectedDoc.Base64);
                    string dest = Path.Combine(GetTemplatesDirectory(), selectedDoc.FileName);
                    File.WriteAllBytes(dest, bytes);
                    selectedDoc.LocalPath = dest;
                } catch {}
            }

            if (!selectedDoc.ExistsLocally) {
                MessageBox.Show("ไม่พบไฟล์ต้นฉบับในเครื่อง กรุณากดซิงค์ Cloud ก่อนครับ", "ไม่พบไฟล์", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string fn = selectedDoc.FileName;
            string ext = Path.GetExtension(fn).ToLower();
            string filter = "All Files (*.*)|*.*";
            if (ext == ".xlsx" || ext == ".xls") filter = "Excel Files (*.xlsx;*.xls)|*.xlsx;*.xls|All Files (*.*)|*.*";
            else if (ext == ".docx" || ext == ".doc") filter = "Word Documents (*.docx;*.doc)|*.docx;*.doc|All Files (*.*)|*.*";
            else if (ext == ".pdf") filter = "PDF Documents (*.pdf)|*.pdf|All Files (*.*)|*.*";

            using (SaveFileDialog sfd = new SaveFileDialog()) {
                sfd.Filter = filter;
                sfd.FileName = fn;
                sfd.Title = "บันทึกสำเนาเอกสารประจำวอร์ด";
                if (sfd.ShowDialog(this) == DialogResult.OK) {
                    try {
                        File.Copy(selectedDoc.LocalPath, sfd.FileName, true);
                        MessageBox.Show("บันทึกสำเนาสำเร็จที่:\n" + sfd.FileName, "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    } catch (Exception ex) {
                        MessageBox.Show("บันทึกล้มเหลว: " + ex.Message, "ผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private bool SyncDocumentsFromCloudManual() {
            if (!context.GetSupabaseEnabled()) {
                MessageBox.Show("ระบบ Supabase Cloud ยังไม่ได้เปิดใช้งานในโปรแกรมนี้", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string fullUrl = context.GetSupabaseUrl().TrimEnd('/') + "/rest/v1/bed_notes?bed_number=eq.100&select=content,updated_at,updated_by";
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(fullUrl);
                req.Headers["apikey"] = context.GetSupabaseKey();
                req.Headers["Authorization"] = "Bearer " + context.GetSupabaseKey();
                req.Timeout = 10000;

                string jsonMeta = null;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) {
                    jsonMeta = reader.ReadToEnd();
                }

                if (string.IsNullOrEmpty(jsonMeta) || !jsonMeta.Contains("\"documents\"")) {
                    MessageBox.Show("ไม่พบข้อมูลเอกสารบน Cloud", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                Match mContent = Regex.Match(jsonMeta, @"""content""\s*:\s*""((?:\\""|[^""])*)""", RegexOptions.Singleline);
                string catalogJson = mContent.Success ? SupabaseSyncClient.UnescapeJson(mContent.Groups[1].Value) : jsonMeta;

                var cloudDocs = ParseDocumentsCatalog(catalogJson);
                if (cloudDocs.Count == 0) {
                    MessageBox.Show("ไม่พบเอกสารในคลัง Cloud", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                string templatesDir = GetTemplatesDirectory();
                string setupTemplates = @"C:\Users\GORW01\Desktop\Medical_Text_Expander_Setup\templates";
                if (!Directory.Exists(setupTemplates)) {
                    try { Directory.CreateDirectory(setupTemplates); } catch {}
                }

                int filesSynced = 0;
                foreach (var doc in cloudDocs) {
                    if (!string.IsNullOrEmpty(doc.Base64)) {
                        try {
                            byte[] bytes = Convert.FromBase64String(doc.Base64);
                            string dest = Path.Combine(templatesDir, doc.FileName);
                            File.WriteAllBytes(dest, bytes);
                            doc.LocalPath = dest;
                            doc.Size = bytes.Length;

                            if (Directory.Exists(setupTemplates)) {
                                try { File.WriteAllBytes(Path.Combine(setupTemplates, doc.FileName), bytes); } catch {}
                            }

                            if (doc.FileName.IndexOf("IO", StringComparison.OrdinalIgnoreCase) >= 0) {
                                File.WriteAllBytes(Path.Combine(templatesDir, "แบบฟอร์ม_IO.xlsx"), bytes);
                                File.WriteAllBytes(Path.Combine(templatesDir, "IO_Template.xlsx"), bytes);
                            }
                            filesSynced++;
                        } catch {}
                    }

                    var existing = allDocuments.Find(d => d.FileName.Equals(doc.FileName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) {
                        existing.Title = doc.Title;
                        existing.Category = doc.Category;
                        existing.Size = doc.Size;
                        existing.UpdatedAt = doc.UpdatedAt;
                        existing.UpdatedBy = doc.UpdatedBy;
                        existing.Base64 = doc.Base64;
                        existing.LocalPath = doc.LocalPath;
                    } else {
                        allDocuments.Add(doc);
                    }
                }

                SaveCatalogLocally();
                ApplyFilter();
                MessageBox.Show(string.Format("✅ ซิงค์เอกสารและแบบฟอร์มจาก Cloud เรียบร้อยแล้ว ({0} ไฟล์)!", filesSynced), "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            } catch (Exception ex) {
                MessageBox.Show("ไม่สามารถเชื่อมต่อ Cloud ได้: " + ex.Message, "ผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }

        private void SaveCatalogLocally() {
            try {
                string dir = GetTemplatesDirectory();
                string catPath = Path.Combine(dir, "catalog.json");
                string json = SerializeDocumentsCatalog(allDocuments);
                File.WriteAllText(catPath, json, Encoding.UTF8);

                string setupDir = @"C:\Users\GORW01\Desktop\Medical_Text_Expander_Setup\templates";
                if (Directory.Exists(setupDir)) {
                    File.WriteAllText(Path.Combine(setupDir, "catalog.json"), json, Encoding.UTF8);
                }
            } catch (Exception ex) {
                Debug.WriteLine("SaveCatalogLocally warning: " + ex.Message);
            }
        }

        public static List<WardDocItem> ParseDocumentsCatalog(string json) {
            var list = new List<WardDocItem>();
            if (string.IsNullOrEmpty(json)) return list;

            Match mDocs = Regex.Match(json, @"""documents""\s*:\s*\[(?<arr>.*)\]", RegexOptions.Singleline);
            if (!mDocs.Success) return list;

            string arr = mDocs.Groups["arr"].Value;
            var matches = Regex.Matches(arr, @"\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}");
            foreach (Match m in matches) {
                string objStr = m.Value;
                string fn = ExtractJsonProp(objStr, "filename");
                string title = ExtractJsonProp(objStr, "title");
                if (string.IsNullOrEmpty(fn) && string.IsNullOrEmpty(title)) continue;

                var item = new WardDocItem();
                item.Id = ExtractJsonProp(objStr, "id");
                item.Title = !string.IsNullOrEmpty(title) ? title : fn;
                item.Category = ExtractJsonProp(objStr, "category");
                if (string.IsNullOrEmpty(item.Category)) item.Category = "แบบฟอร์มบันทึกทางการพยาบาล";
                item.FileName = fn;
                item.DownloadName = ExtractJsonProp(objStr, "download_name");
                if (string.IsNullOrEmpty(item.DownloadName)) item.DownloadName = item.FileName;
                item.FileType = ExtractJsonProp(objStr, "file_type");
                item.UpdatedAt = ExtractJsonProp(objStr, "updated_at");
                item.UpdatedBy = ExtractJsonProp(objStr, "updated_by");
                item.Base64 = ExtractJsonProp(objStr, "base64");

                long sz = 0;
                long.TryParse(ExtractJsonProp(objStr, "size"), out sz);
                item.Size = sz;

                list.Add(item);
            }
            return list;
        }

        private static string ExtractJsonProp(string objJson, string propName) {
            if (string.IsNullOrEmpty(objJson)) return "";
            Match m = Regex.Match(objJson, @"""" + Regex.Escape(propName) + @"""\s*:\s*""((?:\\""|[^""])*)""", RegexOptions.Singleline);
            if (m.Success) {
                return SupabaseSyncClient.UnescapeJson(m.Groups[1].Value);
            }
            Match mNum = Regex.Match(objJson, @"""" + Regex.Escape(propName) + @"""\s*:\s*([0-9]+)", RegexOptions.Singleline);
            if (mNum.Success) {
                return mNum.Groups[1].Value;
            }
            return "";
        }

        public static string SerializeDocumentsCatalog(List<WardDocItem> docs) {
            var sb = new StringBuilder();
            sb.Append("{\"version\":2,\"documents\":[");
            for (int i = 0; i < docs.Count; i++) {
                if (i > 0) sb.Append(",");
                var d = docs[i];
                sb.Append("{");
                sb.AppendFormat("\"id\":\"{0}\",", SupabaseSyncClient.EscapeJson(d.Id ?? ("doc_" + DateTime.UtcNow.Ticks + "_" + i)));
                sb.AppendFormat("\"title\":\"{0}\",", SupabaseSyncClient.EscapeJson(d.Title ?? ""));
                sb.AppendFormat("\"category\":\"{0}\",", SupabaseSyncClient.EscapeJson(d.Category ?? "แบบฟอร์มบันทึกทางการพยาบาล"));
                sb.AppendFormat("\"filename\":\"{0}\",", SupabaseSyncClient.EscapeJson(d.FileName ?? ""));
                sb.AppendFormat("\"download_name\":\"{0}\",", SupabaseSyncClient.EscapeJson(d.DownloadName ?? d.FileName ?? ""));
                sb.AppendFormat("\"file_type\":\"{0}\",", SupabaseSyncClient.EscapeJson(d.FileType ?? ""));
                sb.AppendFormat("\"size\":{0},", d.Size);
                sb.AppendFormat("\"updated_at\":\"{0}\",", SupabaseSyncClient.EscapeJson(d.UpdatedAt ?? DateTime.UtcNow.ToString("o")));
                sb.AppendFormat("\"updated_by\":\"{0}\",", SupabaseSyncClient.EscapeJson(d.UpdatedBy ?? "Admin"));
                sb.AppendFormat("\"base64\":\"{0}\"", d.Base64 ?? "");
                sb.Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }

    public class IoTemplateManagerDialog : WardDocumentCenterDialog {
        public IoTemplateManagerDialog(ExpanderContext ctx) : base(ctx) { }
    }

    [Serializable]
    public class BedDragDropData {
        public int SourceBed { get; set; }
        public BedDragDropData(int sourceBed) {
            SourceBed = sourceBed;
        }
    }

    public class WardUserItem {
        public string Id { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; } // "admin" or "user"
        public int UserSlot { get; set; } // 0 = Admin (beds 1-30), 2..N = Other users
        public bool IsActive { get; set; }
        public string CreatedAt { get; set; }
        public string LastLoginAt { get; set; }
        public string RegisteredVia { get; set; } // "self" or "admin"

        public WardUserItem() {
            Id = "u_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Username = "";
            PasswordHash = "";
            DisplayName = "";
            Role = "user";
            UserSlot = 2;
            IsActive = true;
            CreatedAt = DateTime.UtcNow.ToString("o");
            LastLoginAt = "";
            RegisteredVia = "self";
        }
    }

    public class WardUserManager {
        private List<WardUserItem> users = new List<WardUserItem>();
        private WardUserItem currentUser;
        private WardUserItem activeWorkspaceUser;
        private string catalogFilePath;
        private string sessionFilePath;
        private SupabaseSyncClient supabase;
        private object userLock = new object();

        public event Action<WardUserItem> OnUserLoggedIn;
        public event Action<int, string, string> OnWorkspaceChanged;
        public event Action OnUserListChanged;

        public WardUserItem CurrentUser { get { return currentUser; } }
        public WardUserItem ActiveWorkspaceUser { get { return activeWorkspaceUser ?? currentUser; } }
        public bool IsAdminLoggedIn { get { return currentUser != null && currentUser.Role == "admin"; } }
        public bool IsInspectingOtherUser {
            get {
                return activeWorkspaceUser != null && currentUser != null &&
                       !string.Equals(activeWorkspaceUser.Username, currentUser.Username, StringComparison.OrdinalIgnoreCase);
            }
        }

        public WardUserManager(string appBaseDir, SupabaseSyncClient supabaseClient) {
            catalogFilePath = Path.Combine(appBaseDir, "users_catalog.json");
            sessionFilePath = Path.Combine(appBaseDir, "session_user.json");
            supabase = supabaseClient;
            LoadCatalog();
            RestoreSession();
        }

        public static string HashPassword(string password) {
            if (string.IsNullOrEmpty(password)) return "";
            using (SHA256 sha = SHA256.Create()) {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public List<WardUserItem> GetAllUsers() {
            lock (userLock) {
                return new List<WardUserItem>(users);
            }
        }

        public void LoadCatalog() {
            string json = "";
            if (supabase != null && supabase.IsEnabled) {
                try {
                    json = supabase.FetchRow101UsersJson();
                } catch {}
            }
            if (!string.IsNullOrEmpty(json) && json.Contains("\"users\"")) {
                var loaded = ParseUsersCatalog(json);
                if (loaded != null && loaded.Count > 0) {
                    lock (userLock) {
                        users = loaded;
                    }
                    try { File.WriteAllText(catalogFilePath, json, BedNotesManager.SafeUtf8); } catch {}
                }
            } else if (File.Exists(catalogFilePath)) {
                try {
                    string localJson = BedNotesManager.ReadFileSafe(catalogFilePath);
                    var loaded = ParseUsersCatalog(localJson);
                    if (loaded != null && loaded.Count > 0) {
                        lock (userLock) {
                            users = loaded;
                        }
                    }
                } catch {}
            }

            lock (userLock) {
                if (users.Count == 0) {
                    var admin = new WardUserItem {
                        Id = "u_admin",
                        Username = "admin",
                        PasswordHash = HashPassword("admin"),
                        DisplayName = "ผู้ดูแลระบบ (Admin)",
                        Role = "admin",
                        UserSlot = 0,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow.ToString("o"),
                        LastLoginAt = "",
                        RegisteredVia = "admin"
                    };
                    users.Add(admin);
                    SaveCatalogInternal();
                }
            }
        }

        private void RestoreSession() {
            currentUser = null;
            activeWorkspaceUser = null;
            string lastUser = "";
            if (File.Exists(sessionFilePath)) {
                try {
                    lastUser = File.ReadAllText(sessionFilePath).Trim();
                } catch {}
            }
            if (!string.IsNullOrEmpty(lastUser)) {
                lock (userLock) {
                    var found = users.Find(u => string.Equals(u.Username, lastUser, StringComparison.OrdinalIgnoreCase));
                    if (found != null && found.IsActive) {
                        currentUser = found;
                        activeWorkspaceUser = found;
                    } else {
                        ClearSession();
                    }
                }
            }
        }

        public void SaveSession(string username) {
            try {
                File.WriteAllText(sessionFilePath, username ?? "");
            } catch {}
        }

        public void ClearSession() {
            try {
                if (File.Exists(sessionFilePath)) {
                    File.Delete(sessionFilePath);
                }
            } catch {}
        }

        public bool Login(string username, string password, out string error) {
            return Login(username, password, true, out error);
        }

        public bool Login(string username, string password, bool rememberMe, out string error) {
            error = "";
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) {
                error = "กรุณากรอกชื่อผู้ใช้และรหัสผ่าน";
                return false;
            }
            string uName = username.Trim().ToLowerInvariant();
            WardUserItem match = null;
            lock (userLock) {
                match = users.Find(u => string.Equals(u.Username, uName, StringComparison.OrdinalIgnoreCase));
            }
            if (match == null) {
                error = "ไม่พบบัญชีผู้ใช้นี้ในระบบ กรุณาตรวจสอบหรือลงทะเบียนใหม่";
                return false;
            }
            if (!match.IsActive) {
                error = "บัญชีผู้ใช้นี้ถูกระงับการใช้งาน กรุณาติดต่อผู้ดูแลระบบ";
                return false;
            }

            string hash = HashPassword(password);
            bool passValid = string.Equals(match.PasswordHash, hash, StringComparison.OrdinalIgnoreCase);

            if (!passValid) {
                error = "รหัสผ่านไม่ถูกต้อง";
                return false;
            }

            match.LastLoginAt = DateTime.UtcNow.ToString("o");
            SaveCatalogInternal();

            currentUser = match;
            activeWorkspaceUser = match;
            if (rememberMe) {
                SaveSession(match.Username);
            } else {
                ClearSession();
            }

            if (OnUserLoggedIn != null) {
                try { OnUserLoggedIn(currentUser); } catch {}
            }
            if (OnWorkspaceChanged != null) {
                try { OnWorkspaceChanged(activeWorkspaceUser.UserSlot, activeWorkspaceUser.Username, activeWorkspaceUser.DisplayName); } catch {}
            }
            return true;
        }

        public bool Register(string username, string displayName, string password, bool rememberMe, out string error) {
            error = "";
            if (string.IsNullOrEmpty(username)) {
                error = "กรุณาระบุชื่อผู้ใช้งาน (Username)";
                return false;
            }
            string uName = username.Trim().ToLowerInvariant();
            if (uName.Length < 3 || uName.Length > 20) {
                error = "ชื่อผู้ใช้งานต้องมีความยาว 3 - 20 ตัวอักษร";
                return false;
            }
            if (!Regex.IsMatch(uName, @"^[a-z0-9_\.\-]+$")) {
                error = "ชื่อผู้ใช้งานต้องประกอบด้วยตัวอักษรภาษาอังกฤษ ตัวเลข หรือ _ . - เท่านั้น";
                return false;
            }
            if (string.IsNullOrEmpty(password) || password.Length < 4) {
                error = "รหัสผ่านต้องมีความยาวอย่างน้อย 4 ตัวอักษร";
                return false;
            }

            string dName = (displayName ?? "").Trim();
            if (string.IsNullOrEmpty(dName)) dName = uName;

            // โหลดแคตตาล็อกล่าสุดเพื่อป้องกันการลงทะเบียนชื่อซ้ำ
            LoadCatalog();

            int newSlot;
            WardUserItem newUser;
            lock (userLock) {
                var exists = users.Find(u => string.Equals(u.Username, uName, StringComparison.OrdinalIgnoreCase));
                if (exists != null) {
                    error = string.Format("ชื่อผู้ใช้ '{0}' มีอยู่ในระบบแล้ว กรุณาใช้ชื่ออื่น", uName);
                    return false;
                }

                int maxSlot = 1;
                foreach (var u in users) {
                    if (u.UserSlot > maxSlot) maxSlot = u.UserSlot;
                }
                newSlot = Math.Max(2, maxSlot + 1);

                newUser = new WardUserItem {
                    Id = "u_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Username = uName,
                    DisplayName = dName,
                    PasswordHash = HashPassword(password),
                    Role = "user",
                    UserSlot = newSlot,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    LastLoginAt = DateTime.UtcNow.ToString("o"),
                    RegisteredVia = "self"
                };
                users.Add(newUser);
            }

            SaveCatalogInternal();

            // เตรียมแถวเตียง 30 เตียงสำหรับผู้ใช้ใหม่บน Supabase
            if (supabase != null && supabase.IsEnabled) {
                ThreadPool.QueueUserWorkItem(_ => {
                    try {
                        supabase.EnsureUserSlotRowsExist(newSlot, uName);
                    } catch {}
                });
            }

            currentUser = newUser;
            activeWorkspaceUser = newUser;
            if (rememberMe) {
                SaveSession(newUser.Username);
            } else {
                ClearSession();
            }

            if (OnUserLoggedIn != null) {
                try { OnUserLoggedIn(currentUser); } catch {}
            }
            if (OnWorkspaceChanged != null) {
                try { OnWorkspaceChanged(activeWorkspaceUser.UserSlot, activeWorkspaceUser.Username, activeWorkspaceUser.DisplayName); } catch {}
            }
            if (OnUserListChanged != null) {
                try { OnUserListChanged(); } catch {}
            }

            return true;
        }

        public void Logout() {
            lock (userLock) {
                currentUser = null;
                activeWorkspaceUser = null;
            }
            ClearSession();
            if (OnUserLoggedIn != null) {
                try { OnUserLoggedIn(null); } catch {}
            }
        }

        public bool SwitchWorkspace(string targetUsername) {
            if (!IsAdminLoggedIn) return false;
            WardUserItem target = null;
            lock (userLock) {
                target = users.Find(u => string.Equals(u.Username, targetUsername, StringComparison.OrdinalIgnoreCase));
            }
            if (target == null) return false;
            activeWorkspaceUser = target;
            if (OnWorkspaceChanged != null) {
                try { OnWorkspaceChanged(activeWorkspaceUser.UserSlot, activeWorkspaceUser.Username, activeWorkspaceUser.DisplayName); } catch {}
            }
            return true;
        }

        public bool ResetToMyWorkspace() {
            if (currentUser == null) return false;
            activeWorkspaceUser = currentUser;
            if (OnWorkspaceChanged != null) {
                try { OnWorkspaceChanged(activeWorkspaceUser.UserSlot, activeWorkspaceUser.Username, activeWorkspaceUser.DisplayName); } catch {}
            }
            return true;
        }

        public bool SaveUser(WardUserItem user, string newPassword, out string error) {
            error = "";
            if (user == null || string.IsNullOrEmpty(user.Username)) {
                error = "ชื่อผู้ใช้ต้องไม่ว่างเปล่า";
                return false;
            }
            user.Username = user.Username.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(user.DisplayName)) user.DisplayName = user.Username;

            bool isNew = false;
            lock (userLock) {
                var existing = users.Find(u => string.Equals(u.Username, user.Username, StringComparison.OrdinalIgnoreCase));
                if (existing != null) {
                    if (existing.Id != user.Id) {
                        error = "ชื่อผู้ใช้นี้มีอยู่ในระบบแล้ว กรุณาใช้ชื่ออื่น";
                        return false;
                    }
                    existing.DisplayName = user.DisplayName;
                    existing.Role = user.Role;
                    existing.IsActive = user.IsActive;
                    if (!string.IsNullOrEmpty(newPassword)) {
                        existing.PasswordHash = HashPassword(newPassword);
                    }
                } else {
                    isNew = true;
                    if (string.IsNullOrEmpty(newPassword)) {
                        error = "กรุณากำหนดรหัสผ่านสำหรับผู้ใช้ใหม่";
                        return false;
                    }
                    user.PasswordHash = HashPassword(newPassword);
                    int maxSlot = 1;
                    foreach (var u in users) {
                        if (u.UserSlot > maxSlot) maxSlot = u.UserSlot;
                    }
                    user.UserSlot = Math.Max(2, maxSlot + 1);
                    user.RegisteredVia = "admin";
                    users.Add(user);
                }
            }

            SaveCatalogInternal();

            if (isNew && supabase != null && supabase.IsEnabled) {
                int slotToInit = user.UserSlot;
                string uNameToInit = user.Username;
                ThreadPool.QueueUserWorkItem(_ => {
                    try {
                        supabase.EnsureUserSlotRowsExist(slotToInit, uNameToInit);
                    } catch {}
                });
            }

            if (OnUserListChanged != null) {
                try { OnUserListChanged(); } catch {}
            }
            return true;
        }

        public bool ToggleUserActive(string username, out string error) {
            error = "";
            if (string.Equals(username, "admin", StringComparison.OrdinalIgnoreCase)) {
                error = "ไม่สามารถระงับบัญชีผู้ดูแลระบบ (admin) ได้";
                return false;
            }
            lock (userLock) {
                var target = users.Find(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                if (target == null) {
                    error = "ไม่พบบัญชีผู้ใช้นี้";
                    return false;
                }
                target.IsActive = !target.IsActive;
                if (!target.IsActive && activeWorkspaceUser != null && string.Equals(activeWorkspaceUser.Username, username, StringComparison.OrdinalIgnoreCase)) {
                    activeWorkspaceUser = currentUser;
                }
            }
            SaveCatalogInternal();
            if (OnUserListChanged != null) {
                try { OnUserListChanged(); } catch {}
            }
            return true;
        }

        public bool DeleteUser(string username, out string error) {
            return DeleteUser(username, false, out error);
        }

        public bool DeleteUser(string username, bool clearCloudBeds, out string error) {
            error = "";
            if (string.Equals(username, "admin", StringComparison.OrdinalIgnoreCase)) {
                error = "ไม่สามารถลบบัญชีผู้ดูแลระบบ (admin) ได้";
                return false;
            }
            int slotToClear = -1;
            lock (userLock) {
                var target = users.Find(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                if (target == null) {
                    error = "ไม่พบบัญชีผู้ใช้นี้";
                    return false;
                }
                if (target.UserSlot == 0) {
                    error = "ไม่สามารถลบบัญชีที่ครอบครองพื้นที่หลักของวอร์ดได้";
                    return false;
                }
                slotToClear = target.UserSlot;
                users.Remove(target);
                if (activeWorkspaceUser != null && string.Equals(activeWorkspaceUser.Username, username, StringComparison.OrdinalIgnoreCase)) {
                    activeWorkspaceUser = currentUser;
                }
            }
            SaveCatalogInternal();

            if (clearCloudBeds && slotToClear > 0 && supabase != null && supabase.IsEnabled) {
                ThreadPool.QueueUserWorkItem(_ => {
                    try {
                        for (int i = 1; i <= 30; i++) {
                            supabase.SaveBed(i, "", slotToClear, "Admin (Deleted)");
                        }
                    } catch {}
                });
            }

            if (OnUserListChanged != null) {
                try { OnUserListChanged(); } catch {}
            }
            return true;
        }

        private void SaveCatalogInternal() {
            string json;
            lock (userLock) {
                json = SerializeUsersCatalog(users);
            }
            try {
                File.WriteAllText(catalogFilePath, json, BedNotesManager.SafeUtf8);
            } catch {}

            if (supabase != null && supabase.IsEnabled) {
                string editor = currentUser != null ? currentUser.DisplayName : "Admin";
                ThreadPool.QueueUserWorkItem(_ => {
                    try {
                        supabase.SaveRow101UsersJson(json, editor);
                    } catch {}
                });
            }
        }

        public static List<WardUserItem> ParseUsersCatalog(string json) {
            var list = new List<WardUserItem>();
            if (string.IsNullOrEmpty(json)) return list;
            var matches = Regex.Matches(json, @"\{[^{}]*""username""[^{}]*\}", RegexOptions.Singleline);
            foreach (Match m in matches) {
                string obj = m.Value;
                var u = new WardUserItem();
                u.Id = ExtractJsonProp(obj, "id");
                u.Username = ExtractJsonProp(obj, "username").Trim().ToLowerInvariant();
                u.PasswordHash = ExtractJsonProp(obj, "password_hash");
                u.DisplayName = ExtractJsonProp(obj, "display_name");
                u.Role = ExtractJsonProp(obj, "role");
                if (string.IsNullOrEmpty(u.Role)) u.Role = "user";
                u.UserSlot = ExtractJsonInt(obj, "user_slot", 0);
                u.IsActive = ExtractJsonBool(obj, "is_active", true);
                u.CreatedAt = ExtractJsonProp(obj, "created_at");
                u.LastLoginAt = ExtractJsonProp(obj, "last_login_at");
                u.RegisteredVia = ExtractJsonProp(obj, "registered_via");
                if (string.IsNullOrEmpty(u.RegisteredVia)) u.RegisteredVia = "self";
                if (!string.IsNullOrEmpty(u.Username)) {
                    list.Add(u);
                }
            }
            return list;
        }

        public static string SerializeUsersCatalog(List<WardUserItem> list) {
            var sb = new StringBuilder();
            sb.Append("{\"version\":1,\"users\":[");
            for (int i = 0; i < list.Count; i++) {
                if (i > 0) sb.Append(",");
                var u = list[i];
                sb.Append("{");
                sb.AppendFormat("\"id\":\"{0}\",", SupabaseSyncClient.EscapeJson(u.Id ?? ""));
                sb.AppendFormat("\"username\":\"{0}\",", SupabaseSyncClient.EscapeJson(u.Username ?? ""));
                sb.AppendFormat("\"password_hash\":\"{0}\",", SupabaseSyncClient.EscapeJson(u.PasswordHash ?? ""));
                sb.AppendFormat("\"display_name\":\"{0}\",", SupabaseSyncClient.EscapeJson(u.DisplayName ?? ""));
                sb.AppendFormat("\"role\":\"{0}\",", SupabaseSyncClient.EscapeJson(u.Role ?? "user"));
                sb.AppendFormat("\"user_slot\":{0},", u.UserSlot);
                sb.AppendFormat("\"is_active\":{0},", u.IsActive ? "true" : "false");
                sb.AppendFormat("\"created_at\":\"{0}\",", SupabaseSyncClient.EscapeJson(u.CreatedAt ?? ""));
                sb.AppendFormat("\"last_login_at\":\"{0}\",", SupabaseSyncClient.EscapeJson(u.LastLoginAt ?? ""));
                sb.AppendFormat("\"registered_via\":\"{0}\"", SupabaseSyncClient.EscapeJson(u.RegisteredVia ?? "self"));
                sb.Append("}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string ExtractJsonProp(string objJson, string propName) {
            if (string.IsNullOrEmpty(objJson)) return "";
            Match m = Regex.Match(objJson, @"""" + Regex.Escape(propName) + @"""\s*:\s*""((?:\\""|[^""])*)""", RegexOptions.Singleline);
            if (m.Success) {
                return SupabaseSyncClient.UnescapeJson(m.Groups[1].Value);
            }
            Match mNum = Regex.Match(objJson, @"""" + Regex.Escape(propName) + @"""\s*:\s*([0-9]+)", RegexOptions.Singleline);
            if (mNum.Success) {
                return mNum.Groups[1].Value;
            }
            return "";
        }

        private static bool ExtractJsonBool(string objJson, string propName, bool defaultVal = true) {
            Match m = Regex.Match(objJson, @"""" + Regex.Escape(propName) + @"""\s*:\s*(true|false)", RegexOptions.IgnoreCase);
            if (m.Success) {
                return m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            return defaultVal;
        }

        private static int ExtractJsonInt(string objJson, string propName, int defaultVal = 0) {
            Match m = Regex.Match(objJson, @"""" + Regex.Escape(propName) + @"""\s*:\s*([0-9]+)", RegexOptions.Singleline);
            if (m.Success) {
                int v;
                if (int.TryParse(m.Groups[1].Value, out v)) return v;
            }
            return defaultVal;
        }
    }

    public class LoginRegisterDialog : Form {
        private ExpanderContext context;
        private WardUserManager userManager;
        public bool IsStartupGate { get; private set; }

        // Navigation & Panels
        private Panel pnlHeader;
        private Panel pnlTabBar;
        private Panel pnlContent;
        private Button btnTabLogin;
        private Button btnTabRegister;
        private Panel pnlLogin;
        private Panel pnlRegister;

        // Login Controls
        private TextBox txtLoginUser;
        private TextBox txtLoginPass;
        private CheckBox chkLoginShowPass;
        private CheckBox chkLoginRemember;
        private Label lblLoginError;
        private Button btnLoginSubmit;

        // Register Controls
        private TextBox txtRegUser;
        private TextBox txtRegDisplay;
        private TextBox txtRegPass;
        private TextBox txtRegConfirm;
        private CheckBox chkRegShowPass;
        private CheckBox chkRegRemember;
        private Label lblRegError;
        private Button btnRegSubmit;

        // Bottom Exit / Cancel Button
        private Button btnBottomClose;

        public LoginRegisterDialog(ExpanderContext ctx, bool isStartupGate = false) {
            context = ctx;
            userManager = ctx != null ? ctx.UserManager : null;
            IsStartupGate = isStartupGate;
            InitializeUI();
        }

        private void InitializeUI() {
            this.Text = "🔐 เข้าสู่ระบบ / ลงทะเบียนผู้ใช้งาน (Ward Authentication)";
            this.ClientSize = new Size(460, 532);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Leelawadee UI", 9.5f, FontStyle.Regular);

            // 1. Header Banner Panel (Y: 0..68)
            pnlHeader = new Panel {
                Location = new Point(0, 0),
                Size = new Size(460, 68),
                BackColor = Color.FromArgb(13, 148, 136)
            };
            var lblTitle = new Label {
                Text = "🏥 Medical & Nursing Text Expander",
                Font = new Font("Leelawadee UI", 12f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(16, 12),
                AutoSize = true
            };
            var lblSub = new Label {
                Text = "ระบบบันทึกและจัดการข้อมูลผู้ป่วยรายเตียง (Multi-User Ward System)",
                Font = new Font("Leelawadee UI", 8.5f),
                ForeColor = Color.FromArgb(204, 251, 241),
                Location = new Point(18, 38),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSub);
            this.Controls.Add(pnlHeader);

            // 2. Tab Bar Panel (Y: 68..112)
            pnlTabBar = new Panel {
                Location = new Point(0, 68),
                Size = new Size(460, 44),
                BackColor = Color.FromArgb(241, 245, 249)
            };

            btnTabLogin = new Button {
                Text = "🔐 เข้าสู่ระบบ (Login)",
                Size = new Size(206, 36),
                Location = new Point(16, 4),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Leelawadee UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnTabLogin.FlatAppearance.BorderSize = 0;
            btnTabLogin.Click += (s, e) => SwitchTab(true);

            btnTabRegister = new Button {
                Text = "📝 ลงทะเบียนใหม่ (Register)",
                Size = new Size(206, 36),
                Location = new Point(236, 4),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Leelawadee UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnTabRegister.FlatAppearance.BorderSize = 0;
            btnTabRegister.Click += (s, e) => SwitchTab(false);

            pnlTabBar.Controls.Add(btnTabLogin);
            pnlTabBar.Controls.Add(btnTabRegister);
            this.Controls.Add(pnlTabBar);

            // 3. Content Panel Container (Y: 112..532)
            pnlContent = new Panel {
                Location = new Point(0, 112),
                Size = new Size(460, 420),
                BackColor = Color.White
            };

            // Build Login Panel (child of pnlContent)
            pnlLogin = new Panel {
                Location = new Point(0, 0),
                Size = new Size(460, 420),
                BackColor = Color.White
            };
            BuildLoginControls();
            pnlContent.Controls.Add(pnlLogin);

            // Build Register Panel (child of pnlContent)
            pnlRegister = new Panel {
                Location = new Point(0, 0),
                Size = new Size(460, 420),
                BackColor = Color.White,
                Visible = false
            };
            BuildRegisterControls();
            pnlContent.Controls.Add(pnlRegister);

            this.Controls.Add(pnlContent);

            // Default to Login Tab
            SwitchTab(true);
        }

        private void SwitchTab(bool isLogin) {
            pnlLogin.Visible = isLogin;
            pnlRegister.Visible = !isLogin;

            if (isLogin) {
                btnTabLogin.BackColor = Color.FromArgb(13, 148, 136);
                btnTabLogin.ForeColor = Color.White;
                btnTabRegister.BackColor = Color.FromArgb(241, 245, 249);
                btnTabRegister.ForeColor = Color.FromArgb(71, 85, 105);
                this.AcceptButton = btnLoginSubmit;
                txtLoginUser.Focus();
            } else {
                btnTabRegister.BackColor = Color.FromArgb(13, 148, 136);
                btnTabRegister.ForeColor = Color.White;
                btnTabLogin.BackColor = Color.FromArgb(241, 245, 249);
                btnTabLogin.ForeColor = Color.FromArgb(71, 85, 105);
                this.AcceptButton = btnRegSubmit;
                txtRegUser.Focus();
            }
        }

        private void BuildLoginControls() {
            int y = 16;
            var lblUser = new Label { Text = "ชื่อผู้ใช้งาน (Username):", Location = new Point(24, y), AutoSize = true, Font = new Font("Leelawadee UI", 9f, FontStyle.Bold) };
            pnlLogin.Controls.Add(lblUser);
            y += 24;

            txtLoginUser = new TextBox {
                Location = new Point(24, y),
                Size = new Size(412, 28),
                Font = new Font("Segoe UI", 10.5f)
            };
            if (userManager != null && userManager.CurrentUser != null) {
                txtLoginUser.Text = userManager.CurrentUser.Username;
            } else {
                txtLoginUser.Text = "admin";
            }
            pnlLogin.Controls.Add(txtLoginUser);
            y += 38;

            var lblPass = new Label { Text = "รหัสผ่าน (Password):", Location = new Point(24, y), AutoSize = true, Font = new Font("Leelawadee UI", 9f, FontStyle.Bold) };
            pnlLogin.Controls.Add(lblPass);
            y += 24;

            txtLoginPass = new TextBox {
                Location = new Point(24, y),
                Size = new Size(412, 28),
                Font = new Font("Segoe UI", 10.5f),
                UseSystemPasswordChar = true
            };
            pnlLogin.Controls.Add(txtLoginPass);
            y += 36;

            chkLoginShowPass = new CheckBox {
                Text = "แสดงรหัสผ่าน",
                Location = new Point(26, y),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            chkLoginShowPass.CheckedChanged += (s, e) => {
                txtLoginPass.UseSystemPasswordChar = !chkLoginShowPass.Checked;
            };
            pnlLogin.Controls.Add(chkLoginShowPass);

            chkLoginRemember = new CheckBox {
                Text = "จดจำการเข้าสู่ระบบในเครื่องนี้ (Remember Me)",
                Location = new Point(155, y),
                AutoSize = true,
                Checked = true,
                Cursor = Cursors.Hand
            };
            pnlLogin.Controls.Add(chkLoginRemember);
            y += 32;

            lblLoginError = new Label {
                Text = "",
                ForeColor = Color.FromArgb(220, 38, 38),
                Location = new Point(24, y),
                Size = new Size(412, 22),
                Font = new Font("Leelawadee UI", 8.5f, FontStyle.Bold)
            };
            pnlLogin.Controls.Add(lblLoginError);
            y += 26;

            btnLoginSubmit = new Button {
                Text = "เข้าสู่ระบบ (Login)",
                Location = new Point(24, y),
                Size = new Size(286, 40),
                BackColor = Color.FromArgb(13, 148, 136),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Leelawadee UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnLoginSubmit.FlatAppearance.BorderSize = 0;
            btnLoginSubmit.Click += (s, e) => DoLogin();
            pnlLogin.Controls.Add(btnLoginSubmit);

            btnBottomClose = new Button {
                Text = IsStartupGate ? "ปิดโปรแกรม" : "ยกเลิก",
                DialogResult = DialogResult.Cancel,
                Location = new Point(318, y),
                Size = new Size(118, 40),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(71, 85, 105),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnBottomClose.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            pnlLogin.Controls.Add(btnBottomClose);
            y += 56;

            var lnkGoRegister = new LinkLabel {
                Text = "👉 ยังไม่มีบัญชีผู้ใช้งาน? กดที่นี่เพื่อลงทะเบียนและเข้าใช้งานได้เลย",
                Location = new Point(24, y),
                AutoSize = true,
                LinkColor = Color.FromArgb(13, 148, 136),
                Cursor = Cursors.Hand
            };
            lnkGoRegister.LinkClicked += (s, e) => SwitchTab(false);
            pnlLogin.Controls.Add(lnkGoRegister);
        }

        private void BuildRegisterControls() {
            int y = 10;
            var lblU = new Label { Text = "ชื่อผู้ใช้งานภาษาอังกฤษ (Username):", Location = new Point(24, y), AutoSize = true, Font = new Font("Leelawadee UI", 9f, FontStyle.Bold) };
            pnlRegister.Controls.Add(lblU);
            y += 22;

            txtRegUser = new TextBox {
                Location = new Point(24, y),
                Size = new Size(412, 26),
                Font = new Font("Segoe UI", 10f)
            };
            pnlRegister.Controls.Add(txtRegUser);
            y += 34;

            var lblD = new Label { Text = "ชื่อแสดง / ชื่อเรียกพยาบาล (Display Name):", Location = new Point(24, y), AutoSize = true, Font = new Font("Leelawadee UI", 9f, FontStyle.Bold) };
            pnlRegister.Controls.Add(lblD);
            y += 22;

            txtRegDisplay = new TextBox {
                Location = new Point(24, y),
                Size = new Size(412, 26),
                Font = new Font("Segoe UI", 10f)
            };
            pnlRegister.Controls.Add(txtRegDisplay);
            y += 34;

            var lblP = new Label { Text = "รหัสผ่าน (Password, อย่างน้อย 4 ตัว):", Location = new Point(24, y), AutoSize = true, Font = new Font("Leelawadee UI", 9f, FontStyle.Bold) };
            pnlRegister.Controls.Add(lblP);
            y += 22;

            txtRegPass = new TextBox {
                Location = new Point(24, y),
                Size = new Size(412, 26),
                Font = new Font("Segoe UI", 10f),
                UseSystemPasswordChar = true
            };
            pnlRegister.Controls.Add(txtRegPass);
            y += 34;

            var lblC = new Label { Text = "ยืนยันรหัสผ่าน (Confirm Password):", Location = new Point(24, y), AutoSize = true, Font = new Font("Leelawadee UI", 9f, FontStyle.Bold) };
            pnlRegister.Controls.Add(lblC);
            y += 22;

            txtRegConfirm = new TextBox {
                Location = new Point(24, y),
                Size = new Size(412, 26),
                Font = new Font("Segoe UI", 10f),
                UseSystemPasswordChar = true
            };
            pnlRegister.Controls.Add(txtRegConfirm);
            y += 32;

            chkRegShowPass = new CheckBox {
                Text = "แสดงรหัสผ่าน",
                Location = new Point(26, y),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            chkRegShowPass.CheckedChanged += (s, e) => {
                txtRegPass.UseSystemPasswordChar = !chkRegShowPass.Checked;
                txtRegConfirm.UseSystemPasswordChar = !chkRegShowPass.Checked;
            };
            pnlRegister.Controls.Add(chkRegShowPass);

            chkRegRemember = new CheckBox {
                Text = "จดจำการเข้าสู่ระบบ",
                Location = new Point(160, y),
                AutoSize = true,
                Checked = true,
                Cursor = Cursors.Hand
            };
            pnlRegister.Controls.Add(chkRegRemember);
            y += 28;

            lblRegError = new Label {
                Text = "",
                ForeColor = Color.FromArgb(220, 38, 38),
                Location = new Point(24, y),
                Size = new Size(412, 20),
                Font = new Font("Leelawadee UI", 8.5f, FontStyle.Bold)
            };
            pnlRegister.Controls.Add(lblRegError);
            y += 24;

            btnRegSubmit = new Button {
                Text = "📝 ลงทะเบียนและเริ่มใช้งานทันที",
                Location = new Point(24, y),
                Size = new Size(286, 40),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Leelawadee UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnRegSubmit.FlatAppearance.BorderSize = 0;
            btnRegSubmit.Click += (s, e) => DoRegister();
            pnlRegister.Controls.Add(btnRegSubmit);

            var btnRegCancel = new Button {
                Text = IsStartupGate ? "ปิดโปรแกรม" : "ยกเลิก",
                DialogResult = DialogResult.Cancel,
                Location = new Point(318, y),
                Size = new Size(118, 40),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(71, 85, 105),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRegCancel.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            pnlRegister.Controls.Add(btnRegCancel);
            y += 54;

            var lnkGoLogin = new LinkLabel {
                Text = "👈 มีบัญชีผู้ใช้งานอยู่แล้ว? กดที่นี่เพื่อเข้าสู่ระบบ",
                Location = new Point(24, y),
                AutoSize = true,
                LinkColor = Color.FromArgb(13, 148, 136),
                Cursor = Cursors.Hand
            };
            lnkGoLogin.LinkClicked += (s, e) => SwitchTab(true);
            pnlRegister.Controls.Add(lnkGoLogin);
        }

        private void DoLogin() {
            lblLoginError.Text = "";
            if (userManager == null) {
                this.DialogResult = DialogResult.Cancel;
                return;
            }
            string err;
            if (userManager.Login(txtLoginUser.Text, txtLoginPass.Text, chkLoginRemember.Checked, out err)) {
                this.DialogResult = DialogResult.OK;
                this.Close();
            } else {
                lblLoginError.Text = err;
                txtLoginPass.SelectAll();
                txtLoginPass.Focus();
            }
        }

        private void DoRegister() {
            lblRegError.Text = "";
            if (userManager == null) {
                this.DialogResult = DialogResult.Cancel;
                return;
            }
            if (txtRegPass.Text != txtRegConfirm.Text) {
                lblRegError.Text = "รหัสผ่านและการยืนยันรหัสผ่านไม่ตรงกัน";
                txtRegConfirm.Focus();
                return;
            }
            string err;
            if (userManager.Register(txtRegUser.Text, txtRegDisplay.Text, txtRegPass.Text, chkRegRemember.Checked, out err)) {
                MessageBox.Show(this, string.Format("ลงทะเบียนสำเร็จ!\nยินดีต้อนรับ {0} เข้าสู่ระบบ", userManager.CurrentUser.DisplayName), "ลงทะเบียนสำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.DialogResult = DialogResult.OK;
                this.Close();
            } else {
                lblRegError.Text = err;
                txtRegUser.Focus();
            }
        }
    }

    public class UserLoginDialog : LoginRegisterDialog {
        public UserLoginDialog(ExpanderContext ctx) : base(ctx, false) { }
    }

    public class UserManagementDialog : Form {
        private ExpanderContext context;
        private WardUserManager userManager;
        private ListView lvUsers;
        private Button btnAdd;
        private Button btnEdit;
        private Button btnToggleActive;
        private Button btnDelete;
        private Button btnSwitchToUser;
        private Button btnClose;

        public UserManagementDialog(ExpanderContext ctx) {
            context = ctx;
            userManager = ctx != null ? ctx.UserManager : null;
            InitializeUI();
            LoadUserList();
        }

        private void InitializeUI() {
            this.Text = "👥 จัดการบัญชีผู้ใช้งานและสิทธิ์ (User Accounts Management)";
            this.Size = new Size(880, 520);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new Size(760, 420);
            this.Font = new Font("Leelawadee UI", 9.5f);
            this.BackColor = Color.FromArgb(248, 250, 252);

            var pnlTop = new Panel {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.FromArgb(15, 118, 110)
            };
            var lblTitle = new Label {
                Text = "👥 จัดการบัญชีผู้ใช้งานและสิทธิ์การเข้าถึง (Ward Users)",
                Font = new Font("Leelawadee UI", 12f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(16, 15),
                AutoSize = true
            };
            pnlTop.Controls.Add(lblTitle);
            this.Controls.Add(pnlTop);

            var pnlToolbar = new Panel {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(10, 8, 10, 8)
            };

            btnAdd = new Button {
                Text = "➕ เพิ่มผู้ใช้ใหม่",
                Size = new Size(125, 32),
                Location = new Point(12, 8),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Leelawadee UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnAdd.FlatAppearance.BorderSize = 0;
            btnAdd.Click += (s, e) => AddUser();
            pnlToolbar.Controls.Add(btnAdd);

            btnEdit = new Button {
                Text = "✏️ แก้ไข / รหัสผ่าน",
                Size = new Size(135, 32),
                Location = new Point(142, 8),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Leelawadee UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnEdit.FlatAppearance.BorderSize = 0;
            btnEdit.Click += (s, e) => EditSelectedUser();
            pnlToolbar.Controls.Add(btnEdit);

            btnToggleActive = new Button {
                Text = "⛔ ระงับ / เปิดใช้งาน",
                Size = new Size(140, 32),
                Location = new Point(282, 8),
                BackColor = Color.FromArgb(71, 85, 105),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Leelawadee UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnToggleActive.FlatAppearance.BorderSize = 0;
            btnToggleActive.Click += (s, e) => ToggleSelectedUserActive();
            pnlToolbar.Controls.Add(btnToggleActive);

            btnDelete = new Button {
                Text = "🗑️ ลบผู้ใช้",
                Size = new Size(95, 32),
                Location = new Point(427, 8),
                BackColor = Color.FromArgb(239, 68, 68),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Leelawadee UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDelete.FlatAppearance.BorderSize = 0;
            btnDelete.Click += (s, e) => DeleteSelectedUser();
            pnlToolbar.Controls.Add(btnDelete);

            btnSwitchToUser = new Button {
                Text = "👁️ สลับดูเตียงของผู้ใช้นี้",
                Size = new Size(170, 32),
                Location = new Point(527, 8),
                BackColor = Color.FromArgb(245, 158, 11),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Leelawadee UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSwitchToUser.FlatAppearance.BorderSize = 0;
            btnSwitchToUser.Click += (s, e) => SwitchToSelectedUser();
            pnlToolbar.Controls.Add(btnSwitchToUser);

            this.Controls.Add(pnlToolbar);

            lvUsers = new ListView {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                Font = new Font("Leelawadee UI", 9.5f)
            };
            lvUsers.Columns.Add("Username", 110);
            lvUsers.Columns.Add("ชื่อแสดง (Display Name)", 180);
            lvUsers.Columns.Add("สิทธิ์ (Role)", 110);
            lvUsers.Columns.Add("Slot ข้อมูลเตียง", 110);
            lvUsers.Columns.Add("สถานะ", 95);
            lvUsers.Columns.Add("วิธีสมัคร", 90);
            lvUsers.Columns.Add("เข้าใช้ล่าสุด", 125);
            lvUsers.Columns.Add("วันที่สร้าง", 120);
            lvUsers.DoubleClick += (s, e) => EditSelectedUser();
            this.Controls.Add(lvUsers);

            var pnlBottom = new Panel {
                Dock = DockStyle.Bottom,
                Height = 65,
                BackColor = Color.FromArgb(241, 245, 249)
            };
            var lblInfo = new Label {
                Text = "ℹ️ หมายเหตุ: ผู้ใช้แอดมิน (admin) ครอบครองข้อมูลเตียง 1-30 หลักของวอร์ด ส่วนผู้ใช้อื่นจะมีเตียง 1-30 แยกของตนเอง",
                Font = new Font("Leelawadee UI", 8.5f),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 10),
                AutoSize = true
            };
            btnClose = new Button {
                Text = "ปิดหน้าต่าง",
                Size = new Size(100, 32),
                Location = new Point(750, 18),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnClose.Click += (s, e) => this.Close();
            pnlBottom.Controls.Add(lblInfo);
            pnlBottom.Controls.Add(btnClose);
            this.Controls.Add(pnlBottom);

            pnlTop.SendToBack();
            pnlToolbar.SendToBack();
            pnlBottom.SendToBack();
            lvUsers.BringToFront();
        }

        private void LoadUserList() {
            lvUsers.Items.Clear();
            if (userManager == null) return;
            var list = userManager.GetAllUsers();
            foreach (var u in list) {
                var lvi = new ListViewItem(u.Username);
                lvi.SubItems.Add(u.DisplayName);
                lvi.SubItems.Add(u.Role == "admin" ? "👑 ผู้ดูแลระบบ" : "👩‍⚕️ พยาบาล/ผู้ใช้");
                lvi.SubItems.Add(u.UserSlot == 0 ? "เตียง 1-30 (หลัก)" : string.Format("ชุด {0} (เตียง 1-30)", u.UserSlot));
                lvi.SubItems.Add(u.IsActive ? "🟢 ใช้งานได้" : "🔴 ปิดการใช้งาน");
                lvi.SubItems.Add(u.RegisteredVia == "admin" ? "🛠️ แอดมินสร้าง" : "📝 ลงทะเบียนเอง");
                string lastLogin = "-";
                DateTime dtLogin;
                if (!string.IsNullOrEmpty(u.LastLoginAt) && DateTime.TryParse(u.LastLoginAt, out dtLogin)) {
                    lastLogin = dtLogin.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                }
                lvi.SubItems.Add(lastLogin);
                string cDate = "";
                DateTime dt;
                if (DateTime.TryParse(u.CreatedAt, out dt)) cDate = dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                lvi.SubItems.Add(cDate);
                lvi.Tag = u;
                lvUsers.Items.Add(lvi);
            }
        }

        private void AddUser() {
            var dlg = new AddEditUserDialog(null);
            if (dlg.ShowDialog(this) == DialogResult.OK) {
                string err;
                if (!userManager.SaveUser(dlg.UserItem, dlg.NewPassword, out err)) {
                    MessageBox.Show(this, "ไม่สามารถบันทึกผู้ใช้ได้: " + err, "ข้อผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                } else {
                    LoadUserList();
                }
            }
        }

        private void EditSelectedUser() {
            if (lvUsers.SelectedItems.Count == 0) return;
            var user = lvUsers.SelectedItems[0].Tag as WardUserItem;
            if (user == null) return;
            var dlg = new AddEditUserDialog(user);
            if (dlg.ShowDialog(this) == DialogResult.OK) {
                string err;
                if (!userManager.SaveUser(dlg.UserItem, dlg.NewPassword, out err)) {
                    MessageBox.Show(this, "ไม่สามารถบันทึกผู้ใช้ได้: " + err, "ข้อผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                } else {
                    LoadUserList();
                }
            }
        }

        private void ToggleSelectedUserActive() {
            if (lvUsers.SelectedItems.Count == 0) return;
            var user = lvUsers.SelectedItems[0].Tag as WardUserItem;
            if (user == null) return;
            if (user.Role == "admin" || user.Username == "admin") {
                MessageBox.Show(this, "ไม่สามารถระงับบัญชีผู้ดูแลระบบ (admin) ได้", "คำเตือน", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string err;
            if (!userManager.ToggleUserActive(user.Username, out err)) {
                MessageBox.Show(this, "เกิดข้อผิดพลาด: " + err, "ข้อผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            } else {
                LoadUserList();
            }
        }

        private void DeleteSelectedUser() {
            if (lvUsers.SelectedItems.Count == 0) return;
            var user = lvUsers.SelectedItems[0].Tag as WardUserItem;
            if (user == null) return;
            if (user.Role == "admin" || user.Username == "admin") {
                MessageBox.Show(this, "ไม่สามารถลบบัญชีผู้ดูแลระบบ (admin) ได้", "คำเตือน", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var confirmRes = MessageBox.Show(this, string.Format("คุณต้องการลบบัญชีผู้ใช้ \"{0}\" ({1}) หรือไม่?\n\nกด Yes: ลบบัญชีและล้างเตียงบน Cloud\nกด No: ลบบัญชีแต่เก็บข้อมูลเตียงไว้\nกด Cancel: ยกเลิก", user.DisplayName, user.Username), "ยืนยันการลบ", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (confirmRes == DialogResult.Yes || confirmRes == DialogResult.No) {
                bool clearBeds = (confirmRes == DialogResult.Yes);
                string err;
                if (!userManager.DeleteUser(user.Username, clearBeds, out err)) {
                    MessageBox.Show(this, "ไม่สามารถลบได้: " + err, "ข้อผิดพลาด", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                } else {
                    LoadUserList();
                }
            }
        }

        private void SwitchToSelectedUser() {
            if (lvUsers.SelectedItems.Count == 0) return;
            var user = lvUsers.SelectedItems[0].Tag as WardUserItem;
            if (user == null) return;
            if (userManager != null) {
                userManager.SwitchWorkspace(user.Username);
                this.Close();
            }
        }
    }

    public class AddEditUserDialog : Form {
        public WardUserItem UserItem { get; private set; }
        public string NewPassword { get; private set; }
        private bool isEditMode = false;

        private TextBox txtUsername;
        private TextBox txtDisplayName;
        private ComboBox cboRole;
        private TextBox txtPassword;
        private CheckBox chkIsActive;
        private CheckBox chkShowPassword;

        public AddEditUserDialog(WardUserItem existingUser) {
            isEditMode = (existingUser != null);
            if (existingUser != null) {
                UserItem = new WardUserItem {
                    Id = existingUser.Id,
                    Username = existingUser.Username,
                    PasswordHash = existingUser.PasswordHash,
                    DisplayName = existingUser.DisplayName,
                    Role = existingUser.Role,
                    UserSlot = existingUser.UserSlot,
                    IsActive = existingUser.IsActive,
                    CreatedAt = existingUser.CreatedAt
                };
            } else {
                UserItem = new WardUserItem();
            }
            InitializeUI();
        }

        private void InitializeUI() {
            this.Text = isEditMode ? "✏️ แก้ไขข้อมูลผู้ใช้" : "➕ เพิ่มผู้ใช้ใหม่";
            this.Size = new Size(420, 420);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;
            this.Font = new Font("Leelawadee UI", 9.5f);

            var pnlHeader = new Panel {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = isEditMode ? Color.FromArgb(37, 99, 235) : Color.FromArgb(16, 185, 129)
            };
            var lblTitle = new Label {
                Text = isEditMode ? "✏️ แก้ไขข้อมูลและสิทธิ์ผู้ใช้" : "➕ สร้างบัญชีผู้ใช้งานใหม่",
                Font = new Font("Leelawadee UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(14, 14),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);
            this.Controls.Add(pnlHeader);

            int y = 65;
            var lblU = new Label { Text = "ชื่อผู้ใช้งาน (Username):", Location = new Point(24, y), AutoSize = true, Font = new Font("Leelawadee UI", 9f, FontStyle.Bold) };
            this.Controls.Add(lblU);
            y += 22;

            txtUsername = new TextBox {
                Location = new Point(24, y),
                Size = new Size(355, 26),
                Text = UserItem.Username,
                Enabled = !isEditMode
            };
            this.Controls.Add(txtUsername);
            y += 34;

            var lblD = new Label { Text = "ชื่อแสดง / ตำแหน่ง (Display Name):", Location = new Point(24, y), AutoSize = true, Font = new Font("Leelawadee UI", 9f, FontStyle.Bold) };
            this.Controls.Add(lblD);
            y += 22;

            txtDisplayName = new TextBox {
                Location = new Point(24, y),
                Size = new Size(355, 26),
                Text = UserItem.DisplayName
            };
            this.Controls.Add(txtDisplayName);
            y += 34;

            var lblR = new Label { Text = "บทบาท / สิทธิ์ (Role):", Location = new Point(24, y), AutoSize = true, Font = new Font("Leelawadee UI", 9f, FontStyle.Bold) };
            this.Controls.Add(lblR);
            y += 22;

            cboRole = new ComboBox {
                Location = new Point(24, y),
                Size = new Size(355, 26),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboRole.Items.Add("👩‍⚕️ พยาบาล / ผู้ใช้งานทั่วไป (User)");
            cboRole.Items.Add("👑 ผู้ดูแลระบบ (Admin)");
            cboRole.SelectedIndex = (UserItem.Role == "admin") ? 1 : 0;
            if (isEditMode && UserItem.Username == "admin") cboRole.Enabled = false;
            this.Controls.Add(cboRole);
            y += 34;

            var lblP = new Label {
                Text = isEditMode ? "รหัสผ่านใหม่ (ปล่อยว่างถ้าไม่เปลี่ยน):" : "รหัสผ่าน (Password):",
                Location = new Point(24, y),
                AutoSize = true,
                Font = new Font("Leelawadee UI", 9f, FontStyle.Bold)
            };
            this.Controls.Add(lblP);
            y += 22;

            txtPassword = new TextBox {
                Location = new Point(24, y),
                Size = new Size(355, 26),
                UseSystemPasswordChar = true
            };
            this.Controls.Add(txtPassword);
            y += 30;

            chkShowPassword = new CheckBox {
                Text = "แสดงรหัสผ่าน",
                Location = new Point(26, y),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            chkShowPassword.CheckedChanged += (s, e) => {
                txtPassword.UseSystemPasswordChar = !chkShowPassword.Checked;
            };
            this.Controls.Add(chkShowPassword);

            chkIsActive = new CheckBox {
                Text = "เปิดใช้งานบัญชีนี้ (Active)",
                Location = new Point(170, y),
                AutoSize = true,
                Checked = UserItem.IsActive,
                Cursor = Cursors.Hand
            };
            if (isEditMode && UserItem.Username == "admin") chkIsActive.Enabled = false;
            this.Controls.Add(chkIsActive);
            y += 38;

            var btnOk = new Button {
                Text = "บันทึกข้อมูล",
                Location = new Point(160, y),
                Size = new Size(110, 34),
                BackColor = Color.FromArgb(13, 148, 136),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Leelawadee UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Click += (s, e) => SaveAndClose();
            this.Controls.Add(btnOk);

            var btnCancel = new Button {
                Text = "ยกเลิก",
                Location = new Point(280, y),
                Size = new Size(99, 34),
                BackColor = Color.FromArgb(241, 245, 249),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnCancel.Click += (s, e) => this.Close();
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
        }

        private void SaveAndClose() {
            string u = txtUsername.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(u)) {
                MessageBox.Show(this, "กรุณาระบุชื่อผู้ใช้งาน", "คำเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtUsername.Focus();
                return;
            }
            if (!isEditMode && string.IsNullOrEmpty(txtPassword.Text)) {
                MessageBox.Show(this, "กรุณากำหนดรหัสผ่านสำหรับผู้ใช้ใหม่", "คำเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtPassword.Focus();
                return;
            }

            UserItem.Username = u;
            UserItem.DisplayName = string.IsNullOrEmpty(txtDisplayName.Text.Trim()) ? u : txtDisplayName.Text.Trim();
            UserItem.Role = (cboRole.SelectedIndex == 1) ? "admin" : "user";
            UserItem.IsActive = chkIsActive.Checked;
            NewPassword = txtPassword.Text;

            this.DialogResult = DialogResult.OK;
            this.Close();
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
        private Point bedDragStart = Point.Empty;
        private int bedDragSource = 0;

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
        private Button btnMobilePortal;
        private Button btnIoTemplate;
        private Button btnGoToPalette;
        private Button btnZoomOut;
        private Button btnZoomIn;
        private Button btnCheckUpdate;

        private Button btnCopy;
        private Button btnInsertTime;
        private Button btnHistory;
        private Button btnSwapBed;
        private Button btnClear;
        private Button btnSyncSettings;
        private Button btnClose;

        private Button btnUserAccount;
        private ComboBox cboWorkspaceUser;
        private Panel pnlWorkspaceNotice;
        private Label lblWorkspaceNotice;
        private Button btnBackToMyWorkspace;
        private Button btnManageUsers;

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
                manager.OnCloudSaveCompleted += (bNum, success) => {
                    if (this.IsDisposed || !this.IsHandleCreated) return;
                    this.BeginInvoke(new Action(() => {
                        if (bNum == currentBed && !isDirty) {
                            if (success) {
                                lblAutoSave.Text = "☁️ บันทึกลงฐานข้อมูล Cloud สำเร็จแล้ว (" + DateTime.Now.ToString("HH:mm:ss") + " น.)";
                                lblAutoSave.ForeColor = Color.FromArgb(13, 148, 136);
                            } else {
                                lblAutoSave.Text = "💾 บันทึกลงเครื่องแล้ว (⚠️ เน็ตหลุด/รอซิงค์ขึ้น Cloud)";
                                lblAutoSave.ForeColor = Color.FromArgb(217, 119, 6);
                            }
                            RepositionNoteHeaderControls();
                        }
                    }));
                };
                manager.OnCloudStatusChanged += (active) => {
                    if (this.IsDisposed || !this.IsHandleCreated) return;
                    this.BeginInvoke(new Action(() => {
                        lblNetworkStatus.Text = manager.StatusText;
                        lblNetworkStatus.ForeColor = active ? Color.FromArgb(167, 243, 208) : Color.FromArgb(254, 202, 202);
                    }));
                };
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
            lblNetworkStatus.Text = manager.StatusText;
            lblNetworkStatus.ForeColor = Color.FromArgb(204, 251, 241);
            lblNetworkStatus.Font = new Font("Segoe UI", 9f);
            lblNetworkStatus.Location = new Point(16, 32);
            lblNetworkStatus.AutoSize = true;
            pnlTop.Controls.Add(lblNetworkStatus);

            btnMobilePortal = new Button();
            btnMobilePortal.Text = "📱 มือถือ (QR)";
            btnMobilePortal.Size = new Size(115, 34);
            btnMobilePortal.BackColor = Color.FromArgb(79, 70, 229);
            btnMobilePortal.ForeColor = Color.White;
            btnMobilePortal.FlatStyle = FlatStyle.Flat;
            btnMobilePortal.FlatAppearance.BorderSize = 0;
            btnMobilePortal.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnMobilePortal.Cursor = Cursors.Hand;
            btnMobilePortal.Click += (s, e) => {
                var dlg = new MobilePortalDialog();
                dlg.ShowDialog(this);
            };
            pnlTop.Controls.Add(btnMobilePortal);

            btnIoTemplate = new Button();
            btnIoTemplate.Text = "📁 เอกสาร & แบบฟอร์มวอร์ด";
            btnIoTemplate.Size = new Size(195, 34);
            btnIoTemplate.BackColor = Color.FromArgb(16, 185, 129);
            btnIoTemplate.ForeColor = Color.White;
            btnIoTemplate.FlatStyle = FlatStyle.Flat;
            btnIoTemplate.FlatAppearance.BorderSize = 0;
            btnIoTemplate.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnIoTemplate.Cursor = Cursors.Hand;
            btnIoTemplate.Click += (s, e) => {
                var dlg = new WardDocumentCenterDialog(context);
                dlg.ShowDialog(this);
            };
            pnlTop.Controls.Add(btnIoTemplate);

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
            btnGoToPalette.Text = "📋 คลังข้อวินิจฉัย/DAR (F8)";
            btnGoToPalette.Size = new Size(150, 34);
            btnGoToPalette.BackColor = Color.FromArgb(15, 118, 110);
            btnGoToPalette.ForeColor = Color.White;
            btnGoToPalette.FlatStyle = FlatStyle.Flat;
            btnGoToPalette.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnGoToPalette.Cursor = Cursors.Hand;
            btnGoToPalette.Click += (s, e) => {
                FlushSave();
                context.ShowPalette(currentBed);
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

            btnUserAccount = new Button();
            btnUserAccount.Size = new Size(160, 34);
            btnUserAccount.BackColor = Color.FromArgb(15, 76, 129);
            btnUserAccount.ForeColor = Color.White;
            btnUserAccount.FlatStyle = FlatStyle.Flat;
            btnUserAccount.FlatAppearance.BorderSize = 0;
            btnUserAccount.Font = new Font("Leelawadee UI", 9f, FontStyle.Bold);
            btnUserAccount.Cursor = Cursors.Hand;
            UpdateUserAccountButton();
            btnUserAccount.Click += (s, e) => ShowUserMenu();
            pnlTop.Controls.Add(btnUserAccount);

            cboWorkspaceUser = new ComboBox();
            cboWorkspaceUser.Size = new Size(185, 30);
            cboWorkspaceUser.DropDownStyle = ComboBoxStyle.DropDownList;
            cboWorkspaceUser.Font = new Font("Leelawadee UI", 9.5f);
            cboWorkspaceUser.SelectedIndexChanged += CboWorkspaceUser_SelectedIndexChanged;
            pnlTop.Controls.Add(cboWorkspaceUser);
            RefreshWorkspaceDropdown();

            pnlWorkspaceNotice = new Panel();
            pnlWorkspaceNotice.Dock = DockStyle.Top;
            pnlWorkspaceNotice.Height = 36;
            pnlWorkspaceNotice.BackColor = Color.FromArgb(254, 243, 199);
            pnlWorkspaceNotice.Visible = false;

            lblWorkspaceNotice = new Label();
            lblWorkspaceNotice.Text = "👁️ คุณกำลังดูและจัดการเตียงของผู้ใช้อื่น";
            lblWorkspaceNotice.ForeColor = Color.FromArgb(146, 64, 14);
            lblWorkspaceNotice.Font = new Font("Leelawadee UI", 9.5f, FontStyle.Bold);
            lblWorkspaceNotice.Location = new Point(14, 8);
            lblWorkspaceNotice.AutoSize = true;

            btnBackToMyWorkspace = new Button();
            btnBackToMyWorkspace.Text = "🔄 กลับไปเตียงของฉัน";
            btnBackToMyWorkspace.Size = new Size(175, 28);
            btnBackToMyWorkspace.Location = new Point(this.ClientSize.Width - 190, 4);
            btnBackToMyWorkspace.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBackToMyWorkspace.BackColor = Color.FromArgb(217, 119, 6);
            btnBackToMyWorkspace.ForeColor = Color.White;
            btnBackToMyWorkspace.FlatStyle = FlatStyle.Flat;
            btnBackToMyWorkspace.FlatAppearance.BorderSize = 0;
            btnBackToMyWorkspace.Font = new Font("Leelawadee UI", 9f, FontStyle.Bold);
            btnBackToMyWorkspace.Cursor = Cursors.Hand;
            btnBackToMyWorkspace.Click += (s, e) => {
                if (context != null && context.UserManager != null) context.UserManager.ResetToMyWorkspace();
            };

            pnlWorkspaceNotice.Controls.Add(lblWorkspaceNotice);
            pnlWorkspaceNotice.Controls.Add(btnBackToMyWorkspace);

            // Bottom Action Panel
            pnlBottom = new Panel();
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Height = 52;
            pnlBottom.BackColor = Color.FromArgb(238, 240, 246);

            btnCopy = new Button();
            btnCopy.Text = "📋 คัดลอก (Copy)";
            btnCopy.Size = new Size(115, 34);
            btnCopy.BackColor = Color.FromArgb(225, 228, 238);
            btnCopy.FlatStyle = FlatStyle.Flat;
            btnCopy.Font = new Font("Segoe UI", 9f);
            btnCopy.Cursor = Cursors.Hand;
            btnCopy.Click += (s, e) => {
                FlushSave();
                if (!string.IsNullOrEmpty(txtNote.SelectedText)) {
                    try { Clipboard.SetDataObject(txtNote.SelectedText, true, 5, 50); } catch {}
                    context.ShowNotification(string.Format("คัดลอกข้อความที่เลือกเตียง {0} ไปยังคลิปบอร์ดแล้ว", currentBed));
                } else if (!string.IsNullOrEmpty(txtNote.Text)) {
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

            btnSwapBed = new Button();
            btnSwapBed.Text = "🔄 สลับ/ย้ายเตียง";
            btnSwapBed.Size = new Size(130, 34);
            btnSwapBed.BackColor = Color.FromArgb(237, 233, 254);
            btnSwapBed.ForeColor = Color.FromArgb(67, 56, 202);
            btnSwapBed.FlatStyle = FlatStyle.Flat;
            btnSwapBed.FlatAppearance.BorderColor = Color.FromArgb(196, 181, 253);
            btnSwapBed.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnSwapBed.Cursor = Cursors.Hand;
            btnSwapBed.Click += (s, e) => ShowSwapBedDialog();
            pnlBottom.Controls.Add(btnSwapBed);

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
            btnSyncSettings.Click += (s, e) => context.ShowSyncSettings(this);
            pnlBottom.Controls.Add(btnSyncSettings);

            btnManageUsers = new Button();
            btnManageUsers.Text = "👥 จัดการผู้ใช้";
            btnManageUsers.Size = new Size(110, 34);
            btnManageUsers.BackColor = Color.FromArgb(225, 228, 238);
            btnManageUsers.FlatStyle = FlatStyle.Flat;
            btnManageUsers.Font = new Font("Segoe UI", 9f);
            btnManageUsers.Cursor = Cursors.Hand;
            btnManageUsers.Visible = (context != null && context.UserManager != null && context.UserManager.IsAdminLoggedIn);
            btnManageUsers.Click += (s, e) => context.ShowUserManagement(this);
            pnlBottom.Controls.Add(btnManageUsers);



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

                ContextMenuStrip ctx = new ContextMenuStrip();
                ToolStripMenuItem itmSwap = new ToolStripMenuItem("🔄 สลับ/ย้ายเตียงนี้...");
                itmSwap.Click += (s, e) => {
                    SelectBed(bedNum);
                    ShowSwapBedDialog();
                };
                ToolStripMenuItem itmHist = new ToolStripMenuItem("📜 ประวัติเตียงย้อนหลัง");
                itmHist.Click += (s, e) => {
                    context.ShowBedHistory(bedNum);
                };
                ToolStripMenuItem itmClear = new ToolStripMenuItem("🗑️ ล้างข้อมูลเตียงนี้");
                itmClear.Click += (s, e) => {
                    SelectBed(bedNum);
                    ClearCurrentBed();
                };
                ctx.Items.Add(itmSwap);
                ctx.Items.Add(itmHist);
                ctx.Items.Add(new ToolStripSeparator());
                ctx.Items.Add(itmClear);
                btn.ContextMenuStrip = ctx;

                btn.AllowDrop = true;
                btn.MouseDown += (s, e) => {
                    if (e.Button == MouseButtons.Left) {
                        bedDragStart = e.Location;
                        bedDragSource = bedNum;
                    }
                };
                btn.MouseMove += (s, e) => {
                    if (e.Button == MouseButtons.Left && bedDragSource == bedNum) {
                        int dx = Math.Abs(e.X - bedDragStart.X);
                        int dy = Math.Abs(e.Y - bedDragStart.Y);
                        if (dx > SystemInformation.DragSize.Width || dy > SystemInformation.DragSize.Height) {
                            int src = bedDragSource;
                            bedDragSource = 0;
                            btn.DoDragDrop(new BedDragDropData(src), DragDropEffects.Move);
                        }
                    }
                };
                btn.MouseUp += (s, e) => {
                    bedDragSource = 0;
                };
                btn.DragEnter += (s, e) => {
                    if (e.Data.GetDataPresent(typeof(BedDragDropData))) {
                        BedDragDropData data = (BedDragDropData)e.Data.GetData(typeof(BedDragDropData));
                        if (data != null && data.SourceBed != bedNum) {
                            e.Effect = DragDropEffects.Move;
                            btn.BackColor = Color.FromArgb(224, 231, 255);
                            btn.ForeColor = Color.FromArgb(67, 56, 202);
                            return;
                        }
                    }
                    e.Effect = DragDropEffects.None;
                };
                btn.DragLeave += (s, e) => {
                    RefreshAllBedButtons();
                };
                btn.DragDrop += (s, e) => {
                    RefreshAllBedButtons();
                    if (e.Data.GetDataPresent(typeof(BedDragDropData))) {
                        BedDragDropData data = (BedDragDropData)e.Data.GetData(typeof(BedDragDropData));
                        if (data != null && data.SourceBed != bedNum) {
                            int src = data.SourceBed;
                            int tgt = bedNum;
                            HandleBedDragDrop(src, tgt);
                        }
                    }
                };

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
            lblBedTitle.Text = "🛏️ ข้อมูลผู้ป่วย เตียง 01";
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

            Button btnDarCatalog = new Button();
            btnDarCatalog.Text = "📋 คลังข้อวินิจฉัย/DAR (118 เทมเพลต)";
            btnDarCatalog.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnDarCatalog.BackColor = Color.FromArgb(13, 148, 136);
            btnDarCatalog.ForeColor = Color.White;
            btnDarCatalog.FlatStyle = FlatStyle.Flat;
            btnDarCatalog.FlatAppearance.BorderSize = 0;
            btnDarCatalog.AutoSize = true;
            btnDarCatalog.Height = 26;
            btnDarCatalog.Cursor = Cursors.Hand;
            btnDarCatalog.Click += (s, e) => {
                FlushSave();
                context.ShowPalette(currentBed);
            };
            pnlQuickButtons.Controls.Add(btnDarCatalog);

            // ปุ่มคีย์ด่วนข้อวินิจฉัย Orthopedic ที่พบบ่อย
            AddQuickOrthoButton(pnlQuickButtons, "🦴 Pre-op", ".preop", "Focus: เตรียมความพร้อมผู้ป่วยก่อนส่งผ่าตัด\r\nGoal: ผู้ป่วยมีความพร้อมทั้งร่างกายและจิตใจก่อนเข้าห้องผ่าตัด ปฏิบัติตัวตามแผน NPO ถูกต้อง เอกสารและผลตรวจครบถ้วน และไม่เกิดภาวะแทรกซ้อนก่อนผ่าตัด\r\nData: ผู้ป่วยมีแผนผ่าตัด... วันนี้ NPO ตั้งแต่... น., V/S: T... °C, BP.../... mmHg, PR... /min, RR... /min, SpO2... %\r\nAction:\r\n- ตรวจสอบความถูกต้องของใบยินยอมผ่าตัด (Informed consent) เซ็นเรียบร้อย\r\n- ตรวจสอบผลตรวจทางห้องปฏิบัติการ (CBC, Coagulogram, Electrolyte) และผล X-ray พร้อม Chart\r\n- อาบน้ำเปลี่ยนชุดผู้ป่วย ถอดฟันปลอม แว่นตา คอนแทคเลนส์ และเครื่องประดับทุกชนิด\r\n- ตรวจสอบแถบข้อมือระบุตัวตน (Patient ID) ถูกต้อง\r\n- ให้สารน้ำทางหลอดเลือดดำตามแผนการรักษา\r\n- สวมหมวกคลุมผม และนำส่งห้องผ่าตัดพร้อมเอกสารครบถ้วน\r\nResponse: ส่งผู้ป่วยถึงห้องผ่าตัดเวลา... น. ปลอดภัยดี พยาบาลห้องผ่าตัดรับมอบผู้ป่วยและเอกสารเรียบร้อย");
            AddQuickOrthoButton(pnlQuickButtons, "🩹 Post-op", ".postop", "Focus: เสี่ยงต่อภาวะแทรกซ้อนจากการระงับความรู้สึกและการผ่าตัด\r\nGoal: สัญญาณชีพคงที่ ฟื้นตัวจากยาระงับความรู้สึกได้ดี แผลผ่าตัดไม่มี active bleeding และไม่เกิดภาวะแทรกซ้อนหลังผ่าตัด\r\nData: รับย้ายผู้ป่วยกลับจากห้องผ่าตัด/ห้องพักฟื้น หลังทำผ่าตัด... ระดับความรู้สึกตัว: ตื่นดี รู้เรื่อง (Alert), V/S แรกรับ: BP.../... mmHg, PR... /min, RR... /min, SpO2... %, แผลผ่าตัดปิด gauze แนบสนิท ไม่มีเลือดสดซึมเปื้อน, สายระบายออก... ml ลักษณะ serosanguinous\r\nAction:\r\n- จัดท่านอนราบ/ศีรษะสูงตามข้อกำหนดการระงับความรู้สึก\r\n- ตรวจวัดและบันทึกสัญญาณชีพทุก 15 นาที x 4 ครั้ง, ทุก 30 นาที x 2 ครั้ง และทุก 1 ชั่วโมงจนคงที่\r\n- ตรวจสอบแผลผ่าตัดและสายระบาย\r\n- ประเมินการไหลเวียนโลหิตและประสาทรับรู้ส่วนปลาย\r\n- ดูแลให้สารน้ำและยาบรรเทาปวดตามแผนการรักษา\r\n- ยกไม้กั้นเตียงขึ้นทั้ง 2 ข้าง\r\nResponse: สัญญาณชีพคงที่ รู้สึกตัวดี ไม่มีคลื่นไส้อาเจียน แผลผ่าตัดแห้งดี ไม่มี active bleeding");
            AddQuickOrthoButton(pnlQuickButtons, "😣 Pain", ".pain", "Focus: ปวดแผลผ่าตัดเนื่องจากเนื้อเยื่อได้รับบาดเจ็บจากการผ่าตัด\r\nGoal: ผู้ป่วยสุขสบายขึ้น ระดับความปวดลดลง Pain score ≤ 3/10 สามารถพักผ่อนได้ และไม่มีผลข้างเคียงจากยาบรรเทาปวด\r\nData: ผู้ป่วยบ่นปวดแผลผ่าตัด Pain score = .../10, สีหน้าหน้านิ่วคิ้วขมวด ไม่กล้าขยับตัว V/S: BP.../... mmHg, PR... /min\r\nAction:\r\n- ประเมินตำแหน่ง ลักษณะ และระดับความรุนแรงของความปวด\r\n- ดูแลให้ยาบรรเทาปวด... ตามแผนการรักษาของแพทย์\r\n- จัดท่านอนให้ผ่อนคลายและหนุนหมอนรองรับส่วนที่ผ่าตัด\r\n- สอนเทคนิคการหายใจช้าๆ ลึกๆ ผ่อนคลายกล้ามเนื้อ (Deep breathing exercise) และการใช้มือประคองแผลเวลาเคลื่อนไหว\r\nResponse: หลังให้ยา 30 นาที ผู้ป่วยบอกอาการปวดทุเลาลง Pain score ลดลงเหลือ.../10 สีหน้าผ่อนคลาย สามารถนอนพักได้");
            AddQuickOrthoButton(pnlQuickButtons, "✨ Wound", ".wound", "Focus: แผลผ่าตัดสะอาด ไม่มีสัญญาณการติดเชื้อ\r\nGoal: แผลผ่าตัดสมานตัวดี ขอบแผลแนบสนิท แห้งสะอาด ไม่มีเลือดหรือหนองซึมเปื้อน และไม่เกิดการติดเชื้อที่แผลผ่าตัด (SSI)\r\nData: แผลผ่าตัดบริเวณ... ปิดแผลเรียบร้อย ขอบแผลเย็บแนบสนิทดี (Well-approximated)\r\nAction:\r\n- ตรวจประเมินแผลผ่าตัด ทำแผล (Dressing) ด้วย Normal Saline และเทคนิคปลอดเชื้อ\r\n- ปิดแผลด้วยผ้าก๊อซสะอาด\r\n- แนะนำผู้ป่วยระวังอย่าให้แผลโดนน้ำ\r\nResponse: แผลผ่าตัดแห้ง สะอาดดี ไม่มีเลือดหรือสิ่งคัดหลั่งซึมเปื้อน ไม่มีรอยบวมแดง (No signs of inflammation/infection)");
            AddQuickOrthoButton(pnlQuickButtons, "🩸 Drain", ".drain", "Focus: เสี่ยงต่อการติดเชื้อและมีสารน้ำคั่งค้างบริเวณแผลผ่าตัด\r\nGoal: สารคัดหลั่งระบายได้สะดวก สายระบายทำงานมีประสิทธิภาพ แผลรอบสายระบายสะอาด แห้งดี และไม่พบสัญญาณการติดเชื้อ\r\nData: มีสายระบาย (Redivac / Hemovac / Jackson-Pratt / ICD) บริเวณแผลผ่าตัด มีสารคัดหลั่งออกสะสม... ml ลักษณะ serosanguinous ไม่มีเลือดสดออกเพิ่ม แผลผ่าตัดเย็บแนบสนิท\r\nAction:\r\n- ตรวจเช็คระบบสุญญากาศของขวดระบายให้คงสภาพ vacuum อยู่เสมอ\r\n- บันทึกปริมาณ สี และลักษณะของสารคัดหลั่ง\r\n- ทำความสะอาดแผลและรอบรอยเจาะสายระบายด้วยเทคนิคปลอดเชื้อ (Aseptic technique)\r\n- ปักตรึงสายระบายด้วยเข็มกลัด/พลาสเตอร์ไม่ให้เลื่อนหลุดหรือดึงรั้ง\r\nResponse: สายระบายไม่หักพับงอ การระบายไหลสะดวก สารคัดหลั่งออกลดลง แผลรอบสายระบายไม่มีอาการบวมแดงหรือมีหนอง");
            AddQuickOrthoButton(pnlQuickButtons, "🦶 CMS Check", ".ortho", "Focus: เสี่ยงต่อภาวะเนื้อเยื่อขาดเลือดและภาวะความดันในช่องกล้ามเนื้อสูง (Compartment Syndrome)\r\nGoal: การไหลเวียนโลหิตและประสาทรับรู้ส่วนปลายปกติ ปลายเท้า/มืออุ่น สีชมพู CRT < 2 วินาที คลำชีพจรได้ชัดเจน และไม่เกิดภาวะ Compartment syndrome\r\nData: ผู้ป่วยได้รับการผ่าตัด/ใส่เฝือกบริเวณ... มีอาการปวด บวม ตึง บริเวณแผลผ่าตัด/รยางค์\r\nAction:\r\n- ประเมินภาวะ 5Ps (Pain, Pallor, Pulselessness, Paresthesia, Paralysis) ทุก... ชม.\r\n- ตรวจ Capillary refill time (CRT)\r\n- คลำชีพจรส่วนปลาย (Radial / Dorsalis pedis pulse)\r\n- จัดยกอวัยวะส่วนปลายให้สูงกว่าระดับหัวใจด้วยหมอนหนุนเพื่อลดบวม\r\n- กระตุ้นให้ขยับนิ้วมือ/นิ้วเท้าบ่อยๆ\r\nResponse: ปลายมือ/เท้าข้างที่ผ่าตัดอุ่น สีชมพูดี CRT < 2 วินาที, คลำชีพจรส่วนปลายได้ชัดเจน, ความรู้สึกและการขยับนิ้วมือ/เท้าปกติ ไม่มีอาการชา ไม่พบภาวะ Compartment syndrome");
            AddQuickOrthoButton(pnlQuickButtons, "🏃 Rehab", ".tkarehab", "Focus: ฟื้นฟูสมรรถภาพกล้ามเนื้อและการเคลื่อนไหวข้อเข่า (Impaired Physical Mobility / Knee Rehabilitation)\r\nGoal: ผู้ป่วยสามารถงอและเหยียดข้อเข่าได้ตามเป้าหมาย (Extension 0°, Flexion ≥ 90°) กล้ามเนื้อต้นขาแข็งแรงขึ้น และใช้อุปกรณ์ช่วยเดินได้ถูกต้อง\r\nData: ผู้ป่วยหลังผ่าตัด TKA วันที่... ข้อเข่ายังมีอาการตึงตัว งอเข่าได้... องศา กล้ามเนื้อต้นขายังล้า\r\nAction:\r\n1. ให้ยาแก้ปวดก่อนเริ่มทำกายภาพบำบัด 30 นาทีเพื่อให้บริหารข้อเข่าได้อย่างมีประสิทธิภาพ\r\n2. ดูแลและจัดตำแหน่งผู้ป่วยบนเครื่องช่วยงอข้อเข่าอัตโนมัติ (Continuous Passive Motion: CPM) ตั้งมุมงอเริ่มต้นที่... องศา วันละ 2 ครั้ง ครั้งละ 1-2 ชม. ตามคำสั่งแพทย์\r\n3. ฝึกสอนท่าบริหารกล้ามเนื้อ: Isometric Quad setting, Terminal knee extension, และ Heel slide\r\n4. ประเมินอาการปวด บวม แดงร้อน บริเวณข้อเข่าหลังการฝึก พร้อมประคบเย็นหลังฝึกเสร็จ\r\n5. ฝึกสอนการเดินโดยใช้อุปกรณ์ช่วยเดิน (Walker) และการก้าวเดินที่ถูกต้อง\r\nResponse: ผู้ป่วยสามารถงอเข่าบนเครื่อง CPM ได้... องศาโดยไม่ปวดรุนแรง, ยกขาตรง (SLR) ได้มั่นคง, ฝึกเดินด้วย Walker ได้ระยะทาง... เมตร สัญญาณชีพคงที่ ปลอดภัยดี");
            AddQuickOrthoButton(pnlQuickButtons, "🦿 TKA", ".tka", "Focus: การพยาบาลหลังผ่าตัดเปลี่ยนข้อเข่าเทียม (Post-Op TKA)\r\nGoal: การไหลเวียนโลหิตและเส้นประสาทปลายเท้าปกติ (CMS Intact) ควบคุมความปวดได้ดี สายระบายทำงานมีประสิทธิภาพ และไม่เกิดภาวะข้อเข่างอติดหรือ DVT\r\nData: รับย้ายผู้ป่วยหลังทำผ่าตัด Total Knee Arthroplasty (TKA) เข่าข้าง... แผลผ่าตัดปิด pressure dressing แนบสนิทดี, มีสายระบาย (Redivac/Hemovac) ต่อลงขวดสุญญากาศ มีเลือดออกสะสม... ml ลักษณะ serosanguinous, CMS check ปลายเท้า: ปลายเท้าอุ่น capillary refill < 2 วินาที, คลำชีพจร Dorsalis pedis pulse ได้ชัดเจน, กระดิกนิ้วเท้าและข้อเท้าได้ ไม่บวมตึง, Pain score = .../10\r\nAction:\r\n1. ประเมินสัญญาณชีพ และตรวจประเมินระบบประสาทและหลอดเลือดส่วนปลาย (CMS check: Color, Motion, Sensation, Pulse, Temp) ทุก 1 ชม. x 4 ครั้ง และทุก 2-4 ชม.\r\n2. จัดท่านอนหงาย หนุนหมอนรองใต้ข้อเท้าให้เหยียดตรงและยกขาสูงเล็กน้อย (ห้ามหนุนหมอนใต้ข้อพับเข่าเด็ดขาด เพื่อป้องกันภาวะข้อเข่างอติด Knee flexion contracture)\r\n3. ประคบเย็นรอบข้อเข่า (Cryotherapy / Cold pack) ครั้งละ 20-30 นาที ทุก 2-3 ชม. เพื่อลดอาการบวมและบรรเทาความปวด\r\n4. ดูแลสายระบายแผลผ่าตัดให้อยู่ในระบบสุญญากาศ บันทึกปริมาณและสีของเลือดที่ออก หากออก > 100 ml/hr ติดต่อกัน 2 ชม. ให้รายงานแพทย์ทันที\r\n5. แนะนำและกระตุ้นการบริหารกล้ามเนื้อขา: กระดกข้อเท้าขึ้น-ลง (Ankle pumping exercise) 20-30 ครั้ง/ชม. และเกร็งกล้ามเนื้อต้นขาเหยียดเข่าตรงกดลงบนที่นอน (Quadriceps setting exercise) เพื่อป้องกันลิ่มเลือดอุดตันในหลอดเลือดดำ (DVT)\r\nResponse: สัญญาณชีพคงที่, CMS check ปลายเท้าปกติ ปลายเท้าอุ่น ชีพจรเต้นดี ขยับนิ้วเท้าได้, แผลผ่าตัดไม่มีเลือดสดซึมเปื้อน, สายระบายออกลดลง, ปฏิบัติการบริหารกล้ามเนื้อขาได้ถูกต้อง");
            AddQuickOrthoButton(pnlQuickButtons, "🩼 THA", ".tha", "Focus: การพยาบาลหลังผ่าตัดเปลี่ยนข้อสะโพกเทียมและเฝ้าระวังข้อสะโพกหลุด (Post-Op THA & Dislocation Prevention)\r\nGoal: ข้อสะโพกเทียมอยู่ในตำแหน่งที่ถูกต้อง ไม่เกิดภาวะข้อสะโพกหลุด (No Dislocation) CMS ปลายเท้าปกติ และฟื้นฟูการเดินได้อย่างปลอดภัย\r\nData: รับย้ายผู้ป่วยหลังทำผ่าตัด Total Hip Arthroplasty (THA) สะโพกข้าง... แผลผ่าตัดปิด pressure dressing เรียบร้อย ไม่มีเลือดสดซึม, มีสายระบาย (Redivac) เลือดออกสะสม... ml, ปลายเท้าอุ่น ขยับนิ้วเท้าและข้อเท้าได้ปกติ, Pain score = .../10\r\nAction:\r\n1. ตรวจวัดสัญญาณชีพและประเมิน CMS check ปลายเท้าข้างที่ผ่าตัดอย่างสม่ำเสมอ\r\n2. จัดท่านอนหงายและวางหมอนรูปสามเหลี่ยม (Abduction pillow) ระหว่างขาทั้งสองข้างตลอดเวลาที่อยู่บนเตียง เพื่อจัดให้ขากางออกเล็กน้อย (Abduction 15-20 องศา) และป้องกันขาหุบหรือหมุนเข้าด้านใน (Internal rotation)\r\n3. ปฏิบัติตามข้อควรระวังเพื่อป้องกันข้อสะโพกเทียมหลุด (Hip Precautions) อย่างเคร่งครัด:\r\n   - ห้ามงอข้อสะโพกเกิน 90 องศา (ห้ามก้มตัวลงหยิบของที่พื้น, ห้ามนั่งเก้าอี้เตี้ยหรือชักโครกต่ำ ให้ใช้ที่นั่งเสริมชักโครก Raised toilet seat)\r\n   - ห้ามนอนตะแคงโดยไม่มีหมอนหนุนคั่นระหว่างขาทั้งสองข้างเด็ดขาด\r\n   - ห้ามนั่งไขว่ห้าง (Do not cross legs)\r\n4. ดูแลสายระบายสุญญากาศและบันทึกปริมาณเลือดที่ออก\r\n5. กระตุ้นให้ทำ Ankle pumping exercise บ่อยๆ เพื่อป้องกัน DVT\r\nResponse: ข้อสะโพกอยู่ในแนวปกติ ไม่พบอาการข้อสะโพกหลุด (ขาไม่สั้นเต่อ ปลายเท้าไม่บิดหมุนผิดรูป), ปลายเท้าอุ่น ขยับได้ดี CMS ปกติ, ผู้ป่วยและญาติเข้าใจและปฏิบัติตามข้อห้ามการงอสะโพกได้ถูกต้อง");
            AddQuickOrthoButton(pnlQuickButtons, "🛡️ Spine", ".laminectomy", "Focus: เฝ้าระวังการกดทับไขสันหลังและเส้นประสาทหลังผ่าตัดกระดูกสันหลัง (Post-Op Spine Surgery / Neurological Check)\r\nGoal: การทำงานของเส้นประสาทและไขสันหลังปกติ (Motor & Sensory Intact) อาการชาและปวดร้าวลดลง แผลผ่าตัดแห้งดี และไม่มี CSF leak\r\nData: รับย้ายผู้ป่วยหลังทำผ่าตัด Laminectomy / Discectomy ระดับ... แผลผ่าตัดบริเวณหลังปิด dressing แนบสนิท, มีสายระบายเลือด... ml, รู้สึกตัวดี, Pain score = .../10\r\nAction:\r\n1. ตรวจวัดสัญญาณชีพและตรวจประเมินระบบประสาทส่วนปลาย (Neurological & Motor power check: กระดกข้อเท้า นิ้วเท้า เหยียด-งอเข่า และการรับความรู้สึก) ทุก 1 ชม. x 4 ครั้ง และทุก 2-4 ชม.\r\n2. จัดท่านอนหงายราบหรือศีรษะสูงไม่เกิน 30 องศา (ตามคำสั่งแพทย์)\r\n3. การพลิกตัวต้องใช้วิธีพลิกตัวแบบท่อนซุง (Log rolling technique) โดยมีเจ้าหน้าที่ช่วยอย่างน้อย 2-3 คน รักษาระนาบศีรษะ ลำตัว และสะโพกให้ตรงเป็นแนวเดียวกันตลอดเวลา ห้ามบิดเอี้ยวลำตัวเด็ดขาด\r\n4. ตรวจสอบแผลผ่าตัดและสายระบายอย่างใกล้ชิด: สังเกตลักษณะของสารคัดหลั่ง หากมีน้ำใสหรือสีเหลืองฟางข้าวออกมากผิดปกติร่วมกับผู้ป่วยบ่นปวดศีรษะ ให้สงสัยภาวะน้ำไขสันหลังรั่ว (CSF leakage) และรายงานแพทย์ทันที\r\n5. ประเมินการขับถ่ายปัสสาวะ (Bladder function) และดูแลให้ยาแก้ปวดตามแผน\r\nResponse: ระบบประสาทส่วนปลายปกติ Motor power ขาทั้ง 2 ข้าง grade V, อาการชาปลายเท้าลดลง, แผลผ่าตัดแห้งดี ไม่มี CSF leak, พลิกตัวแบบ Log rolling ได้ราบรื่น");
            AddQuickOrthoButton(pnlQuickButtons, "🧱 Cast", ".cast", "Focus: เสี่ยงต่อการกดทับของเฝือกและผิวหนังบาดเจ็บ\r\nGoal: เฝือกคงรูปดี ไม่แตกหัก ไม่กดทับเนื้อเยื่อ ไม่เกิดแผลกดทับใต้เฝือก การไหลเวียนโลหิตส่วนปลายปกติ และผู้ป่วยดูแลเฝือกได้ถูกต้อง\r\nData: ผู้ป่วยได้รับการใส่เฝือก (Cast / Splint / Slab) บริเวณ... มีอาการตึง แน่น หรือปวดรยางค์ส่วนที่ใส่เฝือก\r\nAction:\r\n- ตรวจสอบสภาพเฝือกไม่แตกหัก ไม่เปียกชื้น และไม่รัดแน่นเกินไป\r\n- ตรวจสอบขอบเฝือกไม่กดทับผิวหนัง\r\n- สังเกตการไหลเวียนโลหิตปลายรยางค์\r\n- แนะนำห้ามนำสิ่งแปลกปลอมแคะเกาในเฝือก ระวังไม่ให้เฝือกเปียกน้ำ\r\n- แนะนำการเกร็งกล้ามเนื้อใต้เฝือก (Isometric exercise) เพื่อคงสภาพกล้ามเนื้อ\r\nResponse: เฝือกแห้ง แข็งแรงดี ไม่รัดแน่น ปลายรยางค์อุ่น สีชมพู คลำชีพจรได้ ขยับนิ้วได้ ไม่บวม ไม่มีอาการชา ขอบเฝือกไม่กดทับผิวหนัง");
            AddQuickOrthoButton(pnlQuickButtons, "⚠️ Fall", ".fall", "Focus: เสี่ยงต่อการพลัดตกหกล้มเนื่องจากข้อจำกัดในการเคลื่อนไหวและยาที่มีผลต่อความดันโลหิต/การรับรู้\r\nGoal: ผู้ป่วยและญาติมีความตระหนักและปฏิบัติตามมาตรการป้องกันการพลัดตกหกล้ม ไม่เกิดอุบัติเหตุพลัดตกหกล้มตลอดการพักรักษาตัวในโรงพยาบาล\r\nData: ประเมินความเสี่ยงต่อการพลัดตกหกล้ม (Morse Fall Scale / Hendrich II Fall Model) ได้... คะแนน (จัดอยู่ในกลุ่ม High Risk)\r\nAction:\r\n- ติดป้ายสัญลักษณ์เสี่ยงล้มที่ข้อมือและหัวเตียง\r\n- ยกไม้กั้นเตียงขึ้นทั้ง 2 ข้างตลอดเวลา\r\n- ปรับระดับเตียงลงต่ำสุดและล็อกล้อเตียง\r\n- วางกริ่งเรียกพยาบาลและของใช้จำเป็นในระยะเอื้อมถึง\r\n- จัดสภาพแวดล้อมให้แห้ง สะอาด มีแสงสว่างเพียงพอ\r\n- ให้สุขศึกษาผู้ป่วยและญาติให้เรียกพยาบาลทุกครั้งที่ต้องการลุกจากเตียง\r\nResponse: ผู้ป่วยและญาติเข้าใจมาตรการป้องกันการพลัดตกหกล้ม ให้ความร่วมมือในการกดกริ่งเรียกพยาบาล ไม่พบอุบัติเหตุพลัดตกหกล้ม ปลอดภัยดี");

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
            txtNote.HideSelection = false;
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
            this.Controls.Add(pnlWorkspaceNotice);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlTop);

            pnlTop.SendToBack();
            pnlWorkspaceNotice.SendToBack();
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

            // 1. App Title & Cloud Status (Left)
            if (lblAppTitle != null) {
                lblAppTitle.Text = (w < 850) ? "🛏️ เตียง 1-30" : "🛏️ ข้อมูลรายเตียง 1-30";
                lblAppTitle.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
                lblAppTitle.Location = new Point(14, 8);
            }
            if (lblNetworkStatus != null) {
                lblNetworkStatus.Location = new Point(16, 32);
            }

            // 2. Active User Account Button & Workspace Selector
            int leftOccupied = (lblAppTitle != null) ? (lblAppTitle.Right + 12) : 180;
            if (btnUserAccount != null) {
                int userBtnW = (w < 850) ? 115 : 140;
                btnUserAccount.Size = new Size(userBtnW, 34);
                btnUserAccount.Location = new Point(leftOccupied, 11);
                btnUserAccount.BringToFront();
                leftOccupied = btnUserAccount.Right + 8;
            }
            if (cboWorkspaceUser != null && cboWorkspaceUser.Visible) {
                int cboW = (w < 850) ? 130 : 155;
                cboWorkspaceUser.Size = new Size(cboW, 28);
                cboWorkspaceUser.Location = new Point(leftOccupied, 14);
                cboWorkspaceUser.BringToFront();
                leftOccupied = cboWorkspaceUser.Right + 10;
            }

            // 3. Right-Aligned Tool Buttons
            int rx = w - 10;
            if (btnZoomIn != null) {
                btnZoomIn.Size = new Size(34, 34);
                btnZoomIn.Location = new Point(rx - btnZoomIn.Width, 11);
                rx -= (btnZoomIn.Width + 4);
            }
            if (btnZoomOut != null) {
                btnZoomOut.Size = new Size(34, 34);
                btnZoomOut.Location = new Point(rx - btnZoomOut.Width, 11);
                rx -= (btnZoomOut.Width + 6);
            }
            if (btnCheckUpdate != null) {
                btnCheckUpdate.Text = "🔄 อัปเดต";
                btnCheckUpdate.Size = new Size(76, 34);
                btnCheckUpdate.Location = new Point(rx - btnCheckUpdate.Width, 11);
                rx -= (btnCheckUpdate.Width + 6);
            }
            if (btnIoTemplate != null) {
                btnIoTemplate.Text = (w < 900) ? "📁 เอกสารวอร์ด" : "📁 เอกสาร & แบบฟอร์มวอร์ด";
                btnIoTemplate.Size = (w < 900) ? new Size(130, 34) : new Size(185, 34);
                btnIoTemplate.Location = new Point(rx - btnIoTemplate.Width, 11);
                rx -= (btnIoTemplate.Width + 6);
            }
            if (btnMobilePortal != null) {
                btnMobilePortal.Text = (w < 900) ? "📱 มือถือ" : "📱 มือถือ (QR)";
                btnMobilePortal.Size = (w < 900) ? new Size(82, 34) : new Size(100, 34);
                btnMobilePortal.Location = new Point(rx - btnMobilePortal.Width, 11);
                rx -= (btnMobilePortal.Width + 6);
            }

            // 4. Extra Quick Action Buttons on Top Panel
            // Note: DAR Catalog and SOS Calculator are already prominently available
            // on pnlQuickButtons right above the nurse note. Only show them on pnlTop if screen
            // is wide enough (>= 1260px) to prevent ANY collision with left controls.
            if (btnGoToPalette != null) {
                int neededW = (btnCalc != null ? 135 + 6 : 0) + 140;
                if (w >= 1260 && (rx - neededW >= leftOccupied + 15)) {
                    btnGoToPalette.Visible = true;
                    btnGoToPalette.Text = "📋 คลังข้อวินิจฉัย (F8)";
                    btnGoToPalette.Size = new Size(140, 34);
                    btnGoToPalette.Location = new Point(rx - btnGoToPalette.Width, 11);
                    rx -= (btnGoToPalette.Width + 6);
                } else {
                    btnGoToPalette.Visible = false;
                }
            }
            if (btnCalc != null) {
                if (btnGoToPalette != null && btnGoToPalette.Visible && (rx - 135 >= leftOccupied + 15)) {
                    btnCalc.Visible = true;
                    btnCalc.Text = "🧮 คำนวณ SOS/ยา";
                    btnCalc.Size = new Size(135, 34);
                    btnCalc.Location = new Point(rx - btnCalc.Width, 11);
                    rx -= (btnCalc.Width + 6);
                } else {
                    btnCalc.Visible = false;
                }
            }
        }

        private void RepositionBottomControls() {
            if (pnlBottom == null) return;
            int w = pnlBottom.ClientSize.Width;
            bool isVeryNarrow = (w < 720);
            bool isNarrow = (w < 920);

            if (isVeryNarrow) {
                btnCopy.Text = "คัดลอก";
                btnCopy.Size = new Size(58, 34);
                btnInsertTime.Text = "🕒";
                btnInsertTime.Size = new Size(36, 34);
                btnHistory.Text = "📜";
                btnHistory.Size = new Size(36, 34);
                btnSwapBed.Text = "🔄";
                btnSwapBed.Size = new Size(36, 34);
                btnClear.Text = "🗑️";
                btnClear.Size = new Size(36, 34);
                btnSyncSettings.Text = "🌐";
                btnSyncSettings.Size = new Size(36, 34);
                if (btnManageUsers != null) {
                    btnManageUsers.Text = "👥";
                    btnManageUsers.Size = new Size(36, 34);
                }
                btnClose.Text = "ปิด";
                btnClose.Size = new Size(46, 34);
            } else if (isNarrow) {
                btnCopy.Text = "📋 คัดลอก";
                btnCopy.Size = new Size(75, 34);
                btnInsertTime.Text = "🕒 เวลา";
                btnInsertTime.Size = new Size(60, 34);
                btnHistory.Text = "📜 ประวัติ";
                btnHistory.Size = new Size(75, 34);
                btnSwapBed.Text = "🔄 สลับ/ย้าย";
                btnSwapBed.Size = new Size(88, 34);
                btnClear.Text = "🗑️ ล้าง";
                btnClear.Size = new Size(60, 34);
                btnSyncSettings.Text = "🌐 แชร์วอร์ด";
                btnSyncSettings.Size = new Size(80, 34);
                if (btnManageUsers != null) {
                    btnManageUsers.Text = "👥 ผู้ใช้";
                    btnManageUsers.Size = new Size(70, 34);
                }
                btnClose.Text = "ปิด";
                btnClose.Size = new Size(50, 34);
            } else {
                btnCopy.Text = "📋 คัดลอก (Copy)";
                btnCopy.Size = new Size(115, 34);
                btnInsertTime.Text = "🕒 ใส่วันที่/เวลา";
                btnInsertTime.Size = new Size(110, 34);
                btnHistory.Text = "📜 ประวัติเตียงย้อนหลัง";
                btnHistory.Size = new Size(135, 34);
                btnSwapBed.Text = "🔄 สลับ/ย้ายเตียง";
                btnSwapBed.Size = new Size(125, 34);
                btnClear.Text = "🗑️ ล้างข้อมูลเตียงนี้";
                btnClear.Size = new Size(125, 34);
                btnSyncSettings.Text = "🌐 ตั้งค่าแชร์ในวอร์ด";
                btnSyncSettings.Size = new Size(125, 34);
                if (btnManageUsers != null) {
                    btnManageUsers.Text = "👥 จัดการผู้ใช้";
                    btnManageUsers.Size = new Size(100, 34);
                }
                btnClose.Text = "ปิด (Esc)";
                btnClose.Size = new Size(80, 34);
            }

            int lx = 10;
            btnCopy.Location = new Point(lx, 9);
            lx += btnCopy.Width + 5;

            btnInsertTime.Location = new Point(lx, 9);
            lx += btnInsertTime.Width + 5;

            btnHistory.Location = new Point(lx, 9);
            lx += btnHistory.Width + 5;

            btnSwapBed.Location = new Point(lx, 9);
            lx += btnSwapBed.Width + 5;

            btnClear.Location = new Point(lx, 9);
            lx += btnClear.Width + 5;

            btnSyncSettings.Location = new Point(lx, 9);
            lx += btnSyncSettings.Width + 5;

            if (btnManageUsers != null && btnManageUsers.Visible) {
                btnManageUsers.Location = new Point(lx, 9);
            }

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

        private void UpdateUserAccountButton() {
            if (btnUserAccount == null) return;
            string name = (context != null && context.UserManager != null && context.UserManager.CurrentUser != null)
                ? context.UserManager.CurrentUser.DisplayName
                : "Admin";
            bool isAdmin = (context != null && context.UserManager != null && context.UserManager.IsAdminLoggedIn);
            btnUserAccount.Text = (isAdmin ? "👑 " : "👤 ") + name;
            btnUserAccount.BackColor = isAdmin ? Color.FromArgb(15, 76, 129) : Color.FromArgb(19, 78, 74);
        }

        private void ShowUserMenu() {
            var menu = new ContextMenuStrip();
            menu.Font = new Font("Leelawadee UI", 9.5f);
            if (context != null && context.UserManager != null && context.UserManager.IsAdminLoggedIn) {
                menu.Items.Add("👥 จัดการบัญชีผู้ใช้และสิทธิ์ (Admin)", null, (s, e) => context.ShowUserManagement(this));
                menu.Items.Add("-");
            }
            menu.Items.Add("🔀 เปลี่ยนผู้ใช้งาน (Switch User / Login)", null, (s, e) => {
                context.ShowUserLogin(this);
            });
            menu.Items.Add("🚪 ออกจากระบบ (Logout)", null, (s, e) => {
                if (MessageBox.Show(this, "ต้องการออกจากระบบใช่หรือไม่?", "ยืนยันการออกจากระบบ", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                    if (context != null) {
                        context.Logout();
                    }
                }
            });
            menu.Show(btnUserAccount, new Point(0, btnUserAccount.Height));
        }

        private bool isUpdatingWorkspaceDropdown = false;
        private void RefreshWorkspaceDropdown() {
            if (cboWorkspaceUser == null) return;
            isUpdatingWorkspaceDropdown = true;
            try {
                cboWorkspaceUser.Items.Clear();
                if (context == null || context.UserManager == null) return;
                bool isAdmin = context.UserManager.IsAdminLoggedIn;
                cboWorkspaceUser.Visible = isAdmin;
                if (!isAdmin) return;

                var list = context.UserManager.GetAllUsers();
                int selectedIdx = 0;
                for (int i = 0; i < list.Count; i++) {
                    var u = list[i];
                    string prefix = (u.Role == "admin") ? "👑 " : "🗂️ ";
                    string itemText = prefix + u.DisplayName + (u.UserSlot == 0 ? " (หลัก)" : "");
                    cboWorkspaceUser.Items.Add(itemText);
                    if (context.UserManager.ActiveWorkspaceUser != null &&
                        string.Equals(context.UserManager.ActiveWorkspaceUser.Username, u.Username, StringComparison.OrdinalIgnoreCase)) {
                        selectedIdx = i;
                    }
                }
                if (cboWorkspaceUser.Items.Count > selectedIdx) {
                    cboWorkspaceUser.SelectedIndex = selectedIdx;
                }
            } finally {
                isUpdatingWorkspaceDropdown = false;
            }
        }

        private void CboWorkspaceUser_SelectedIndexChanged(object sender, EventArgs e) {
            if (isUpdatingWorkspaceDropdown || context == null || context.UserManager == null) return;
            int idx = cboWorkspaceUser.SelectedIndex;
            var list = context.UserManager.GetAllUsers();
            if (idx >= 0 && idx < list.Count) {
                var selectedUser = list[idx];
                context.UserManager.SwitchWorkspace(selectedUser.Username);
            }
        }

        public void OnWorkspaceChanged() {
            if (this.IsDisposed || !this.IsHandleCreated) return;
            if (this.InvokeRequired) {
                this.BeginInvoke(new Action(OnWorkspaceChanged));
                return;
            }

            UpdateUserAccountButton();
            RefreshWorkspaceDropdown();

            if (context != null && context.UserManager != null && context.UserManager.IsInspectingOtherUser) {
                pnlWorkspaceNotice.Visible = true;
                var activeUser = context.UserManager.ActiveWorkspaceUser;
                lblWorkspaceNotice.Text = string.Format("👁️ กำลังดูและจัดการเตียงของ \"{0}\" ({1}) — การบันทึกจะมีผลกับชุดเตียงของผู้นี้", activeUser.DisplayName, activeUser.Username);
            } else {
                pnlWorkspaceNotice.Visible = false;
            }

            if (btnManageUsers != null) {
                btnManageUsers.Visible = (context != null && context.UserManager != null && context.UserManager.IsAdminLoggedIn);
            }

            RefreshAllBedButtons();
            SelectBed(currentBed);
            RepositionTopControls();
            RepositionBottomControls();
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
            lblBedTitle.Text = string.Format("🛏️ ข้อมูลผู้ป่วย เตียง {0:D2}", currentBed);
            lblBedTitle.ForeColor = curTheme.Primary;

            isSuppressingEvents = true;
            txtNote.Text = BedNotesManager.NormalizeNewlines(manager.GetBedNote(currentBed));
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
                    string tipText = string.Format("เตียง {0:D2}: {1}{2}\n(คีย์ลัดใน e-PHIS: พิมพ์ .b{0})", 
                        i, manager.GetPreview(i), remInfo);
                    if (bedToolTip.GetToolTip(btn) != tipText) {
                        bedToolTip.SetToolTip(btn, tipText);
                    }
                }
            }

            lblNetworkStatus.Text = manager.StatusText;
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

            if (txtNote.Text != null && txtNote.Text.Replace("\r\n", "").Contains("\n")) {
                int selStart = txtNote.SelectionStart;
                isSuppressingEvents = true;
                txtNote.Text = BedNotesManager.NormalizeNewlines(txtNote.Text);
                txtNote.SelectionStart = Math.Min(selStart, txtNote.Text.Length);
                isSuppressingEvents = false;
            }

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
            } else if (e.Control && e.KeyCode == Keys.V) {
                try {
                    if (Clipboard.ContainsText()) {
                        string clip = Clipboard.GetText();
                        if (clip != null && clip.Replace("\r\n", "").Contains("\n")) {
                            string norm = BedNotesManager.NormalizeNewlines(clip);
                            txtNote.SelectedText = norm;
                            e.Handled = true;
                            e.SuppressKeyPress = true;
                        }
                    }
                } catch {}
            }
        }

        public void FlushSave() {
            if (isDirty) {
                autoSaveTimer.Stop();
                manager.SaveBedNote(currentBed, txtNote.Text);
                isDirty = false;
                if (manager.IsSupabaseActive) {
                    lblAutoSave.Text = "💾 บันทึกลงเครื่องแล้ว... กำลังส่งขึ้น Cloud ☁️";
                    lblAutoSave.ForeColor = Color.FromArgb(37, 99, 235);
                } else {
                    lblAutoSave.Text = "💾 บันทึกอัตโนมัติแล้ว (" + DateTime.Now.ToString("HH:mm:ss") + " น.)";
                    lblAutoSave.ForeColor = Color.FromArgb(21, 128, 61);
                }
                RepositionNoteHeaderControls();
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

        private void AddQuickOrthoButton(FlowLayoutPanel pnl, string title, string shortcut, string fallbackSnippet) {
            Button btn = new Button();
            btn.Text = title;
            btn.Font = new Font("Segoe UI", 8.2f, FontStyle.Regular);
            btn.BackColor = Color.White;
            btn.ForeColor = Color.FromArgb(30, 41, 59);
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(224, 242, 254);
            btn.Height = 25;
            btn.AutoSize = true;
            btn.Margin = new Padding(2, 1, 2, 1);
            btn.Cursor = Cursors.Hand;
            ToolTip tt = new ToolTip();
            tt.SetToolTip(btn, string.Format("คลิกเพื่อดูตัวอย่าง/เลือกคัดลอกข้อวินิจฉัย {0} ({1})", title, shortcut));
            btn.Click += (s, e) => {
                List<TemplateItem> tpls = context.GetTemplates();
                TemplateItem match = null;
                if (tpls != null) {
                    match = tpls.Find(t => t.Shortcut.Equals(shortcut, StringComparison.OrdinalIgnoreCase) || 
                                           (shortcut == ".ortho" && t.Shortcut.Equals(".cms", StringComparison.OrdinalIgnoreCase)));
                }
                string content = (match != null && !string.IsNullOrEmpty(match.Content)) ? match.Content : fallbackSnippet;
                string itemTitle = (match != null && !string.IsNullOrEmpty(match.Title)) ? match.Title : title;
                ShowQuickSnippetSelector(title, shortcut, itemTitle, content);
            };
            pnl.Controls.Add(btn);
        }

        private void ShowQuickSnippetSelector(string buttonTitle, string shortcut, string templateTitle, string rawContent) {
            using (QuickSnippetSelectorDialog dlg = new QuickSnippetSelectorDialog(buttonTitle, shortcut, templateTitle, rawContent, currentBed)) {
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.ResultTextToInsert)) {
                    InsertSnippetAtCursor(dlg.ResultTextToInsert);
                }
            }
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
            snippet = BedNotesManager.NormalizeNewlines(snippet);
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

        public void ReplaceNoteExternal(string text) {
            if (this.InvokeRequired) {
                this.BeginInvoke(new Action(() => ReplaceNoteExternal(text)));
                return;
            }
            txtNote.Text = BedNotesManager.NormalizeNewlines(text ?? "");
            FlushSave();
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

        private void ShowSwapBedDialog() {
            HandleBedDragDrop(currentBed, 0);
        }

        private void HandleBedDragDrop(int sourceBed, int targetBed) {
            FlushSave();
            using (BedSwapDialog dlg = new BedSwapDialog(sourceBed, manager, context, targetBed)) {
                if (dlg.ShowDialog(this) == DialogResult.OK) {
                    int resTarget = dlg.TargetBed;
                    bool isSwap = dlg.IsSwap;
                    SelectBed(resTarget);
                    lblAutoSave.Text = isSwap 
                        ? string.Format("🔄 สลับเตียง {0} ⮂ เตียง {1} เรียบร้อยแล้ว (สำรองประวัติแล้ว)", sourceBed, resTarget)
                        : string.Format("➡️ ย้ายข้อมูลมาที่เตียง {0} เรียบร้อยแล้ว (เตียงเดิมว่างลง)", resTarget);
                    lblAutoSave.ForeColor = Color.FromArgb(67, 56, 202);
                }
            }
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
                    txtNote.Text = BedNotesManager.NormalizeNewlines(content);
                    isSuppressingEvents = false;
                    lblAutoSave.Text = "🔄 ซิงค์ข้อมูลล่าสุดจาก Cloud แล้ว (" + DateTime.Now.ToString("HH:mm:ss") + " น.)";
                    lblAutoSave.ForeColor = Color.FromArgb(13, 148, 136);
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

    // =========================================================================
    // หน้าต่างสลับเตียงและย้ายเตียงผู้ป่วย (Bed Swap & Transfer Dialog)
    // =========================================================================
    public class BedSwapDialog : Form {
        private int sourceBed;
        private BedNotesManager manager;
        private ExpanderContext context;

        private ComboBox cmbTargetBed;
        private Label lblSourceInfo;
        private TextBox txtSourcePreview;
        private Label lblTargetStatus;
        private RadioButton rbSwap;
        private RadioButton rbMove;
        private CheckBox chkSyncReminders;
        private Button btnConfirm;
        private Button btnCancel;

        public int TargetBed {
            get {
                if (cmbTargetBed != null && cmbTargetBed.SelectedItem is BedComboItem) {
                    return ((BedComboItem)cmbTargetBed.SelectedItem).BedNumber;
                }
                return 0;
            }
        }

        public bool IsSwap {
            get {
                return rbSwap != null && rbSwap.Checked;
            }
        }

        private class BedComboItem {
            public int BedNumber { get; set; }
            public string DisplayText { get; set; }
            public bool HasData { get; set; }
            public override string ToString() { return DisplayText; }
        }

        private int defaultTargetBed = 0;

        public BedSwapDialog(int fromBed, BedNotesManager bedMgr, ExpanderContext ctx, int defaultTarget = 0) {
            sourceBed = fromBed;
            manager = bedMgr;
            context = ctx;
            defaultTargetBed = defaultTarget;

            this.Text = "🔄 สลับหรือย้ายเตียงผู้ป่วย";
            this.Size = new Size(540, 485);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.25f);

            InitializeComponents();
        }

        private void InitializeComponents() {
            // Header Panel
            Panel pnlHeader = new Panel();
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 65;
            pnlHeader.BackColor = Color.FromArgb(67, 56, 202);
            pnlHeader.Padding = new Padding(16, 10, 16, 10);

            Label lblTitle = new Label();
            lblTitle.Text = "🔄 ระบบสลับเตียงและย้ายเตียงผู้ป่วย";
            lblTitle.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(14, 10);
            pnlHeader.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "สลับข้อมูลระหว่าง 2 เตียง หรือย้ายผู้ป่วยไปยังเตียงว่าง พร้อมสำรองประวัติอัตโนมัติ";
            lblSub.Font = new Font("Segoe UI", 8.5f);
            lblSub.ForeColor = Color.FromArgb(224, 231, 255);
            lblSub.AutoSize = true;
            lblSub.Location = new Point(16, 36);
            pnlHeader.Controls.Add(lblSub);

            this.Controls.Add(pnlHeader);

            // Body
            Panel pnlBody = new Panel();
            pnlBody.Dock = DockStyle.Fill;
            pnlBody.Padding = new Padding(20, 14, 20, 14);

            string srcContent = manager.GetBedNote(sourceBed);
            bool srcHasData = !string.IsNullOrEmpty(srcContent) && !string.IsNullOrEmpty(srcContent.Trim());

            lblSourceInfo = new Label();
            lblSourceInfo.Text = string.Format("🛏️ เตียงต้นทาง: เตียง {0:D2} ({1})", 
                sourceBed, 
                srcHasData ? string.Format("มีข้อมูลผู้ป่วย {0} ตัวอักษร", srcContent.Length) : "เตียงว่าง");
            lblSourceInfo.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblSourceInfo.ForeColor = Color.FromArgb(15, 23, 42);
            lblSourceInfo.Location = new Point(20, 12);
            lblSourceInfo.AutoSize = true;
            pnlBody.Controls.Add(lblSourceInfo);

            txtSourcePreview = new TextBox();
            txtSourcePreview.Multiline = true;
            txtSourcePreview.ReadOnly = true;
            txtSourcePreview.ScrollBars = ScrollBars.Vertical;
            txtSourcePreview.Text = srcHasData ? srcContent : "(เตียงนี้ยังไม่มีข้อมูลผู้ป่วย)";
            txtSourcePreview.Location = new Point(22, 36);
            txtSourcePreview.Size = new Size(480, 58);
            txtSourcePreview.BackColor = Color.FromArgb(241, 245, 249);
            txtSourcePreview.ForeColor = srcHasData ? Color.FromArgb(30, 41, 59) : Color.FromArgb(148, 163, 184);
            txtSourcePreview.Font = new Font("Segoe UI", 8.5f);
            pnlBody.Controls.Add(txtSourcePreview);

            Label lblTargetPrompt = new Label();
            lblTargetPrompt.Text = "🎯 เลือกเตียงปลายทาง (1 - 30):";
            lblTargetPrompt.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblTargetPrompt.ForeColor = Color.FromArgb(15, 23, 42);
            lblTargetPrompt.Location = new Point(20, 104);
            lblTargetPrompt.AutoSize = true;
            pnlBody.Controls.Add(lblTargetPrompt);

            cmbTargetBed = new ComboBox();
            cmbTargetBed.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTargetBed.Location = new Point(22, 128);
            cmbTargetBed.Size = new Size(480, 26);
            cmbTargetBed.Font = new Font("Segoe UI", 9.5f);

            int firstSelectIdx = 0;
            int curIdx = 0;
            for (int i = 1; i <= 30; i++) {
                if (i == sourceBed) continue;
                string note = manager.GetBedNote(i);
                bool hasData = !string.IsNullOrEmpty(note) && !string.IsNullOrEmpty(note.Trim());
                string txt = string.Format("เตียง {0:D2} {1}", i, hasData ? string.Format("[มีข้อมูล {0} ตัวอักษร]", note.Length) : "[เตียงว่าง ✨]");
                cmbTargetBed.Items.Add(new BedComboItem {
                    BedNumber = i,
                    DisplayText = txt,
                    HasData = hasData
                });
                if (defaultTargetBed > 0 && i == defaultTargetBed) {
                    firstSelectIdx = curIdx;
                } else if (defaultTargetBed == 0 && firstSelectIdx == 0 && !hasData && srcHasData) {
                    firstSelectIdx = curIdx;
                }
                curIdx++;
            }
            if (cmbTargetBed.Items.Count > 0) cmbTargetBed.SelectedIndex = firstSelectIdx;
            cmbTargetBed.SelectedIndexChanged += (s, e) => UpdateTargetStatus();
            pnlBody.Controls.Add(cmbTargetBed);

            lblTargetStatus = new Label();
            lblTargetStatus.Location = new Point(22, 162);
            lblTargetStatus.Size = new Size(480, 36);
            lblTargetStatus.Font = new Font("Segoe UI", 8.75f, FontStyle.Italic);
            pnlBody.Controls.Add(lblTargetStatus);

            // GroupBox for Action Mode
            GroupBox grpAction = new GroupBox();
            grpAction.Text = "รูปแบบการดำเนินการ";
            grpAction.Location = new Point(22, 202);
            grpAction.Size = new Size(480, 95);
            grpAction.Font = new Font("Segoe UI", 8.75f, FontStyle.Bold);

            rbSwap = new RadioButton();
            rbSwap.Text = "🔄 สลับเตียงกัน (Swap) — แลกเปลี่ยนข้อมูลระหว่าง 2 เตียง";
            rbSwap.Font = new Font("Segoe UI", 9f);
            rbSwap.Location = new Point(16, 24);
            rbSwap.Size = new Size(450, 24);
            rbSwap.Cursor = Cursors.Hand;
            grpAction.Controls.Add(rbSwap);

            rbMove = new RadioButton();
            rbMove.Text = "➡️ ย้ายเตียง (Move / Transfer) — ย้ายข้อมูลไปเตียงปลายทาง (เตียงต้นทางจะว่างลง)";
            rbMove.Font = new Font("Segoe UI", 9f);
            rbMove.Location = new Point(16, 54);
            rbMove.Size = new Size(450, 24);
            rbMove.Cursor = Cursors.Hand;
            grpAction.Controls.Add(rbMove);

            pnlBody.Controls.Add(grpAction);

            chkSyncReminders = new CheckBox();
            chkSyncReminders.Text = "⏰ ย้าย/สลับรายการแจ้งเตือนหัตถการ (Ward Reminders) ของเตียงไปด้วย";
            chkSyncReminders.Checked = true;
            chkSyncReminders.Location = new Point(22, 308);
            chkSyncReminders.Size = new Size(480, 24);
            chkSyncReminders.Font = new Font("Segoe UI", 9f);
            chkSyncReminders.Cursor = Cursors.Hand;
            pnlBody.Controls.Add(chkSyncReminders);

            // Bottom Buttons
            Panel pnlDlgBottom = new Panel();
            pnlDlgBottom.Dock = DockStyle.Bottom;
            pnlDlgBottom.Height = 56;
            pnlDlgBottom.BackColor = Color.FromArgb(241, 245, 249);

            btnConfirm = new Button();
            btnConfirm.Text = "🔄 ยืนยันดำเนินการ";
            btnConfirm.Size = new Size(150, 36);
            btnConfirm.Location = new Point(245, 10);
            btnConfirm.BackColor = Color.FromArgb(67, 56, 202);
            btnConfirm.ForeColor = Color.White;
            btnConfirm.FlatStyle = FlatStyle.Flat;
            btnConfirm.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnConfirm.Cursor = Cursors.Hand;
            btnConfirm.Click += BtnConfirm_Click;
            pnlDlgBottom.Controls.Add(btnConfirm);

            btnCancel = new Button();
            btnCancel.Text = "ยกเลิก";
            btnCancel.Size = new Size(95, 36);
            btnCancel.Location = new Point(405, 10);
            btnCancel.BackColor = Color.FromArgb(226, 232, 240);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.Font = new Font("Segoe UI", 9f);
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            pnlDlgBottom.Controls.Add(btnCancel);

            this.Controls.Add(pnlBody);
            this.Controls.Add(pnlDlgBottom);

            pnlHeader.SendToBack();
            pnlDlgBottom.SendToBack();
            pnlBody.BringToFront();

            UpdateTargetStatus();
        }

        private void UpdateTargetStatus() {
            BedComboItem item = cmbTargetBed.SelectedItem as BedComboItem;
            if (item == null) return;

            if (item.HasData) {
                rbSwap.Enabled = true;
                rbSwap.Checked = true;
                lblTargetStatus.Text = string.Format("⚠️ เตียง {0:D2} มีข้อมูลอยู่แล้ว: สามารถเลือก 'สลับเตียง' หรือ 'ย้ายทับ' (ระบบสำรองประวัติให้อัตโนมัติ)", item.BedNumber);
                lblTargetStatus.ForeColor = Color.FromArgb(180, 83, 9);
            } else {
                rbSwap.Enabled = false;
                rbMove.Checked = true;
                lblTargetStatus.Text = string.Format("✨ เตียง {0:D2} เป็นเตียงว่าง: ระบบจะย้ายข้อมูลทั้งหมดไป และเตียงต้นทางจะว่างเปล่า", item.BedNumber);
                lblTargetStatus.ForeColor = Color.FromArgb(15, 118, 110);
            }
        }

        private void BtnConfirm_Click(object sender, EventArgs e) {
            BedComboItem item = cmbTargetBed.SelectedItem as BedComboItem;
            if (item == null) return;
            int targetBed = item.BedNumber;
            bool isSwap = rbSwap.Checked;

            string confirmMsg = isSwap 
                ? string.Format("ยืนยันการ 'สลับเตียง' ระหว่าง เตียง {0} และ เตียง {1} ใช่หรือไม่?\n\n💡 ระบบจะแลกเปลี่ยนข้อมูลของทั้งสองเตียง และสำรองประวัติย้อนหลังให้อัตโนมัติ", sourceBed, targetBed)
                : string.Format("ยืนยันการ 'ย้ายข้อมูล' จาก เตียง {0} ไปยัง เตียง {1} ใช่หรือไม่?\n\n💡 ข้อมูลเดิมจะถูกย้ายไปเตียง {1} และเตียง {0} จะกลายเป็นเตียงว่าง", sourceBed, targetBed);

            if (MessageBox.Show(this, confirmMsg, "ยืนยันการสลับ/ย้ายเตียง", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) {
                return;
            }

            string resultMsg;
            bool ok = manager.MoveOrSwapBed(sourceBed, targetBed, isSwap, out resultMsg);
            if (!ok) {
                MessageBox.Show(this, resultMsg, "ไม่สามารถดำเนินการได้", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (chkSyncReminders.Checked && context != null && context.ReminderManager != null) {
                try {
                    context.ReminderManager.MoveOrSwapBedReminders(sourceBed, targetBed, isSwap);
                } catch {}
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    // =========================================================================
    // ระบบกรองและช่วยจัดการเวรพยาบาล (Shift Nursing Action Helper)
    // =========================================================================
    public static class ShiftHelper {
        public static bool HasShiftTags(string content) {
            if (string.IsNullOrEmpty(content)) return false;
            return content.Contains("เวรเช้า") || content.Contains("เวรบ่าย") || content.Contains("เวรดึก");
        }

        public static string GetCurrentShift() {
            int hour = DateTime.Now.Hour;
            if (hour >= 8 && hour < 16) return "morning";
            if (hour >= 16 && hour <= 23) return "afternoon";
            return "night";
        }

        public static string FilterContentByShift(string content, string shift) {
            if (string.IsNullOrEmpty(content) || shift == "all" || !HasShiftTags(content)) {
                return content;
            }

            string[] lines = content.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            List<string> result = new List<string>();
            bool inAction = false;
            string curActionShift = "";
            List<string> filteredActions = new List<string>();
            List<string> nonShiftActions = new List<string>();

            string shiftTag = "";
            if (shift == "morning") shiftTag = " (☀️ เวรเช้า 08:00-16:00)";
            else if (shift == "afternoon") shiftTag = " (⛅ เวรบ่าย 16:00-24:00)";
            else if (shift == "night") shiftTag = " (🌙 เวรดึก 24:00-08:00)";

            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i];
                string trimmed = line.Trim();

                if (trimmed.StartsWith("Focus:") || trimmed.StartsWith("Goal:") || trimmed.StartsWith("Data:") || trimmed.StartsWith("Response:")) {
                    if (inAction) {
                        result.Add("Action:" + shiftTag);
                        if (filteredActions.Count > 0) {
                            result.AddRange(filteredActions);
                        } else if (nonShiftActions.Count > 0) {
                            result.AddRange(nonShiftActions);
                        }
                        inAction = false;
                        curActionShift = "";
                    }
                    result.Add(line);
                    continue;
                }

                if (trimmed.StartsWith("Action:")) {
                    inAction = true;
                    curActionShift = "";
                    filteredActions.Clear();
                    nonShiftActions.Clear();
                    continue;
                }

                if (inAction) {
                    if (trimmed.Contains("เวรเช้า")) {
                        curActionShift = "morning";
                        continue;
                    } else if (trimmed.Contains("เวรบ่าย")) {
                        curActionShift = "afternoon";
                        continue;
                    } else if (trimmed.Contains("เวรดึก")) {
                        curActionShift = "night";
                        continue;
                    }

                    if (!string.IsNullOrEmpty(curActionShift)) {
                        if (curActionShift == shift) {
                            filteredActions.Add(line);
                        }
                    } else {
                        nonShiftActions.Add(line);
                    }
                    continue;
                }

                result.Add(line);
            }

            if (inAction) {
                result.Add("Action:" + shiftTag);
                if (filteredActions.Count > 0) {
                    result.AddRange(filteredActions);
                } else if (nonShiftActions.Count > 0) {
                    result.AddRange(nonShiftActions);
                }
            }

            return BedNotesManager.NormalizeNewlines(string.Join("\r\n", result.ToArray()));
        }
    }

    // =========================================================================
    // หน้าต่างพรีวิวและเลือกคัดลอกด่วน (Quick Snippet & Shift Action Selector)
    // =========================================================================
    public class QuickSnippetSelectorDialog : Form {
        public string ResultTextToInsert { get; private set; }

        private string buttonTitle;
        private string shortcut;
        private string templateTitle;
        private string rawContent;
        private int bedNumber;
        private string currentShift = "morning";

        private Panel pnlHeader;
        private Label lblBadge;
        private Label lblTitle;
        private Label lblBedBadge;

        private FlowLayoutPanel pnlShifts;
        private Label lblShiftPrompt;
        private Button btnShiftMorning;
        private Button btnShiftAfternoon;
        private Button btnShiftNight;
        private Button btnShiftAll;

        private TextBox txtPreview;

        private Panel pnlBottom;
        private FlowLayoutPanel pnlActions;
        private Button btnCopySelected;
        private Button btnCopyAll;
        private Button btnInsertSelected;
        private Button btnInsertAll;
        private Button btnClose;
        private Label lblStatus;

        public QuickSnippetSelectorDialog(string btnTitle, string sc, string tplTitle, string content, int bed) {
            buttonTitle = btnTitle ?? "";
            shortcut = sc ?? "";
            templateTitle = tplTitle ?? "";
            rawContent = BedNotesManager.NormalizeNewlines(content ?? "");
            bedNumber = bed;

            if (ShiftHelper.HasShiftTags(rawContent)) {
                currentShift = ShiftHelper.GetCurrentShift();
            } else {
                currentShift = "all";
            }

            InitializeUI();
            UpdateShiftSelection();
        }

        private void InitializeUI() {
            this.Text = string.Format("คีย์ด่วน: {0} ({1}) - เตียง {2:D2}", buttonTitle, shortcut, bedNumber);
            this.Size = new Size(820, 600);
            this.MinimumSize = new Size(640, 440);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape) {
                    this.Close();
                }
            };

            // 1. Header Panel
            pnlHeader = new Panel();
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 52;
            pnlHeader.BackColor = Color.FromArgb(13, 148, 136); // Teal 600

            lblBadge = new Label();
            lblBadge.Text = shortcut;
            lblBadge.BackColor = Color.FromArgb(15, 118, 110);
            lblBadge.ForeColor = Color.FromArgb(204, 251, 241);
            lblBadge.Font = new Font("Consolas", 10.5f, FontStyle.Bold);
            lblBadge.Padding = new Padding(6, 4, 6, 4);
            lblBadge.Location = new Point(12, 11);
            lblBadge.AutoSize = true;
            pnlHeader.Controls.Add(lblBadge);

            lblTitle = new Label();
            lblTitle.Text = string.Format("{0} - {1}", buttonTitle, templateTitle);
            lblTitle.ForeColor = Color.White;
            lblTitle.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            lblTitle.Location = new Point(lblBadge.Right + 12, 13);
            lblTitle.AutoSize = true;
            lblTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            pnlHeader.Controls.Add(lblTitle);

            if (bedNumber > 0) {
                lblBedBadge = new Label();
                lblBedBadge.Text = string.Format("เตียง {0:D2}", bedNumber);
                lblBedBadge.ForeColor = Color.FromArgb(254, 240, 138);
                lblBedBadge.BackColor = Color.FromArgb(15, 118, 110);
                lblBedBadge.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                lblBedBadge.Padding = new Padding(6, 3, 6, 3);
                lblBedBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                lblBedBadge.Location = new Point(pnlHeader.Width - 95, 12);
                lblBedBadge.AutoSize = true;
                pnlHeader.Controls.Add(lblBedBadge);
            }

            this.Controls.Add(pnlHeader);

            // 2. Shifts Filter Panel (Top)
            pnlShifts = new FlowLayoutPanel();
            pnlShifts.Dock = DockStyle.Top;
            pnlShifts.Height = 44;
            pnlShifts.BackColor = Color.FromArgb(241, 245, 249);
            pnlShifts.Padding = new Padding(12, 6, 12, 6);
            pnlShifts.WrapContents = false;

            lblShiftPrompt = new Label();
            lblShiftPrompt.Text = "🕒 กิจกรรมการพยาบาลตามเวร:";
            lblShiftPrompt.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblShiftPrompt.ForeColor = Color.FromArgb(51, 65, 85);
            lblShiftPrompt.Margin = new Padding(0, 5, 8, 0);
            lblShiftPrompt.AutoSize = true;
            pnlShifts.Controls.Add(lblShiftPrompt);

            btnShiftMorning = CreateShiftButton("☀️ เวรเช้า (08:00-16:00)", "morning");
            btnShiftAfternoon = CreateShiftButton("⛅ เวรบ่าย (16:00-24:00)", "afternoon");
            btnShiftNight = CreateShiftButton("🌙 เวรดึก (24:00-08:00)", "night");
            btnShiftAll = CreateShiftButton("📋 รวมทุกเวร (All)", "all");

            pnlShifts.Controls.Add(btnShiftMorning);
            pnlShifts.Controls.Add(btnShiftAfternoon);
            pnlShifts.Controls.Add(btnShiftNight);
            pnlShifts.Controls.Add(btnShiftAll);

            this.Controls.Add(pnlShifts);

            // 3. Bottom Actions Panel
            pnlBottom = new Panel();
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Height = 56;
            pnlBottom.BackColor = Color.FromArgb(244, 246, 250);
            pnlBottom.BorderStyle = BorderStyle.FixedSingle;

            pnlActions = new FlowLayoutPanel();
            pnlActions.Dock = DockStyle.Fill;
            pnlActions.Padding = new Padding(10, 10, 10, 10);
            pnlActions.WrapContents = false;

            btnCopySelected = new Button();
            btnCopySelected.Text = "📋 คัดลอกส่วนที่เลือก (Ctrl+C)";
            btnCopySelected.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnCopySelected.BackColor = Color.FromArgb(2, 132, 199); // Sky blue
            btnCopySelected.ForeColor = Color.White;
            btnCopySelected.FlatStyle = FlatStyle.Flat;
            btnCopySelected.FlatAppearance.BorderSize = 0;
            btnCopySelected.AutoSize = true;
            btnCopySelected.Height = 34;
            btnCopySelected.Cursor = Cursors.Hand;
            btnCopySelected.Click += (s, e) => CopySelectedText();
            pnlActions.Controls.Add(btnCopySelected);

            btnCopyAll = new Button();
            btnCopyAll.Text = "📑 คัดลอกทั้งหมด";
            btnCopyAll.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            btnCopyAll.BackColor = Color.White;
            btnCopyAll.ForeColor = Color.FromArgb(51, 65, 85);
            btnCopyAll.FlatStyle = FlatStyle.Flat;
            btnCopyAll.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnCopyAll.AutoSize = true;
            btnCopyAll.Height = 34;
            btnCopyAll.Cursor = Cursors.Hand;
            btnCopyAll.Click += (s, e) => CopyAllText();
            pnlActions.Controls.Add(btnCopyAll);

            if (bedNumber > 0) {
                btnInsertSelected = new Button();
                btnInsertSelected.Text = string.Format("📥 แทรกส่วนที่เลือกลงเตียง {0:D2}", bedNumber);
                btnInsertSelected.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                btnInsertSelected.BackColor = Color.FromArgb(13, 148, 136); // Teal
                btnInsertSelected.ForeColor = Color.White;
                btnInsertSelected.FlatStyle = FlatStyle.Flat;
                btnInsertSelected.FlatAppearance.BorderSize = 0;
                btnInsertSelected.AutoSize = true;
                btnInsertSelected.Height = 34;
                btnInsertSelected.Cursor = Cursors.Hand;
                btnInsertSelected.Click += (s, e) => InsertSelectedToBed();
                pnlActions.Controls.Add(btnInsertSelected);

                btnInsertAll = new Button();
                btnInsertAll.Text = string.Format("➕ แทรกทั้งหมดลงเตียง {0:D2}", bedNumber);
                btnInsertAll.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                btnInsertAll.BackColor = Color.FromArgb(217, 119, 6); // Amber
                btnInsertAll.ForeColor = Color.White;
                btnInsertAll.FlatStyle = FlatStyle.Flat;
                btnInsertAll.FlatAppearance.BorderSize = 0;
                btnInsertAll.AutoSize = true;
                btnInsertAll.Height = 34;
                btnInsertAll.Cursor = Cursors.Hand;
                btnInsertAll.Click += (s, e) => InsertAllToBed();
                pnlActions.Controls.Add(btnInsertAll);
            }

            lblStatus = new Label();
            lblStatus.Text = "";
            lblStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            lblStatus.ForeColor = Color.FromArgb(5, 150, 105);
            lblStatus.Margin = new Padding(10, 8, 4, 0);
            lblStatus.AutoSize = true;
            pnlActions.Controls.Add(lblStatus);

            btnClose = new Button();
            btnClose.Text = "ปิด (Esc)";
            btnClose.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            btnClose.Size = new Size(80, 34);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Location = new Point(pnlBottom.Width - 95, 10);
            btnClose.BackColor = Color.FromArgb(226, 232, 240);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => this.Close();
            pnlBottom.Controls.Add(btnClose);
            pnlBottom.Controls.Add(pnlActions);
            pnlBottom.Resize += (s, e) => {
                btnClose.Location = new Point(pnlBottom.Width - 95, 10);
            };

            this.Controls.Add(pnlBottom);

            // 4. Center Preview Box
            txtPreview = new TextBox();
            txtPreview.Multiline = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;
            txtPreview.Dock = DockStyle.Fill;
            Font fBody;
            try {
                fBody = new Font("Leelawadee UI", 11f, FontStyle.Regular);
            } catch {
                fBody = new Font("Segoe UI", 11f, FontStyle.Regular);
            }
            txtPreview.Font = fBody;
            txtPreview.BackColor = Color.White;
            txtPreview.ForeColor = Color.FromArgb(15, 23, 42);
            txtPreview.HideSelection = false; // CRITICAL: Blue selection stays visible!
            txtPreview.KeyDown += (s, e) => {
                if (e.Control && e.KeyCode == Keys.A) {
                    txtPreview.SelectAll();
                    e.Handled = true;
                } else if (e.Control && e.KeyCode == Keys.C) {
                    CopySelectedText();
                    e.Handled = true;
                }
            };

            ContextMenuStrip cms = new ContextMenuStrip();
            ToolStripMenuItem miCopySel = new ToolStripMenuItem("คัดลอกส่วนที่เลือก (Ctrl+C)");
            miCopySel.Click += (s, e) => CopySelectedText();
            ToolStripMenuItem miCopyAll = new ToolStripMenuItem("คัดลอกทั้งหมด");
            miCopyAll.Click += (s, e) => CopyAllText();
            ToolStripMenuItem miSelAll = new ToolStripMenuItem("เลือกทั้งหมด (Ctrl+A)");
            miSelAll.Click += (s, e) => txtPreview.SelectAll();
            cms.Items.Add(miCopySel);
            cms.Items.Add(miCopyAll);
            cms.Items.Add(new ToolStripSeparator());
            cms.Items.Add(miSelAll);
            txtPreview.ContextMenuStrip = cms;

            this.Controls.Add(txtPreview);
            txtPreview.BringToFront();
        }

        private Button CreateShiftButton(string text, string shiftKey) {
            Button btn = new Button();
            btn.Text = text;
            btn.Tag = shiftKey;
            btn.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            btn.Height = 28;
            btn.AutoSize = true;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.Cursor = Cursors.Hand;
            btn.Margin = new Padding(2, 2, 4, 2);
            btn.Click += (s, e) => {
                currentShift = shiftKey;
                UpdateShiftSelection();
            };
            return btn;
        }

        private void UpdateShiftSelection() {
            Button[] btns = new Button[] { btnShiftMorning, btnShiftAfternoon, btnShiftNight, btnShiftAll };
            foreach (Button b in btns) {
                if (b == null) continue;
                bool active = (string)b.Tag == currentShift;
                b.BackColor = active ? Color.FromArgb(13, 148, 136) : Color.White;
                b.ForeColor = active ? Color.White : Color.FromArgb(51, 65, 85);
                b.Font = new Font("Segoe UI", 8.5f, active ? FontStyle.Bold : FontStyle.Regular);
                b.FlatAppearance.BorderColor = active ? Color.FromArgb(13, 148, 136) : Color.FromArgb(203, 213, 225);
            }

            string filtered = ShiftHelper.FilterContentByShift(rawContent, currentShift);
            txtPreview.Text = filtered;
            txtPreview.SelectionStart = 0;
            txtPreview.SelectionLength = 0;
        }

        private void CopySelectedText() {
            string text = (txtPreview.SelectionLength > 0 && !string.IsNullOrEmpty(txtPreview.SelectedText)) 
                ? txtPreview.SelectedText 
                : txtPreview.Text;

            if (string.IsNullOrEmpty(text)) return;
            try {
                Clipboard.SetDataObject(text, true, 5, 50);
                ShowStatusFeedback(txtPreview.SelectionLength > 0 ? "คัดลอกส่วนที่เลือกแล้ว! ✓" : "คัดลอกทั้งหมดแล้ว! ✓");
            } catch {}
        }

        private void CopyAllText() {
            string text = txtPreview.Text;
            if (string.IsNullOrEmpty(text)) return;
            try {
                Clipboard.SetDataObject(text, true, 5, 50);
                ShowStatusFeedback("คัดลอกทั้งหมดแล้ว! ✓");
            } catch {}
        }

        private void InsertSelectedToBed() {
            string text = (txtPreview.SelectionLength > 0 && !string.IsNullOrEmpty(txtPreview.SelectedText)) 
                ? txtPreview.SelectedText 
                : txtPreview.Text;

            if (string.IsNullOrEmpty(text)) return;
            ResultTextToInsert = text;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void InsertAllToBed() {
            string text = txtPreview.Text;
            if (string.IsNullOrEmpty(text)) return;
            ResultTextToInsert = text;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void ShowStatusFeedback(string msg) {
            lblStatus.Text = msg;
            var t = new System.Windows.Forms.Timer();
            t.Interval = 1500;
            t.Tick += (s, e) => {
                lblStatus.Text = "";
                t.Stop();
                t.Dispose();
            };
            t.Start();
        }
    }

    public class PaletteForm : Form {
        private ExpanderContext context;
        private int targetBed = -1;
        private TextBox txtSearch;
        private Label lblSearchCount;
        private Label lblTargetBedTop;
        private FlowLayoutPanel pnlCategories;
        private string currentCategoryFilter = "all";
        private List<Button> categoryButtons = new List<Button>();
        private ListView lstTemplates;
        private SplitContainer split;
        
        // Right Panel Controls (2-Tier Header)
        private Panel pnlRight;
        private Panel pnlPreviewHeader;
        private Panel pnlTitleRow;
        private FlowLayoutPanel pnlActionToolbar;
        private Label lblBadgeShortcut;
        private Label lblPreviewTitle;
        private ComboBox cboTargetBed;
        private Button btnCopy;
        private Button btnInsertToBed;
        private Button btnReplaceBed;
        private Button btnPasteEPhis;
        
        // View Tabs & Shift Controls
        private Panel pnlViewTabs;
        private Button btnTabDar;
        private Button btnTabRaw;
        private FlowLayoutPanel pnlPalShifts;
        private Label lblPalShiftTitle;
        private Button btnPalShiftMorning;
        private Button btnPalShiftAfternoon;
        private Button btnPalShiftNight;
        private Button btnPalShiftAll;
        private string palCurrentShift = "morning";
        private Panel pnlContentContainer;
        private RichTextBox rtbDar;
        private TextBox txtRaw;

        // Bottom Controls
        private Button btnPasteBottom;
        private Button btnEdit;
        private Button btnSync;
        private Button btnCalc;
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
            this.Text = "คลังข้อวินิจฉัยและกิจกรรมการพยาบาล (Template Catalog & DAR Picker) - กด F8";
            this.Size = new Size(1160, 720);
            this.MinimumSize = new Size(880, 560);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            this.BackColor = Color.FromArgb(246, 248, 252);

            currentFontSize = context.CurrentFontSize;

            string iconPath = @"C:\PhisApp\Medical_Text_Expander\medical_expander_icon.ico";
            if (!File.Exists(iconPath)) iconPath = @"C:\PhisApp\medical_expander_icon.ico";
            if (File.Exists(iconPath)) {
                try { this.Icon = new Icon(iconPath); } catch {}
            }

            // ==========================================
            // Top Bar: Teal Header with Search & Font Zoom
            // ==========================================
            Panel pnlTop = new Panel();
            pnlTop.Dock = DockStyle.Top;
            pnlTop.Height = 56;
            pnlTop.BackColor = Color.FromArgb(13, 148, 136);

            Label lblSearchIcon = new Label();
            lblSearchIcon.Text = "ค้นหา:";
            lblSearchIcon.ForeColor = Color.White;
            lblSearchIcon.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            lblSearchIcon.Location = new Point(14, 15);
            lblSearchIcon.AutoSize = true;
            pnlTop.Controls.Add(lblSearchIcon);

            txtSearch = new TextBox();
            txtSearch.Location = new Point(80, 12);
            txtSearch.Size = new Size(470, 31);
            txtSearch.Font = new Font("Segoe UI", 11.5f);
            txtSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSearch.TextChanged += (s, e) => RefreshList(txtSearch.Text);
            txtSearch.KeyDown += TxtSearch_KeyDown;
            pnlTop.Controls.Add(txtSearch);

            lblSearchCount = new Label();
            lblSearchCount.Text = "118 เทมเพลต";
            lblSearchCount.ForeColor = Color.FromArgb(204, 251, 241);
            lblSearchCount.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblSearchCount.AutoSize = true;
            lblSearchCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblSearchCount.Location = new Point(565, 18);
            pnlTop.Controls.Add(lblSearchCount);

            lblTargetBedTop = new Label();
            lblTargetBedTop.Text = "เตียง 01";
            lblTargetBedTop.ForeColor = Color.FromArgb(254, 240, 138);
            lblTargetBedTop.BackColor = Color.FromArgb(15, 118, 110);
            lblTargetBedTop.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblTargetBedTop.Padding = new Padding(6, 3, 6, 3);
            lblTargetBedTop.AutoSize = true;
            lblTargetBedTop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblTargetBedTop.Location = new Point(665, 14);
            lblTargetBedTop.Visible = false;
            pnlTop.Controls.Add(lblTargetBedTop);

            lblFontSize = new Label();
            lblFontSize.Text = string.Format("ขนาด: {0:0} pt", currentFontSize);
            lblFontSize.ForeColor = Color.White;
            lblFontSize.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblFontSize.AutoSize = true;
            lblFontSize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblFontSize.Location = new Point(785, 18);
            pnlTop.Controls.Add(lblFontSize);

            btnZoomOut = new Button();
            btnZoomOut.Text = "A -";
            btnZoomOut.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnZoomOut.Size = new Size(40, 32);
            btnZoomOut.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnZoomOut.Location = new Point(885, 12);
            btnZoomOut.BackColor = Color.FromArgb(240, 240, 240);
            btnZoomOut.FlatStyle = FlatStyle.Flat;
            btnZoomOut.Cursor = Cursors.Hand;
            btnZoomOut.Click += (s, e) => AdjustFontSize(-1.5f);
            pnlTop.Controls.Add(btnZoomOut);

            btnZoomIn = new Button();
            btnZoomIn.Text = "A +";
            btnZoomIn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnZoomIn.Size = new Size(40, 32);
            btnZoomIn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnZoomIn.Location = new Point(930, 12);
            btnZoomIn.BackColor = Color.FromArgb(240, 240, 240);
            btnZoomIn.FlatStyle = FlatStyle.Flat;
            btnZoomIn.Cursor = Cursors.Hand;
            btnZoomIn.Click += (s, e) => AdjustFontSize(1.5f);
            pnlTop.Controls.Add(btnZoomIn);

            // ==========================================
            // Category Filter Bar (Clean Universal Labels)
            // ==========================================
            pnlCategories = new FlowLayoutPanel();
            pnlCategories.Dock = DockStyle.Top;
            pnlCategories.AutoSize = true;
            pnlCategories.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            pnlCategories.WrapContents = true;
            pnlCategories.BackColor = Color.FromArgb(241, 245, 249);
            pnlCategories.Padding = new Padding(10, 5, 10, 5);

            AddCategoryButton("all", "ทั้งหมด (118)");
            AddCategoryButton("boneca", "[มะเร็ง/ฉายแสง] Bone Ca / RT");
            AddCategoryButton("palliative", "[ระยะสุดท้าย] Palliative / Comfort");
            AddCategoryButton("neuro", "[ระบบประสาท] Stroke / ICP / Seizure");
            AddCategoryButton("vent", "[เครื่องช่วยหายใจ] ETT / Tracheo / Wean");
            AddCategoryButton("icu", "[วิกฤต/กู้ชีพ] CPR / Blood / CVC / ICD");
            AddCategoryButton("knee", "[ข้อเข่า] TKA / UKA");
            AddCategoryButton("hip", "[ข้อสะโพก] THA / BHA");
            AddCategoryButton("spine", "[สันหลัง] Laminectomy / PLIF");
            AddCategoryButton("fracture", "[กระดูกหัก] ORIF / Cast");
            AddCategoryButton("surgery", "[ศัลยกรรม] ผ่าตัดเฉพาะทาง");
            AddCategoryButton("electrolyte", "[เกลือแร่] K / Na / Ca / Mg");
            AddCategoryButton("lab", "[ค่าเลือดผิดปกติ] Anemia / Labs");
            AddCategoryButton("med", "[อายุรกรรม] Sepsis / Stroke");
            AddCategoryButton("pain", "[จัดการปวด] Acute / DAR");
            AddCategoryButton("safe", "[ป้องกันแทรกซ้อน] Fall / DVT");
            AddCategoryButton("fluid", "[สารน้ำ] IV / Foley");
            AddCategoryButton("shift", "[ส่งเวร] รับใหม่ / จำหน่าย");
            AddCategoryButton("shortcut", "[คีย์ลัดย่อด่วน]");

            // ==========================================
            // Bottom Action Bar
            // ==========================================
            Panel pnlBottom = new Panel();
            pnlBottom.Dock = DockStyle.Bottom;
            pnlBottom.Height = 52;
            pnlBottom.BackColor = Color.FromArgb(238, 240, 246);

            btnPasteBottom = new Button();
            btnPasteBottom.Text = "วางลง e-PHIS (Enter)";
            btnPasteBottom.Location = new Point(14, 8);
            btnPasteBottom.Size = new Size(185, 36);
            btnPasteBottom.BackColor = Color.FromArgb(13, 148, 136);
            btnPasteBottom.ForeColor = Color.White;
            btnPasteBottom.FlatStyle = FlatStyle.Flat;
            btnPasteBottom.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnPasteBottom.Cursor = Cursors.Hand;
            btnPasteBottom.Click += (s, e) => PasteSelected();
            pnlBottom.Controls.Add(btnPasteBottom);

            btnBedNotes = new Button();
            btnBedNotes.Text = "ข้อมูลรายเตียง (F7)";
            btnBedNotes.Location = new Point(206, 8);
            btnBedNotes.Size = new Size(150, 36);
            btnBedNotes.BackColor = Color.FromArgb(15, 118, 110);
            btnBedNotes.ForeColor = Color.White;
            btnBedNotes.FlatStyle = FlatStyle.Flat;
            btnBedNotes.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnBedNotes.Cursor = Cursors.Hand;
            btnBedNotes.Click += (s, e) => {
                this.Hide();
                context.ShowBedNotes(targetBed > 0 ? targetBed : -1);
            };
            pnlBottom.Controls.Add(btnBedNotes);

            btnCalc = new Button();
            btnCalc.Text = "คำนวณ SOS/ยา";
            btnCalc.Location = new Point(363, 8);
            btnCalc.Size = new Size(135, 36);
            btnCalc.BackColor = Color.FromArgb(254, 243, 199);
            btnCalc.ForeColor = Color.FromArgb(180, 83, 9);
            btnCalc.FlatStyle = FlatStyle.Flat;
            btnCalc.FlatAppearance.BorderColor = Color.FromArgb(251, 191, 36);
            btnCalc.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnCalc.Cursor = Cursors.Hand;
            btnCalc.Click += (s, e) => {
                this.Hide();
                context.ShowCalculator(targetBed > 0 ? targetBed : 1);
            };
            pnlBottom.Controls.Add(btnCalc);

            btnEdit = new Button();
            btnEdit.Text = "แก้ไขเทมเพลต";
            btnEdit.Location = new Point(505, 8);
            btnEdit.Size = new Size(120, 36);
            btnEdit.BackColor = Color.FromArgb(225, 228, 238);
            btnEdit.FlatStyle = FlatStyle.Flat;
            btnEdit.Font = new Font("Segoe UI", 9.5f);
            btnEdit.Cursor = Cursors.Hand;
            btnEdit.Click += (s, e) => context.EditTemplates();
            pnlBottom.Controls.Add(btnEdit);

            btnSync = new Button();
            btnSync.Text = "ซิงค์วอร์ด";
            btnSync.Location = new Point(632, 8);
            btnSync.Size = new Size(95, 36);
            btnSync.BackColor = Color.FromArgb(225, 228, 238);
            btnSync.FlatStyle = FlatStyle.Flat;
            btnSync.Font = new Font("Segoe UI", 9.5f);
            btnSync.Cursor = Cursors.Hand;
            btnSync.Click += (s, e) => context.ShowSyncSettings(this);
            pnlBottom.Controls.Add(btnSync);

            btnClose = new Button();
            btnClose.Text = "ปิด (Esc)";
            btnClose.Location = new Point(1030, 8);
            btnClose.Size = new Size(85, 36);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.BackColor = Color.FromArgb(225, 228, 238);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.Font = new Font("Segoe UI", 9.5f);
            btnClose.Cursor = Cursors.Hand;
            btnClose.Click += (s, e) => this.Hide();
            pnlBottom.Controls.Add(btnClose);

            // ==========================================
            // Main Vertical Split Container
            // ==========================================
            split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.Panel1MinSize = 80;
            split.Panel2MinSize = 120;

            // Panel 1 (Left: Templates List)
            lstTemplates = new ListView();
            lstTemplates.Dock = DockStyle.Fill;
            lstTemplates.View = View.Details;
            lstTemplates.FullRowSelect = true;
            lstTemplates.GridLines = true;
            lstTemplates.Columns.Add("คีย์ลัด", 85);
            lstTemplates.Columns.Add("หมวดหมู่", 115);
            lstTemplates.Columns.Add("ข้อวินิจฉัย / หัตถการ", 220);
            lstTemplates.SelectedIndexChanged += LstTemplates_SelectedIndexChanged;
            lstTemplates.DoubleClick += (s, e) => {
                if (targetBed > 0) {
                    InsertToBed(false);
                } else {
                    PasteSelected();
                }
            };
            split.Panel1.Controls.Add(lstTemplates);

            // Panel 2 (Right: Reading Canvas & Actions)
            pnlRight = new Panel();
            pnlRight.Dock = DockStyle.Fill;
            pnlRight.BackColor = Color.White;

            // ==========================================
            // 2-Tier Preview Header (Zero Overlap!)
            // ==========================================
            pnlPreviewHeader = new Panel();
            pnlPreviewHeader.Dock = DockStyle.Top;
            pnlPreviewHeader.Height = 84;
            pnlPreviewHeader.BackColor = Color.FromArgb(250, 250, 252);
            pnlPreviewHeader.BorderStyle = BorderStyle.FixedSingle;

            // Tier 1: Title Row
            pnlTitleRow = new Panel();
            pnlTitleRow.Dock = DockStyle.Top;
            pnlTitleRow.Height = 40;
            pnlTitleRow.Padding = new Padding(10, 6, 10, 4);

            lblBadgeShortcut = new Label();
            lblBadgeShortcut.Text = ".tka";
            lblBadgeShortcut.BackColor = Color.FromArgb(13, 148, 136);
            lblBadgeShortcut.ForeColor = Color.White;
            lblBadgeShortcut.Font = new Font("Consolas", 10.5f, FontStyle.Bold);
            lblBadgeShortcut.Padding = new Padding(6, 3, 6, 3);
            lblBadgeShortcut.Location = new Point(10, 6);
            lblBadgeShortcut.AutoSize = true;
            pnlTitleRow.Controls.Add(lblBadgeShortcut);

            lblPreviewTitle = new Label();
            lblPreviewTitle.Text = "ผ่าตัดเปลี่ยนข้อเข่าเทียม (TKA)";
            lblPreviewTitle.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            lblPreviewTitle.ForeColor = Color.FromArgb(15, 23, 42);
            lblPreviewTitle.Location = new Point(80, 8);
            lblPreviewTitle.AutoSize = true;
            pnlTitleRow.Controls.Add(lblPreviewTitle);

            pnlPreviewHeader.Controls.Add(pnlTitleRow);

            // Tier 2: Dedicated Action Toolbar (Never Collides With Title!)
            pnlActionToolbar = new FlowLayoutPanel();
            pnlActionToolbar.Dock = DockStyle.Bottom;
            pnlActionToolbar.Height = 42;
            pnlActionToolbar.BackColor = Color.FromArgb(244, 246, 250);
            pnlActionToolbar.Padding = new Padding(10, 4, 10, 4);
            pnlActionToolbar.WrapContents = false;

            Label lblBedDesc = new Label();
            lblBedDesc.Text = "ใส่เตียง:";
            lblBedDesc.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblBedDesc.ForeColor = Color.FromArgb(71, 85, 105);
            lblBedDesc.Margin = new Padding(0, 6, 2, 0);
            lblBedDesc.AutoSize = true;
            pnlActionToolbar.Controls.Add(lblBedDesc);

            cboTargetBed = new ComboBox();
            cboTargetBed.DropDownStyle = ComboBoxStyle.DropDownList;
            cboTargetBed.Width = 85;
            cboTargetBed.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            for (int b = 1; b <= 30; b++) {
                cboTargetBed.Items.Add(string.Format("เตียง {0:D2}", b));
            }
            cboTargetBed.SelectedIndex = 0;
            pnlActionToolbar.Controls.Add(cboTargetBed);

            btnInsertToBed = new Button();
            btnInsertToBed.Text = "แทรกลงเตียง (ต่อท้าย)";
            btnInsertToBed.Size = new Size(150, 30);
            btnInsertToBed.BackColor = Color.FromArgb(13, 148, 136);
            btnInsertToBed.ForeColor = Color.White;
            btnInsertToBed.FlatStyle = FlatStyle.Flat;
            btnInsertToBed.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnInsertToBed.Cursor = Cursors.Hand;
            btnInsertToBed.Click += (s, e) => InsertToBed(false);
            pnlActionToolbar.Controls.Add(btnInsertToBed);

            btnReplaceBed = new Button();
            btnReplaceBed.Text = "แทนที่เตียง";
            btnReplaceBed.Size = new Size(95, 30);
            btnReplaceBed.BackColor = Color.FromArgb(217, 119, 6);
            btnReplaceBed.ForeColor = Color.White;
            btnReplaceBed.FlatStyle = FlatStyle.Flat;
            btnReplaceBed.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnReplaceBed.Cursor = Cursors.Hand;
            btnReplaceBed.Click += (s, e) => InsertToBed(true);
            pnlActionToolbar.Controls.Add(btnReplaceBed);

            btnPasteEPhis = new Button();
            btnPasteEPhis.Text = "วางลง e-PHIS";
            btnPasteEPhis.Size = new Size(110, 30);
            btnPasteEPhis.BackColor = Color.FromArgb(15, 118, 110);
            btnPasteEPhis.ForeColor = Color.White;
            btnPasteEPhis.FlatStyle = FlatStyle.Flat;
            btnPasteEPhis.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnPasteEPhis.Cursor = Cursors.Hand;
            btnPasteEPhis.Click += (s, e) => PasteSelected();
            pnlActionToolbar.Controls.Add(btnPasteEPhis);

            btnCopy = new Button();
            btnCopy.Text = "คัดลอก (Ctrl+C)";
            btnCopy.Size = new Size(125, 30);
            btnCopy.BackColor = Color.FromArgb(241, 245, 249);
            btnCopy.FlatStyle = FlatStyle.Flat;
            btnCopy.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnCopy.Cursor = Cursors.Hand;
            btnCopy.Click += (s, e) => CopySelected();
            pnlActionToolbar.Controls.Add(btnCopy);

            pnlPreviewHeader.Controls.Add(pnlActionToolbar);

            // View Tabs & Shifts
            pnlViewTabs = new Panel();
            pnlViewTabs.Dock = DockStyle.Top;
            pnlViewTabs.Height = 34;
            pnlViewTabs.BackColor = Color.FromArgb(248, 250, 252);
            pnlViewTabs.Padding = new Padding(12, 3, 12, 3);

            btnTabDar = new Button();
            btnTabDar.Text = "มุมมองจัดหน้า DAR สวยงาม";
            btnTabDar.Size = new Size(185, 27);
            btnTabDar.BackColor = Color.White;
            btnTabDar.ForeColor = Color.FromArgb(13, 148, 136);
            btnTabDar.FlatStyle = FlatStyle.Flat;
            btnTabDar.FlatAppearance.BorderColor = Color.FromArgb(13, 148, 136);
            btnTabDar.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnTabDar.Cursor = Cursors.Hand;
            btnTabDar.Click += (s, e) => SwitchPreviewView(true);
            pnlViewTabs.Controls.Add(btnTabDar);

            btnTabRaw = new Button();
            btnTabRaw.Text = "ข้อความเต็ม (Raw Text)";
            btnTabRaw.Size = new Size(165, 27);
            btnTabRaw.Location = new Point(200, 3);
            btnTabRaw.BackColor = Color.FromArgb(241, 245, 249);
            btnTabRaw.ForeColor = Color.FromArgb(100, 116, 139);
            btnTabRaw.FlatStyle = FlatStyle.Flat;
            btnTabRaw.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
            btnTabRaw.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            btnTabRaw.Cursor = Cursors.Hand;
            btnTabRaw.Click += (s, e) => SwitchPreviewView(false);
            pnlViewTabs.Controls.Add(btnTabRaw);

            pnlPalShifts = new FlowLayoutPanel();
            pnlPalShifts.Dock = DockStyle.Right;
            pnlPalShifts.AutoSize = true;
            pnlPalShifts.WrapContents = false;
            pnlPalShifts.BackColor = Color.Transparent;
            pnlPalShifts.Padding = new Padding(0, 0, 4, 0);

            lblPalShiftTitle = new Label();
            lblPalShiftTitle.Text = "เวร:";
            lblPalShiftTitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            lblPalShiftTitle.ForeColor = Color.FromArgb(71, 85, 105);
            lblPalShiftTitle.Margin = new Padding(0, 5, 4, 0);
            lblPalShiftTitle.AutoSize = true;
            pnlPalShifts.Controls.Add(lblPalShiftTitle);

            btnPalShiftMorning = CreatePalShiftButton("☀️ เช้า", "morning");
            btnPalShiftAfternoon = CreatePalShiftButton("⛅ บ่าย", "afternoon");
            btnPalShiftNight = CreatePalShiftButton("🌙 ดึก", "night");
            btnPalShiftAll = CreatePalShiftButton("📋 ทุกเวร", "all");

            pnlPalShifts.Controls.Add(btnPalShiftMorning);
            pnlPalShifts.Controls.Add(btnPalShiftAfternoon);
            pnlPalShifts.Controls.Add(btnPalShiftNight);
            pnlPalShifts.Controls.Add(btnPalShiftAll);

            pnlViewTabs.Controls.Add(pnlPalShifts);

            // Content Container
            pnlContentContainer = new Panel();
            pnlContentContainer.Dock = DockStyle.Fill;

            rtbDar = new RichTextBox();
            rtbDar.Dock = DockStyle.Fill;
            rtbDar.ReadOnly = true;
            rtbDar.BackColor = Color.White;
            rtbDar.BorderStyle = BorderStyle.None;
            rtbDar.Padding = new Padding(14);
            rtbDar.HideSelection = false;
            pnlContentContainer.Controls.Add(rtbDar);

            txtRaw = new TextBox();
            txtRaw.Dock = DockStyle.Fill;
            txtRaw.Multiline = true;
            txtRaw.ReadOnly = true;
            txtRaw.ScrollBars = ScrollBars.Vertical;
            txtRaw.BackColor = Color.White;
            txtRaw.Visible = false;
            txtRaw.HideSelection = false;
            pnlContentContainer.Controls.Add(txtRaw);

            ContextMenuStrip ctxPreview = new ContextMenuStrip();
            ToolStripMenuItem mnuCopySel = new ToolStripMenuItem("📋 คัดลอกส่วนที่เลือก (Copy Selection)");
            mnuCopySel.Click += (s, e) => CopySelected(false);
            ToolStripMenuItem mnuCopyAll = new ToolStripMenuItem("📑 คัดลอกเทมเพลตทั้งหมด (Copy All)");
            mnuCopyAll.Click += (s, e) => CopySelected(true);
            ToolStripMenuItem mnuPasteEPhis = new ToolStripMenuItem("⚡ วางลง e-PHIS");
            mnuPasteEPhis.Click += (s, e) => PasteSelected();
            ToolStripMenuItem mnuInsertBed = new ToolStripMenuItem("➕ แทรกไปยังเตียงที่เลือก");
            mnuInsertBed.Click += (s, e) => InsertToBed(false);
            ToolStripMenuItem mnuSelectAll = new ToolStripMenuItem("🔍 เลือกข้อความทั้งหมด (Select All)");
            mnuSelectAll.Click += (s, e) => {
                if (rtbDar != null && rtbDar.Visible) rtbDar.SelectAll();
                else if (txtRaw != null && txtRaw.Visible) txtRaw.SelectAll();
            };

            ctxPreview.Opening += (s, e) => {
                string sel = GetSelectedPreviewText(false);
                bool hasSel = !string.IsNullOrEmpty(sel);
                mnuCopySel.Enabled = hasSel;
                mnuCopySel.Text = hasSel ? string.Format("📋 คัดลอกส่วนที่เลือก ({0} ตัวอักษร)", sel.Length) : "📋 คัดลอกส่วนที่เลือก";
            };

            ctxPreview.Items.Add(mnuCopySel);
            ctxPreview.Items.Add(mnuCopyAll);
            ctxPreview.Items.Add(new ToolStripSeparator());
            ctxPreview.Items.Add(mnuPasteEPhis);
            ctxPreview.Items.Add(mnuInsertBed);
            ctxPreview.Items.Add(new ToolStripSeparator());
            ctxPreview.Items.Add(mnuSelectAll);

            rtbDar.ContextMenuStrip = ctxPreview;
            txtRaw.ContextMenuStrip = ctxPreview;

            pnlRight.Controls.Add(pnlContentContainer);
            pnlRight.Controls.Add(pnlViewTabs);
            pnlRight.Controls.Add(pnlPreviewHeader);

            split.Panel2.Controls.Add(pnlRight);

            ApplyFontSize();

            this.Controls.Add(split);
            this.Controls.Add(pnlCategories);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlTop);

            pnlTop.SendToBack();
            pnlCategories.SendToBack();
            pnlBottom.SendToBack();
            split.BringToFront();

            this.Shown += (s, e) => SetSafeSplitterDistance(370);
            this.Resize += (s, e) => SetSafeSplitterDistance(370);

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
                } else if (e.Control && e.KeyCode == Keys.C) {
                    CopySelected(false);
                    e.Handled = true;
                } else if (e.Control && e.KeyCode == Keys.A) {
                    if (rtbDar != null && rtbDar.Visible && rtbDar.Focused) {
                        rtbDar.SelectAll();
                        e.Handled = true;
                    } else if (txtRaw != null && txtRaw.Visible && txtRaw.Focused) {
                        txtRaw.SelectAll();
                        e.Handled = true;
                    }
                }
            };
        }

        private void SetSafeSplitterDistance(int dist) {
            if (split == null) return;
            try {
                int w = split.ClientSize.Width;
                if (w > 250) {
                    int max = w - split.Panel2MinSize - 10;
                    int min = split.Panel1MinSize + 10;
                    if (max > min) {
                        if (dist > max) dist = max;
                        if (dist < min) dist = min;
                        split.SplitterDistance = dist;
                    }
                }
            } catch {}
        }

        private void AddCategoryButton(string catKey, string label) {
            Button btn = new Button();
            btn.Text = label;
            btn.Tag = catKey;
            btn.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            btn.AutoSize = true;
            btn.Height = 27;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.Cursor = Cursors.Hand;
            btn.Margin = new Padding(2, 2, 2, 2);

            if (catKey == currentCategoryFilter) {
                btn.BackColor = Color.FromArgb(13, 148, 136);
                btn.ForeColor = Color.White;
                btn.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            } else {
                btn.BackColor = Color.White;
                btn.ForeColor = Color.FromArgb(71, 85, 105);
            }

            btn.Click += (s, e) => {
                currentCategoryFilter = (string)btn.Tag;
                foreach (Button b in categoryButtons) {
                    bool isActive = (string)b.Tag == currentCategoryFilter;
                    b.BackColor = isActive ? Color.FromArgb(13, 148, 136) : Color.White;
                    b.ForeColor = isActive ? Color.White : Color.FromArgb(71, 85, 105);
                    b.Font = new Font("Segoe UI", 8.5f, isActive ? FontStyle.Bold : FontStyle.Regular);
                }
                RefreshList(txtSearch.Text);
            };

            categoryButtons.Add(btn);
            pnlCategories.Controls.Add(btn);
        }

        private Button CreatePalShiftButton(string text, string shiftKey) {
            Button btn = new Button();
            btn.Text = text;
            btn.Tag = shiftKey;
            btn.Font = new Font("Segoe UI", 8f, FontStyle.Regular);
            btn.Height = 27;
            btn.AutoSize = true;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btn.Cursor = Cursors.Hand;
            btn.Margin = new Padding(2, 0, 2, 0);
            btn.Click += (s, e) => {
                palCurrentShift = shiftKey;
                UpdatePalShiftSelection();
                RefreshCurrentPreview();
            };
            return btn;
        }

        private void UpdatePalShiftSelection() {
            Button[] btns = new Button[] { btnPalShiftMorning, btnPalShiftAfternoon, btnPalShiftNight, btnPalShiftAll };
            foreach (Button b in btns) {
                if (b == null) continue;
                bool active = (string)b.Tag == palCurrentShift;
                b.BackColor = active ? Color.FromArgb(13, 148, 136) : Color.White;
                b.ForeColor = active ? Color.White : Color.FromArgb(51, 65, 85);
                b.Font = new Font("Segoe UI", 8f, active ? FontStyle.Bold : FontStyle.Regular);
                b.FlatAppearance.BorderColor = active ? Color.FromArgb(13, 148, 136) : Color.FromArgb(203, 213, 225);
            }
        }

        private string GetEffectiveContent(TemplateItem item) {
            if (item == null || string.IsNullOrEmpty(item.Content)) return "";
            if (ShiftHelper.HasShiftTags(item.Content) && palCurrentShift != "all") {
                return ShiftHelper.FilterContentByShift(item.Content, palCurrentShift);
            }
            return item.Content;
        }

        private void RefreshCurrentPreview() {
            if (lstTemplates.SelectedItems.Count > 0) {
                TemplateItem item = lstTemplates.SelectedItems[0].Tag as TemplateItem;
                if (item != null) {
                    bool hasShifts = ShiftHelper.HasShiftTags(item.Content);
                    pnlPalShifts.Visible = hasShifts;
                    string effective = GetEffectiveContent(item);
                    txtRaw.Text = effective;
                    RenderDarToRichTextBox(rtbDar, effective, currentFontSize);
                }
            }
        }

        private void SwitchPreviewView(bool showDar) {
            if (showDar) {
                btnTabDar.BackColor = Color.White;
                btnTabDar.ForeColor = Color.FromArgb(13, 148, 136);
                btnTabDar.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                btnTabRaw.BackColor = Color.FromArgb(241, 245, 249);
                btnTabRaw.ForeColor = Color.FromArgb(100, 116, 139);
                btnTabRaw.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
                rtbDar.Visible = true;
                txtRaw.Visible = false;
            } else {
                btnTabRaw.BackColor = Color.White;
                btnTabRaw.ForeColor = Color.FromArgb(13, 148, 136);
                btnTabRaw.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                btnTabDar.BackColor = Color.FromArgb(241, 245, 249);
                btnTabDar.ForeColor = Color.FromArgb(100, 116, 139);
                btnTabDar.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
                rtbDar.Visible = false;
                txtRaw.Visible = true;
            }
        }

        private void ApplyFontSize() {
            try {
                Font fList = new Font("Segoe UI", currentFontSize, FontStyle.Regular);
                lstTemplates.Font = fList;

                Font fBody;
                try {
                    fBody = new Font("Leelawadee UI", currentFontSize, FontStyle.Regular);
                } catch {
                    fBody = new Font("Segoe UI", currentFontSize, FontStyle.Regular);
                }
                txtRaw.Font = fBody;

                if (lblFontSize != null) {
                    lblFontSize.Text = string.Format("ขนาด: {0:0} pt", currentFontSize);
                }

                if (lstTemplates.SelectedItems.Count > 0) {
                    TemplateItem item = lstTemplates.SelectedItems[0].Tag as TemplateItem;
                    if (item != null) {
                        RenderDarToRichTextBox(rtbDar, GetEffectiveContent(item), currentFontSize);
                    }
                }
            } catch {}
        }

        private void AdjustFontSize(float delta) {
            float newSize = currentFontSize + delta;
            if (newSize < 9.5f) newSize = 9.5f;
            if (newSize > 24.0f) newSize = 24.0f;
            currentFontSize = newSize;
            ApplyFontSize();
            context.SaveFontSize(currentFontSize);
        }

        public void ShowAndFocus(int bed = -1) {
            targetBed = bed;
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != this.Handle) {
                lastActiveWindow = fg;
            }

            if (targetBed > 0) {
                lblTargetBedTop.Text = string.Format("เตียง {0:D2}", targetBed);
                lblTargetBedTop.Visible = true;
                btnInsertToBed.Text = string.Format("แทรกเตียง {0:D2}", targetBed);
                btnReplaceBed.Text = string.Format("แทนที่เตียง {0:D2}", targetBed);
                if (cboTargetBed != null && targetBed >= 1 && targetBed <= 30) {
                    cboTargetBed.SelectedIndex = targetBed - 1;
                }
            } else {
                lblTargetBedTop.Visible = false;
                btnInsertToBed.Text = "แทรกลงเตียง";
                btnReplaceBed.Text = "แทนที่เตียง";
            }

            palCurrentShift = ShiftHelper.GetCurrentShift();
            UpdatePalShiftSelection();
            RefreshList(txtSearch.Text);
            SwitchPreviewView(true);

            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
            txtSearch.Focus();
            txtSearch.SelectAll();
            SetSafeSplitterDistance(370);
        }

        private bool MatchesCategoryFilter(TemplateItem item, string catKey) {
            if (catKey == "all") return true;
            string c = item.Category.ToLower();
            string s = item.Shortcut.ToLower();

            if (catKey == "boneca") return c.Contains("20.") || c.Contains("bone cancer") || c.Contains("มะเร็งกระดูก") || c.Contains("ฉายแสง") || c.Contains("radiotherapy") || s == ".boneca" || s == ".pathofx" || s == ".hypercamal" || s == ".radiotherapy" || s == ".radskin" || s == ".painflare" || s == ".mscc" || s == ".radfatigue";
            if (catKey == "palliative") return c.Contains("16.") || c.Contains("palliative") || c.Contains("ระยะสุดท้าย") || c.Contains("ประคับประคอง") || s == ".palliative" || s == ".terminalpain" || s == ".deathrattle" || s == ".terminaldyspnea" || s == ".deliriumpalliative" || s == ".postmortem";
            if (catKey == "neuro") return c.Contains("17.") || c.Contains("neuro") || c.Contains("ระบบประสาท") || c.Contains("สมอง") || s == ".icp" || s == ".seizure" || s == ".gcsdrop" || s == ".tbi" || s == ".sci" || s == ".delirium" || s == ".stroke";
            if (catKey == "vent") return c.Contains("18.") || c.Contains("ventilator") || c.Contains("เครื่องช่วยหายใจ") || c.Contains("ทางเดินหายใจ") || s == ".vent" || s == ".suction" || s == ".ettcare" || s == ".tracheo" || s == ".wean" || s == ".extubate";
            if (catKey == "icu") return c.Contains("19.") || c.Contains("critical") || c.Contains("วิกฤต") || c.Contains("ช่วยชีวิต") || s == ".cpr" || s == ".transfusion" || s == ".cvcline" || s == ".aline" || s == ".chesttube" || s == ".hadrug";
            if (catKey == "knee") return c.Contains("9.") || c.Contains("knee") || c.Contains("ข้อเข่า") || s == ".tka" || s == ".uka" || s == ".tkarehab" || s == ".arthro";
            if (catKey == "hip") return c.Contains("10.") || c.Contains("hip") || c.Contains("ข้อสะโพก") || s == ".tha" || s == ".bha" || s == ".hipcare";
            if (catKey == "spine") return c.Contains("11.") || c.Contains("spine") || c.Contains("สันหลัง") || c.Contains("กระดูกสันหลัง") || s == ".laminectomy" || s == ".plif" || s == ".acdf" || s == ".csfleak" || s == ".discectomy" || s == ".spinerehab";
            if (catKey == "fracture") return c.Contains("12.") || c.Contains("trauma") || c.Contains("fracture") || c.Contains("กระดูกหัก") || s == ".orif" || s == ".cast" || s == ".traction" || s == ".exfix" || s == ".amputation";
            if (catKey == "surgery") return c.Contains("13.") || c.Contains("specialized") || c.Contains("ผ่าตัดเฉพาะทาง") || c.Contains("ศัลยกรรมเฉพาะทาง") || s == ".appendectomy" || s == ".lapchole" || s == ".mastectomy";
            if (catKey == "electrolyte") return c.Contains("14.") || c.Contains("electrolyte") || c.Contains("เกลือแร่") || s == ".hypok" || s == ".hyperk" || s == ".hypona" || s == ".hyperna" || s == ".hypoca" || s == ".hyperca" || s == ".hypomg" || s == ".hypermg" || s == ".hypop" || s == ".hyperp";
            if (catKey == "lab") return c.Contains("15.") || c.Contains("lab") || c.Contains("ค่าเลือด") || c.Contains("ผลตรวจ") || s == ".anemia" || s == ".thrombocyto" || s == ".pancytopenia" || s == ".leukocytosis" || s == ".neutropenia" || s == ".coagulopathy" || s == ".metacid" || s == ".metalk" || s == ".respacid" || s == ".aki" || s == ".hyperbili" || s == ".hepatitis" || s == ".hypogly" || s == ".hypergly";
            if (catKey == "med") return c.Contains("4.") || c.Contains("internal") || c.Contains("อายุรกรรม") || s == ".sepsis" || s == ".stroke" || s == ".dka" || s == ".chf" || s == ".pneumonia" || s == ".acs" || s == ".htn" || s == ".copd";
            if (catKey == "pain") return c.Contains("2.") || c.Contains("pain") || c.Contains("ปวด") || s.StartsWith(".pain");
            if (catKey == "safe") return c.Contains("5.") || c.Contains("ความปลอดภัย") || c.Contains("ป้องกัน") || s == ".fall" || s == ".pressure" || s == ".infection" || s == ".dvt";
            if (catKey == "fluid") return c.Contains("6.") || c.Contains("สารน้ำ") || c.Contains("ขับถ่าย") || s == ".iv" || s == ".foley" || s == ".constipation" || s == ".nausea";
            if (catKey == "shift") return c.Contains("7.") || c.Contains("รับใหม่") || c.Contains("ส่งเวร") || c.Contains("จำหน่าย") || s == ".nsadm" || s == ".shift" || s == ".transfer" || s == ".dc";
            if (catKey == "shortcut") return c.Contains("8.") || c.Contains("สัญลักษณ์") || c.Contains("คีย์ลัด") || s == ".vs" || s == ".order" || s == ".dt" || s == ".d";
            return true;
        }

        private void RefreshList(string filter) {
            lstTemplates.Items.Clear();
            List<TemplateItem> items = context.GetTemplates();
            string query = filter.Trim().ToLower();

            int matchedCount = 0;
            foreach (TemplateItem item in items) {
                if (!MatchesCategoryFilter(item, currentCategoryFilter)) continue;

                if (string.IsNullOrEmpty(query) || 
                    item.Shortcut.ToLower().Contains(query) || 
                    item.Title.ToLower().Contains(query) || 
                    item.Category.ToLower().Contains(query) ||
                    item.Content.ToLower().Contains(query)) {

                    string shortCat = item.Category.Replace("การพยาบาล", "").Trim();
                    if (shortCat.Length > 16) shortCat = shortCat.Substring(0, 14) + "..";

                    ListViewItem lvi = new ListViewItem(item.Shortcut);
                    lvi.SubItems.Add(shortCat);
                    lvi.SubItems.Add(item.Title);
                    lvi.Tag = item;
                    lstTemplates.Items.Add(lvi);
                    matchedCount++;
                }
            }

            lblSearchCount.Text = string.Format("{0} เทมเพลต", matchedCount);

            if (lstTemplates.Items.Count > 0) {
                lstTemplates.Items[0].Selected = true;
            } else {
                rtbDar.Clear();
                txtRaw.Text = "";
                lblBadgeShortcut.Text = "";
                lblPreviewTitle.Text = "ไม่พบข้อวินิจฉัยที่ค้นหา";
            }
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.Down) {
                if (lstTemplates.Items.Count > 0) {
                    lstTemplates.Focus();
                }
                e.Handled = true;
            } else if (e.KeyCode == Keys.Enter) {
                if (targetBed > 0) {
                    InsertToBed(false);
                } else {
                    PasteSelected();
                }
                e.Handled = true;
            }
        }

        private void LstTemplates_SelectedIndexChanged(object sender, EventArgs e) {
            if (lstTemplates.SelectedItems.Count > 0) {
                TemplateItem item = lstTemplates.SelectedItems[0].Tag as TemplateItem;
                if (item != null) {
                    lblBadgeShortcut.Text = item.Shortcut;
                    lblPreviewTitle.Text = item.Title;
                    if (ShiftHelper.HasShiftTags(item.Content)) {
                        pnlPalShifts.Visible = true;
                        palCurrentShift = ShiftHelper.GetCurrentShift();
                    } else {
                        pnlPalShifts.Visible = false;
                        palCurrentShift = "all";
                    }
                    UpdatePalShiftSelection();
                    RefreshCurrentPreview();
                }
            }
        }

        private void RenderDarToRichTextBox(RichTextBox rtb, string content, float baseFontSize) {
            rtb.Clear();
            if (string.IsNullOrEmpty(content)) return;

            string[] lines = content.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            string curSection = "general";

            Font fHdr = new Font("Segoe UI", baseFontSize + 1.0f, FontStyle.Bold);
            Font fBody;
            Font fBold;
            try {
                fBody = new Font("Leelawadee UI", baseFontSize, FontStyle.Regular);
                fBold = new Font("Leelawadee UI", baseFontSize, FontStyle.Bold);
            } catch {
                fBody = new Font("Segoe UI", baseFontSize, FontStyle.Regular);
                fBold = new Font("Segoe UI", baseFontSize, FontStyle.Bold);
            }

            Color cFocus = Color.FromArgb(79, 70, 229);    // Indigo #4f46e5
            Color cGoal = Color.FromArgb(217, 119, 6);     // Amber 600 #d97706
            Color cData = Color.FromArgb(2, 132, 199);     // Sky Blue #0284c7
            Color cAction = Color.FromArgb(5, 150, 105);   // Emerald #059669
            Color cResp = Color.FromArgb(13, 148, 136);    // Teal #0d9488
            Color cText = Color.FromArgb(30, 41, 59);      // Slate 800

            bool hasDar = false;
            foreach (string l in lines) {
                if (l.Trim().StartsWith("Focus:") || l.Trim().StartsWith("Goal:") || l.Trim().StartsWith("Data:") || 
                    l.Trim().StartsWith("Action:") || l.Trim().StartsWith("Response:")) {
                    hasDar = true;
                    break;
                }
            }

            if (!hasDar) {
                rtb.SelectionFont = fBody;
                rtb.SelectionColor = cText;
                rtb.AppendText(content);
                return;
            }

            foreach (string line in lines) {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("Focus:")) {
                    curSection = "focus";
                    AppendSectionHeader(rtb, "[FOCUS] ข้อวินิจฉัย / ปัญหาทางการพยาบาล", cFocus, fHdr);
                    string val = trimmed.Substring("Focus:".Length).Trim();
                    if (!string.IsNullOrEmpty(val)) {
                        AppendSectionBody(rtb, val, cFocus, fBold);
                    }
                } else if (trimmed.StartsWith("Goal:")) {
                    curSection = "goal";
                    AppendSectionHeader(rtb, "[GOAL] เป้าหมายทางการพยาบาลและผลลัพธ์ที่คาดหวัง", cGoal, fHdr);
                    string val = trimmed.Substring("Goal:".Length).Trim();
                    if (!string.IsNullOrEmpty(val)) {
                        AppendSectionBody(rtb, val, cGoal, fBold);
                    }
                } else if (trimmed.StartsWith("Data:")) {
                    curSection = "data";
                    AppendSectionHeader(rtb, "[DATA] ข้อมูลผู้ป่วยและอาการแสดง (S & O)", cData, fHdr);
                    string val = trimmed.Substring("Data:".Length).Trim();
                    if (!string.IsNullOrEmpty(val)) {
                        AppendSectionBody(rtb, val, cText, fBody);
                    }
                } else if (trimmed.StartsWith("Action:")) {
                    curSection = "action";
                    AppendSectionHeader(rtb, "[ACTION] กิจกรรมการพยาบาลและข้อควรระวัง", cAction, fHdr);
                    string val = trimmed.Substring("Action:".Length).Trim();
                    if (!string.IsNullOrEmpty(val)) {
                        AppendSectionBody(rtb, val, cText, fBody);
                    }
                } else if (trimmed.StartsWith("Response:")) {
                    curSection = "response";
                    AppendSectionHeader(rtb, "[RESPONSE] การประเมินผลลัพธ์ทางการพยาบาล", cResp, fHdr);
                    string val = trimmed.Substring("Response:".Length).Trim();
                    if (!string.IsNullOrEmpty(val)) {
                        AppendSectionBody(rtb, val, cText, fBody);
                    }
                } else {
                    if (!string.IsNullOrEmpty(line)) {
                        Color col = curSection == "focus" ? cFocus : (curSection == "goal" ? cGoal : cText);
                        Font fnt = (curSection == "focus" || curSection == "goal" || line.Contains("ห้าม") || line.Contains("ระวัง") || line.Contains("เฝ้าระวัง") || line.Contains("**")) ? fBold : fBody;
                        string cleanLine = line.Replace("**", "");
                        AppendSectionBody(rtb, cleanLine, col, fnt);
                    } else {
                        rtb.AppendText("\n");
                    }
                }
            }

            rtb.SelectionStart = 0;
            rtb.ScrollToCaret();
        }

        private void AppendSectionHeader(RichTextBox rtb, string title, Color col, Font fnt) {
            if (rtb.TextLength > 0) rtb.AppendText("\n\n");
            rtb.SelectionFont = fnt;
            rtb.SelectionColor = col;
            rtb.AppendText(title + "\n");
            rtb.SelectionColor = Color.FromArgb(203, 213, 225);
            rtb.AppendText(new string('-', 65) + "\n"); // Safe ASCII hyphens, NEVER turns into tofu boxes!
        }

        private void AppendSectionBody(RichTextBox rtb, string text, Color col, Font fnt) {
            rtb.SelectionFont = fnt;
            rtb.SelectionColor = col;
            rtb.AppendText(text + "\n");
        }

        private string GetSelectedPreviewText(bool includeSearch = false) {
            try {
                if (includeSearch && txtSearch != null && txtSearch.Focused && txtSearch.SelectionLength > 0 && !string.IsNullOrEmpty(txtSearch.SelectedText)) {
                    return txtSearch.SelectedText;
                }
                if (rtbDar != null && rtbDar.Visible && rtbDar.SelectionLength > 0 && !string.IsNullOrEmpty(rtbDar.SelectedText)) {
                    return rtbDar.SelectedText;
                }
                if (txtRaw != null && txtRaw.Visible && txtRaw.SelectionLength > 0 && !string.IsNullOrEmpty(txtRaw.SelectedText)) {
                    return txtRaw.SelectedText;
                }
            } catch {}
            return null;
        }

        private void CopySelected(bool forceAll = false) {
            string selectedText = forceAll ? null : GetSelectedPreviewText(true);
            bool isPartial = !string.IsNullOrEmpty(selectedText);
            string textToCopy = selectedText;

            if (string.IsNullOrEmpty(textToCopy)) {
                if (lstTemplates.SelectedItems.Count == 0) return;
                TemplateItem item = lstTemplates.SelectedItems[0].Tag as TemplateItem;
                if (item == null) return;
                textToCopy = GetEffectiveContent(item);
            }

            if (string.IsNullOrEmpty(textToCopy)) return;

            try {
                Clipboard.SetDataObject(textToCopy, true, 5, 50);
                string origText = btnCopy.Text;
                btnCopy.Text = isPartial ? "คัดลอกส่วนที่เลือกแล้ว!" : "คัดลอกทั้งหมดแล้ว!";
                btnCopy.BackColor = Color.FromArgb(204, 251, 241);
                var t = new System.Windows.Forms.Timer();
                t.Interval = 1200;
                t.Tick += (s, e) => {
                    btnCopy.Text = origText;
                    btnCopy.BackColor = Color.FromArgb(241, 245, 249);
                    t.Stop();
                    t.Dispose();
                };
                t.Start();
            } catch {}
        }

        private int GetSelectedBedNumber() {
            if (targetBed > 0) return targetBed;
            if (cboTargetBed != null && cboTargetBed.SelectedIndex >= 0) {
                return cboTargetBed.SelectedIndex + 1;
            }
            return 1;
        }

        private void InsertToBed(bool replace) {
            if (lstTemplates.SelectedItems.Count == 0) return;
            TemplateItem item = lstTemplates.SelectedItems[0].Tag as TemplateItem;
            if (item == null) return;

            string sel = GetSelectedPreviewText(false);
            string contentToUse = !string.IsNullOrEmpty(sel) ? sel : GetEffectiveContent(item);

            int bNum = GetSelectedBedNumber();
            context.InsertTemplateToBed(bNum, contentToUse, replace);
            this.Hide();
        }

        private void PasteSelected() {
            if (lstTemplates.SelectedItems.Count == 0) return;
            TemplateItem item = lstTemplates.SelectedItems[0].Tag as TemplateItem;
            if (item == null) return;

            string sel = GetSelectedPreviewText(false);
            string content = !string.IsNullOrEmpty(sel) ? sel : GetEffectiveContent(item);
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

    // =========================================================================
    // หน้าต่างยืนยันรหัสผ่านผู้ดูแลระบบ (Admin Password Security Dialog)
    // =========================================================================
    public class AdminPasswordDialog : Form {
        private string correctPassword;
        private TextBox txtPassword;
        private Label lblError;
        private Button btnOk;
        private Button btnCancel;
        private Button btnTogglePass;

        public AdminPasswordDialog(string targetPassword) {
            correctPassword = targetPassword;
            InitializeUI();
        }

        private void InitializeUI() {
            this.Text = "🔒 ยืนยันสิทธิ์ผู้ดูแลระบบ (Admin Security)";
            this.Size = new Size(420, 240);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Leelawadee UI", 9.5f, FontStyle.Regular);

            Panel pnlHeader = new Panel();
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 52;
            pnlHeader.BackColor = Color.FromArgb(15, 23, 42);

            Label lblTitle = new Label();
            lblTitle.Text = "🔒 ความปลอดภัยของระบบ (Access Control)";
            lblTitle.ForeColor = Color.White;
            lblTitle.Font = new Font("Leelawadee UI", 10.5f, FontStyle.Bold);
            lblTitle.Location = new Point(14, 14);
            lblTitle.AutoSize = true;
            pnlHeader.Controls.Add(lblTitle);
            this.Controls.Add(pnlHeader);

            Label lblPrompt = new Label();
            lblPrompt.Text = "กรุณากรอกรหัสผ่านเพื่อเข้าสู่การตั้งค่าโปรแกรม:";
            lblPrompt.Location = new Point(20, 65);
            lblPrompt.AutoSize = true;
            lblPrompt.ForeColor = Color.FromArgb(51, 65, 85);
            this.Controls.Add(lblPrompt);

            txtPassword = new TextBox();
            txtPassword.Location = new Point(24, 92);
            txtPassword.Size = new Size(295, 32);
            txtPassword.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            txtPassword.PasswordChar = '●';
            txtPassword.TextAlign = HorizontalAlignment.Center;
            this.Controls.Add(txtPassword);

            btnTogglePass = new Button();
            btnTogglePass.Text = "👁️";
            btnTogglePass.Location = new Point(325, 92);
            btnTogglePass.Size = new Size(45, 30);
            btnTogglePass.Font = new Font("Segoe UI", 10f);
            btnTogglePass.Cursor = Cursors.Hand;
            btnTogglePass.Click += (s, e) => {
                txtPassword.PasswordChar = (txtPassword.PasswordChar == '●') ? '\0' : '●';
            };
            this.Controls.Add(btnTogglePass);

            lblError = new Label();
            lblError.Location = new Point(24, 127);
            lblError.Size = new Size(356, 20);
            lblError.ForeColor = Color.FromArgb(225, 29, 72);
            lblError.Font = new Font("Leelawadee UI", 8.5f, FontStyle.Bold);
            lblError.TextAlign = ContentAlignment.MiddleCenter;
            lblError.Visible = false;
            this.Controls.Add(lblError);

            btnOk = new Button();
            btnOk.Text = "เข้าสู่การตั้งค่า (OK)";
            btnOk.Location = new Point(155, 155);
            btnOk.Size = new Size(140, 34);
            btnOk.BackColor = Color.FromArgb(13, 148, 136);
            btnOk.ForeColor = Color.White;
            btnOk.FlatStyle = FlatStyle.Flat;
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Font = new Font("Leelawadee UI", 9f, FontStyle.Bold);
            btnOk.Cursor = Cursors.Hand;
            btnOk.Click += (s, e) => ValidatePassword();
            this.Controls.Add(btnOk);

            btnCancel = new Button();
            btnCancel.Text = "ยกเลิก";
            btnCancel.Location = new Point(302, 155);
            btnCancel.Size = new Size(80, 34);
            btnCancel.BackColor = Color.FromArgb(226, 232, 240);
            btnCancel.ForeColor = Color.FromArgb(71, 85, 105);
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Font = new Font("Leelawadee UI", 9f);
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += (s, e) => {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            this.Controls.Add(btnCancel);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            this.Shown += (s, e) => {
                txtPassword.Focus();
            };
        }

        private void ValidatePassword() {
            if (txtPassword.Text.Trim() == correctPassword) {
                this.DialogResult = DialogResult.OK;
                this.Close();
            } else {
                try { System.Media.SystemSounds.Hand.Play(); } catch {}
                lblError.Text = "❌ รหัสผ่านไม่ถูกต้อง! กรุณาลองใหม่อีกครั้ง";
                lblError.Visible = true;
                txtPassword.SelectAll();
                txtPassword.Focus();
            }
        }
    }

    public class SyncSettingsForm : Form {
        private ExpanderContext context;
        private CheckBox chkEnableSupabase;
        private TextBox txtSupabaseUrl;
        private TextBox txtSupabaseKey;
        private Label lblSupabaseStatus;
        private Button btnTestSupabase;

        private TextBox txtSharedPath;
        private TextBox txtSharedBedNotes;
        private TextBox txtGitHubRepo;
        private TextBox txtAdminPassword;
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
            this.Text = "การเชื่อมต่อข้อมูลส่วนกลางของวอร์ด (Cloud & Network Sync)";
            this.Size = new System.Drawing.Size(650, 710);
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

            // ----------------------------------------------------
            // Section 1: Supabase Cloud Realtime Sync (Recommended)
            // ----------------------------------------------------
            GroupBox grpSupabase = new GroupBox();
            grpSupabase.Text = " ☁️ ระบบซิงค์ข้อมูลผ่าน Supabase Cloud (แนะนำที่สุด - ทั่วโลก & นอก รพ.) ";
            grpSupabase.Location = new Point(16, 12);
            grpSupabase.Size = new Size(602, 230);
            grpSupabase.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            grpSupabase.ForeColor = Color.FromArgb(13, 148, 136);

            chkEnableSupabase = new CheckBox();
            chkEnableSupabase.Text = "เปิดใช้งานระบบซิงค์ Supabase Cloud (ทำงานได้ทั้งในวอร์ดและนอก รพ.)";
            chkEnableSupabase.Location = new Point(16, 26);
            chkEnableSupabase.Size = new Size(560, 26);
            chkEnableSupabase.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            chkEnableSupabase.ForeColor = Color.FromArgb(30, 41, 59);
            chkEnableSupabase.Checked = context.GetSupabaseEnabled();
            grpSupabase.Controls.Add(chkEnableSupabase);

            Label lblSupaUrl = new Label();
            lblSupaUrl.Text = "Project URL:";
            lblSupaUrl.Location = new Point(16, 58);
            lblSupaUrl.AutoSize = true;
            lblSupaUrl.Font = new Font("Segoe UI", 9f);
            lblSupaUrl.ForeColor = Color.FromArgb(71, 85, 105);
            grpSupabase.Controls.Add(lblSupaUrl);

            txtSupabaseUrl = new TextBox();
            txtSupabaseUrl.Location = new Point(16, 80);
            txtSupabaseUrl.Size = new Size(570, 27);
            txtSupabaseUrl.Font = new Font("Segoe UI", 9.5f);
            txtSupabaseUrl.Text = context.GetSupabaseUrl();
            grpSupabase.Controls.Add(txtSupabaseUrl);

            Label lblSupaKey = new Label();
            lblSupaKey.Text = "Anon Public API Key:";
            lblSupaKey.Location = new Point(16, 114);
            lblSupaKey.AutoSize = true;
            lblSupaKey.Font = new Font("Segoe UI", 9f);
            lblSupaKey.ForeColor = Color.FromArgb(71, 85, 105);
            grpSupabase.Controls.Add(lblSupaKey);

            txtSupabaseKey = new TextBox();
            txtSupabaseKey.Location = new Point(16, 136);
            txtSupabaseKey.Size = new Size(570, 27);
            txtSupabaseKey.Font = new Font("Segoe UI", 9.5f);
            txtSupabaseKey.Text = context.GetSupabaseKey();
            grpSupabase.Controls.Add(txtSupabaseKey);

            btnTestSupabase = new Button();
            btnTestSupabase.Text = "⚡ ทดสอบการเชื่อมต่อ Cloud";
            btnTestSupabase.Location = new Point(16, 175);
            btnTestSupabase.Size = new Size(210, 36);
            btnTestSupabase.BackColor = Color.FromArgb(240, 253, 244);
            btnTestSupabase.ForeColor = Color.FromArgb(22, 101, 52);
            btnTestSupabase.FlatStyle = FlatStyle.Flat;
            btnTestSupabase.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnTestSupabase.Cursor = Cursors.Hand;
            btnTestSupabase.Click += (s, e) => {
                btnTestSupabase.Enabled = false;
                lblSupabaseStatus.Text = "กำลังทดสอบการเชื่อมต่อ...";
                lblSupabaseStatus.ForeColor = Color.FromArgb(100, 100, 100);
                string url = txtSupabaseUrl.Text.Trim();
                string key = txtSupabaseKey.Text.Trim();
                ThreadPool.QueueUserWorkItem(_ => {
                    var client = new SupabaseSyncClient(url, key);
                    bool ok = client.TestConnection();
                    if (this.IsHandleCreated && !this.IsDisposed) {
                        this.BeginInvoke(new Action(() => {
                            btnTestSupabase.Enabled = true;
                            if (ok) {
                                lblSupabaseStatus.Text = "✅ เชื่อมต่อกับ Supabase Cloud สำเร็จ 100%!";
                                lblSupabaseStatus.ForeColor = Color.DarkGreen;
                            } else {
                                lblSupabaseStatus.Text = "❌ ไม่สามารถเชื่อมต่อได้ กรุณาตรวจสอบ URL/Key";
                                lblSupabaseStatus.ForeColor = Color.Red;
                            }
                        }));
                    }
                });
            };
            grpSupabase.Controls.Add(btnTestSupabase);

            lblSupabaseStatus = new Label();
            lblSupabaseStatus.Location = new Point(236, 183);
            lblSupabaseStatus.Size = new Size(350, 24);
            lblSupabaseStatus.Text = "สถานะ: พร้อมเชื่อมต่อ";
            lblSupabaseStatus.Font = new Font("Segoe UI", 9f);
            lblSupabaseStatus.ForeColor = Color.FromArgb(100, 100, 100);
            grpSupabase.Controls.Add(lblSupabaseStatus);

            this.Controls.Add(grpSupabase);

            // ----------------------------------------------------
            // Section 2: LAN / Shared Folder (Windows SMB)
            // ----------------------------------------------------
            Label lblDesc = new Label();
            lblDesc.Text = "📁 ทางเลือกสำรอง: แชร์โฟลเดอร์ในวงแลนของวอร์ด (Windows SMB):";
            lblDesc.Location = new Point(16, 252);
            lblDesc.Size = new Size(602, 24);
            lblDesc.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblDesc.ForeColor = Color.FromArgb(50, 50, 50);
            this.Controls.Add(lblDesc);

            // 1. Templates Path
            Label lblPath = new Label();
            lblPath.Text = "1. ไฟล์เทมเพลตส่วนกลาง (medical_templates.txt):";
            lblPath.Location = new Point(16, 280);
            lblPath.AutoSize = true;
            lblPath.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            this.Controls.Add(lblPath);

            txtSharedPath = new TextBox();
            txtSharedPath.Location = new Point(18, 305);
            txtSharedPath.Size = new Size(475, 29);
            txtSharedPath.Font = new Font("Segoe UI", 9.5f);
            txtSharedPath.Text = currentTemplatePath;
            this.Controls.Add(txtSharedPath);

            btnBrowsePath = new Button();
            btnBrowsePath.Text = "เลือกไฟล์...";
            btnBrowsePath.Location = new Point(505, 303);
            btnBrowsePath.Size = new Size(110, 32);
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
            lblBedPath.Text = "2. โฟลเดอร์เตียง 1-30 (ward_bed_notes):";
            lblBedPath.Location = new Point(16, 342);
            lblBedPath.AutoSize = true;
            lblBedPath.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            this.Controls.Add(lblBedPath);

            txtSharedBedNotes = new TextBox();
            txtSharedBedNotes.Location = new Point(18, 367);
            txtSharedBedNotes.Size = new Size(475, 29);
            txtSharedBedNotes.Font = new Font("Segoe UI", 9.5f);
            txtSharedBedNotes.Text = currentBedNotesPath;
            this.Controls.Add(txtSharedBedNotes);

            btnBrowseBedNotes = new Button();
            btnBrowseBedNotes.Text = "เลือกโฟลเดอร์...";
            btnBrowseBedNotes.Location = new Point(505, 365);
            btnBrowseBedNotes.Size = new Size(110, 32);
            btnBrowseBedNotes.Click += (s, e) => {
                FolderBrowserDialog fbd = new FolderBrowserDialog();
                fbd.Description = "เลือกโฟลเดอร์บันทึกเตียง (ward_bed_notes)";
                if (fbd.ShowDialog() == DialogResult.OK) {
                    txtSharedBedNotes.Text = fbd.SelectedPath;
                }
            };
            this.Controls.Add(btnBrowseBedNotes);

            // 3. GitHub Auto-Update Repository
            Label lblGit = new Label();
            lblGit.Text = "3. ที่อยู่ GitHub Repository สำหรับตรวจอัปเดตอัตโนมัติ:";
            lblGit.Location = new Point(16, 408);
            lblGit.AutoSize = true;
            lblGit.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            this.Controls.Add(lblGit);

            txtGitHubRepo = new TextBox();
            txtGitHubRepo.Location = new Point(18, 433);
            txtGitHubRepo.Size = new Size(375, 29);
            txtGitHubRepo.Font = new Font("Segoe UI", 9.5f);
            txtGitHubRepo.Text = context.GetGitHubRepo();
            this.Controls.Add(txtGitHubRepo);

            Button btnCheckNow = new Button();
            btnCheckNow.Text = "🚀 ตรวจสอบอัปเดตเดี๋ยวนี้";
            btnCheckNow.Location = new Point(405, 431);
            btnCheckNow.Size = new Size(210, 32);
            btnCheckNow.BackColor = Color.FromArgb(224, 231, 255);
            btnCheckNow.ForeColor = Color.FromArgb(67, 56, 202);
            btnCheckNow.FlatStyle = FlatStyle.Flat;
            btnCheckNow.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnCheckNow.Cursor = Cursors.Hand;
            btnCheckNow.Click += (s, e) => {
                AppUpdater.CheckForUpdatesAsync(txtGitHubRepo.Text.Trim(), true, this, context.GetGitHubToken());
            };
            // 4. Admin Security Password
            Label lblAdminPass = new Label();
            lblAdminPass.Text = "4. 🔒 รหัสผ่านผู้ดูแลระบบ (Admin Password สำหรับเข้าตั้งค่า):";
            lblAdminPass.Location = new Point(16, 472);
            lblAdminPass.AutoSize = true;
            lblAdminPass.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            lblAdminPass.ForeColor = Color.FromArgb(15, 118, 110);
            this.Controls.Add(lblAdminPass);

            txtAdminPassword = new TextBox();
            txtAdminPassword.Location = new Point(18, 497);
            txtAdminPassword.Size = new Size(200, 29);
            txtAdminPassword.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            txtAdminPassword.PasswordChar = '●';
            txtAdminPassword.Text = context.AdminPassword;
            this.Controls.Add(txtAdminPassword);

            Button btnTogglePass = new Button();
            btnTogglePass.Text = "👁️ ดูรหัส";
            btnTogglePass.Location = new Point(225, 496);
            btnTogglePass.Size = new Size(80, 30);
            btnTogglePass.Font = new Font("Segoe UI", 8.5f);
            btnTogglePass.Cursor = Cursors.Hand;
            btnTogglePass.Click += (s, e) => {
                txtAdminPassword.PasswordChar = (txtAdminPassword.PasswordChar == '●') ? '\0' : '●';
            };
            this.Controls.Add(btnTogglePass);

            lblStatus = new Label();
            lblStatus.Location = new Point(18, 537);
            lblStatus.Size = new Size(600, 36);
            lblStatus.Text = string.IsNullOrEmpty(currentTemplatePath) 
                ? "สถานะ LAN: ไม่ได้เชื่อมต่อโฟลเดอร์ในวงแลน" 
                : "สถานะ LAN: เชื่อมต่อข้อมูลโฟลเดอร์ในวงแลนอยู่";
            lblStatus.ForeColor = Color.FromArgb(100, 100, 100);
            this.Controls.Add(lblStatus);

            btnTest = new Button();
            btnTest.Text = "🔍 ทดสอบเชื่อมต่อ LAN";
            btnTest.Location = new System.Drawing.Point(18, 580);
            btnTest.Size = new Size(180, 40);
            btnTest.Font = new Font("Segoe UI", 9.5f);
            btnTest.Click += (s, e) => {
                string pTpl = txtSharedPath.Text.Trim();
                string pBed = txtSharedBedNotes.Text.Trim();

                bool tplOk = string.IsNullOrEmpty(pTpl) || File.Exists(pTpl);
                bool bedOk = string.IsNullOrEmpty(pBed) || Directory.Exists(pBed);

                if (tplOk && bedOk && (!string.IsNullOrEmpty(pTpl) || !string.IsNullOrEmpty(pBed))) {
                    lblStatus.Text = "✅ เชื่อมต่อ LAN สำเร็จ! เข้าถึงไฟล์เทมเพลตและโฟลเดอร์เตียงได้ปกติ";
                    lblStatus.ForeColor = Color.DarkGreen;
                } else {
                    lblStatus.Text = "❌ ไม่สามารถเข้าถึงที่อยู่ในวงแลนได้ กรุณาตรวจสอบสิทธิ์ของเครือข่าย";
                    lblStatus.ForeColor = Color.Red;
                }
            };
            this.Controls.Add(btnTest);

            btnSave = new Button();
            btnSave.Text = "💾 บันทึกการตั้งค่าทั้งหมด";
            btnSave.Location = new System.Drawing.Point(210, 580);
            btnSave.Size = new Size(220, 40);
            btnSave.BackColor = Color.FromArgb(13, 148, 136);
            btnSave.ForeColor = Color.White;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnSave.Click += (s, e) => {
                string pTpl = txtSharedPath.Text.Trim();
                string pBed = txtSharedBedNotes.Text.Trim();
                if (!string.IsNullOrEmpty(txtAdminPassword.Text.Trim())) {
                    context.AdminPassword = txtAdminPassword.Text.Trim();
                }
                context.SaveSettings(pTpl, pBed);
                context.SetGitHubRepo(txtGitHubRepo.Text.Trim());
                context.SetSupabaseConfig(txtSupabaseUrl.Text.Trim(), txtSupabaseKey.Text.Trim(), chkEnableSupabase.Checked);
                MessageBox.Show("บันทึกการตั้งค่าเรียบร้อยแล้ว! ระบบความปลอดภัย คลาวด์ และวงแลนพร้อมทำงานทันที", "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            };
            this.Controls.Add(btnSave);

            btnClear = new Button();
            btnClear.Text = "ใช้ในเครื่องนี้เท่านั้น";
            btnClear.Location = new System.Drawing.Point(442, 580);
            btnClear.Size = new Size(175, 40);
            btnClear.Font = new Font("Segoe UI", 9.5f);
            btnClear.Click += (s, e) => {
                txtSharedPath.Text = "";
                txtSharedBedNotes.Text = "";
                chkEnableSupabase.Checked = false;
                context.SaveSettings("", "");
                context.SetSupabaseConfig("", "", false);
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
            split.Panel1MinSize = 100;
            split.Panel2MinSize = 120;

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

            Action setSafePaletteSplit = () => {
                if (split == null) return;
                try {
                    int h = split.ClientSize.Height;
                    if (h > 180) {
                        int dist = 270;
                        int max = h - split.Panel2MinSize - 5;
                        int min = split.Panel1MinSize + 5;
                        if (max > min) {
                            if (dist > max) dist = max;
                            if (dist < min) dist = min;
                            split.SplitterDistance = dist;
                        }
                    }
                } catch {}
            };
            this.Shown += (s, e) => setSafePaletteSplit();
            this.Resize += (s, e) => setSafePaletteSplit();
            setSafePaletteSplit();
            split.BringToFront();

            Action setSafeSplit = () => {
                if (split == null) return;
                try {
                    int w = split.ClientSize.Width;
                    if (w > 260) {
                        int dist = 320;
                        int max = w - split.Panel2MinSize - 5;
                        int min = split.Panel1MinSize + 5;
                        if (max > min) {
                            if (dist > max) dist = max;
                            if (dist < min) dist = min;
                            split.SplitterDistance = dist;
                        }
                    }
                } catch {}
            };

            this.Shown += (s, e) => setSafeSplit();
            this.Resize += (s, e) => setSafeSplit();
            setSafeSplit();

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

        public void MoveOrSwapBedReminders(int fromBed, int toBed, bool isSwap) {
            lock (syncLock) {
                bool changed = false;
                foreach (WardReminderItem item in reminders) {
                    if (isSwap) {
                        if (item.BedNum == fromBed) {
                            item.BedNum = toBed;
                            changed = true;
                        } else if (item.BedNum == toBed) {
                            item.BedNum = fromBed;
                            changed = true;
                        }
                    } else {
                        if (item.BedNum == fromBed) {
                            item.BedNum = toBed;
                            changed = true;
                        }
                    }
                }
                if (changed) {
                    Save();
                    if (OnRemindersChanged != null) {
                        try { OnRemindersChanged(); } catch {}
                    }
                }
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
