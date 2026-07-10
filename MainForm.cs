using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MixTray;

public class MainForm : Form
{
    private static readonly string LogFilePath =
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "MixTray.log");
    private static readonly string LogLock = "";

    private NotifyIcon _trayIcon;
    private ContextMenuStrip _contextMenu = null!;
    private System.Windows.Forms.Timer _pollTimer = null!;
    private System.Windows.Forms.Timer _blinkTimer = null!;
    private Icon? _currentIcon;
    private int _battery = -1;
    private bool _charging;
    private bool _showDimmed;
    private IBatteryReader? _reader;
    private Config _config;
    private ToolStripMenuItem _schemeSubmenu = null!;
    private ToolStripMenuItem _freqSubmenu = null!;

    private static readonly string[] SchemeNames = ["InzoneBatteryTray（ClrMD）", "InzoneUsbTray（USB CDC）"];

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public MainForm(Config config)
    {
        _config = config;
        Log($"App started, scheme={_config.SchemeIndex}, freq={Config.IntervalValues[_config.RefreshIntervalIndex]}ms");

        BuildContextMenu();
        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Visible = true,
            Text = "INZONE H9: initializing..."
        };
        SetTrayIcon(MakeIcon(-1));

        _blinkTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _blinkTimer.Tick += (_, _) =>
        {
            _showDimmed = !_showDimmed;
            SetTrayIcon(MakeIcon(_battery, _charging, _showDimmed));
        };

        _pollTimer = new System.Windows.Forms.Timer { Interval = Config.IntervalValues[_config.RefreshIntervalIndex] };
        _pollTimer.Tick += (_, _) => ReadBattery();
        _pollTimer.Start();

        SwitchReader(_config.SchemeIndex);

        FormClosing += (_, _) =>
        {
            _reader?.Dispose();
            Log("App closed");
        };
    }

    private void BuildContextMenu()
    {
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add("Refresh Now", null, (_, _) => ReadBattery());
        _contextMenu.Items.Add("Scan COM Port", null, (_, _) => { UsbCdcReader.ClearCache(); ReadBattery(); });
        _contextMenu.Items.Add(new ToolStripSeparator());

        _schemeSubmenu = new ToolStripMenuItem("方案切换");
        for (int i = 0; i < SchemeNames.Length; i++)
        {
            int idx = i;
            var item = new ToolStripMenuItem(SchemeNames[i], null, (_, _) => SwitchReader(idx))
            {
                Checked = i == _config.SchemeIndex
            };
            _schemeSubmenu.DropDownItems.Add(item);
        }
        _contextMenu.Items.Add(_schemeSubmenu);

        _freqSubmenu = new ToolStripMenuItem("刷新频率");
        for (int i = 0; i < Config.IntervalLabels.Length; i++)
        {
            int idx = i;
            var item = new ToolStripMenuItem(Config.IntervalLabels[i], null, (_, _) => SetFrequency(idx))
            {
                Checked = i == _config.RefreshIntervalIndex
            };
            _freqSubmenu.DropDownItems.Add(item);
        }
        _contextMenu.Items.Add(_freqSubmenu);

        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, (_, _) => ExitApp());
    }

    private void SwitchReader(int schemeIndex)
    {
        _config.SchemeIndex = schemeIndex;
        _config.Save();

        _reader?.Dispose();
        _reader = schemeIndex == 0 ? new ClrMdReader() : new UsbCdcReader();

        for (int i = 0; i < _schemeSubmenu.DropDownItems.Count; i++)
            ((ToolStripMenuItem)_schemeSubmenu.DropDownItems[i]).Checked = i == schemeIndex;

        Log($"Switched to scheme {schemeIndex} ({SchemeNames[schemeIndex]})");
        ReadBattery();
    }

    private void SetFrequency(int index)
    {
        _config.RefreshIntervalIndex = index;
        _config.Save();
        _pollTimer.Interval = Config.IntervalValues[index];

        for (int i = 0; i < _freqSubmenu.DropDownItems.Count; i++)
            ((ToolStripMenuItem)_freqSubmenu.DropDownItems[i]).Checked = i == index;

        Log($"Frequency changed to {Config.IntervalLabels[index]}");
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated)
        {
            value = false;
            CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    private void ReadBattery()
    {
        _battery = -1;
        var oldCharging = _charging;

        try
        {
            if (_reader != null)
            {
                var (bat, chg) = _reader.Read();
                _battery = bat;
                _charging = chg;
            }
        }
        catch (Exception ex) { Log($"Error: {ex.Message}"); }

        Log(_battery >= 0
            ? $"battery={_battery} method={(_reader?.Method ?? "none")}{( _charging ? " CHG" : "")}"
            : "battery=-- method=none");

        if (_battery < 0)
            _pollTimer.Interval = 2000;
        else
            _pollTimer.Interval = Config.IntervalValues[_config.RefreshIntervalIndex];

        UpdateTray();

        if (_charging && !_blinkTimer.Enabled)
        {
            _showDimmed = false;
            _blinkTimer.Start();
        }
        else if (!_charging && _blinkTimer.Enabled)
        {
            _blinkTimer.Stop();
            SetTrayIcon(MakeIcon(_battery, false, false));
        }
    }

    private void UpdateTray()
    {
        string method = _reader?.Method ?? "none";
        string chg = _charging ? " CHG" : "";
        string text = _battery >= 0
            ? $"INZONE H9: {_battery}%{chg} (via {method})"
            : "INZONE H9: Not detected";
        _trayIcon.Text = text.Length > 128 ? text[..128] : text;
        SetTrayIcon(MakeIcon(_battery, _charging, false));
    }

    private void SetTrayIcon(Icon icon)
    {
        if (_currentIcon != null)
        {
            DestroyIcon(_currentIcon.Handle);
            _currentIcon.Dispose();
        }
        _currentIcon = icon;
        _trayIcon.Icon = icon;
    }

    private static (Color bg, Color fg) IconColors(int battery) => battery switch
    {
        < 0 => (Color.FromArgb(0x75, 0x75, 0x75), Color.White),
        < 50 => (Color.FromArgb(0xC6, 0x28, 0x28), Color.White),
        < 70 => (Color.FromArgb(0xA0, 0x63, 0x00), Color.FromArgb(0x1A, 0x0F, 0x00)),
        _ => (Color.FromArgb(0x1B, 0x5E, 0x20), Color.White)
    };

    private static Icon MakeIcon(int battery, bool charging = false, bool dimmed = false)
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        var (bg, fg) = IconColors(battery);

        if (charging && dimmed)
            bg = Color.FromArgb(Math.Min(bg.R + 80, 255), Math.Min(bg.G + 80, 255), Math.Min(bg.B + 80, 255));

        using (var brush = new SolidBrush(bg))
            g.FillRectangle(brush, 0, 0, 32, 32);

        if (battery >= 0)
        {
            string text = battery.ToString();
            float fontSize = text.Length >= 3 ? 10f : 16f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold);
            var size = g.MeasureString(text, font);
            using var brush = new SolidBrush(fg);
            g.DrawString(text, font, brush,
                (32 - size.Width) / 2, (32 - size.Height) / 2);
        }
        else
        {
            using var font = new Font("Segoe UI", 10, FontStyle.Bold);
            using var brush = new SolidBrush(fg);
            g.DrawString("--", font, brush, 5, 6);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    private static void Log(string message)
    {
        lock (LogLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using var w = new StreamWriter(LogFilePath, append: true);
                w.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            }
            catch { }
        }
    }

    private void ExitApp()
    {
        _pollTimer.Stop();
        _blinkTimer.Stop();
        _trayIcon.Visible = false;
        _reader?.Dispose();
        if (_currentIcon != null)
        {
            DestroyIcon(_currentIcon.Handle);
            _currentIcon.Dispose();
        }
        Application.Exit();
    }
}
