// Bully Co-op Hamdan | Launcher v0.17b LAN LOBBY ALPHA
// Independent Windows Forms launcher. Bully gameplay remains in unchanged v0.16b DLL.
// This is NOT a mission-complete co-op mod. One host and one guest only.
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BullyCoopHamdan {
    public sealed class Launcher : Form {
        const long KnownExeSize = 8204288; // validated by the existing v0.16b bridge
        const int DefaultPort = 7792;
        static readonly Color Back = Color.FromArgb(15, 17, 38);
        static readonly Color Surface = Color.FromArgb(31, 36, 65);
        static readonly Color Accent = Color.FromArgb(244, 75, 145);
        static readonly Color Cyan = Color.FromArgb(78, 226, 227);
        static readonly Color TextColor = Color.FromArgb(238, 240, 252);
        static readonly Color Subtle = Color.FromArgb(168, 175, 201);

        TextBox gameDir, nick, hostAddress, roomPort, roomCode, output;
        Button openHost, joinHost, ready, start, browse, stop;
        Label status, lobbyStatus, pingStatus, hint;
        CheckBox samePc;
        readonly string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BullyCoopLauncher.settings.ini");
        readonly System.Windows.Forms.Timer timer;
        readonly object sendLock = new object();
        TcpListener listener;
        TcpClient peerClient;
        StreamReader reader;
        StreamWriter writer;
        volatile bool stopping;
        volatile bool connected, isHost, guestReady, launched, gameNetworkVerified;
        int activePort;
        string activeCode = "", activeIP = "", activeName = "";
        long lastPingTicks;
        long gameLogStartLength; // prevents a previous session from faking a UDP handshake

        public Launcher() {
            Text = "Bully Co-op Hamdan | Launcher v0.17b ALPHA";
            ClientSize = new Size(904, 692);
            MinimumSize = new Size(925, 731);
            BackColor = Back;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 10f);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BuildInterface();
            LoadPreferences();
            roomCode.Text = NewCode();
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 2500;
            timer.Tick += (sender, e) => OnTick();
            timer.Start();
            FormClosing += (sender, e) => { timer.Stop(); StopNetworking(false); SavePreferences(); };
        }

        Label L(string value, int x, int y, int w, int h, int size, Color c) {
            Label label = new Label();
            label.Text = value; label.SetBounds(x, y, w, h);
            label.Font = new Font("Segoe UI", size, size >= 16 ? FontStyle.Bold : FontStyle.Regular);
            label.ForeColor = c;
            Controls.Add(label);
            return label;
        }
        TextBox T(int x, int y, int w, string sample) {
            TextBox item = new TextBox();
            item.SetBounds(x, y, w, 31);
            item.BackColor = Surface; item.ForeColor = TextColor;
            item.BorderStyle = BorderStyle.FixedSingle;
            item.Font = new Font("Segoe UI", 11f);
            item.Text = sample;
            Controls.Add(item); return item;
        }
        Button B(string value, int x, int y, int w, Color color) {
            Button b = new Button();
            b.Text = value; b.SetBounds(x, y, w, 43);
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.BackColor = color; b.ForeColor = TextColor;
            b.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            Controls.Add(b); return b;
        }
        void BuildInterface() {
            L("BULLY  /  CO-OP", 27, 20, 480, 43, 26, TextColor);
            L("HAMDAN  •  LAUNCHER v0.17b", 30, 65, 450, 24, 11, Accent);
            L("PRIVATE TEST • 1 HOST + 1 GUEST", 595, 34, 310, 26, 11, Cyan);

            L("مجلد اللعبة (Bully.exe و dinput8.dll)", 29, 106, 570, 28, 11, Subtle);
            gameDir = T(29, 140, 695, "");
            browse = B("اختيار...", 739, 135, 138, Surface);
            browse.Click += (sender, e) => {
                using (FolderBrowserDialog d = new FolderBrowserDialog()) {
                    d.Description = "حدد مجلد نسختك من Bully";
                    if (Directory.Exists(gameDir.Text)) d.SelectedPath = gameDir.Text;
                    if (d.ShowDialog(this) == DialogResult.OK) gameDir.Text = d.SelectedPath;
                }
            };
            L("اسمك", 29, 189, 250, 23, 10, Subtle);
            L("عنوان الـHost (في جهاز الضيف)", 284, 189, 370, 23, 10, Subtle);
            L("المنفذ", 658, 189, 110, 23, 10, Subtle);
            nick = T(29, 214, 240, "Hamdan");
            hostAddress = T(284, 214, 360, "");
            roomPort = T(658, 214, 110, DefaultPort.ToString());
            L("رمز الغرفة (6 أرقام)", 29, 259, 270, 26, 10, Subtle);
            roomCode = T(29, 285, 240, "");
            samePc = new CheckBox();
            samePc.Text = "تجربة نسختين على نفس الكمبيوتر";
            samePc.SetBounds(285, 286, 375, 32);
            samePc.ForeColor = Cyan;
            samePc.BackColor = Back;
            samePc.Checked = false;
            samePc.CheckedChanged += (sender, e) => {
                if (samePc.Checked) hostAddress.Text = "127.0.0.1";
                else if (hostAddress.Text == "127.0.0.1") hostAddress.Text = FindLocalIPv4();
            };
            openHost = B("إنشاء غرفة HOST", 29, 340, 202, Accent);
            joinHost = B("الانضمام JOIN", 246, 340, 197, Color.FromArgb(59, 132, 150));
            ready = B("جاهز READY", 458, 340, 190, Surface);
            start = B("START GAME", 664, 340, 213, Surface);
            stop = B("فصل الاتصال", 29, 402, 167, Surface);
            openHost.Click += (sender, e) => OpenRoom();
            joinHost.Click += (sender, e) => JoinRoom();
            ready.Click += (sender, e) => ReadyToggle();
            start.Click += (sender, e) => StartBothGames();
            stop.Click += (sender, e) => StopNetworking(true);
            ready.Enabled = false; start.Enabled = false; stop.Enabled = false;
            lobbyStatus = L("LOBBY: لم يتم إنشاء غرفة", 211, 402, 460, 36, 10, TextColor);
            pingStatus = L("PING: --", 711, 402, 166, 36, 10, Cyan);
            status = L("الشبكة: في انتظار إعداد الغرفة", 29, 447, 850, 34, 11, Accent);
            output = new TextBox();
            output.SetBounds(29, 488, 848, 144);
            output.Multiline = true; output.ReadOnly = true;
            output.BackColor = Surface; output.ForeColor = TextColor;
            output.BorderStyle = BorderStyle.None;
            output.Font = new Font("Consolas", 10f);
            output.ScrollBars = ScrollBars.Vertical;
            Controls.Add(output);
            hint = L("LOBBY TCP = PORT + 1  |  GAME UDP = PORT  |  لا تغير dinput8.dll المجرب", 29, 644, 860, 25, 10, Subtle);
            Note("اختر اللعبة في كل جهاز. هذا اللانشر يدير الغرفة؛ اللعب يحتاج DLL v0.16b.");
        }
        void Note(string message) {
            if (output.IsDisposed) return;
            if (output.TextLength > 16000) output.Text = output.Text.Substring(output.TextLength - 10000);
            output.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        }
        void Ui(Action action) {
            try {
                if (IsDisposed || Disposing || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke((MethodInvoker)(() => { if (!IsDisposed) action(); }));
                else action();
            } catch (InvalidOperationException) { } catch (ObjectDisposedException) { }
        }
        static string NewCode() {
            byte[] bytes = new byte[4];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            uint value = BitConverter.ToUInt32(bytes, 0);
            return (100000 + value % 900000).ToString();
        }
        static string FindLocalIPv4() {
            try {
                foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName())) {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) return ip.ToString();
                }
            } catch { }
            return "IP not detected — use ipconfig"; // never invent a local network address
        }
        bool CheckInputs(bool guest, out int port, out string code, out string ip, out string name) {
            port = 0; code = ""; ip = ""; name = "";
            string dir = gameDir.Text.Trim();
            string exe = Path.Combine(dir, "Bully.exe");
            string dll = Path.Combine(dir, "dinput8.dll");
            if (!File.Exists(exe) || !File.Exists(dll)) {
                Fail("لازم Bully.exe و dinput8.dll يكونان في مجلد اللعبة المحدد."); return false;
            }
            if (new FileInfo(exe).Length != KnownExeSize) {
                Fail("ملف Bully.exe مختلف عن النسخة التي تحققنا من توافقها. لا نشغّل DLL على نسخة أخرى."); return false;
            }
            if (!IsCompatibleBridge(dll)) {
                Fail("ملف dinput8.dll لازم يكون من بناء v0.16b x86 المجرب؛ لا تستخدم DLL قديمًا."); return false;
            }
            int value;
            if (!int.TryParse(roomPort.Text.Trim(), out value) || value < 1024 || value > 65534) {
                Fail("المنفذ لازم بين 1024 و65534 (لأن اللانشر يستخدم المنفذ +1)."); return false;
            }
            port = value;
            code = roomCode.Text.Trim();
            if (code.Length != 6 || !OnlyDigits(code) || code[0] == '0') {
                Fail("رمز الغرفة يجب يكون 6 أرقام، نفس الرمز على الطرفين."); return false;
            }
            name = nick.Text.Trim().Replace("|", "").Replace("\r", "").Replace("\n", "");
            if (name.Length < 1 || name.Length > 24) {
                Fail("الاسم لازم بين 1 و24 حرف."); return false;
            }
            ip = guest ? hostAddress.Text.Trim() : "";
            IPAddress parsed;
            if (guest && (!IPAddress.TryParse(ip, out parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)) {
                Fail("عنوان الـHost لازم IPv4 صحيح مثل 192.168.1.10."); return false;
            }
            if (guest && samePc.Checked && ip != "127.0.0.1") {
                Fail("إذا اختبار نفس الجهاز فعّلته، عنوان الـHost لازم 127.0.0.1."); return false;
            }
            if (!guest && samePc.Checked) Note("وضع نفس الجهاز: HOST يستقبل على 127.0.0.1 فقط.");
            return true;
        }
        static bool OnlyDigits(string code) {
            foreach (char c in code) if (c < '0' || c > '9') return false;
            return true;
        }
        static bool IsX86Dll(string file) {
            try {
                using (FileStream f = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                    using (BinaryReader b = new BinaryReader(f)) {
                        if (f.Length < 256 || b.ReadUInt16() != 0x5a4d) return false;
                        f.Position = 0x3c; int peOffset = b.ReadInt32();
                        if (peOffset < 0x40 || peOffset > f.Length - 24) return false;
                        f.Position = peOffset;
                        return b.ReadUInt32() == 0x4550 && b.ReadUInt16() == 0x014c;
                    }
                }
            } catch { return false; }
        }
        static bool IsCompatibleBridge(string file) {
            if (!IsX86Dll(file)) return false;
            try {
                // This text is a v0.16b log literal kept in the compiled DLL.
                // A signature check in the game DLL still validates Bully.exe itself.
                return Encoding.ASCII.GetString(File.ReadAllBytes(file)).Contains("BullyCoop v0.16b");
            } catch { return false; }
        }
        void Fail(string msg) { Note("ERROR: " + msg); MessageBox.Show(this, msg, "Bully Co-op Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        void FreezeInputs(bool freeze) {
            gameDir.Enabled = !freeze; nick.Enabled = !freeze;
            hostAddress.Enabled = !freeze; roomPort.Enabled = !freeze; roomCode.Enabled = !freeze;
            browse.Enabled = !freeze; samePc.Enabled = !freeze;
            openHost.Enabled = !freeze; joinHost.Enabled = !freeze; stop.Enabled = freeze;
        }
        void OpenRoom() {
            int port; string code, ip, name;
            if (!CheckInputs(false, out port, out code, out ip, out name)) return;
            try {
                stopping = false; isHost = true; guestReady = false; connected = false; launched = false;
                gameNetworkVerified = false;
                activePort = port; activeCode = code; activeIP = ""; activeName = name;
                listener = new TcpListener(IPAddress.Any, port + 1);
                listener.Start(1);
                FreezeInputs(true); status.Text = "الشبكة: Host جاهز في انتظار ضيف";
                lobbyStatus.Text = "ROOM " + code + " | host " + FindLocalIPv4();
                Note("HOST lobby TCP " + (port + 1) + " listening. Game UDP " + port + ". Code " + code);
                Note("قل لخويك يدخل IP حق الجهاز ورمز الغرفة. من نفس الكمبيوتر يستخدم 127.0.0.1.");
                Task.Run(() => AcceptGuest());
            } catch (Exception ex) {
                Fail("فشل فتح الغرفة: " + ex.Message); StopNetworking(true);
            }
        }
        void AcceptGuest() {
            TcpClient candidate = null;
            try {
                candidate = listener.AcceptTcpClient();
                candidate.NoDelay = true;
                NetworkStream s = candidate.GetStream();
                StreamReader r = new StreamReader(s, Encoding.UTF8);
                StreamWriter w = new StreamWriter(s, new UTF8Encoding(false)); w.AutoFlush = true;
                // Only six-digit room code (not secure authentication) on trusted LAN/VPN.
                string hello = r.ReadLine();
                string[] parts = (hello ?? "").Split('|');
                if (parts.Length != 3 || parts[0] != "HELLO" || parts[1] != activeCode ||
                    parts[2].Length == 0 || parts[2].Length > 24) {
                    w.WriteLine("DENIED"); candidate.Close();
                    Ui(() => Note("رفض محاولة اتصال: رمز أو معلومات الغرفة غير متطابقة."));
                    Task.Run(() => AcceptGuest()); return;
                }
                peerClient = candidate; reader = r; writer = w;
                connected = true;
                Send("WELCOME|" + activeName);
                string guestName = parts[2];
                string guestEndpoint = candidate.Client.RemoteEndPoint.ToString();
                Ui(() => {
                    lobbyStatus.Text = "HOST: " + activeName + "  |  GUEST: " + guestName;
                    status.Text = "الشبكة: ضيف متصل، ننتظر READY";
                    Note("GUEST joined: " + guestName + " from " + guestEndpoint);
                });
                ReadMessages();
            } catch (Exception ex) {
                if (!stopping) Ui(() => Note("غرفة HOST: " + ex.Message));
            } finally {
                connected = false;
                if (candidate != null) try { candidate.Close(); } catch { }
                if (!stopping && !launched) Ui(() => {
                    guestReady = false; start.Enabled = false;
                    status.Text = "الشبكة: الضيف فصل، تقدر تعيد فتح الغرفة";
                    Note("الضيف انفصل؛ اضغط فصل الاتصال ثم أنشئ غرفة جديدة.");
                });
            }
        }
        void JoinRoom() {
            int port; string code, ip, name;
            if (!CheckInputs(true, out port, out code, out ip, out name)) return;
            stopping = false; connected = false; isHost = false; launched = false;
            gameNetworkVerified = false;
            activePort = port; activeCode = code; activeIP = ip; activeName = name;
            FreezeInputs(true); status.Text = "الشبكة: نتصل بالـHost...";
            Task.Run(() => ConnectGuest(ip, port, code, name));
        }
        void ConnectGuest(string ip, int port, string code, string name) {
            TcpClient c = new TcpClient(AddressFamily.InterNetwork);
            try {
                IAsyncResult ar = c.BeginConnect(IPAddress.Parse(ip), port + 1, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(5500)) throw new IOException("Timeout (TCP lobby)");
                c.EndConnect(ar); c.NoDelay = true;
                peerClient = c;
                NetworkStream s = c.GetStream();
                reader = new StreamReader(s, Encoding.UTF8);
                writer = new StreamWriter(s, new UTF8Encoding(false)); writer.AutoFlush = true;
                Send("HELLO|" + code + "|" + name);
                string response = reader.ReadLine();
                if (response == "DENIED") throw new IOException("Host rejected code or nickname");
                if (response == null || !response.StartsWith("WELCOME|")) throw new IOException("Invalid lobby reply");
                string hostName = response.Substring("WELCOME|".Length);
                connected = true;
                Ui(() => {
                    lobbyStatus.Text = "HOST: " + hostName + "  |  GUEST: " + activeName;
                    status.Text = "الشبكة: اتصال اللوبي ناجح! اضغط READY";
                    ready.Enabled = true;
                    Note("LOBBY CONNECTED to " + ip + ":" + (port + 1));
                });
                ReadMessages();
            } catch (Exception ex) {
                if (!stopping) Ui(() => {
                    Note("JOIN FAILED: " + ex.Message);
                    status.Text = "الشبكة: فشل اتصال اللوبي؛ تأكد من IP والمنفذ وFirewall";
                    FreezeInputs(false); ready.Enabled = false;
                });
            } finally {
                connected = false;
                try { c.Close(); } catch { }
                if (!stopping && !launched) Ui(() => {
                    ready.Enabled = false;
                    status.Text = "الشبكة: انفصل اللوبي";
                    FreezeInputs(false);
                });
            }
        }
        void ReadMessages() {
            while (!stopping && connected) {
                string line = reader.ReadLine();
                if (line == null) break;
                if (line.StartsWith("PING|")) { Send("PONG|" + line.Substring(5)); continue; }
                if (line.StartsWith("PONG|")) {
                    long ticks;
                    if (long.TryParse(line.Substring(5), out ticks)) {
                        long age = (DateTime.UtcNow.Ticks - ticks) / TimeSpan.TicksPerMillisecond;
                        if (age >= 0 && age < 120000) Ui(() => pingStatus.Text = "LOBBY PING: " + age + "ms");
                    }
                    continue;
                }
                if (line.StartsWith("READY|") && isHost) {
                    bool value = line == "READY|1";
                    Ui(() => {
                        guestReady = value;
                        start.Enabled = value && !launched;
                        status.Text = value ? "الضيف جاهز! اضغط START GAME" : "الضيف ليس جاهزًا";
                        Note("GUEST READY = " + (value ? "YES" : "NO"));
                    });
                    continue;
                }
                if (line == "START" && !isHost) {
                    Ui(() => {
                        Note("HOST requested START. Launching your own local copy...");
                        LaunchGame(false);
                    });
                }
            }
        }
        void ReadyToggle() {
            if (isHost || !connected || launched) return;
            guestReady = !guestReady;
            ready.BackColor = guestReady ? Color.FromArgb(41, 122, 99) : Surface;
            ready.Text = guestReady ? "READY ✓" : "جاهز READY";
            Send(guestReady ? "READY|1" : "READY|0");
            Note("Ready = " + guestReady);
        }
        void StartBothGames() {
            if (!isHost || !connected || !guestReady || launched) return;
            Send("START");
            Note("START SENT to guest; launching local HOST.");
            LaunchGame(true);
        }
        void Send(string line) {
            try { lock (sendLock) { if (writer != null) writer.WriteLine(line); } }
            catch (Exception ex) { Ui(() => Note("LOBBY SEND: " + ex.Message)); }
        }
        static string Config(bool host, bool local, string ip, int port, string code) {
            // All settings are tested v0.16b defaults; no teleport, no game background walking.
            StringBuilder b = new StringBuilder();
            b.Append("[Network]\r\nEnabled=1\r\nRole=").Append(host ? "host" : "guest").Append("\r\n");
            if (!host) b.Append("HostAddress=").Append(ip).Append("\r\n");
            b.Append("Port=").Append(port).Append("\r\nSessionCode=").Append(code).Append("\r\n\r\n");
            b.Append("[Experimental]\r\nLocalLoopback=").Append(host && local ? "1" : "0").Append("\r\n");
            b.Append("EnableNPCSpawnProbe=").Append(host ? "1" : "0").Append("\r\n");
            b.Append("EnableRemoteMovementProbe=").Append(host ? "1" : "0").Append("\r\n");
            b.Append("EnableNPCWalkProbe=").Append(host ? "1" : "0").Append("\r\n");
            b.Append("AllowBackgroundHostWalk=0\r\nWalkTaskIntervalMs=1300\r\nWalkLeadCm=0\r\n");
            return b.ToString();
        }
        void LaunchGame(bool host) {
            if (launched) return;
            string dir = gameDir.Text.Trim();
            try {
                // Check again in case files changed while waiting in the lobby.
                if (new FileInfo(Path.Combine(dir, "Bully.exe")).Length != KnownExeSize ||
                    !IsCompatibleBridge(Path.Combine(dir, "dinput8.dll"))) throw new IOException("Invalid EXE/DLL at launch");
                string ini = Path.Combine(dir, "BullyCoop.ini");
                string backup = Path.Combine(dir, "BullyCoop.ini.before_launcher.bak");
                if (File.Exists(ini) && !File.Exists(backup)) File.Copy(ini, backup);
                File.WriteAllText(ini, Config(host, host && samePc.Checked, activeIP, activePort, activeCode), Encoding.ASCII);
                // A new log distinguishes real game handshake from stale logs in prior runs.
                string log = Path.Combine(dir, "BullyCoop_bridge.log");
                gameLogStartLength = 0;
                if (File.Exists(log)) {
                    string archived = Path.Combine(dir, "BullyCoop_bridge.before_launcher_" + DateTime.Now.ToString("yyyyMMdd_HHmmssfff") + ".log");
                    try { File.Move(log, archived); }
                    catch {
                        gameLogStartLength = new FileInfo(log).Length;
                        Note("Previous log locked; will read only newly appended log bytes.");
                    }
                }
                Process p = new Process();
                p.StartInfo.FileName = Path.Combine(dir, "Bully.exe");
                p.StartInfo.WorkingDirectory = dir;
                p.StartInfo.UseShellExecute = true;
                if (!p.Start()) throw new IOException("Bully.exe could not start");
                launched = true;
                start.Enabled = false; ready.Enabled = false;
                status.Text = "بدأت اللعبة. ننتظر PEER VERIFIED من DLL UDP...";
                Note("GAME LAUNCHED as " + (host ? "HOST" : "GUEST") + "; PID=" + p.Id);
                Note("لو ما ظهر PEER VERIFIED في لوق اللعبة: تأكد من UDP " + activePort + " في Windows Firewall.");
                SavePreferences();
            } catch (Exception ex) {
                Fail("تعذر تشغيل اللعبة: " + ex.Message);
                status.Text = "لم تبدأ اللعبة؛ تأكد من الملفات وأعد المحاولة";
            }
        }
        void OnTick() {
            if (connected && !stopping) {
                lastPingTicks = DateTime.UtcNow.Ticks;
                Send("PING|" + lastPingTicks);
            }
            if (launched && !gameNetworkVerified) {
                try {
                    string log = Path.Combine(gameDir.Text.Trim(), "BullyCoop_bridge.log");
                    if (File.Exists(log)) {
                        byte[] tail;
                        using (FileStream f = File.Open(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                            long from = Math.Max(gameLogStartLength, f.Length - 32768);
                            if (from > f.Length) return;
                            long length = f.Length - from;
                            f.Seek(from, SeekOrigin.Begin);
                            tail = new byte[(int)length];
                            int read = 0; int count;
                            while (read < tail.Length && (count = f.Read(tail, read, tail.Length - read)) > 0) read += count;
                        }
                        string content = Encoding.ASCII.GetString(tail);
                        string expected = "PEER VERIFIED role=" + (isHost ? "guest" : "host");
                        if (content.Contains(expected)) {
                            gameNetworkVerified = true;
                            status.Text = "UDP GAME CONNECTED ✓ | " + expected;
                            Note("SUCCESS: game DLL verified REAL position connection (not only launcher lobby).");
                        }
                    }
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        void StopNetworking(bool user) {
            stopping = true;
            connected = false;
            try { if (listener != null) listener.Stop(); } catch { }
            try { if (peerClient != null) peerClient.Close(); } catch { }
            listener = null; peerClient = null; reader = null; writer = null;
            guestReady = false;
            if (user) {
                ready.Enabled = false; start.Enabled = false;
                ready.BackColor = Surface; ready.Text = "جاهز READY";
                FreezeInputs(false); status.Text = "الشبكة: تم فصل اللوبي (اللعبة لا تُغلق)";
                lobbyStatus.Text = "LOBBY: خارج الغرفة";
                Note("Lobby closed. If already started, the game process is unaffected.");
                launched = false; gameNetworkVerified = false;
            }
        }
        void LoadPreferences() {
            try {
                if (!File.Exists(settingsFile)) return;
                foreach (string line in File.ReadAllLines(settingsFile)) {
                    int i = line.IndexOf('='); if (i < 1) continue;
                    string key = line.Substring(0, i), val = line.Substring(i + 1);
                    if (key == "GameDirectory") gameDir.Text = val;
                    else if (key == "Nickname") nick.Text = val;
                    else if (key == "HostAddress") hostAddress.Text = val;
                    else if (key == "Port") roomPort.Text = val;
                    else if (key == "SamePC") samePc.Checked = val == "1";
                }
            } catch { Note("Launcher preferences could not be read."); }
        }
        void SavePreferences() {
            try {
                File.WriteAllLines(settingsFile, new string[] {
                    "GameDirectory=" + gameDir.Text, "Nickname=" + nick.Text,
                    "HostAddress=" + hostAddress.Text, "Port=" + roomPort.Text,
                    "SamePC=" + (samePc.Checked ? "1" : "0")
                }, Encoding.UTF8);
            } catch { /* settings are optional; no elevation needed */ }
        }
        [STAThread]
        public static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Launcher());
        }
    }
}
