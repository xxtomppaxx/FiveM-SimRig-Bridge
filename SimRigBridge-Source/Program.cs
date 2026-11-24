using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using SharpDX.DirectInput;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace SimRigBridge
{
    public class AxisBind { public Guid Dev { get; set; } public int Axis { get; set; } public bool Inv { get; set; } public int Min { get; set; } = 0; public int Max { get; set; } = 100; }
    public class BtnBind { public Guid Dev { get; set; } public int Btn { get; set; } public string Cmd { get; set; } = ""; }

    public class RigConfig
    {
        public Guid FfbDev { get; set; }
        public int Gain { get; set; } = 35;
        public int Center { get; set; } = 45;
        public int Road { get; set; } = 25;
        public bool InvFfb { get; set; } = false;
        public bool UdpOn { get; set; } = true;
        public int UdpPort { get; set; } = 20777;
        public AxisBind? Steer { get; set; }
        public AxisBind? Throt { get; set; }
        public AxisBind? Brake { get; set; }
        public AxisBind? Clutch { get; set; }
        public AxisBind? Hb { get; set; }
        public List<BtnBind> Btns { get; set; } = new();
    }

    public class TelemetryData
    {
        public bool active { get; set; }
        public float speed { get; set; }
        public float lateral { get; set; }
        public float steer { get; set; }
        public float rpm { get; set; }
        public int gear { get; set; }
        public float skid { get; set; }
        public bool inAir { get; set; }
        public float bump { get; set; }
    }

    public class MainForm : Form
    {
        private const int N5 = 5, N4096 = 4096, I0 = 0, I1 = 1, I2 = 2, I3 = 3, I4 = 4;
        private readonly string cfgPath = Path.Combine(AppContext.BaseDirectory, "rig_config.json");
        private RigConfig cfg = new();
        private DirectInput di = new();
        private Dictionary<Guid, Joystick> sticks = new();
        private List<DeviceInstance> devList = new();
        private Joystick? ffbWheel;
        private Effect? constEffect;

        private ComboBox wheelBox = new();
        private Label statusLbl = new(), bindStatusLbl = new();
        private TrackBar gainSld = new(), centerSld = new(), roadSld = new();
        private CheckBox invFfbChk = new(), udpChk = new();
        private ListBox btnList = new();
        private TextBox cmdBox = new();
        private Panel[] axisBars = new Panel[N5];
        private int[] axisVals = new int[N5];
        private long lastUiTick = 0;
        private TrackBar[] minSlds = new TrackBar[N5], maxSlds = new TrackBar[N5];
        private Label[] minLbls = new Label[N5], maxLbls = new Label[N5];

        private string listenTarget = "";
        private Dictionary<Guid, int[]> baseAxes = new();
        private string[] wizQueue = Array.Empty<string>();
        private int wizStep = 0;
        private Button skipBtn = new();
        private bool wasInGear = false;
        private bool[] prevBtns = new bool[N4096];
        private List<string> pendingCmds = new();
        private volatile bool running = true;
        public MainForm()
        {
            Text = "TompanServeri SimRig Bridge v1.3 (FFB + Pedals + Shifters + Boxes + VAM/HF8)";
            Size = new Size(640, 560);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(24, 24, 28);
            ForeColor = Color.White;

            LoadCfg();
            LoadDevices();
            BuildUI();
            if (cfg.FfbDev != Guid.Empty) ConnectFFB(cfg.FfbDev);
            StartLoops();
            FormClosing += (s, e) => { running = false; constEffect?.Stop(); };
        }

        private void LoadCfg()
        {
            try { if (File.Exists(cfgPath)) cfg = JsonSerializer.Deserialize<RigConfig>(File.ReadAllText(cfgPath)) ?? new(); }
            catch { cfg = new(); }
        }

        private void SaveCfg()
        {
            try { File.WriteAllText(cfgPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }

        private void LoadDevices()
        {
            foreach (var j in sticks.Values) { try { j.Unacquire(); j.Dispose(); } catch { } }
            sticks.Clear();
            devList.Clear();
            foreach (var d in di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
            {
                try
                {
                    var j = new Joystick(di, d.InstanceGuid);
                    j.Properties.BufferSize = 128;
                    j.Acquire();
                    sticks[d.InstanceGuid] = j;
                    devList.Add(d);
                }
                catch { }
            }
            if (wheelBox.IsHandleCreated)
            {
                wheelBox.Items.Clear();
                foreach (var d in devList) wheelBox.Items.Add(d.InstanceName);
                int idx = devList.FindIndex(x => x.InstanceGuid == cfg.FfbDev);
                if (idx >= 0) wheelBox.SelectedIndex = idx;
                else if (wheelBox.Items.Count > 0) wheelBox.SelectedIndex = 0;
            }
        }
        private void BuildUI()
        {
            var tabs = new TabControl { Dock = DockStyle.Top, Height = 485 };
            var t1 = new TabPage("1. Ratti & FFB") { BackColor = BackColor };
            var t2 = new TabPage("2. Polkimet & Käsijarru") { BackColor = BackColor };
            var t3 = new TabPage("3. Vaihteet, Boxit & VAM/HF8") { BackColor = BackColor };

            var l1 = new Label { Text = "Valitse FFB-ratti:", Location = new Point(15, 15), AutoSize = true };
            wheelBox = new ComboBox { Location = new Point(15, 38), Width = 235, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var d in devList) wheelBox.Items.Add(d.InstanceName);
            int idx = devList.FindIndex(x => x.InstanceGuid == cfg.FfbDev);
            if (idx >= 0) wheelBox.SelectedIndex = idx;
            else if (wheelBox.Items.Count > 0) wheelBox.SelectedIndex = 0;

            var connBtn = new Button { Text = "Yhdistä FFB", Location = new Point(258, 37), Width = 95, Height = 27, BackColor = Color.FromArgb(0, 120, 215), FlatStyle = FlatStyle.Flat };
            connBtn.Click += (s, e) => { if (wheelBox.SelectedIndex >= 0) ConnectFFB(devList[wheelBox.SelectedIndex].InstanceGuid); };

            var testBtn = new Button { Text = "Testaa FFB", Location = new Point(360, 37), Width = 95, Height = 27, BackColor = Color.FromArgb(60, 60, 70), FlatStyle = FlatStyle.Flat };
            testBtn.Click += (s, e) => TestFFB();

            var refBtn = new Button { Text = "Päivitä laitteet", Location = new Point(462, 37), Width = 135, Height = 27, BackColor = Color.FromArgb(55, 85, 65), FlatStyle = FlatStyle.Flat };
            refBtn.Click += (s, e) => { LoadDevices(); statusLbl.Text = "USB-laitteet päivitetty (" + sticks.Count + " kpl)"; };

            gainSld = AddSlider(t1, "Kokonaisvoima (Gain %):", 85, cfg.Gain, v => { cfg.Gain = v; SaveCfg(); });
            centerSld = AddSlider(t1, "Keskitys / Drift-palautus (%):", 155, cfg.Center, v => { cfg.Center = v; SaveCfg(); });
            roadSld = AddSlider(t1, "Tienpinta / Töyssyt (%):", 225, cfg.Road, v => { cfg.Road = v; SaveCfg(); });

            invFfbChk = new CheckBox { Text = "Käännä FFB-suunta (Invert Force)", Location = new Point(15, 300), Checked = cfg.InvFfb, AutoSize = true };
            invFfbChk.CheckedChanged += (s, e) => { cfg.InvFfb = invFfbChk.Checked; SaveCfg(); };
            t1.Controls.AddRange(new Control[] { l1, wheelBox, connBtn, testBtn, refBtn, invFfbChk });

            bindStatusLbl = new Label { Text = "Klikkaa nappia ja liikuta haluttua poljinta / rattia / käsijarrua:", Location = new Point(15, 15), AutoSize = true, ForeColor = Color.SkyBlue };
            t2.Controls.Add(bindStatusLbl);

            string[] names = { "Ohjaus (Steer)", "Kaasu (Throttle)", "Jarru (Brake)", "Kytkin (Clutch)", "Käsijarru (Handbrake)" };
            string[] keys = { "steer", "throt", "brake", "clutch", "hb" };
            for (int i = 0; i < names.Length; i++)
            {
                int i2 = i;
                string k = keys[i];
                int y = 45 + i * 82;
                var b = new Button { Text = $"Tunnista: {names[i]}", Location = new Point(15, y), Width = 190, Height = 28, BackColor = Color.FromArgb(55, 55, 65), FlatStyle = FlatStyle.Flat };
                b.Click += (s, e) => StartAxisListen(k);
                var invB = new Button { Text = "Käännä", Location = new Point(212, y), Width = 75, Height = 28, BackColor = Color.FromArgb(45, 45, 50), FlatStyle = FlatStyle.Flat };
                invB.Click += (s, e) => ToggleInvert(k);

                int barIdx = i2;
                axisBars[i2] = new Panel { Location = new Point(295, y + 2), Width = 310, Height = 24, BackColor = Color.FromArgb(40, 40, 48) };
                axisBars[i2].Paint += (s, pe) => { int w = (int)(310 * (axisVals[barIdx] / 100f)); pe.Graphics.FillRectangle(Brushes.DeepSkyBlue, 0, 0, w, 24); };

                var ab = GetBind(k);
                minLbls[i2] = new Label { Text = $"Min: {ab?.Min ?? 0}%", Location = new Point(15, y + 34), Width = 75, AutoSize = false };
                minSlds[i2] = new TrackBar { Location = new Point(90, y + 30), Width = 195, Maximum = 95, TickStyle = TickStyle.None, Value = ab?.Min ?? 0 };
                minSlds[i2].ValueChanged += (s, e) => { var x = GetBind(k); if (x != null) { x.Min = minSlds[i2].Value; minLbls[i2].Text = $"Min: {x.Min}%"; SaveCfg(); } };

                maxLbls[i2] = new Label { Text = $"Max: {ab?.Max ?? 100}%", Location = new Point(295, y + 34), Width = 80, AutoSize = false };
                maxSlds[i2] = new TrackBar { Location = new Point(375, y + 30), Width = 230, Minimum = 5, Maximum = 100, TickStyle = TickStyle.None, Value = ab?.Max ?? 100 };
                maxSlds[i2].ValueChanged += (s, e) => { var x = GetBind(k); if (x != null) { x.Max = maxSlds[i2].Value; maxLbls[i2].Text = $"Max: {x.Max}%"; SaveCfg(); } };

                t2.Controls.AddRange(new Control[] { b, invB, axisBars[i2], minLbls[i2], minSlds[i2], maxLbls[i2], maxSlds[i2] });
            }

            udpChk = new CheckBox { Text = "Haptiikka UDP (SimHub / Simsonn VAM / NLR HF8 - portti 20777)", Location = new Point(15, 15), Checked = cfg.UdpOn, AutoSize = true, ForeColor = Color.LightGreen };
            udpChk.CheckedChanged += (s, e) => { cfg.UdpOn = udpChk.Checked; SaveCfg(); };

            var cmdLbl = new Label { Text = "Komento (esim. suomiradio_next, sk_flip, gear_up, gear_down):", Location = new Point(15, 50), AutoSize = true };
            cmdBox = new TextBox { Location = new Point(15, 72), Width = 260, Text = "suomiradio_next" };
            var addBtn = new Button { Text = "Paina nappia / Vaihdetta", Location = new Point(285, 70), Width = 190, Height = 26, BackColor = Color.FromArgb(0, 120, 215), FlatStyle = FlatStyle.Flat };
            addBtn.Click += (s, e) => { ClearPrevBtns(); listenTarget = "btn"; statusLbl.Text = "Paina nyt haluttua nappia Boxissa tai vaihdekepissä..."; };

            btnList = new ListBox { Location = new Point(15, 110), Width = 580, Height = 190 };
            RefreshBtnList();

            var delBtn = new Button { Text = "Poista valittu", Location = new Point(15, 315), Width = 115, Height = 28, BackColor = Color.DarkRed, FlatStyle = FlatStyle.Flat };
            delBtn.Click += (s, e) => { if (btnList.SelectedIndex >= 0) { cfg.Btns.RemoveAt(btnList.SelectedIndex); SaveCfg(); RefreshBtnList(); } };

            var hWizBtn = new Button { Text = "Velho: H-kaavio (R, 1–6)", Location = new Point(140, 315), Width = 165, Height = 28, BackColor = Color.FromArgb(0, 130, 90), FlatStyle = FlatStyle.Flat };
            hWizBtn.Click += (s, e) => StartWizard(new[] { "gear_r", "gear_1", "gear_2", "gear_3", "gear_4", "gear_5", "gear_6" });

            var sqWizBtn = new Button { Text = "Velho: SQ (Ylös/Alas)", Location = new Point(315, 315), Width = 145, Height = 28, BackColor = Color.FromArgb(0, 120, 180), FlatStyle = FlatStyle.Flat };
            sqWizBtn.Click += (s, e) => StartWizard(new[] { "gear_up", "gear_down" });

            skipBtn = new Button { Text = "Ohita", Location = new Point(470, 315), Width = 70, Height = 28, BackColor = Color.FromArgb(80, 80, 90), FlatStyle = FlatStyle.Flat, Visible = false };
            skipBtn.Click += (s, e) => NextWizardStep();

            t3.Controls.AddRange(new Control[] { udpChk, cmdLbl, cmdBox, addBtn, btnList, delBtn, hWizBtn, sqWizBtn, skipBtn });
            tabs.TabPages.AddRange(new[] { t1, t2, t3 });

            statusLbl = new Label { Text = "Valmis. Kytkettyjä USB-ohjaimia: " + sticks.Count, Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(15, 0, 0, 0), ForeColor = Color.LightGreen };
            Controls.AddRange(new Control[] { tabs, statusLbl });
        }
        private TrackBar AddSlider(TabPage p, string title, int y, int val, Action<int> onChange)
        {
            var l = new Label { Text = $"{title} {val}%", Location = new Point(15, y), AutoSize = true };
            var tb = new TrackBar { Location = new Point(15, y + 20), Width = 580, Minimum = 0, Maximum = 150, Value = Math.Clamp(val, 0, 150), TickFrequency = 10 };
            tb.ValueChanged += (s, e) => { l.Text = $"{title} {tb.Value}%"; onChange(tb.Value); };
            p.Controls.AddRange(new Control[] { l, tb });
            return tb;
        }

        private void ClearPrevBtns() => Array.Clear(prevBtns, 0, prevBtns.Length);

        private void RefreshBtnList()
        {
            btnList.Items.Clear();
            foreach (var b in cfg.Btns)
            {
                string devName = devList.FirstOrDefault(d => d.InstanceGuid == b.Dev)?.InstanceName ?? "USB";
                string bLabel = b.Btn >= 1000 ? $"POV {(b.Btn - 1000) / 4500}" : $"Nappi {b.Btn + 1}";
                btnList.Items.Add($"[{devName}] {bLabel} -> /{b.Cmd}");
            }
        }

        private void StartAxisListen(string key)
        {
            baseAxes.Clear();
            foreach (var kv in sticks) baseAxes[kv.Key] = GetAxes(kv.Value.GetCurrentState());
            listenTarget = key;
            bindStatusLbl.Text = $"Liikuta nyt: {key.ToUpper()}...";
        }

        private AxisBind? GetBind(string k) => k switch { "steer" => cfg.Steer, "throt" => cfg.Throt, "brake" => cfg.Brake, "clutch" => cfg.Clutch, "hb" => cfg.Hb, _ => null };

        private void StartWizard(string[] steps)
        {
            ClearPrevBtns();
            wizQueue = steps; wizStep = 0; listenTarget = "wiz"; skipBtn.Visible = true;
            statusLbl.Text = $"Velho (1/{wizQueue.Length}): Laita päälle -> {wizQueue.First()}";
        }

        private void NextWizardStep()
        {
            ClearPrevBtns();
            wizStep++;
            if (wizStep >= wizQueue.Length) { listenTarget = ""; skipBtn.Visible = false; statusLbl.Text = "Vaihteiden tunnistus valmis!"; }
            else statusLbl.Text = $"Velho ({wizStep + 1}/{wizQueue.Length}): Laita päälle -> {wizQueue[wizStep]}";
        }

        private void ToggleInvert(string k)
        {
            var ab = GetBind(k);
            if (ab != null) { ab.Inv = !ab.Inv; SaveCfg(); bindStatusLbl.Text = $"{k} suunta käännetty ({ab.Inv})"; }
        }

        private int[] GetAxes(JoystickState s) => new[] { s.X, s.Y, s.Z, s.RotationX, s.RotationY, s.RotationZ, s.Sliders.ElementAtOrDefault(0), s.Sliders.ElementAtOrDefault(1) };

        private float ReadAxis(AxisBind? b, bool centered)
        {
            if (b == null || !sticks.TryGetValue(b.Dev, out var j)) return 0f;
            float norm = GetAxes(j.GetCurrentState())[b.Axis] / 65535f;
            if (b.Inv) norm = 1f - norm;
            if (centered) return (norm * 2f) - 1f;
            float minF = b.Min / 100f, maxF = Math.Max(minF + 0.01f, b.Max / 100f);
            return Math.Clamp((norm - minF) / (maxF - minF), 0f, 1f);
        }

        private void ConnectFFB(Guid guid)
        {
            try
            {
                constEffect?.Stop();
                constEffect?.Dispose();
                ffbWheel?.Unacquire();
                ffbWheel = new Joystick(di, guid);
                ffbWheel.SetCooperativeLevel(Handle, CooperativeLevel.Background | CooperativeLevel.Exclusive);
                ffbWheel.Acquire();

                var effParams = new EffectParameters
                {
                    Duration = int.MaxValue,
                    Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                    Gain = 10000,
                    SamplePeriod = 0,
                    TriggerButton = -1,
                    TriggerRepeatInterval = 0,
                    Axes = new[] { (int)JoystickOffset.X },
                    Directions = new[] { 0 },
                    Parameters = new ConstantForce { Magnitude = 0 }
                };

                try
                {
                    constEffect = new Effect(ffbWheel, EffectGuid.ConstantForce, effParams);
                }
                catch
                {
                    var ffbObjs = ffbWheel.GetObjects(DeviceObjectTypeFlags.ForceFeedbackActuator);
                    effParams.Flags = EffectFlags.Cartesian | EffectFlags.ObjectIds;
                    effParams.Axes = new[] { ffbObjs.Count > 0 ? (int)ffbObjs.First().ObjectId : 2 };
                    constEffect = new Effect(ffbWheel, EffectGuid.ConstantForce, effParams);
                }

                constEffect.Start(1);
                cfg.FfbDev = guid;
                SaveCfg();
                statusLbl.Text = "FFB yhdistetty: " + ffbWheel.Information.InstanceName;
            }
            catch (Exception ex) { statusLbl.Text = "FFB-virhe: " + ex.Message; }
        }

        private void TestFFB()
        {
            if (constEffect == null) return;
            new Thread(() => {
                SetForce(4500); Thread.Sleep(250);
                SetForce(-4500); Thread.Sleep(250);
                SetForce(0);
            }).Start();
        }

        private void SetForce(int mag)
        {
            if (constEffect == null) return;
            try
            {
                int val = Math.Clamp(cfg.InvFfb ? -mag : mag, -10000, 10000);
                constEffect.SetParameters(new EffectParameters { Parameters = new ConstantForce { Magnitude = val } }, EffectParameterFlags.TypeSpecificParameters);
            }
            catch { }
        }
        private void HandleBtnPress(Guid dev, int code)
        {
            if (listenTarget == "wiz")
            {
                string cmd = wizQueue[wizStep];
                Invoke(() => { cfg.Btns.RemoveAll(x => x.Cmd == cmd); cfg.Btns.Add(new BtnBind { Dev = dev, Btn = code, Cmd = cmd }); SaveCfg(); RefreshBtnList(); NextWizardStep(); });
            }
            else if (listenTarget == "btn")
            {
                listenTarget = "";
                Invoke(() => { cfg.Btns.Add(new BtnBind { Dev = dev, Btn = code, Cmd = cmdBox.Text }); SaveCfg(); RefreshBtnList(); statusLbl.Text = $"Sidottu -> /{cmdBox.Text}"; });
            }
            else
            {
                var match = cfg.Btns.FirstOrDefault(x => x.Dev == dev && x.Btn == code);
                if (match != null) lock (pendingCmds) pendingCmds.Add(match.Cmd);
            }
        }

        private void StartLoops()
        {
            new Thread(() => {
                while (running)
                {
                    try
                    {
                        foreach (var kv in sticks)
                        {
                            kv.Value.Poll();
                            var st = kv.Value.GetCurrentState();
                            var cur = GetAxes(st);
                            if (listenTarget != "" && listenTarget != "btn" && listenTarget != "wiz" && baseAxes.TryGetValue(kv.Key, out var bAx))
                            {
                                for (int i = 0; i < cur.Length; i++)
                                    if (Math.Abs(cur[i] - bAx[i]) > 18000)
                                    {
                                        var bind = new AxisBind { Dev = kv.Key, Axis = i };
                                        if (listenTarget == "steer") cfg.Steer = bind;
                                        else if (listenTarget == "throt") cfg.Throt = bind;
                                        else if (listenTarget == "brake") cfg.Brake = bind;
                                        else if (listenTarget == "clutch") cfg.Clutch = bind;
                                        else if (listenTarget == "hb") cfg.Hb = bind;
                                        listenTarget = ""; SaveCfg();
                                        Invoke(() => bindStatusLbl.Text = $"Tallennettu! (Akseli {i})");
                                        break;
                                    }
                            }
                            for (int b = 0; b < st.Buttons.Length; b++)
                            {
                                int idx = Math.Abs((kv.Key.GetHashCode() * 31 + b) % 4000);
                                if (st.Buttons[b] && !prevBtns[idx]) HandleBtnPress(kv.Key, b);
                                prevBtns[idx] = st.Buttons[b];
                            }
                            int pov = st.PointOfViewControllers.ElementAtOrDefault(0);
                            int povIdx = Math.Abs((kv.Key.GetHashCode() * 31 + 999) % 4000);
                            bool povDown = pov >= 0;
                            if (povDown && !prevBtns[povIdx]) HandleBtnPress(kv.Key, 1000 + pov);
                            prevBtns[povIdx] = povDown;
                        }

                        var hGears = cfg.Btns.Where(x => x.Cmd == "gear_r" || (x.Cmd.StartsWith("gear_") && x.Cmd.Length == 6 && char.IsDigit(x.Cmd.Last()))).ToList();
                        if (hGears.Count > 0)
                        {
                            bool anyHeld = hGears.Any(g => sticks.TryGetValue(g.Dev, out var j) && ((g.Btn < 1000 && g.Btn < j.GetCurrentState().Buttons.Length && j.GetCurrentState().Buttons[g.Btn]) || (g.Btn >= 1000 && j.GetCurrentState().PointOfViewControllers.ElementAtOrDefault(0) == (g.Btn - 1000))));
                            if (anyHeld) wasInGear = true;
                            else if (wasInGear) { wasInGear = false; lock (pendingCmds) pendingCmds.Add("gear_n"); }
                        }

                        long now = Environment.TickCount64;
                        if (IsHandleCreated && Visible && now - lastUiTick >= 16)
                        {
                            lastUiTick = now;
                            axisVals[I0] = Math.Clamp((int)((ReadAxis(cfg.Steer, true) + 1f) * 50f), 0, 100);
                            axisVals[I1] = Math.Clamp((int)(ReadAxis(cfg.Throt, false) * 100f), 0, 100);
                            axisVals[I2] = Math.Clamp((int)(ReadAxis(cfg.Brake, false) * 100f), 0, 100);
                            axisVals[I3] = Math.Clamp((int)(ReadAxis(cfg.Clutch, false) * 100f), 0, 100);
                            axisVals[I4] = Math.Clamp((int)(ReadAxis(cfg.Hb, false) * 100f), 0, 100);
                            BeginInvoke(() => { for (int k = 0; k < N5; k++) axisBars[k]?.Invalidate(); });
                        }
                    }
                    catch { }
                    Thread.Sleep(1);
                }
            }) { IsBackground = true }.Start();

            new Thread(() => {
                try
                {
                     var http = new HttpListener();
                    http.Prefixes.Add("http://127.0.0");
                    http.Start();
                    using var udp = new UdpClient();
                    while (running)
                    {
                        var ctx = http.GetContext();
                        ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                        ctx.Response.AddHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                        ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                        ctx.Response.AddHeader("Access-Control-Allow-Private-Network", "true");
                        if (ctx.Request.HttpMethod == "OPTIONS") { ctx.Response.StatusCode = 200; ctx.Response.Close(); continue; }

                        if (ctx.Request.IsWebSocketRequest)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var wsCtx = await ctx.AcceptWebSocketAsync(null);
                                    var ws = wsCtx.WebSocket;
                                    var buf = new byte[N4096];
                                    while (ws.State == WebSocketState.Open && running)
                                    {
                                        var res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                                        if (res.MessageType == WebSocketMessageType.Close) break;
                                        string body = Encoding.UTF8.GetString(buf, 0, res.Count);
                                        var d = string.IsNullOrWhiteSpace(body) ? new TelemetryData() : (JsonSerializer.Deserialize<TelemetryData>(body) ?? new());

                                        if (d.active && !d.inAir)
                                        {
                                            float g = cfg.Gain / 100f, cf = (cfg.Center / 100f) * 3500f, rf = (cfg.Road / 100f) * 2000f;
                                            float sf = Math.Clamp(d.speed / 25f, 0.1f, 1.5f);
                                            int mag = (int)(((-d.steer * sf * cf) + (d.lateral * cf * 0.45f) + (d.bump * rf)) * g);
                                            SetForce(mag);
                                        }
                                        else SetForce(0);

                                        if (cfg.UdpOn && d.active)
                                        {
                                            byte[] pkt = Encoding.UTF8.GetBytes(body);
                                            udp.Send(pkt, pkt.Length, "127.0.0.1", cfg.UdpPort);
                                        }

                                        string[] cmds;
                                        lock (pendingCmds) { cmds = pendingCmds.ToArray(); pendingCmds.Clear(); }

                                        var respObj = new { active = true, steer = ReadAxis(cfg.Steer, true), throt = ReadAxis(cfg.Throt, false), brake = ReadAxis(cfg.Brake, false), clutch = ReadAxis(cfg.Clutch, false), hb = ReadAxis(cfg.Hb, false), cmds };
                                        byte[] outBuf = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(respObj));
                                        await ws.SendAsync(new ArraySegment<byte>(outBuf), WebSocketMessageType.Text, true, CancellationToken.None);
                                    }
                                }
                                catch { }
                            });
                            continue;
                        }

                        ctx.Response.StatusCode = 200;
                        ctx.Response.Close();
                    }

                        }
                        else SetForce(0);

                        if (cfg.UdpOn && d.active)
                        {
                            byte[] pkt = Encoding.UTF8.GetBytes(body);
                            udp.Send(pkt, pkt.Length, "127.0.0.1", cfg.UdpPort);
                        }

                        string[] cmds;
                        lock (pendingCmds) { cmds = pendingCmds.ToArray(); pendingCmds.Clear(); }

                        var respObj = new { active = true, steer = ReadAxis(cfg.Steer, true), throt = ReadAxis(cfg.Throt, false), brake = ReadAxis(cfg.Brake, false), clutch = ReadAxis(cfg.Clutch, false), hb = ReadAxis(cfg.Hb, false), cmds };
                        byte[] outBuf = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(respObj));
                        ctx.Response.ContentType = "application/json";
                        ctx.Response.OutputStream.Write(outBuf, 0, outBuf.Length);
                        ctx.Response.Close();
                    }
                }
                catch (Exception ex)
                {
                    if (IsHandleCreated) BeginInvoke(() => statusLbl.Text = "HTTP-virhe: " + ex.Message);
                }
            }) { IsBackground = true }.Start();
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
