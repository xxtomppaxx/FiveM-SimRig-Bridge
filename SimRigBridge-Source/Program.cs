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

namespace SimRigBridge
{
    public class AxisBind { public Guid Dev { get; set; } public int Axis { get; set; } public bool Inv { get; set; } }
    public class BtnBind { public Guid Dev { get; set; } public int Btn { get; set; } public string Cmd { get; set; } }

    public class RigConfig
    {
        public Guid FfbDev { get; set; }
        public int Gain { get; set; } = 35;
        public int Center { get; set; } = 60;
        public int Road { get; set; } = 20;
        public bool InvFfb { get; set; }
        public bool UdpOn { get; set; } = true;
        public int UdpPort { get; set; } = 20777;
        public AxisBind Steer { get; set; }
        public AxisBind Throt { get; set; }
        public AxisBind Brake { get; set; }
        public AxisBind Clutch { get; set; }
        public AxisBind Hb { get; set; }
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
        private DirectInput di = new();
        private Dictionary<Guid, Joystick> sticks = new();
        private RigConfig cfg = new();
        private string cfgPath = "rig_config.json";
        private Joystick ffbWheel;
        private Effect ffbEffect;
        private HttpListener http;
        private UdpClient udp = new();
        private bool running = true;
        private string listenTarget = "";
        private Dictionary<Guid, int[]> baseAxes = new();
        private bool[] prevBtns = new bool[512];
        private List<string> pendingCmds = new();

        private ComboBox ffbCombo;
        private CheckBox invFfbChk, udpChk;
        private TrackBar gainSld, centSld, roadSld;
        private Label statusLbl, bindStatusLbl;
        private ListBox btnList;
        private TextBox cmdBox;

        public MainForm()
        {
            Text = "TompanServeri SimRig Bridge (FFB + Pedals + Shifters + Boxes + VAM/HF8)";
            Size = new Size(540, 460);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(28, 28, 32);
            ForeColor = Color.White;

            if (File.Exists(cfgPath))
                try { cfg = JsonSerializer.Deserialize<RigConfig>(File.ReadAllText(cfgPath)) ?? new(); } catch { }

            OpenAllDevices();
            BuildUI();
            StartLoops();
        }
        private void OpenAllDevices()
        {
            foreach (var s in sticks.Values) try { s.Unacquire(); s.Dispose(); } catch { }
            sticks.Clear();
            foreach (var d in di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
            {
                try
                {
                    var j = new Joystick(di, d.InstanceGuid);
                    j.SetCooperativeLevel(this.Handle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    j.Acquire();
                    sticks[d.InstanceGuid] = j;
                }
                catch { }
            }
        }

        private void SaveCfg() => File.WriteAllText(cfgPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));

        private void BuildUI()
        {
            var tabs = new TabControl { Dock = DockStyle.Top, Height = 385 };
            var t1 = new TabPage("1. Ratti & FFB") { BackColor = BackColor };
            var t2 = new TabPage("2. Polkimet & Käsijarru") { BackColor = BackColor };
            var t3 = new TabPage("3. Vaihteet, Boxit & VAM/HF8") { BackColor = BackColor };

            ffbCombo = new ComboBox { Location = new Point(15, 20), Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var kv in sticks) ffbCombo.Items.Add(new DevItem { Name = kv.Value.Information.InstanceName, Guid = kv.Key });
            if (ffbCombo.Items.Count > 0) ffbCombo.SelectedIndex = 0;

            var connBtn = new Button { Text = "Yhdistä FFB", Location = new Point(385, 19), Width = 115, BackColor = Color.FromArgb(0, 120, 215), FlatStyle = FlatStyle.Flat };
            connBtn.Click += (s, e) => ConnectFFB();

            invFfbChk = new CheckBox { Text = "Käännä FFB-suunta (Invert)", Location = new Point(15, 55), Checked = cfg.InvFfb, AutoSize = true };
            invFfbChk.CheckedChanged += (s, e) => { cfg.InvFfb = invFfbChk.Checked; SaveCfg(); };

            var gLbl = new Label { Text = $"Gain: {cfg.Gain}%", Location = new Point(15, 90), AutoSize = true };
            gainSld = new TrackBar { Location = new Point(15, 110), Width = 480, Maximum = 100, Value = cfg.Gain };
            gainSld.ValueChanged += (s, e) => { cfg.Gain = gainSld.Value; gLbl.Text = $"Gain: {cfg.Gain}%"; SaveCfg(); };

            var cLbl = new Label { Text = $"Keskitys / Drift: {cfg.Center}%", Location = new Point(15, 165), AutoSize = true };
            centSld = new TrackBar { Location = new Point(15, 185), Width = 480, Maximum = 100, Value = cfg.Center };
            centSld.ValueChanged += (s, e) => { cfg.Center = centSld.Value; cLbl.Text = $"Keskitys / Drift: {cfg.Center}%"; SaveCfg(); };

            var rLbl = new Label { Text = $"Tienpinta / Töyssyt: {cfg.Road}%", Location = new Point(15, 240), AutoSize = true };
            roadSld = new TrackBar { Location = new Point(15, 260), Width = 480, Maximum = 100, Value = cfg.Road };
            roadSld.ValueChanged += (s, e) => { cfg.Road = roadSld.Value; rLbl.Text = $"Tienpinta / Töyssyt: {cfg.Road}%"; SaveCfg(); };

            t1.Controls.AddRange(new Control[] { ffbCombo, connBtn, invFfbChk, gLbl, gainSld, cLbl, centSld, rLbl, roadSld });

            bindStatusLbl = new Label { Text = "Klikkaa nappia ja liikuta haluttua poljinta / rattia / käsijarrua:", Location = new Point(15, 15), AutoSize = true, ForeColor = Color.SkyBlue };
            t2.Controls.Add(bindStatusLbl);

            string[] names = { "Ohjaus (Steer)", "Kaasu (Throttle)", "Jarru (Brake)", "Kytkin (Clutch)", "Käsijarru (Handbrake)" };
            string[] keys = { "steer", "throt", "brake", "clutch", "hb" };
            for (int i = 0; i < names.Length; i++)
            {
                string k = keys[i];
                var b = new Button { Text = $"Tunnista: {names[i]}", Location = new Point(15, 50 + i * 45), Width = 220, Height = 32, BackColor = Color.FromArgb(55, 55, 65), FlatStyle = FlatStyle.Flat };
                b.Click += (s, e) => StartAxisListen(k);
                var invB = new Button { Text = "Käännä akseli", Location = new Point(250, 50 + i * 45), Width = 120, Height = 32, BackColor = Color.FromArgb(45, 45, 50), FlatStyle = FlatStyle.Flat };
                invB.Click += (s, e) => ToggleInvert(k);
                t2.Controls.AddRange(new Control[] { b, invB });
            }

            udpChk = new CheckBox { Text = "Haptiikka UDP (SimHub / Simsonn VAM / NLR HF8 - portti 20777)", Location = new Point(15, 15), Checked = cfg.UdpOn, AutoSize = true, ForeColor = Color.LightGreen };
            udpChk.CheckedChanged += (s, e) => { cfg.UdpOn = udpChk.Checked; SaveCfg(); };

            var cmdLbl = new Label { Text = "Komento (esim. suomiradio_next, sk_flip, gear_up, gear_down):", Location = new Point(15, 50), AutoSize = true };
            cmdBox = new TextBox { Location = new Point(15, 72), Width = 260, Text = "suomiradio_next" };
            var addBtn = new Button { Text = "Paina nappia / Vaihdetta", Location = new Point(285, 70), Width = 190, Height = 26, BackColor = Color.FromArgb(0, 120, 215), FlatStyle = FlatStyle.Flat };
            addBtn.Click += (s, e) => { listenTarget = "btn"; statusLbl.Text = "Paina nyt haluttua nappia Boxissa tai vaihdekepissä..."; };

            btnList = new ListBox { Location = new Point(15, 110), Width = 480, Height = 190 };
            RefreshBtnList();

            var delBtn = new Button { Text = "Poista valittu", Location = new Point(15, 308), Width = 120, BackColor = Color.DarkRed, FlatStyle = FlatStyle.Flat };
            delBtn.Click += (s, e) => { if (btnList.SelectedIndex >= 0) { cfg.Btns.RemoveAt(btnList.SelectedIndex); SaveCfg(); RefreshBtnList(); } };

            t3.Controls.AddRange(new Control[] { udpChk, cmdLbl, cmdBox, addBtn, btnList, delBtn });
            tabs.TabPages.AddRange(new[] { t1, t2, t3 });

            statusLbl = new Label { Text = "Valmis. Kytkettyjä USB-ohjaimia: " + sticks.Count, Dock = DockStyle.Bottom, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(15, 0, 0, 0), ForeColor = Color.LightGreen };
            Controls.AddRange(new Control[] { tabs, statusLbl });
        }
        private void RefreshBtnList()
        {
            btnList.Items.Clear();
            foreach (var b in cfg.Btns) btnList.Items.Add($"Nappi {b.Btn + 1} -> /{b.Cmd}");
        }

        private void StartAxisListen(string key)
        {
            baseAxes.Clear();
            foreach (var kv in sticks) baseAxes[kv.Key] = GetAxes(kv.Value.GetCurrentState());
            listenTarget = key;
            bindStatusLbl.Text = $"Liikuta nyt: {key.ToUpper()}...";
        }

        private void ToggleInvert(string k)
        {
            var ab = k switch { "steer" => cfg.Steer, "throt" => cfg.Throt, "brake" => cfg.Brake, "clutch" => cfg.Clutch, "hb" => cfg.Hb, _ => null };
            if (ab != null) { ab.Inv = !ab.Inv; SaveCfg(); bindStatusLbl.Text = $"{k} suunta käännetty ({ab.Inv})"; }
        }

        private int[] GetAxes(JoystickState s) => new[] { s.X, s.Y, s.Z, s.RotationX, s.RotationY, s.RotationZ, s.Sliders[0], s.Sliders[1] };

        private float ReadAxis(AxisBind b, bool centered)
        {
            if (b == null || !sticks.TryGetValue(b.Dev, out var j)) return 0f;
            int raw = GetAxes(j.GetCurrentState())[b.Axis];
            float norm = raw / 65535f;
            if (b.Inv) norm = 1f - norm;
            return centered ? (norm * 2f) - 1f : norm;
        }

        private void ConnectFFB()
        {
            if (ffbCombo.SelectedItem is not DevItem item) return;
            try
            {
                ffbEffect?.Dispose(); ffbWheel?.Unacquire(); ffbWheel?.Dispose();
                ffbWheel = new Joystick(di, item.Guid);
                ffbWheel.SetCooperativeLevel(this.Handle, CooperativeLevel.Background | CooperativeLevel.Exclusive);
                ffbWheel.Acquire();
                if ((ffbWheel.Capabilities.Flags & DeviceFlags.ForceFeedback) == 0) { statusLbl.Text = "Valittu laite ei tue FFB:tä."; return; }
                try { ffbWheel.Properties.AutoCenter = false; } catch { }
                var axis = ffbWheel.GetObjects(DeviceObjectTypeFlags.Axis).FirstOrDefault(o => (o.ObjectId.Flags & DeviceObjectTypeFlags.ForceFeedbackActuator) != 0);
                int id = axis != null ? (int)axis.ObjectId : 0;
                ffbEffect = new Effect(ffbWheel, EffectGuid.ConstantForce, new EffectParameters
                {
                    Flags = EffectFlags.Cartesian | EffectFlags.ObjectIds, Duration = int.MaxValue, Gain = 10000, TriggerButton = -1,
                    Axes = new[] { id }, Directions = new[] { 0 }, Parameters = new ConstantForce { Magnitude = 0 }
                });
                ffbEffect.Start(1, EffectPlayFlags.None);
                statusLbl.Text = $"FFB yhdistetty: {item.Name}";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void StartLoops()
        {
            new Thread(() =>
            {
                while (running)
                {
                    try
                    {
                        foreach (var kv in sticks)
                        {
                            var st = kv.Value.GetCurrentState();
                            var ax = GetAxes(st);
                            if (listenTarget != "" && listenTarget != "btn" && baseAxes.TryGetValue(kv.Key, out var bAx))
                            {
                                for (int i = 0; i < ax.Length; i++)
                                    if (Math.Abs(ax[i] - bAx[i]) > 15000)
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
                                int idx = Math.Abs((kv.Key.GetHashCode() + b) % 512);
                                if (st.Buttons[b] && !prevBtns[idx])
                                {
                                    if (listenTarget == "btn")
                                    {
                                        listenTarget = "";
                                        Invoke(() => { cfg.Btns.Add(new BtnBind { Dev = kv.Key, Btn = b, Cmd = cmdBox.Text }); SaveCfg(); RefreshBtnList(); statusLbl.Text = $"Nappi {b + 1} sidottu!"; });
                                    }
                                    else
                                    {
                                        var match = cfg.Btns.FirstOrDefault(x => x.Dev == kv.Key && x.Btn == b);
                                        if (match != null) lock (pendingCmds) pendingCmds.Add(match.Cmd);
                                    }
                                }
                                prevBtns[idx] = st.Buttons[b];
                            }
                        }
                    }
                    catch { }
                    Thread.Sleep(10);
                }
            }) { IsBackground = true }.Start();

            http = new HttpListener();
            http.Prefixes.Add("http://127.0.0");
            http.Start();
            new Thread(() =>
            {
                while (running)
                {
                    try
                    {
                        var ctx = http.GetContext();
                        ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                        using var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                        var d = JsonSerializer.Deserialize<TelemetryData>(r.ReadToEnd());

                        if (d != null && d.active && !d.inAir && ffbEffect != null)
                        {
                            float g = cfg.Gain / 100f, cf = (cfg.Center / 100f) * 3500f, rf = (cfg.Road / 100f) * 2000f;
                            float sf = Math.Clamp(d.speed / 25f, 0.1f, 1.5f);
                            int mag = (int)(((-d.steer * sf * cf) + (d.lateral * cf * 0.45f) + (d.bump * rf)) * g);
                            if (cfg.InvFfb) mag = -mag;
                            mag = Math.Clamp(mag, -10000, 10000);
                            ffbEffect.SetParameters(new EffectParameters { Flags = EffectFlags.Cartesian | EffectFlags.ObjectIds, Parameters = new ConstantForce { Magnitude = mag } }, EffectParameterFlags.TypeSpecificParameters | EffectParameterFlags.NoRestart);
                        }

                        if (d != null && d.active && cfg.UdpOn)
                        {
                            byte[] pkt = new byte[324];
                            BitConverter.GetBytes(1).CopyTo(pkt, 0);
                            BitConverter.GetBytes(8500f).CopyTo(pkt, 8);
                            BitConverter.GetBytes(d.rpm * 8500f).CopyTo(pkt, 16);
                            BitConverter.GetBytes(d.speed).CopyTo(pkt, 244);
                            BitConverter.GetBytes(d.skid).CopyTo(pkt, 164);
                            pkt[307] = (byte)Math.Clamp(d.gear, 0, 10);
                            udp.Send(pkt, pkt.Length, "127.0.0.1", cfg.UdpPort);
                        }

                        string[] cmds;
                        lock (pendingCmds) { cmds = pendingCmds.ToArray(); pendingCmds.Clear(); }
                        bool hasAxes = cfg.Steer != null || cfg.Throt != null || cfg.Brake != null;
                        string resp = JsonSerializer.Serialize(new { active = hasAxes, steer = ReadAxis(cfg.Steer, true), throt = ReadAxis(cfg.Throt, false), brake = ReadAxis(cfg.Brake, false), clutch = ReadAxis(cfg.Clutch, false), hb = ReadAxis(cfg.Hb, false), cmds });
                        byte[] buf = Encoding.UTF8.GetBytes(resp);
                        ctx.Response.ContentType = "application/json";
                        ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                        ctx.Response.Close();
                    }
                    catch { }
                }
            }) { IsBackground = true }.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            running = false; http?.Stop(); ffbEffect?.Dispose(); ffbWheel?.Unacquire(); ffbWheel?.Dispose();
            base.OnFormClosing(e);
        }

        private class DevItem { public string Name { get; set; } public Guid Guid { get; set; } public override string ToString() => Name; }
        [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm()); }
    }
}
