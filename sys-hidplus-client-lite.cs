// sys-hidplus-client-lite — Standalone GUI
// Compile: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /win32icon:app.ico /resource:sys-hidplus-client-lite-2048x2048.png,logo.png /out:sys-hidplus-client-lite.exe sys-hidplus-client-lite.cs
// Zero external dependencies. Works on any Windows 7+ with .NET Framework 4.x.

using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

// ---------------------------------------------------------------------------
// XInput P/Invoke
// ---------------------------------------------------------------------------

[StructLayout(LayoutKind.Sequential)]
struct XINPUT_GAMEPAD
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX;
    public short sThumbLY;
    public short sThumbRX;
    public short sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
struct XINPUT_STATE
{
    public uint dwPacketNumber;
    public XINPUT_GAMEPAD Gamepad;
}

static class XInput
{
    // Try xinput1_4 (Win8+), fall back to xinput1_3 (Win7), then xinput9_1_0
    private delegate int XInputGetStateDelegate(int dwUserIndex, ref XINPUT_STATE pState);
    private static XInputGetStateDelegate _getState;

    static XInput()
    {
        IntPtr lib = IntPtr.Zero;
        string[] dlls = { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" };
        foreach (var dll in dlls)
        {
            lib = LoadLibrary(dll);
            if (lib != IntPtr.Zero) break;
        }
        if (lib == IntPtr.Zero)
            throw new Exception("Could not load XInput DLL. Windows 7+ required.");

        IntPtr proc = GetProcAddress(lib, "XInputGetState");
        _getState = (XInputGetStateDelegate)Marshal.GetDelegateForFunctionPointer(
            proc, typeof(XInputGetStateDelegate));
    }

    public static int GetState(int index, ref XINPUT_STATE state)
    {
        return _getState(index, ref state);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
}

// ---------------------------------------------------------------------------
// Main Form
// ---------------------------------------------------------------------------

class MainForm : Form
{
    private const string SettingsDirectoryName = "sys-hidplus-client-lite";
    private const string SettingsFileName = "settings.json";

    // UI
    private TextBox txtIP;
    private Button btnStartStop;

    // State
    private volatile bool running = false;
    private Thread pollThread;
    private UdpClient udp;
    private IPEndPoint endpoint;

    // XInput constants
    const int ERROR_SUCCESS = 0;
    const ushort DPAD_UP = 0x0001, DPAD_DOWN = 0x0002, DPAD_LEFT = 0x0004, DPAD_RIGHT = 0x0008;
    const ushort START = 0x0010, BACK = 0x0020;
    const ushort LTHUMB = 0x0040, RTHUMB = 0x0080;
    const ushort LSHOULDER = 0x0100, RSHOULDER = 0x0200;
    const ushort BTN_A = 0x1000, BTN_B = 0x2000, BTN_X = 0x4000, BTN_Y = 0x8000;

    // Switch button bits
    const ulong SW_A = 1, SW_B = 1 << 1, SW_X = 1 << 2, SW_Y = 1 << 3;
    const ulong SW_LST = 1 << 4, SW_RST = 1 << 5, SW_L = 1 << 6, SW_R = 1 << 7;
    const ulong SW_ZL = 1 << 8, SW_ZR = 1 << 9, SW_PLUS = 1 << 10, SW_MINUS = 1 << 11;
    const ulong SW_DL = 1 << 12, SW_DU = 1 << 13, SW_DR = 1 << 14, SW_DD = 1 << 15;

    const int DEADZONE = 5000;
    const int TRIGGER_THRESHOLD = 100;

    public MainForm()
    {
        Text = "sys-hidplus-client-lite";
        Size = new Size(300, 300);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        // Modern Dark Theme
        BackColor = Color.FromArgb(32, 33, 36); // Google Dark Gray
        ForeColor = Color.FromArgb(232, 234, 237);
        Font = new Font("Segoe UI", 9.5f);

        // Layout Constants
        int padding = 12;
        int logoSize = 64;
        
        // Form Size (fixed width; final height is set after laying out the button)
        int formWidth = 305;
        Size = new Size(formWidth, 220);
        
        // Calculate effective content width
        // Form Width includes borders (~16px), so client area is smaller.
        int totalWidth = formWidth - 18; 
        int contentStart = padding + logoSize + 15;

        // Logo / Title
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            // Try load logo from resources
            using (var stream = assembly.GetManifestResourceStream("logo.png"))
            {
                if (stream != null)
                {
                    var logo = new PictureBox
                    {
                        Image = Image.FromStream(stream),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Size = new Size(logoSize, logoSize),
                        Location = new Point(padding, padding),
                        BackColor = Color.Transparent
                    };
                    Controls.Add(logo);

                    var lblSubtitle = new Label
                    {
                        Text = "sys-hidplus-client-lite",
                        Font = new Font("Segoe UI", 14, FontStyle.Bold),
                        ForeColor = Color.FromArgb(129, 201, 149), // Muted Green
                        AutoSize = true,
                        Location = new Point(contentStart - 10, padding)
                    };
                    Controls.Add(lblSubtitle);
                }
                else
                {
                    // Fallback to text title
                    var lblTitle = new Label
                    {
                        Text = "sys-hidplus-client-lite",
                        Font = new Font("Segoe UI", 18, FontStyle.Bold),
                        ForeColor = Color.FromArgb(129, 201, 149),
                        AutoSize = true,
                        Location = new Point(padding, padding)
                    };
                    Controls.Add(lblTitle);
                }
            }
            
            // Set Form Icon from Exe
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch 
        {
             var lblTitle = new Label
            {
                Text = "sys-hidplus-client-lite",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(129, 201, 149),
                AutoSize = true,
                Location = new Point(padding, padding)
            };
            Controls.Add(lblTitle);
        }

        // IP Section (Aligned with Title start)
        int ipY = padding + 35;
        int labelWidth = 72;
        
        var lblIP = new Label
        {
            Text = "Switch IP:",
            AutoSize = true,
            Location = new Point(contentStart + 10, ipY + 3),
            ForeColor = Color.FromArgb(189, 193, 198)
        };
        Controls.Add(lblIP);

        // IP Input
        txtIP = new TextBox
        {
            Text = "",
            Location = new Point(contentStart + labelWidth, ipY),
            BackColor = Color.FromArgb(48, 51, 57),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10)
        };
        // Span to right padding
        txtIP.Width = (totalWidth - padding) - txtIP.Location.X;
        Controls.Add(txtIP);

        // Start/Stop Button (Spans full width from Logo Left to Input Right)
        btnStartStop = new Button
        {
            Text = "\u25B6 Start Sending Inputs",
            Width = (totalWidth - padding) - padding, // Full span
            Height = 40,
            Location = new Point(padding, padding + logoSize + 20),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 150, 80), // Classic Green
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnStartStop.FlatAppearance.BorderSize = 0;
        btnStartStop.Click += BtnStartStop_Click;
        Controls.Add(btnStartStop);

        // Remove unused vertical space below the main action button.
        ClientSize = new Size(totalWidth, btnStartStop.Bottom + padding);

        txtIP.TextChanged += TxtIP_TextChanged;
        txtIP.Text = LoadSavedIpRaw();
        RefreshStartButtonState();

        FormClosing += MainForm_Closing;
    }

    private string GetSettingsPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, SettingsDirectoryName, SettingsFileName);
    }

    private string LoadSavedIpRaw()
    {
        try
        {
            string settingsPath = GetSettingsPath();
            if (!File.Exists(settingsPath))
                return "";

            string json = File.ReadAllText(settingsPath);
            Match match = Regex.Match(json, "\"switchIp\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"");
            if (!match.Success)
                return "";

            return Regex.Unescape(match.Groups["value"].Value.Replace("\\/", "/"));
        }
        catch
        {
            return "";
        }
    }

    private void SaveIpRaw(string rawIp)
    {
        try
        {
            string settingsPath = GetSettingsPath();
            string settingsDirectory = Path.GetDirectoryName(settingsPath);
            if (!Directory.Exists(settingsDirectory))
                Directory.CreateDirectory(settingsDirectory);

            string escaped = (rawIp ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
            string json = "{\n  \"switchIp\": \"" + escaped + "\"\n}\n";
            File.WriteAllText(settingsPath, json);
        }
        catch
        {
            // Keep app behavior lite: persistence failures are ignored.
        }
    }

    private bool IsValidIp(string text)
    {
        IPAddress ip;
        return IPAddress.TryParse((text ?? "").Trim(), out ip);
    }

    private void RefreshStartButtonState()
    {
        if (running)
        {
            btnStartStop.Enabled = true;
            return;
        }

        btnStartStop.Enabled = IsValidIp(txtIP.Text);
    }

    private void TxtIP_TextChanged(object sender, EventArgs e)
    {
        SaveIpRaw(txtIP.Text);
        RefreshStartButtonState();
    }

    private int ScanControllers()
    {
        int count = 0;
        var state = new XINPUT_STATE();
        for (int i = 0; i < 4; i++)
        {
            if (XInput.GetState(i, ref state) == ERROR_SUCCESS)
            {
                count++;
            }
        }
        return count;
    }

    private void BtnStartStop_Click(object sender, EventArgs e)
    {
        if (!running)
            StartSending();
        else
            StopSending();
    }

    private void StartSending()
    {
        // Validate IP
        IPAddress ip;
        if (!IPAddress.TryParse(txtIP.Text.Trim(), out ip))
        {
            MessageBox.Show("Invalid IP address.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        int count = ScanControllers();
        if (count == 0)
        {
            MessageBox.Show("No XInput controller detected.\nConnect an Xbox or compatible controller.",
                "No Controller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        endpoint = new IPEndPoint(ip, 8000);
        udp = new UdpClient();
        running = true;

        txtIP.Enabled = false;
        btnStartStop.Text = string.Format("\u25A0 Stop Sending Inputs ({0})", count);
        btnStartStop.BackColor = Color.FromArgb(200, 50, 50);

        pollThread = new Thread(PollLoop);
        pollThread.IsBackground = true;
        pollThread.Start();
    }

    private void StopSending()
    {
        running = false;
        if (pollThread != null)
        {
            pollThread.Join(500);
        }

        // Send disconnect packet
        try
        {
            int count = ScanControllers();
            if (count < 1) count = 1;
            byte[] disconnectPacket = BuildPacket(count,
                0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0);
            udp.Send(disconnectPacket, disconnectPacket.Length, endpoint);
        }
        catch { }

        try { udp.Close(); } catch { }

        txtIP.Enabled = true;
        btnStartStop.Text = "\u25B6 Start Sending Inputs";
        btnStartStop.BackColor = Color.FromArgb(0, 150, 80);
        RefreshStartButtonState();
    }

    struct PadData
    {
        public ulong keys;
        public int lx, ly, rx, ry;
    }

    private PadData ReadPad(int index)
    {
        var pd = new PadData();
        var state = new XINPUT_STATE();
        if (XInput.GetState(index, ref state) != ERROR_SUCCESS) return pd;

        var gp = state.Gamepad;
        ulong k = 0;

        // Face buttons (Xbox→Switch swap: A↔B, X↔Y)
        if ((gp.wButtons & BTN_A) != 0) k |= SW_B;
        if ((gp.wButtons & BTN_B) != 0) k |= SW_A;
        if ((gp.wButtons & BTN_X) != 0) k |= SW_Y;
        if ((gp.wButtons & BTN_Y) != 0) k |= SW_X;

        // Shoulders
        if ((gp.wButtons & LSHOULDER) != 0) k |= SW_L;
        if ((gp.wButtons & RSHOULDER) != 0) k |= SW_R;

        // Thumbs
        if ((gp.wButtons & LTHUMB) != 0) k |= SW_LST;
        if ((gp.wButtons & RTHUMB) != 0) k |= SW_RST;

        // Start/Back → Plus/Minus
        if ((gp.wButtons & START) != 0) k |= SW_PLUS;
        if ((gp.wButtons & BACK) != 0) k |= SW_MINUS;

        // D-pad
        if ((gp.wButtons & DPAD_UP) != 0) k |= SW_DU;
        if ((gp.wButtons & DPAD_DOWN) != 0) k |= SW_DD;
        if ((gp.wButtons & DPAD_LEFT) != 0) k |= SW_DL;
        if ((gp.wButtons & DPAD_RIGHT) != 0) k |= SW_DR;

        // Triggers → ZL/ZR
        if (gp.bLeftTrigger >= TRIGGER_THRESHOLD) k |= SW_ZL;
        if (gp.bRightTrigger >= TRIGGER_THRESHOLD) k |= SW_ZR;

        pd.keys = k;

        // Sticks with deadzone (cast to int to avoid OverflowException on -32768)
        pd.lx = Math.Abs((int)gp.sThumbLX) < DEADZONE ? 0 : (int)gp.sThumbLX;
        pd.ly = Math.Abs((int)gp.sThumbLY) < DEADZONE ? 0 : (int)gp.sThumbLY;
        pd.rx = Math.Abs((int)gp.sThumbRX) < DEADZONE ? 0 : (int)gp.sThumbRX;
        pd.ry = Math.Abs((int)gp.sThumbRY) < DEADZONE ? 0 : (int)gp.sThumbRY;

        return pd;
    }

    // Build the UDP packet — same format as input_pc.py
    // <HH HQIIII HQIIII HQIIII HQIIII
    // magic(2) count(2) [type(2) keys(8) lx(4) ly(4) rx(4) ry(4)] x4
    private byte[] BuildPacket(int count,
        ushort t1, ulong k1, int lx1, int ly1, int rx1, int ry1,
        ushort t2, ulong k2, int lx2, int ly2, int rx2, int ry2,
        ushort t3, ulong k3, int lx3, int ly3, int rx3, int ry3,
        ushort t4, ulong k4, int lx4, int ly4, int rx4, int ry4)
    {
        // Total: 2+2 + 4*(2+8+4+4+4+4) = 4 + 4*26 = 108 bytes
        byte[] buf = new byte[108];
        int off = 0;

        // Magic
        WriteUInt16(buf, ref off, 0x3276);
        // Controller count
        WriteUInt16(buf, ref off, (ushort)count);

        // P1
        WriteUInt16(buf, ref off, t1);
        WriteUInt64(buf, ref off, k1);
        WriteInt32(buf, ref off, lx1);
        WriteInt32(buf, ref off, ly1);
        WriteInt32(buf, ref off, rx1);
        WriteInt32(buf, ref off, ry1);

        // P2
        WriteUInt16(buf, ref off, t2);
        WriteUInt64(buf, ref off, k2);
        WriteInt32(buf, ref off, lx2);
        WriteInt32(buf, ref off, ly2);
        WriteInt32(buf, ref off, rx2);
        WriteInt32(buf, ref off, ry2);

        // P3
        WriteUInt16(buf, ref off, t3);
        WriteUInt64(buf, ref off, k3);
        WriteInt32(buf, ref off, lx3);
        WriteInt32(buf, ref off, ly3);
        WriteInt32(buf, ref off, rx3);
        WriteInt32(buf, ref off, ry3);

        // P4
        WriteUInt16(buf, ref off, t4);
        WriteUInt64(buf, ref off, k4);
        WriteInt32(buf, ref off, lx4);
        WriteInt32(buf, ref off, ly4);
        WriteInt32(buf, ref off, rx4);
        WriteInt32(buf, ref off, ry4);

        return buf;
    }

    // Little-endian writers
    private void WriteUInt16(byte[] buf, ref int off, ushort v)
    {
        buf[off++] = (byte)(v & 0xFF);
        buf[off++] = (byte)((v >> 8) & 0xFF);
    }
    private void WriteUInt64(byte[] buf, ref int off, ulong v)
    {
        for (int i = 0; i < 8; i++)
        {
            buf[off++] = (byte)(v & 0xFF);
            v >>= 8;
        }
    }
    private void WriteInt32(byte[] buf, ref int off, int v)
    {
        uint u = (uint)v;
        buf[off++] = (byte)(u & 0xFF);
        buf[off++] = (byte)((u >> 8) & 0xFF);
        buf[off++] = (byte)((u >> 16) & 0xFF);
        buf[off++] = (byte)((u >> 24) & 0xFF);
    }

    private void PollLoop()
    {
        // Controller types: all Pro Controller (1)
        ushort[] conTypes = { 1, 1, 1, 1 };

        // Detect controller count once at start
        int count = 0;
        var tmpState = new XINPUT_STATE();
        for (int i = 0; i < 4; i++)
        {
            if (XInput.GetState(i, ref tmpState) == ERROR_SUCCESS)
                count++;
        }
        if (count < 1) count = 1;

        try
        {
            while (running)
            {
                var p1 = ReadPad(0);
                var p2 = ReadPad(1);
                var p3 = ReadPad(2);
                var p4 = ReadPad(3);

                byte[] packet = BuildPacket(count,
                    conTypes[0], p1.keys, p1.lx, p1.ly, p1.rx, p1.ry,
                    conTypes[1], p2.keys, p2.lx, p2.ly, p2.rx, p2.ry,
                    conTypes[2], p3.keys, p3.lx, p3.ly, p3.rx, p3.ry,
                    conTypes[3], p4.keys, p4.lx, p4.ly, p4.rx, p4.ry);

                try
                {
                    udp.Send(packet, packet.Length, endpoint);
                }
                catch { }

                Thread.Sleep(16); // ~60 Hz
            }
        }
        catch (Exception) { }
    }

    private void MainForm_Closing(object sender, FormClosingEventArgs e)
    {
        if (running)
            StopSending();
    }
}

// ---------------------------------------------------------------------------
// Entry Point
// ---------------------------------------------------------------------------

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
