using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SharpDX.DirectInput;

namespace SimRigBridge
{
    public class AxisBind
    {
        public Guid Dev { get; set; } = Guid.Empty;
        public int Axis { get; set; } = -1;
        public bool Inv { get; set; } = false;
        public int Min { get; set; } = 0;
        public int Max { get; set; } = 100;
    }

    public class BtnBind
    {
        public Guid Dev { get; set; } = Guid.Empty;
        public int Btn { get; set; } = -1;
        public string Cmd { get; set; } = "";
    }

    public class RigConfig
    {
        public Guid FfbDev { get; set; } = Guid.Empty;
        public int Gain { get; set; } = 50;
        public int Center { get; set; } = 85;
        public int MinForce { get; set; } = 18;
        public int Spring { get; set; } = 80;
        public int Damper { get; set; } = 55;
        public int Road { get; set; } = 45;
        public int SteerDeg { get; set; } = 900;
        public int SteerGamma { get; set; } = 180; // 100 = lineaarinen (1.0), 180 = pehmennetty keskialue (1.8)
        public int Softlock { get; set; } = 85;
        public bool InvFfb { get; set; } = false;
        public bool InvSpring { get; set; } = false;
        public bool UdpOn { get; set; } = false;
        public int UdpPort { get; set; } = 20777;
        public AxisBind Steer { get; set; } = new();
        public AxisBind Throt { get; set; } = new();
        public AxisBind Brake { get; set; } = new();
        public AxisBind Clutch { get; set; } = new();
        public AxisBind Hb { get; set; } = new();
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
        private const int I0 = 0, I1 = 1, I2 = 2, I3 = 3, I4 = 4, N5 = 5, N4096 = 4096;
        private readonly string cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rig_config.json");
        private RigConfig cfg = new();
        private DirectInput di = new();
        private Dictionary<Guid, Joystick> sticks = new();
        private bool[] prevBtns = new bool[4000];
        private Joystick? ffbWheel;
        private Effect? constEffect;
        private Effect? springEffect;
        private Effect? damperEffect;
        private bool effectsPlaying = false;
        private HttpListener? httpServer;
        private WebSocket? activeWs;
        private volatile int targetForce = 0;
        private volatile int targetSpring = 0;
        private volatile int targetDamper = 0;
        private float smoothForce = 0f;
        private volatile bool running = true;
        private string listenTarget = "";
        private Dictionary<Guid, int[]> baseAxes = new();
        private List<string> pendingCmds = new();
        private bool wasInGear = false;
        private int[] axisVals = new int[N5];
        private Panel[] axisBars = new Panel[N5];
        private long lastUiTick = 0;
        private long lastTelemetryTick = 0;

        private ComboBox ffbCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
        private TrackBar gainBar = new() { Minimum = 0, Maximum = 100, Width = 280 };
        private TrackBar centerBar = new() { Minimum = 0, Maximum = 100, Width = 280 };
        private TrackBar springBar = new() { Minimum = 0, Maximum = 100, Width = 280 };
        private TrackBar damperBar = new() { Minimum = 0, Maximum = 100, Width = 280 };
        private TrackBar roadBar = new() { Minimum = 0, Maximum = 100, Width = 280 };
        private TrackBar degBar = new() { Minimum = 180, Maximum = 1440, TickFrequency = 90, Width = 280 };
        private TrackBar gammaBar = new() { Minimum = 100, Maximum = 250, TickFrequency = 10, Width = 280 };
        private TrackBar softlockBar = new() { Minimum = 0, Maximum = 100, Width = 280 };
        private CheckBox invFfbChk = new() { Text = "Käännä FFB-suunta (Invert Force)", AutoSize = true };
        private CheckBox invSpringChk = new() { Text = "Käännä Spring-suunta (Invert Spring)", AutoSize = true };
        private CheckBox udpChk = new() { Text = "SimHub UDP-lähetys (127.0.0.1)", AutoSize = true };
        private NumericUpDown udpPortBox = new() { Minimum = 1024, Maximum = 65535, Value = 20777, Width = 80 };
        private Label statusLbl = new() { AutoSize = true, ForeColor = Color.DarkBlue };
        private Label bindStatusLbl = new() { AutoSize = true, ForeColor = Color.DarkGreen };
        private ListBox btnList = new() { Width = 440, Height = 150 };
        private TextBox cmdBox = new() { Width = 180, Text = "gear_up" };
        private Label wizLbl = new() { AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.DarkRed };
        private string[] wizQueue = { "gear_1", "gear_2", "gear_3", "gear_4", "gear_5", "gear_6", "gear_r" };
        private int wizStep = 0;

        public MainForm()
        {
            Text = "SimRig FiveM Bridge & FFB (SW20 V3 / Moza / Simjack)";
            Size = new Size(575, 735);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            LoadCfg();

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var t1 = new TabPage("1. Ratti & FFB");
            var t2 = new TabPage("2. Polkimet & Käsijarru");
            var t3 = new TabPage("3. Vaihteet & Napit");
            tabs.TabPages.AddRange(new[] { t1, t2, t3 });
            Controls.Add(tabs);

            // Välilehti 1: Ratti & FFB
            var p1 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12), WrapContents = false };
            p1.Controls.Add(new Label { Text = "Force Feedback -laite (SW20 V3 / DD / Belt):", AutoSize = true });
            var rowFfb = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var btnConn = new Button { Text = "Yhdistä FFB", Width = 100 };
            var btnTest = new Button { Text = "Testaa FFB", Width = 95 };
            rowFfb.Controls.AddRange(new Control[] { ffbCombo, btnConn, btnTest });
            p1.Controls.Add(rowFfb);

            gainBar.Value = Math.Clamp(cfg.Gain, 0, 100);
            centerBar.Value = Math.Clamp(cfg.Center, 0, 100);
            springBar.Value = Math.Clamp(cfg.Spring, 0, 100);
            damperBar.Value = Math.Clamp(cfg.Damper, 0, 100);
            roadBar.Value = Math.Clamp(cfg.Road, 0, 100);
            degBar.Value = Math.Clamp(cfg.SteerDeg, 180, 1440);
            gammaBar.Value = Math.Clamp(cfg.SteerGamma, 100, 250);
            softlockBar.Value = Math.Clamp(cfg.Softlock, 0, 100);
            invFfbChk.Checked = cfg.InvFfb;
            invSpringChk.Checked = cfg.InvSpring;
            udpChk.Checked = cfg.UdpOn;
            udpPortBox.Value = cfg.UdpPort;

            p1.Controls.Add(MakeSliderRow("Kokonaisvoima (Gain %):", gainBar, v => { cfg.Gain = v; SaveCfg(); }));
            p1.Controls.Add(MakeSliderRow("Kaarre-G / Palautus (%):", centerBar, v => { cfg.Center = v; SaveCfg(); }));
            p1.Controls.Add(MakeSliderRow("Keskitysjousi (Spring %):", springBar, v => { cfg.Spring = v; SaveCfg(); }));
            p1.Controls.Add(MakeSliderRow("Vaimennus (Damper %):", damperBar, v => { cfg.Damper = v; SaveCfg(); }));
            p1.Controls.Add(MakeSliderRow("Tienpinta / Töyssyt (%):", roadBar, v => { cfg.Road = v; SaveCfg(); }));
            p1.Controls.Add(MakeSliderRow("Kääntösäde (180–1440°):", degBar, v => { cfg.SteerDeg = v; SaveCfg(); }, "°"));
            p1.Controls.Add(MakeSliderRow("Ohjauksen käyrä (Gamma %):", gammaBar, v => { cfg.SteerGamma = v; SaveCfg(); }, "%"));
            p1.Controls.Add(MakeSliderRow("Softlock-rajoitin (%):", softlockBar, v => { cfg.Softlock = v; SaveCfg(); }));

            var rowInv = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            invFfbChk.CheckedChanged += (_, _) => { cfg.InvFfb = invFfbChk.Checked; SaveCfg(); };
            invSpringChk.CheckedChanged += (_, _) => { cfg.InvSpring = invSpringChk.Checked; SaveCfg(); };
            rowInv.Controls.AddRange(new Control[] { invFfbChk, invSpringChk });
            p1.Controls.Add(rowInv);

            var rowUdp = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            udpChk.CheckedChanged += (_, _) => { cfg.UdpOn = udpChk.Checked; SaveCfg(); };
            udpPortBox.ValueChanged += (_, _) => { cfg.UdpPort = (int)udpPortBox.Value; SaveCfg(); };
            rowUdp.Controls.AddRange(new Control[] { udpChk, udpPortBox });
            p1.Controls.Add(rowUdp);

            statusLbl.Text = "Valmis (portti 8765)";
            p1.Controls.Add(statusLbl);
            t1.Controls.Add(p1);

            btnConn.Click += (_, _) =>
            {
                if (ffbCombo.SelectedItem is DevItem item) { cfg.FfbDev = item.Id; SaveCfg(); ConnectFFB(item.Id); }
            };
            btnTest.Click += (_, _) =>
            {
                if (constEffect == null) { statusLbl.Text = "Yhdistä FFB ensin!"; return; }
                Task.Run(() =>
                {
                    lastTelemetryTick = Environment.TickCount64 + 600;
                    targetSpring = (int)(5500 * (cfg.Spring / 100f));
                    targetDamper = (int)(3500 * (cfg.Damper / 100f));
                    targetForce = 3500; Thread.Sleep(250);
                    targetForce = -3500; Thread.Sleep(250);
                    targetForce = 0;
                    targetSpring = 0;
                    targetDamper = 0;
                    lastTelemetryTick = 0;
                });
            };

            // Välilehti 2: Polkimet & Käsijarru
            var p2 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12), WrapContents = false };
            p2.Controls.Add(MakeAxisRow("Ohjaus (Steer):", "steer", cfg.Steer, I0));
            p2.Controls.Add(MakeAxisRow("Kaasu (Throttle):", "throt", cfg.Throt, I1));
            p2.Controls.Add(MakeAxisRow("Jarru (Brake):", "brake", cfg.Brake, I2));
            p2.Controls.Add(MakeAxisRow("Kytkin (Clutch):", "clutch", cfg.Clutch, I3));
            p2.Controls.Add(MakeAxisRow("Käsijarru:", "hb", cfg.Hb, I4));
            p2.Controls.Add(bindStatusLbl);
            t2.Controls.Add(p2);

            // Välilehti 3: Vaihteet & Napit
            var p3 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12), WrapContents = false };
            var rowWiz = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var btnWiz = new Button { Text = "H-Kaavio Ohjattu Asetus (1-6 + R)", Width = 230 };
            var btnClear = new Button { Text = "Tyhjennä valittu", Width = 120 };
            rowWiz.Controls.AddRange(new Control[] { btnWiz, btnClear });
            p3.Controls.Add(rowWiz);
            p3.Controls.Add(wizLbl);
            var rowCmd = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var btnBind = new Button { Text = "Tunnista nappi/vaihde", Width = 155 };
            rowCmd.Controls.AddRange(new Control[] { new Label { Text = "Komento:", AutoSize = true }, cmdBox, btnBind });
            p3.Controls.Add(rowCmd);
            p3.Controls.Add(btnList);
            t3.Controls.Add(p3);

            btnWiz.Click += (_, _) => { wizStep = 0; listenTarget = "wiz"; wizLbl.Text = "Kytke nyt 1. VAIHDE..."; };
            btnBind.Click += (_, _) => { listenTarget = "btn"; wizLbl.Text = $"Paina nappia tai vaihdetta komennolle: {cmdBox.Text}"; };
            btnClear.Click += (_, _) =>
            {
                if (btnList.SelectedIndex >= 0 && btnList.SelectedIndex < cfg.Btns.Count)
                { cfg.Btns.RemoveAt(btnList.SelectedIndex); SaveCfg(); RefreshBtnList(); }
            };

            RefreshBtnList();
            InitDevices();
            StartLoops();

            var ffbTimer = new System.Windows.Forms.Timer { Interval = 30 };
            ffbTimer.Tick += (_, _) =>
            {
                if (!running || constEffect == null) return;
                if (Environment.TickCount64 - lastTelemetryTick > 200)
                {
                    if (effectsPlaying)
                    {
                        targetForce = 0;
                        targetSpring = 0;
                        targetDamper = 0;
                        smoothForce = 0f;
                        try { constEffect.Stop(); } catch { }
                        try { springEffect?.Stop(); } catch { }
                        try { damperEffect?.Stop(); } catch { }
                        effectsPlaying = false;
                    }
                    return;
                }
                UpdateEffects(targetForce, targetSpring, targetDamper);
            };
            ffbTimer.Start();

            FormClosing += (_, _) =>
            {
                running = false;
                ffbTimer.Stop();
                try { activeWs?.Abort(); } catch { }
                try { httpServer?.Stop(); httpServer?.Close(); } catch { }
                if (effectsPlaying)
                {
                    UpdateEffects(0, 0, 0);
                    effectsPlaying = false;
                }
                try { constEffect?.Stop(); constEffect?.Dispose(); } catch { }
                try { springEffect?.Stop(); springEffect?.Dispose(); } catch { }
                try { damperEffect?.Stop(); damperEffect?.Dispose(); } catch { }
                try { ffbWheel?.Unacquire(); ffbWheel?.Dispose(); } catch { }
                foreach (var kv in sticks)
                {
                    try { kv.Value.Unacquire(); kv.Value.Dispose(); } catch { }
                }
                try { di.Dispose(); } catch { }
            };
        }

        private void NextWizardStep()
        {
            wizStep++;
            if (wizStep >= wizQueue.Length) { listenTarget = ""; wizLbl.Text = "H-Kaavio 1-6 + R tallennettu!"; return; }
            string lbl = wizQueue[wizStep] == "gear_r" ? "PERUUTUSVAIHDE (R)" : $"{wizStep + 1}. VAIHDE";
            wizLbl.Text = $"Kytke nyt {lbl}...";
        }

        private Control MakeSliderRow(string title, TrackBar tb, Action<int> onChg, string unit = "")
        {
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var lbl = new Label { Text = $"{title} {tb.Value}{unit}", Width = 215 };
            tb.ValueChanged += (_, _) => { lbl.Text = $"{title} {tb.Value}{unit}"; onChg(tb.Value); };
            p.Controls.AddRange(new Control[] { lbl, tb });
            return p;
        }

        private Control MakeAxisRow(string name, string key, AxisBind bind, int idx)
        {
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var btn = new Button { Text = "Tunnista", Width = 75 };
            var chk = new CheckBox { Text = "Käännä", Checked = bind.Inv, AutoSize = true };
            var minN = new NumericUpDown { Minimum = 0, Maximum = 95, Value = bind.Min, Width = 48 };
            var maxN = new NumericUpDown { Minimum = 5, Maximum = 100, Value = bind.Max, Width = 48 };
            var bar = new Panel { Width = 110, Height = 16, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
            axisBars[idx] = bar;
            bar.Paint += (_, e) =>
            {
                int w = (int)(bar.Width * (axisVals[idx] / 100f));
                e.Graphics.FillRectangle(Brushes.DodgerBlue, 0, 0, w, bar.Height);
            };
            btn.Click += (_, _) =>
            {
                baseAxes.Clear();
                foreach (var kv in sticks) { kv.Value.Poll(); baseAxes[kv.Key] = GetAxes(kv.Value.GetCurrentState()); }
                listenTarget = key;
                bindStatusLbl.Text = $"Liikuta akselia: {name}";
            };
            chk.CheckedChanged += (_, _) => { bind.Inv = chk.Checked; SaveCfg(); };
            minN.ValueChanged += (_, _) => { bind.Min = (int)minN.Value; SaveCfg(); };
            maxN.ValueChanged += (_, _) => { bind.Max = (int)maxN.Value; SaveCfg(); };
            p.Controls.AddRange(new Control[] { new Label { Text = name, Width = 115 }, btn, chk, new Label { Text = "Min", AutoSize = true }, minN, new Label { Text = "Max", AutoSize = true }, maxN, bar });
            return p;
        }

        private void RefreshBtnList()
        {
            btnList.Items.Clear();
            foreach (var b in cfg.Btns) btnList.Items.Add($"{b.Cmd}  ->  Nappi {b.Btn} ({b.Dev.ToString().Substring(0, 6)})");
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

        private class DevItem { public Guid Id; public string Name = ""; public override string ToString() => Name; }

        private void InitDevices()
        {
            ffbCombo.Items.Clear();
            foreach (var dev in di.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
            {
                try
                {
                    var joy = new Joystick(di, dev.InstanceGuid);
                    joy.SetCooperativeLevel(this.Handle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    joy.Acquire();
                    sticks[dev.InstanceGuid] = joy;
                    ffbCombo.Items.Add(new DevItem { Id = dev.InstanceGuid, Name = dev.InstanceName });
                }
                catch { }
            }
            for (int i = 0; i < ffbCombo.Items.Count; i++)
                if (((DevItem)ffbCombo.Items[i]!).Id == cfg.FfbDev) { ffbCombo.SelectedIndex = i; ConnectFFB(cfg.FfbDev); break; }
            if (ffbCombo.SelectedIndex < 0 && ffbCombo.Items.Count > 0) ffbCombo.SelectedIndex = 0;
        }

        private ConditionSet BuildCondition(int coeff, int sat = 10000)
        {
            int c = Math.Clamp(coeff, -10000, 10000);
            int s = Math.Clamp(sat, 0, 10000);
            return new ConditionSet
            {
                Conditions = new[]
                {
                    new Condition
                    {
                        Offset = 0,
                        PositiveCoefficient = c,
                        NegativeCoefficient = c,
                        PositiveSaturation = s,
                        NegativeSaturation = s,
                        DeadBand = 0
                    }
                }
            };
        }

        private void ConnectFFB(Guid guid)
        {
            try
            {
                effectsPlaying = false;
                constEffect?.Dispose(); constEffect = null;
                springEffect?.Dispose(); springEffect = null;
                damperEffect?.Dispose(); damperEffect = null;
                ffbWheel?.Unacquire(); ffbWheel?.Dispose(); ffbWheel = null;

                ffbWheel = new Joystick(di, guid);
                ffbWheel.SetCooperativeLevel(this.Handle, CooperativeLevel.Exclusive | CooperativeLevel.Background);
                ffbWheel.Acquire();
                ffbWheel.SendForceFeedbackCommand(ForceFeedbackCommand.Reset);
                Thread.Sleep(80);

                var fxConst = ffbWheel.GetEffects(EffectType.ConstantForce);
                if (fxConst.Count == 0) { statusLbl.Text = "Laite ei tue ConstantForce-efektiä."; return; }

                var epConst = new EffectParameters
                {
                    Duration = int.MaxValue,
                    Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                    Gain = 10000,
                    SamplePeriod = 0,
                    TriggerButton = -1,
                    TriggerRepeatInterval = 0,
                    Axes = new[] { 0 },
                    Directions = new[] { 0 },
                    Envelope = null,
                    Parameters = new ConstantForce { Magnitude = 0 }
                };
                constEffect = new Effect(ffbWheel, fxConst[0].Guid, epConst);

                var allCondFx = ffbWheel.GetEffects(EffectType.Condition);
                var fxSpring = allCondFx.FirstOrDefault(e => e.Guid == EffectGuid.Spring);
                if (fxSpring != null)
                {
                    var epSpring = new EffectParameters
                    {
                        Duration = int.MaxValue,
                        Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                        Gain = 10000,
                        SamplePeriod = 0,
                        TriggerButton = -1,
                        TriggerRepeatInterval = 0,
                        Axes = new[] { 0 },
                        Directions = new[] { 0 },
                        Envelope = null,
                        Parameters = BuildCondition(0, 10000)
                    };
                    springEffect = new Effect(ffbWheel, fxSpring.Guid, epSpring);
                }

                var fxDamper = allCondFx.FirstOrDefault(e => e.Guid == EffectGuid.Damper);
                if (fxDamper != null)
                {
                    var epDamper = new EffectParameters
                    {
                        Duration = int.MaxValue,
                        Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                        Gain = 10000,
                        SamplePeriod = 0,
                        TriggerButton = -1,
                        TriggerRepeatInterval = 0,
                        Axes = new[] { 0 },
                        Directions = new[] { 0 },
                        Envelope = null,
                        Parameters = BuildCondition(0, 10000)
                    };
                    damperEffect = new Effect(ffbWheel, fxDamper.Guid, epDamper);
                }

                statusLbl.Text = $"FFB yhdistetty: {ffbWheel.Information.InstanceName} (Spring={(springEffect != null ? "OK" : "-")}, Damper={(damperEffect != null ? "OK" : "-")})";
            }
            catch (Exception ex) { statusLbl.Text = "FFB-virhe: " + ex.Message; }
        }

        private void UpdateEffects(int mag, int springCoeff, int damperCoeff)
        {
            if (constEffect == null || ffbWheel == null) return;
            smoothForce = (smoothForce * 0.78f) + (mag * 0.22f);
            // InvFfb on jo sovellettu pelin voimiin ennen Softlockin lisäämistä
            int val = Math.Clamp((int)smoothForce, -9000, 9000);
            int rawSpring = Math.Clamp(springCoeff, 0, 10000);
            int sVal = cfg.InvSpring ? -rawSpring : rawSpring;
            int dVal = Math.Clamp(damperCoeff, 0, 10000);

            try
            {
                constEffect.SetParameters(new EffectParameters { Parameters = new ConstantForce { Magnitude = val } }, EffectParameterFlags.TypeSpecificParameters);
                if (!effectsPlaying || (constEffect.Status & EffectStatus.Playing) == 0)
                    constEffect.Start(1, EffectPlayFlags.None);

                if (springEffect != null)
                {
                    springEffect.SetParameters(new EffectParameters { Parameters = BuildCondition(sVal, 10000) }, EffectParameterFlags.TypeSpecificParameters);
                    if (!effectsPlaying || (springEffect.Status & EffectStatus.Playing) == 0)
                        springEffect.Start(1, EffectPlayFlags.None);
                }

                if (damperEffect != null)
                {
                    damperEffect.SetParameters(new EffectParameters { Parameters = BuildCondition(dVal, 10000) }, EffectParameterFlags.TypeSpecificParameters);
                    if (!effectsPlaying || (damperEffect.Status & EffectStatus.Playing) == 0)
                        damperEffect.Start(1, EffectPlayFlags.None);
                }

                effectsPlaying = true;
            }
            catch
            {
                try
                {
                    ffbWheel.Acquire();
                    constEffect.Start(1, EffectPlayFlags.None);
                    springEffect?.Start(1, EffectPlayFlags.None);
                    damperEffect?.Start(1, EffectPlayFlags.None);
                    effectsPlaying = true;
                }
                catch { }
            }
        }

        private int[] GetAxes(JoystickState s) => new[] { s.X, s.Y, s.Z, s.RotationX, s.RotationY, s.RotationZ, s.Sliders[0], s.Sliders[1] };

        private float ReadRawSteer()
        {
            AxisBind b = cfg.Steer;
            if (b.Axis < 0 || !sticks.TryGetValue(b.Dev, out var joy)) return 0f;
            var ax = GetAxes(joy.GetCurrentState());
            if (b.Axis >= ax.Length) return 0f;
            float norm = ax[b.Axis] / 65535f;
            if (b.Inv) norm = 1f - norm;
            float lo = b.Min / 100f, hi = Math.Max(lo + 0.01f, b.Max / 100f);
            norm = Math.Clamp((norm - lo) / (hi - lo), 0f, 1f);
            return norm * 2f - 1f;
        }

        private float ReadAxis(AxisBind b, bool centered)
        {
            if (b.Axis < 0 || !sticks.TryGetValue(b.Dev, out var joy)) return 0f;
            var ax = GetAxes(joy.GetCurrentState());
            if (b.Axis >= ax.Length) return 0f;
            float norm = ax[b.Axis] / 65535f;
            if (b.Inv) norm = 1f - norm;
            float lo = b.Min / 100f, hi = Math.Max(lo + 0.01f, b.Max / 100f);
            norm = Math.Clamp((norm - lo) / (hi - lo), 0f, 1f);
            if (!centered) return norm;
            float cVal = norm * 2f - 1f;
            if (ReferenceEquals(b, cfg.Steer))
            {
                // 1. Kääntösäteen skaalaus (< 900 herkentää, > 900 rauhoittaa)
                float ratio = Math.Clamp(cfg.SteerDeg / 900f, 0.2f, 2.0f);
                cVal = Math.Clamp(cVal / ratio, -1f, 1f);

                // 2. Gamma-käyrä keskialueen pehmentämiseen 900° ratilla (esim. 180% -> gamma 1.8)
                float gamma = Math.Clamp(cfg.SteerGamma / 100f, 1.0f, 2.5f);
                cVal = Math.Sign(cVal) * (float)Math.Pow(Math.Abs(cVal), gamma);
            }
            return cVal;
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
                            JoystickState st;
                            try { kv.Value.Poll(); st = kv.Value.GetCurrentState(); }
                            catch { try { kv.Value.Acquire(); kv.Value.Poll(); st = kv.Value.GetCurrentState(); } catch { continue; } }
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
                    httpServer = new HttpListener();
                    httpServer.Prefixes.Add("http://127.0.0.1:8765/");
                    httpServer.Start();
                    using var udp = new UdpClient();
                    while (running)
                    {
                        HttpListenerContext ctx;
                        try { ctx = httpServer.GetContext(); }
                        catch { if (!running) break; Thread.Sleep(50); continue; }
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
                                    var oldWs = activeWs;
                                    if (oldWs != null) { try { oldWs.Abort(); } catch { } }

                                    var wsCtx = await ctx.AcceptWebSocketAsync(null);
                                    var ws = wsCtx.WebSocket;
                                    activeWs = ws;
                                    var buf = new byte[N4096];
                                    while (ws.State == WebSocketState.Open && running && ReferenceEquals(activeWs, ws))
                                    {
                                        var res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                                        if (res.MessageType == WebSocketMessageType.Close) break;
                                        string body = Encoding.UTF8.GetString(buf, 0, res.Count);
                                        if (!string.IsNullOrWhiteSpace(body) && body.Contains("\"cfgUpdate\""))
                                        {
                                            try
                                            {
                                                using var doc = JsonDocument.Parse(body);
                                                if (doc.RootElement.TryGetProperty("cfg", out var el))
                                                {
                                                    if (el.TryGetProperty("gain", out var p1)) cfg.Gain = p1.GetInt32();
                                                    if (el.TryGetProperty("center", out var p2)) cfg.Center = p2.GetInt32();
                                                    if (el.TryGetProperty("minForce", out var p3)) cfg.MinForce = p3.GetInt32();
                                                    if (el.TryGetProperty("road", out var p4)) cfg.Road = p4.GetInt32();
                                                    if (el.TryGetProperty("steerAngle", out var p5)) cfg.SteerDeg = p5.GetInt32();
                                                    if (el.TryGetProperty("softlock", out var p6)) cfg.Softlock = p6.GetInt32();
                                                    if (el.TryGetProperty("invFfb", out var p7)) cfg.InvFfb = p7.GetBoolean();
                                                    SaveCfg();
                                                }
                                            }
                                            catch { }
                                            continue;
                                        }
                                        var d = string.IsNullOrWhiteSpace(body) ? new TelemetryData() : (JsonSerializer.Deserialize<TelemetryData>(body) ?? new());

                                        if (d.active && !d.inAir)
                                        {
                                            lastTelemetryTick = Environment.TickCount64;
                                            float g = cfg.Gain / 100f;
                                            float centerPct = cfg.Center / 100f;
                                            float springPct = cfg.Spring / 100f;
                                            float damperPct = cfg.Damper / 100f;
                                            float softlockPct = cfg.Softlock / 100f;
                                            float rf = (cfg.Road / 100f) * 3500f;

                                            float safeLat = Math.Clamp(d.lateral, -2.0f, 2.0f);
                                            float safeBump = Math.Clamp(d.bump, -1.0f, 1.0f);
                                            float speedNorm = Math.Clamp(d.speed / 35f, 0f, 1f);

                                            // 1) Rautatason Spring: heti 0 mm:stä lähtevä keskitys (skaalautuu kääntösäteen mukaan)
                                            float degScale = Math.Clamp(900f / Math.Max(cfg.SteerDeg, 180), 1.0f, 2.2f);
                                            float springBase = (0.35f + 0.65f * speedNorm) * springPct * 8500f * g * Math.Min(degScale, 1.5f);
                                            targetSpring = Math.Clamp((int)springBase, 0, 9500);

                                            // 2) Rautatason Damper: tasainen hydraulinen vaimennus
                                            float damperBase = (0.45f + 0.35f * speedNorm) * damperPct * 6500f * g;
                                            targetDamper = Math.Clamp((int)damperBase, 0, 8500);

                                            // 3) ConstantForce: kaarre-G-voimat + tienpinnan töyssyt ja luistovärinä
                                            float rawSteerCenter = ReadRawSteer();
                                            float absSteerCenter = Math.Abs(rawSteerCenter);
                                            float minBoost = (absSteerCenter > 0.001f) ? (cfg.MinForce / 100f) * 3200f : 0f;
                                            float centerSpring = (absSteerCenter > 0.001f) ? -Math.Sign(rawSteerCenter) * (minBoost + (float)Math.Pow(absSteerCenter, 0.55) * 4200f) * centerPct * (0.4f + 0.6f * speedNorm) : 0f;
                                            float latForce = (-safeLat * (2400f + 1600f * speedNorm) * centerPct) + centerSpring;
                                            float skidVib = (d.skid > 1.2f) ? (float)Math.Sin(Environment.TickCount64 * 0.14) * Math.Min((d.skid - 1.2f) * 700f, 1500f) : 0f;
                                            float rpmVib = (d.speed < 3f) ? (float)Math.Sin(Environment.TickCount64 * 0.08 * (0.5f + d.rpm)) * 120f : 0f;
                                            float roadForce = (safeBump * rf) + skidVib + rpmVib;

                                            // Käännä vain pelin voimat InvFfb-asetuksen mukaan
                                            float gameForce = (latForce + roadForce) * g;
                                            if (cfg.InvFfb) gameForce = -gameForce;

                                            // 4) Softlock-päätyrajoitin: työntää AINA kohti keskustaa (-Math.Sign(rawSteer)), riippumatta InvFfb-asetuksesta
                                            float rawSteer = ReadRawSteer();
                                            float limitNorm = Math.Clamp(cfg.SteerDeg / 900f, 0.2f, 1.0f);
                                            float softStart = Math.Max(limitNorm - 0.03f, 0.15f);
                                            float absRaw = Math.Abs(rawSteer);
                                            float softForce = 0f;
                                            if (absRaw > softStart && softlockPct > 0.01f)
                                            {
                                                float over = (absRaw - softStart) / 0.06f;
                                                float lockStrength = Math.Clamp(over * over, 0f, 1.25f) * 8500f * softlockPct;
                                                softForce = -Math.Sign(rawSteer) * lockStrength;
                                                targetDamper = Math.Clamp(targetDamper + (int)(2500f * softlockPct), 0, 9500);
                                            }

                                            targetForce = (int)(gameForce + softForce);
                                        }
                                        else
                                        {
                                            lastTelemetryTick = 0;
                                            targetForce = 0;
                                            targetSpring = 0;
                                            targetDamper = 0;
                                            smoothForce = 0f;
                                        }

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
                                    if (ReferenceEquals(activeWs, ws)) activeWs = null;
                                }
                                catch { }
                            });
                            continue;
                        }
                        ctx.Response.StatusCode = 200;
                        ctx.Response.Close();
                    }
                }
                catch (Exception ex)
                {
                    if (running && IsHandleCreated) BeginInvoke(() => statusLbl.Text = "HTTP-virhe: " + ex.Message);
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
