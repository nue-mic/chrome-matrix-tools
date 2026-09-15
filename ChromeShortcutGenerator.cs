// -*- coding: utf-8 -*-
// Chrome 多开快捷方式生成器 (现代卡片式 GUI + 模板克隆 + 桌面入口)
// 功能:
//   1) 批量生成指向 chrome.exe --user-data-dir="<数据目录>\<前缀N>" 的 .lnk 快捷方式。
//   2) 可选"使用模板": 把配置好的模板 UserData 整套复制到每个分身目录。
//   3) 可选"桌面入口": 生成后在桌面创建一个指向快捷方式文件夹的快捷方式(同名覆盖)。
// 界面: 无边框自定义标题栏 + 自绘圆角卡片, 强调色 #6D5EF6。
// 编译: 见 build.bat (Windows 自带 csc.exe, 零依赖单文件 exe)。
// 语法等级: C# 5 (兼容 .NET Framework 4 自带的 csc.exe)。
// 注意: NetFx4 下单文件全路径 >260 字符会跳过(计入"未复制"告警); 默认排除缓存已大幅降低概率。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ChromeShortcutGenerator
{
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

    // ---------- 主题配色 ----------
    static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(0x6D, 0x5E, 0xF6);
        public static readonly Color AccentHover = Color.FromArgb(0x5B, 0x4F, 0xE0);
        public static readonly Color AccentLight = Color.FromArgb(0xEE, 0xEB, 0xFF);
        public static readonly Color BgWindow = Color.FromArgb(0xF4, 0xF5, 0xFA);
        public static readonly Color CardBg = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(0xE5, 0xE7, 0xEB);
        public static readonly Color TextPrimary = Color.FromArgb(0x1F, 0x29, 0x37);
        public static readonly Color TextSecondary = Color.FromArgb(0x6B, 0x72, 0x80);
        public static readonly Color Track = Color.FromArgb(0xE5, 0xE7, 0xEB);
        public static readonly Color CloseHover = Color.FromArgb(0xE8, 0x11, 0x23);

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d < 2) d = 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ---------- 自绘圆角卡片 ----------
    class CardPanel : Panel
    {
        public CardPanel()
        {
            this.BackColor = Theme.BgWindow;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw, true); // 缩放时整块重绘, 消除圆角残影
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = Theme.Rounded(r, 10))
            using (SolidBrush b = new SolidBrush(Theme.CardBg))
            using (Pen p = new Pen(Theme.CardBorder))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
        }
    }

    // ---------- 自绘药丸按钮 (主按钮/描边按钮) ----------
    class PillButton : Button
    {
        public bool Primary = true;
        public bool Danger = false; // 危险操作: 红色填充 (如"清空重建")
        private bool hover = false;
        public PillButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.BackColor = Theme.BgWindow;
            this.Cursor = Cursors.Hand;
            this.MouseEnter += delegate { hover = true; Invalidate(); };
            this.MouseLeave += delegate { hover = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(this.BackColor);
            Rectangle r = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            int radius = Math.Min(this.Height / 2, 12);
            Color fill, txt, border;
            if (!this.Enabled) { fill = Color.FromArgb(0xCB, 0xD0, 0xDA); txt = Color.White; border = fill; }
            else if (Danger) { fill = hover ? Color.FromArgb(0xC4, 0x0E, 0x1E) : Theme.CloseHover; txt = Color.White; border = fill; }
            else if (Primary) { fill = hover ? Theme.AccentHover : Theme.Accent; txt = Color.White; border = fill; }
            else { fill = hover ? Theme.AccentLight : Color.White; txt = Theme.Accent; border = Theme.Accent; }
            using (GraphicsPath path = Theme.Rounded(r, radius))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                if (!Primary) using (Pen p = new Pen(border)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, this.Text, this.Font, r, txt,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // ---------- 标题栏 最小化/关闭 按钮 ----------
    class CaptionButton : Button
    {
        public bool IsClose = false;
        private bool hover = false;
        public CaptionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.BackColor = Theme.BgWindow;
            this.TabStop = false;
            this.MouseEnter += delegate { hover = true; Invalidate(); };
            this.MouseLeave += delegate { hover = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = this.BackColor;
            if (hover) bg = IsClose ? Theme.CloseHover : Theme.CardBorder;
            g.Clear(bg);
            Color fg = (IsClose && hover) ? Color.White : Theme.TextSecondary;
            int cx = this.Width / 2, cy = this.Height / 2;
            using (Pen p = new Pen(fg, 1.4f))
            {
                if (IsClose)
                {
                    g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5);
                    g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5);
                }
                else
                {
                    g.DrawLine(p, cx - 5, cy + 4, cx + 5, cy + 4);
                }
            }
        }
    }

    // ---------- 自绘扁平进度条 ----------
    class FlatProgress : Control
    {
        private int _min = 0, _max = 100, _val = 0;
        public int Minimum { get { return _min; } set { _min = value; Invalidate(); } }
        public int Maximum { get { return _max; } set { _max = value; Invalidate(); } }
        public int Value { get { return _val; } set { _val = value; Invalidate(); } }
        public FlatProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            this.BackColor = Theme.BgWindow;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(this.BackColor);
            int h = 8;
            Rectangle r = new Rectangle(0, (Height - h) / 2, Width - 1, h);
            using (GraphicsPath track = Theme.Rounded(r, 4))
            using (SolidBrush tb = new SolidBrush(Theme.Track)) g.FillPath(tb, track);
            int span = _max - _min; if (span <= 0) span = 1;
            long w = (long)(_val - _min) * r.Width / span;
            if (w < 0) w = 0; if (w > r.Width) w = r.Width;
            if (w >= 2)
            {
                Rectangle fr = new Rectangle(r.X, r.Y, (int)w, r.Height);
                using (GraphicsPath fp = Theme.Rounded(fr, 4))
                using (SolidBrush fb = new SolidBrush(Theme.Accent)) g.FillPath(fb, fp);
            }
        }
    }

    // ---------- 通用选择对话框 (主题风格, 竖排按钮; 返回点击索引, -1=取消/关闭) ----------
    class ChoiceDialog : Form
    {
        private int _result = -1;
        public int ChosenIndex { get { return _result; } }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; } // CS_DROPSHADOW
        }

        private ChoiceDialog(Form owner, string title, string message, string[] buttons, int dangerIndex)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Theme.CardBg;
            this.ShowInTaskbar = false;
            this.MaximizeBox = false; this.MinimizeBox = false;
            this.KeyPreview = true;
            this.DoubleBuffered = true;
            this.Font = owner.Font;

            const int W = 460, pad = 22;
            int y = pad;

            Label lblTitle = new Label();
            lblTitle.Text = title; lblTitle.AutoSize = true;
            lblTitle.Font = new Font(this.Font.FontFamily, 11.5F, FontStyle.Bold);
            lblTitle.ForeColor = Theme.Accent;
            lblTitle.BackColor = Theme.CardBg;
            lblTitle.Location = new Point(pad, y);
            this.Controls.Add(lblTitle);
            y += lblTitle.PreferredHeight + 12;

            Label lblMsg = new Label();
            lblMsg.Text = message;
            lblMsg.AutoSize = false;
            lblMsg.ForeColor = Theme.TextPrimary;
            lblMsg.BackColor = Theme.CardBg;
            Size msgSize = TextRenderer.MeasureText(message, this.Font,
                new Size(W - pad * 2, 0), TextFormatFlags.WordBreak);
            lblMsg.SetBounds(pad, y, W - pad * 2, msgSize.Height + 4);
            this.Controls.Add(lblMsg);
            y += lblMsg.Height + 18;

            for (int i = 0; i < buttons.Length; i++)
            {
                int idx = i;
                PillButton b = new PillButton();
                b.Text = buttons[i];
                b.Primary = (i == 0);            // 第一个=推荐主操作
                b.Danger = (i == dangerIndex);   // 危险操作=红色
                b.BackColor = Theme.CardBg;
                b.SetBounds(pad, y, W - pad * 2, 36);
                b.Click += delegate { _result = idx; this.Close(); };
                this.Controls.Add(b);
                y += 36 + 10;
            }

            this.ClientSize = new Size(W, (y - 10) + pad);

            this.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { _result = -1; this.Close(); }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.CardBorder))
                e.Graphics.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
        }

        public static int Show(Form owner, string title, string message, string[] buttons, int dangerIndex)
        {
            using (ChoiceDialog d = new ChoiceDialog(owner, title, message, buttons, dangerIndex))
            {
                d.ShowDialog(owner);
                return d.ChosenIndex;
            }
        }
    }

    // ---------- 配置快照: 界面上所有可保存的字段 ----------
    public class ConfigSnapshot
    {
        public string BaseDir { get; set; }
        public string Name { get; set; }
        public string ChromePath { get; set; }
        public string UserDataDir { get; set; }
        public string ShortcutDir { get; set; }
        public string TemplateDir { get; set; }
        public string Prefix { get; set; }
        public string DesktopName { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public bool Overwrite { get; set; }
        public bool UseTemplate { get; set; }
        public bool ExcludeCache { get; set; }
        public bool CreateDesktop { get; set; }
    }

    // ---------- 一条历史记录: 某次成功生成时的配置快照 ----------
    public class HistoryEntry
    {
        public string Time { get; set; }          // yyyy-MM-dd HH:mm
        public int Created { get; set; }          // 本次新建的快捷方式数
        public ConfigSnapshot Config { get; set; }

        // 列表里显示的一行摘要
        public string Summary()
        {
            if (Config == null) return Time;
            int n = Config.End - Config.Start + 1;
            string s = Time + "   " + Config.Prefix + Config.Start + " ~ " + Config.Prefix + Config.End
                     + "  (" + n + " 个";
            if (Created > 0) s += ", 新建 " + Created;
            s += ")";
            if (Config.UseTemplate) s += "   [模板]";
            s += "   " + Config.UserDataDir;
            return s;
        }
    }

    // ---------- 落盘的全部内容 ----------
    public class AppSettings
    {
        public ConfigSnapshot Current { get; set; }
        public List<HistoryEntry> History { get; set; }

        public void AddHistory(ConfigSnapshot c, int created)
        {
            if (c == null) return;
            if (History == null) History = new List<HistoryEntry>();
            HistoryEntry h = new HistoryEntry();
            h.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            h.Created = created;
            h.Config = c;
            History.Insert(0, h);                       // 最新的排最前
            while (History.Count > ConfigStore.MaxHistory)
                History.RemoveAt(History.Count - 1);
        }
    }

    // ---------- 配置读写 (%APPDATA%\ChromeMatrixTools\settings.json) ----------
    // 读写失败一律静默忽略: 配置只是便利功能, 绝不能影响主流程。
    static class ConfigStore
    {
        public const int MaxHistory = 20;

        public static string FilePath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ChromeMatrixTools");
            return Path.Combine(dir, "settings.json");
        }

        public static AppSettings Load()
        {
            try
            {
                string p = FilePath();
                if (!File.Exists(p)) return new AppSettings();
                string json = File.ReadAllText(p, Encoding.UTF8);
                if (json.Trim().Length == 0) return new AppSettings();
                AppSettings s = JsonSerializer.Deserialize<AppSettings>(json);
                return s != null ? s : new AppSettings();
            }
            catch { return new AppSettings(); }   // 文件损坏/被手改坏了也要能正常启动
        }

        public static void Save(AppSettings s)
        {
            try
            {
                string p = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                JsonSerializerOptions o = new JsonSerializerOptions();
                o.WriteIndented = true;
                // 中文路径不转成 \uXXXX, 方便用记事本直接看
                o.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
                File.WriteAllText(p, JsonSerializer.Serialize(s, o), Encoding.UTF8);
            }
            catch { }
        }
    }

    // ---------- 历史记录对话框 (双击或“套用”把该条配置填回界面) ----------
    class HistoryDialog : Form
    {
        private ListBox _list;
        private List<HistoryEntry> _items;
        private ConfigSnapshot _chosen;
        private string _chosenTime = "";
        private bool _changed;

        public ConfigSnapshot Chosen { get { return _chosen; } }
        public string ChosenTime { get { return _chosenTime; } }
        public bool Changed { get { return _changed; } }   // 删过条目, 需要回写文件

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }
        }

        public HistoryDialog(Form owner, List<HistoryEntry> items)
        {
            _items = items;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Theme.CardBg;
            this.ShowInTaskbar = false;
            this.MaximizeBox = false; this.MinimizeBox = false;
            this.KeyPreview = true;
            this.DoubleBuffered = true;
            this.Font = owner.Font;

            const int W = 760, pad = 22;
            int y = pad;

            Label title = new Label();
            title.Text = "历史记录"; title.AutoSize = true;
            title.Font = new Font(this.Font.FontFamily, 11.5F, FontStyle.Bold);
            title.ForeColor = Theme.Accent; title.BackColor = Theme.CardBg;
            title.Location = new Point(pad, y);
            this.Controls.Add(title);
            y += title.PreferredHeight + 6;

            Label hint = new Label();
            hint.Text = "选中一条后点『套用』, 把当时的配置整套填回界面 (双击列表同样可套用)。";
            hint.AutoSize = true;
            hint.ForeColor = Theme.TextSecondary; hint.BackColor = Theme.CardBg;
            hint.Location = new Point(pad, y);
            this.Controls.Add(hint);
            y += hint.PreferredHeight + 10;

            _list = new ListBox();
            _list.SetBounds(pad, y, W - pad * 2, 268);
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.ForeColor = Theme.TextPrimary;
            _list.HorizontalScrollbar = true;
            _list.IntegralHeight = false;
            _list.DoubleClick += delegate { Apply(); };
            this.Controls.Add(_list);
            y += _list.Height + 14;

            Reload();

            int bw = 116, bx = pad;
            PillButton bApply = new PillButton();
            bApply.Text = "套用"; bApply.Primary = true; bApply.BackColor = Theme.CardBg;
            bApply.SetBounds(bx, y, bw, 34);
            bApply.Click += delegate { Apply(); };
            this.Controls.Add(bApply); bx += bw + 10;

            PillButton bDel = new PillButton();
            bDel.Text = "删除选中"; bDel.BackColor = Theme.CardBg;
            bDel.SetBounds(bx, y, bw, 34);
            bDel.Click += delegate { DeleteSelected(); };
            this.Controls.Add(bDel); bx += bw + 10;

            PillButton bClear = new PillButton();
            bClear.Text = "清空全部"; bClear.Danger = true; bClear.BackColor = Theme.CardBg;
            bClear.SetBounds(bx, y, bw, 34);
            bClear.Click += delegate { ClearAll(); };
            this.Controls.Add(bClear);

            PillButton bClose = new PillButton();
            bClose.Text = "关闭"; bClose.BackColor = Theme.CardBg;
            bClose.SetBounds(W - pad - bw, y, bw, 34);
            bClose.Click += delegate { this.Close(); };
            this.Controls.Add(bClose);

            this.ClientSize = new Size(W, y + 34 + pad);

            this.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) this.Close();
            };
        }

        private void Reload()
        {
            _list.Items.Clear();
            foreach (HistoryEntry h in _items) _list.Items.Add(h.Summary());
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void Apply()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _items.Count) return;
            _chosen = _items[i].Config;
            _chosenTime = _items[i].Time;
            this.Close();
        }

        private void DeleteSelected()
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _items.Count) return;
            _items.RemoveAt(i);
            _changed = true;
            Reload();
        }

        private void ClearAll()
        {
            if (_items.Count == 0) return;
            if (MessageBox.Show(this, "确定清空全部 " + _items.Count + " 条历史记录?", "清空历史",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _items.Clear();
            _changed = true;
            Reload();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.CardBorder))
                e.Graphics.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
        }
    }

    public class MainForm : Form
    {
        private const int MaxCount = 10000;

        // 默认配置 ("恢复默认"按钮还原到这一组)
        private const string DefaultBaseDir = @"F:\Chrome_Matrix_Browser";
        private const string DefaultName = "Github";
        private const string DefaultDesktopName = "Github集合";
        private const int DefaultStart = 1;
        private const int DefaultEnd = 20;

        private static readonly string[] ExcludedFiles =
            { "SingletonLock", "SingletonCookie", "SingletonSocket", "lockfile" };
        private static readonly string[] CacheDirNames =
            { "Cache", "Code Cache", "GPUCache", "ShaderCache", "GrShaderCache",
              "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "GraphiteDawnCache" };

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        private TextBox txtBase;
        private TextBox txtName;
        private TextBox txtChrome;
        private TextBox txtUserData;
        private TextBox txtShortcut;
        private TextBox txtPrefix;
        private TextBox txtDesktop;
        private NumericUpDown numStart;
        private NumericUpDown numEnd;
        private RadioButton rbSkip;
        private RadioButton rbOverwrite;
        private CheckBox chkDesktop;
        private CheckBox chkUseTemplate;
        private TextBox txtTemplate;
        private Button btnBrowseTemplate;
        private CheckBox chkExcludeCache;
        private PillButton btnGenerate;
        private TextBox txtLog;
        private FlatProgress progress;
        private Label lblProgress;

        private bool busy = false;

        private AppSettings settings = new AppSettings();
        // 整批填值 (恢复配置/套用历史/恢复默认) 期间抑制"基本目录/名称"联动,
        // 否则手改过的自定义路径会被推导值冲掉。
        private bool suppressDerive = false;

        public MainForm()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw, true); // 整窗缩放重绘, 杜绝自绘控件残影
            BuildUi();
            SetDefaults();
            // 设默认值后再挂"基本目录/名称"联动 (避免初始化时误触发)
            txtBase.TextChanged += delegate { DeriveFromBaseName(); };
            txtName.TextChanged += delegate { DeriveFromBaseName(); };
            LoadSavedConfig();                // 有上次保存的配置就填回
            UpdateTemplateEnabled();
            this.ActiveControl = btnGenerate; // 避免启动时输入框默认全选高亮
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW: 给无边框窗口一点投影
                return cp;
            }
        }

        // ---------- 无边框窗口的边缘缩放 (鼠标移到四边/四角可拖拽改变大小) ----------
        private const int WM_NCHITTEST = 0x84;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
            HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        private const int ResizeEdge = 6; // 边缘可拖拽缩放的感应像素宽度

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m); // 先取默认命中结果 (通常 HTCLIENT)
                int lp = m.LParam.ToInt32();
                Point p = this.PointToClient(new Point((short)(lp & 0xFFFF), (short)(lp >> 16)));
                int w = this.ClientSize.Width, h = this.ClientSize.Height;
                bool l = p.X <= ResizeEdge, r = p.X >= w - ResizeEdge;
                bool t = p.Y <= ResizeEdge, b = p.Y >= h - ResizeEdge;
                if (t && l) m.Result = (IntPtr)HTTOPLEFT;
                else if (t && r) m.Result = (IntPtr)HTTOPRIGHT;
                else if (b && l) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (b && r) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (l) m.Result = (IntPtr)HTLEFT;
                else if (r) m.Result = (IntPtr)HTRIGHT;
                else if (t) m.Result = (IntPtr)HTTOP;
                else if (b) m.Result = (IntPtr)HTBOTTOM;
                return;
            }
            base.WndProc(ref m);
        }

        // ---------- 界面构建 ----------
        private void BuildUi()
        {
            this.Text = "Chrome 分身生成器";
            // 让运行中的窗口/任务栏/Alt-Tab 也显示自定义图标 (优先用同目录 app.ico 多尺寸更清晰, 否则用 exe 内嵌图标)
            try
            {
                string _icoDir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
                string _icoPath = System.IO.Path.Combine(_icoDir, "app.ico");
                this.Icon = System.IO.File.Exists(_icoPath)
                    ? new Icon(_icoPath)
                    : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { /* 取不到图标就用默认, 不影响运行 */ }
            this.Font = MakeUiFont(9F);
            this.ClientSize = new Size(720, 850);
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Theme.BgWindow;
            this.StartPosition = FormStartPosition.CenterScreen;

            BuildTitleBar();

            // 卡片1: 基础配置 (含 基本目录 / 名称 两个联动源)
            CardPanel c1 = MakeCard(16, 56, 688, 224);
            AddTitle(c1, "基础配置", 18, 12);
            // 标题行右侧: 历史记录 / 恢复默认 (锚右, 窄窗口下不会被截断)
            AddBrowse(c1, "历史记录", 500, 8, 88, delegate { OnShowHistory(); });
            AddBrowse(c1, "恢复默认", 596, 8, 88, delegate { OnResetDefaults(); });
            AddFieldLabel(c1, "基本目录", 18, 46);
            txtBase = AddInput(c1, 110, 42, 392);
            AddBrowse(c1, "浏览", 510, 42, 162, delegate { BrowseFolder(txtBase); });
            AddFieldLabel(c1, "名称", 18, 82);
            txtName = AddInput(c1, 110, 78, 150);
            AddHint(c1, "← 改 基本目录 / 名称，下面路径自动跟随（仍可手动改）", 272, 82);
            AddFieldLabel(c1, "Chrome 路径", 18, 118);
            txtChrome = AddInput(c1, 110, 114, 392);
            AddBrowse(c1, "浏览", 510, 114, 76, delegate { BrowseFile(txtChrome); });
            AddBrowse(c1, "探测", 592, 114, 80, delegate { DetectChromeToBox(); });
            AddFieldLabel(c1, "数据目录", 18, 154);
            txtUserData = AddInput(c1, 110, 150, 392);
            AddBrowse(c1, "浏览", 510, 150, 162, delegate { BrowseFolder(txtUserData); });
            AddFieldLabel(c1, "快捷方式目录", 18, 190);
            txtShortcut = AddInput(c1, 110, 186, 392);
            AddBrowse(c1, "浏览", 510, 186, 162, delegate { BrowseFolder(txtShortcut); });

            // 卡片2: 生成参数
            CardPanel c2 = MakeCard(16, 288, 688, 150);
            AddTitle(c2, "生成参数", 18, 12);
            AddFieldLabel(c2, "名称前缀", 18, 48);
            txtPrefix = AddInput(c2, 94, 44, 130);
            AddFieldLabel(c2, "起始", 238, 48);
            numStart = AddNumeric(c2, 278, 44, 64);
            AddFieldLabel(c2, "结束", 356, 48);
            numEnd = AddNumeric(c2, 396, 44, 64);
            AddFieldLabel(c2, "已存在时", 18, 84);
            rbSkip = AddRadio(c2, "跳过", 96, 82, 70); rbSkip.Checked = true;
            rbOverwrite = AddRadio(c2, "覆盖", 172, 82, 70);
            AddFieldLabel(c2, "桌面入口名称", 18, 118);
            txtDesktop = AddInput(c2, 118, 114, 150);
            chkDesktop = AddCheck(c2, "生成后在桌面创建该入口", 280, 116, 300); chkDesktop.Checked = true;

            // 卡片3: 模板(可选)
            CardPanel c3 = MakeCard(16, 446, 688, 150);
            AddTitle(c3, "模板（可选）", 18, 12);
            chkUseTemplate = AddCheck(c3, "使用模板", 18, 44, 90);
            chkUseTemplate.CheckedChanged += delegate { UpdateTemplateEnabled(); };
            txtTemplate = AddInput(c3, 110, 42, 392);
            btnBrowseTemplate = AddBrowse(c3, "浏览", 510, 42, 162, delegate { BrowseFolder(txtTemplate); });
            chkExcludeCache = AddCheck(c3, "排除缓存目录（复制更快、更省空间；登录态/扩展/设置保留）", 110, 74, 540);
            chkExcludeCache.Checked = true;

            // 模板制作工具: 一键创建模板(文件夹+快捷方式) / 直接打开模板做配置 / 打开所在文件夹
            PillButton btnCreateTpl = new PillButton();
            btnCreateTpl.Text = "一键创建模板"; btnCreateTpl.Primary = true; btnCreateTpl.BackColor = Theme.CardBg;
            btnCreateTpl.SetBounds(18, 106, 150, 28);
            btnCreateTpl.Click += delegate { OnCreateTemplate(); };
            c3.Controls.Add(btnCreateTpl);

            PillButton btnOpenTpl = new PillButton();
            btnOpenTpl.Text = "打开模板"; btnOpenTpl.Primary = false; btnOpenTpl.BackColor = Theme.CardBg;
            btnOpenTpl.SetBounds(180, 106, 120, 28);
            btnOpenTpl.Click += delegate { LaunchTemplate(true); };
            c3.Controls.Add(btnOpenTpl);

            PillButton btnOpenTplDir = new PillButton();
            btnOpenTplDir.Text = "打开模板文件夹"; btnOpenTplDir.Primary = false; btnOpenTplDir.BackColor = Theme.CardBg;
            btnOpenTplDir.SetBounds(312, 106, 150, 28);
            btnOpenTplDir.Click += delegate { OpenTemplateFolder(); };
            c3.Controls.Add(btnOpenTplDir);

            // 操作按钮
            btnGenerate = new PillButton();
            btnGenerate.Text = "一键生成";
            btnGenerate.Primary = true;
            btnGenerate.BackColor = Theme.BgWindow;
            btnGenerate.Font = new Font(this.Font.FontFamily, 10.5F, FontStyle.Bold);
            btnGenerate.SetBounds(16, 608, 200, 40);
            btnGenerate.Click += delegate { OnGenerate(); };
            this.Controls.Add(btnGenerate);

            PillButton btnOpen = new PillButton();
            btnOpen.Text = "打开输出文件夹"; btnOpen.Primary = false; btnOpen.BackColor = Theme.BgWindow;
            btnOpen.SetBounds(228, 608, 150, 40);
            btnOpen.Click += delegate { OpenOutputFolder(); };
            this.Controls.Add(btnOpen);

            PillButton btnDesktopBtn = new PillButton();
            btnDesktopBtn.Text = "建桌面入口"; btnDesktopBtn.Primary = false; btnDesktopBtn.BackColor = Theme.BgWindow;
            btnDesktopBtn.SetBounds(390, 608, 150, 40);
            btnDesktopBtn.Click += delegate { OnCreateDesktopEntry(); };
            this.Controls.Add(btnDesktopBtn);

            // 卡片4: 实时日志
            CardPanel c4 = MakeCard(16, 660, 688, 150);
            AddTitle(c4, "实时日志", 18, 10);
            txtLog = new TextBox();
            txtLog.SetBounds(16, 36, 656, 104);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.White;
            txtLog.ForeColor = Theme.TextPrimary;
            c4.Controls.Add(txtLog);

            // 进度
            progress = new FlatProgress();
            progress.SetBounds(16, 820, 540, 18);
            this.Controls.Add(progress);

            lblProgress = new Label();
            lblProgress.SetBounds(566, 819, 140, 20);
            lblProgress.Text = "就绪";
            lblProgress.ForeColor = Theme.TextSecondary;
            lblProgress.BackColor = Theme.BgWindow;
            lblProgress.TextAlign = ContentAlignment.MiddleRight;
            this.Controls.Add(lblProgress);

            // ---------- 响应式布局: 锚定各控件, 跟随窗口缩放自动重排 ----------
            // 三张配置卡横向拉伸; 日志卡四向拉伸(吃掉多余高度)
            foreach (CardPanel card in new CardPanel[] { c1, c2, c3 })
                card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            c4.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            // 路径类宽输入框随卡片横向拉伸 (浏览按钮已在 AddBrowse 里锚定到右缘)
            foreach (TextBox tb in new TextBox[] { txtBase, txtChrome, txtUserData, txtShortcut, txtTemplate })
                tb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            // 日志框填满日志卡; 进度条/状态贴底
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            progress.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblProgress.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            // 默认把窗口开大一些(锚定生效后会自动把卡片/输入框横向撑开), 并设定可缩放的最小尺寸
            this.MinimumSize = new Size(720, 770);
            this.ClientSize = new Size(944, 874);
        }

        private void BuildTitleBar()
        {
            Panel tp = new Panel();
            tp.SetBounds(0, 0, this.ClientSize.Width, 48);
            tp.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; // 顶栏随窗口占满宽度
            tp.BackColor = Theme.BgWindow;
            tp.Resize += delegate { tp.Invalidate(); }; // 顶栏变宽时整条重绘, 防止按钮移动残影
            tp.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // logo 徽章
                Rectangle badge = new Rectangle(16, 11, 26, 26);
                using (GraphicsPath bp = Theme.Rounded(badge, 7))
                using (SolidBrush bb = new SolidBrush(Theme.Accent)) g.FillPath(bb, bp);
                using (Font lf = new Font(this.Font.FontFamily, 12F, FontStyle.Bold))
                    TextRenderer.DrawText(g, "C", lf, badge, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                // 标题 + 版本 (动态测量标题宽度, 避免版本号贴在一起)
                string titleText = "Chrome 分身生成器";
                using (Font tf = new Font(this.Font.FontFamily, 11F, FontStyle.Bold))
                using (Font vf = new Font(this.Font.FontFamily, 8.5F))
                {
                    TextRenderer.DrawText(g, titleText, tf, new Point(52, 13), Theme.TextPrimary);
                    Size ts = TextRenderer.MeasureText(g, titleText, tf);
                    TextRenderer.DrawText(g, "v2.3", vf,
                        new Point(52 + ts.Width + 2, 17), Theme.TextSecondary);
                }
            };
            tp.MouseDown += TitleDrag;
            this.Controls.Add(tp);

            CaptionButton btnClose = new CaptionButton();
            btnClose.IsClose = true;
            btnClose.SetBounds(this.ClientSize.Width - 44, 8, 36, 30);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Click += delegate { this.Close(); };
            tp.Controls.Add(btnClose);

            CaptionButton btnMin = new CaptionButton();
            btnMin.SetBounds(this.ClientSize.Width - 84, 8, 36, 30);
            btnMin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMin.Click += delegate { this.WindowState = FormWindowState.Minimized; };
            tp.Controls.Add(btnMin);
        }

        private void TitleDrag(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        // ---------- 控件工厂 ----------
        private static Font MakeUiFont(float size)
        {
            string[] prefs = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑" };
            foreach (string fam in prefs)
            {
                try { using (new FontFamily(fam)) { } return new Font(fam, size); }
                catch { }
            }
            return new Font(SystemFonts.MessageBoxFont.FontFamily, size);
        }

        private CardPanel MakeCard(int x, int y, int w, int h)
        {
            CardPanel c = new CardPanel();
            c.SetBounds(x, y, w, h);
            this.Controls.Add(c);
            return c;
        }

        private void AddTitle(Control parent, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text; l.AutoSize = true;
            l.Location = new Point(x, y);
            l.Font = new Font(this.Font.FontFamily, 10F, FontStyle.Bold);
            l.ForeColor = Theme.Accent;
            l.BackColor = Theme.CardBg;
            parent.Controls.Add(l);
        }

        private void AddFieldLabel(Control parent, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text; l.AutoSize = true;
            l.Location = new Point(x, y);
            l.ForeColor = Theme.TextPrimary;
            l.BackColor = Theme.CardBg;
            parent.Controls.Add(l);
        }

        private void AddHint(Control parent, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text; l.AutoSize = true;
            l.Location = new Point(x, y);
            l.ForeColor = Theme.TextSecondary;
            l.BackColor = Theme.CardBg;
            parent.Controls.Add(l);
        }

        // 基本目录 / 名称 改动时, 推导下面的路径 (加载完成后才挂此事件, 不会冲掉已有配置)
        private void DeriveFromBaseName()
        {
            if (suppressDerive) return;   // 整批填值期间不推导
            string baseDir = txtBase.Text.Trim();
            string name = txtName.Text.Trim();
            if (baseDir.Length == 0 || name.Length == 0) return;
            if (HasInvalidNameChar(name)) return; // 名称含非法字符时本次不推导
            if (baseDir.Length == 2 && baseDir[1] == ':') baseDir += "\\"; // 裸盘符补斜杠
            string multi = name + "_Multiple";
            try
            {
                txtUserData.Text = Path.Combine(baseDir, multi, name + "_UserData");
                txtShortcut.Text = Path.Combine(baseDir, multi, name + "_ShortCuts");
                txtTemplate.Text = Path.Combine(baseDir, multi, name + "_Template_UserData");
                txtPrefix.Text = name + "_";
            }
            catch { /* 非法路径片段, 忽略本次推导 */ }
        }


        private TextBox AddInput(Control parent, int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.SetBounds(x, y, w, 26);
            t.BorderStyle = BorderStyle.FixedSingle;
            t.ForeColor = Theme.TextPrimary;
            parent.Controls.Add(t);
            return t;
        }

        private PillButton AddBrowse(Control parent, string text, int x, int y, int w, EventHandler onClick)
        {
            PillButton b = new PillButton();
            b.Text = text; b.Primary = false; b.BackColor = Theme.CardBg;
            b.SetBounds(x, y, w, 26);
            b.Anchor = AnchorStyles.Top | AnchorStyles.Right; // 浏览/探测按钮始终贴卡片右缘
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        private NumericUpDown AddNumeric(Control parent, int x, int y, int w)
        {
            NumericUpDown n = new NumericUpDown();
            n.SetBounds(x, y, w, 26);
            n.Minimum = 1; n.Maximum = MaxCount;
            n.BorderStyle = BorderStyle.FixedSingle;
            parent.Controls.Add(n);
            return n;
        }

        private RadioButton AddRadio(Control parent, string text, int x, int y, int w)
        {
            RadioButton r = new RadioButton();
            r.Text = text; r.SetBounds(x, y, w, 24);
            r.ForeColor = Theme.TextPrimary;
            r.BackColor = Theme.CardBg;
            parent.Controls.Add(r);
            return r;
        }

        private CheckBox AddCheck(Control parent, string text, int x, int y, int w)
        {
            CheckBox c = new CheckBox();
            c.Text = text; c.SetBounds(x, y, w, 24);
            c.ForeColor = Theme.TextPrimary;
            c.BackColor = Theme.CardBg;
            parent.Controls.Add(c);
            return c;
        }

        private void UpdateTemplateEnabled()
        {
            // 模板路径框/浏览/制作按钮始终可用 (要先做好模板, 再勾"使用模板");
            // "排除缓存"只在复制时生效, 故仅它跟随"使用模板"。
            txtTemplate.Enabled = true;
            btnBrowseTemplate.Enabled = true;
            chkExcludeCache.Enabled = chkUseTemplate.Checked;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                e.Cancel = true;
                MessageBox.Show(this, "正在生成中, 请等待完成后再关闭。", "请稍候",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SaveCurrentConfig();   // 关窗时留存当前配置, 下次启动填回
            base.OnFormClosing(e);
        }

        // ---------- 默认值 (写死在 exe 中; "恢复默认"按钮还原到这一组) ----------
        private void SetDefaults()
        {
            string baseDir = DefaultBaseDir;
            string name = DefaultName;
            txtBase.Text = baseDir;
            txtName.Text = name;
            txtChrome.Text = DetectChrome();
            txtUserData.Text = Path.Combine(baseDir, name + "_Multiple", name + "_UserData");
            txtShortcut.Text = Path.Combine(baseDir, name + "_Multiple", name + "_ShortCuts");
            txtTemplate.Text = Path.Combine(baseDir, name + "_Multiple", name + "_Template_UserData");
            txtPrefix.Text = name + "_";
            txtDesktop.Text = DefaultDesktopName;
            numStart.Value = DefaultStart;
            numEnd.Value = DefaultEnd;
            rbSkip.Checked = true;
            rbOverwrite.Checked = false;
            chkUseTemplate.Checked = false;
            chkExcludeCache.Checked = true;
            chkDesktop.Checked = true;
        }

        // ---------- 配置留存 / 恢复默认 / 历史记录 ----------

        // 启动时: 有上次保存的配置就整套填回
        private void LoadSavedConfig()
        {
            settings = ConfigStore.Load();
            if (settings.Current == null) return;
            ApplySnapshot(settings.Current);
            Log("已恢复上次的配置 (点『恢复默认』可还原)。");
        }

        // 把界面上的当前配置收成一个快照
        private ConfigSnapshot CollectSnapshot()
        {
            ConfigSnapshot c = new ConfigSnapshot();
            c.BaseDir = txtBase.Text;
            c.Name = txtName.Text;
            c.ChromePath = txtChrome.Text;
            c.UserDataDir = txtUserData.Text;
            c.ShortcutDir = txtShortcut.Text;
            c.TemplateDir = txtTemplate.Text;
            c.Prefix = txtPrefix.Text;
            c.DesktopName = txtDesktop.Text;
            c.Start = (int)numStart.Value;
            c.End = (int)numEnd.Value;
            c.Overwrite = rbOverwrite.Checked;
            c.UseTemplate = chkUseTemplate.Checked;
            c.ExcludeCache = chkExcludeCache.Checked;
            c.CreateDesktop = chkDesktop.Checked;
            return c;
        }

        // 把一个快照填回界面 (期间抑制联动, 保住手改过的自定义路径)
        private void ApplySnapshot(ConfigSnapshot c)
        {
            if (c == null) return;
            suppressDerive = true;
            try
            {
                if (c.BaseDir != null) txtBase.Text = c.BaseDir;
                if (c.Name != null) txtName.Text = c.Name;
                // Chrome 路径若已失效(换机器/重装), 回退到重新探测
                if (!string.IsNullOrEmpty(c.ChromePath) && File.Exists(c.ChromePath))
                    txtChrome.Text = c.ChromePath;
                else
                    txtChrome.Text = DetectChrome();
                if (c.UserDataDir != null) txtUserData.Text = c.UserDataDir;
                if (c.ShortcutDir != null) txtShortcut.Text = c.ShortcutDir;
                if (c.TemplateDir != null) txtTemplate.Text = c.TemplateDir;
                if (c.Prefix != null) txtPrefix.Text = c.Prefix;
                if (c.DesktopName != null) txtDesktop.Text = c.DesktopName;
                numStart.Value = ClampCount(c.Start, DefaultStart);
                numEnd.Value = ClampCount(c.End, DefaultEnd);
                rbOverwrite.Checked = c.Overwrite;
                rbSkip.Checked = !c.Overwrite;
                chkUseTemplate.Checked = c.UseTemplate;
                chkExcludeCache.Checked = c.ExcludeCache;
                chkDesktop.Checked = c.CreateDesktop;
            }
            finally { suppressDerive = false; }
            UpdateTemplateEnabled();
        }

        // 配置文件被手改坏时, 编号可能越界, 这里兜住 (NumericUpDown 越界会抛异常)
        private static decimal ClampCount(int v, int fallback)
        {
            if (v <= 0) return fallback;
            if (v > MaxCount) return MaxCount;
            return v;
        }

        // 保存当前配置 (关窗时、生成成功后各存一次)
        private void SaveCurrentConfig()
        {
            settings.Current = CollectSnapshot();
            ConfigStore.Save(settings);
        }

        private void OnResetDefaults()
        {
            if (!Confirm("将把所有配置恢复为默认值 (基本目录 " + DefaultBaseDir + "),\r\n" +
                         "当前填写的内容会被覆盖。\r\n\r\n历史记录不会被清空。确定继续?")) return;
            suppressDerive = true;
            try { SetDefaults(); }
            finally { suppressDerive = false; }
            UpdateTemplateEnabled();
            SaveCurrentConfig();
            Log("已恢复默认配置。");
        }

        private void OnShowHistory()
        {
            if (settings.History == null || settings.History.Count == 0)
            {
                MessageBox.Show(this,
                    "还没有历史记录。\r\n每次『一键生成』成功后会自动记录一条 (最多保留 " +
                    ConfigStore.MaxHistory + " 条)。",
                    "历史记录", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (HistoryDialog d = new HistoryDialog(this, settings.History))
            {
                d.ShowDialog(this);
                if (d.Changed) ConfigStore.Save(settings);   // 删除/清空后立即落盘
                if (d.Chosen != null)
                {
                    ApplySnapshot(d.Chosen);
                    SaveCurrentConfig();
                    Log("已套用历史记录: " + d.ChosenTime);
                }
            }
        }

        // ---------- Chrome 探测 ----------
        private static string DetectChrome()
        {
            string[] regKeys = new string[]
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"
            };
            foreach (string rk in regKeys)
            {
                try
                {
                    object v = Registry.GetValue(rk, "", null);
                    string s = v as string;
                    if (!string.IsNullOrEmpty(s) && File.Exists(s)) return s;
                }
                catch { }
            }
            string[] folders = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };
            foreach (string f in folders)
            {
                if (string.IsNullOrEmpty(f)) continue;
                string candidate = Path.Combine(f, @"Google\Chrome\Application\chrome.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return "";
        }

        private void DetectChromeToBox()
        {
            string c = DetectChrome();
            if (c.Length > 0) { txtChrome.Text = c; Log("已探测到 Chrome: " + c); }
            else MessageBox.Show(this, "未自动探测到 chrome.exe, 请手动点击“浏览”指定。",
                "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------- 浏览 ----------
        private void BrowseFile(TextBox target)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dlg.Title = "选择 chrome.exe";
                if (target.Text.Length > 0)
                {
                    try { dlg.InitialDirectory = Path.GetDirectoryName(target.Text); } catch { }
                }
                if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.FileName;
            }
        }

        private void BrowseFolder(TextBox target)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择文件夹";
                if (Directory.Exists(target.Text)) dlg.SelectedPath = target.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
            }
        }

        private void OpenOutputFolder()
        {
            string dir = txtShortcut.Text.Trim().TrimEnd('\\');
            if (dir.Length == 0)
            {
                MessageBox.Show(this, "快捷方式目录为空。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                ProcessStartInfo psi = new ProcessStartInfo(dir);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开文件夹: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------- 桌面入口 ----------
        private void OnCreateDesktopEntry()
        {
            string shortcutDir;
            try { shortcutDir = Path.GetFullPath(txtShortcut.Text.Trim()).TrimEnd('\\'); }
            catch { Warn("快捷方式目录无效。"); return; }
            string name = txtDesktop.Text.Trim();
            if (name.Length == 0) { Warn("桌面入口名称不能为空。"); return; }
            if (HasInvalidNameChar(name)) { Warn("桌面入口名称含非法字符 (\\ / : * ? \" < > |)。"); return; }
            if (!Directory.Exists(shortcutDir)) { Warn("快捷方式目录不存在, 请先生成快捷方式。"); return; }

            object shell = null;
            try
            {
                shell = CreateShell();
                string r = CreateDesktopEntry(shell, shortcutDir, name);
                Log(r);
                MessageBox.Show(this, "已在桌面创建入口: " + name, "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "创建桌面入口失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { ReleaseShell(shell); }
        }

        // 在桌面创建指向快捷方式文件夹的快捷方式 (同名覆盖)
        private string CreateDesktopEntry(object shell, string shortcutDir, string name)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = Path.Combine(desktop, name + ".lnk");
            bool existed = File.Exists(lnk);
            CreateShortcut(shell, lnk, shortcutDir, "", shortcutDir, "Chrome 分身快捷方式集合");
            return (existed ? "已覆盖桌面入口: " : "已创建桌面入口: ") + name + "  -> " + shortcutDir;
        }

        // ---------- 模板制作: 创建 / 打开 / 定位 ----------

        // 解析并校验"模板目录"; 通过返回 true 并输出规范化全路径, 否则弹窗提示并返回 false。
        private bool ResolveTemplateDir(out string templateDir)
        {
            templateDir = "";
            string raw = txtTemplate.Text.Trim();
            if (raw.Length == 0) { Warn("请先填写“模板目录”。"); return false; }
            try { templateDir = Path.GetFullPath(raw).TrimEnd('\\'); }
            catch { Warn("模板目录路径无效。"); return false; }
            if (IsRootPath(templateDir))
            { Warn("模板目录不能是盘符根目录, 请指定一个子文件夹。"); return false; }
            if (LooksLikeSensitivePath(templateDir))
            { Warn("模板目录位于系统目录或真实 Chrome 用户数据目录下, 已拒绝操作。\r\n" +
                "请改到一个独立文件夹 (例如 F:\\Chrome_Matrix_Browser\\... )。"); return false; }
            return true;
        }

        // 模板快捷方式(.lnk)路径: 放在模板文件夹的同级目录, 文件名同模板文件夹名 + .lnk
        private static string TemplateLnkPath(string templateDir)
        {
            string parent = Path.GetDirectoryName(templateDir);
            string leaf = Path.GetFileName(templateDir);
            if (string.IsNullOrEmpty(parent)) parent = templateDir; // 兜底(已排除根目录, 理论不触发)
            return Path.Combine(parent, leaf + ".lnk");
        }

        // 在模板目录同级创建/更新指向 Chrome 的快捷方式 (.lnk); 失败弹窗并返回 false。
        private bool CreateTemplateShortcut(string templateDir, string chrome)
        {
            string lnk = TemplateLnkPath(templateDir);
            string workingDir = Path.GetDirectoryName(Path.GetFullPath(chrome));
            string args = "--user-data-dir=\"" + templateDir + "\"";
            object shell = null;
            try
            {
                shell = CreateShell();
                CreateShortcut(shell, lnk, chrome, args, workingDir,
                    "Chrome 模板 - 制作好后用于覆盖所有分身");
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "创建模板快捷方式失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally { ReleaseShell(shell); }
        }

        // 一键创建模板: 建文件夹 + 建快捷方式, 再询问是否立即打开 Chrome 制作模板。
        // 若模板目录已有内容, 不静默覆盖——弹出选择对话框让用户决定。
        private void OnCreateTemplate()
        {
            string chrome = txtChrome.Text.Trim();
            if (chrome.Length == 0 || !File.Exists(chrome))
            { Warn("未找到 chrome.exe, 请点击“探测”或“浏览”指定正确路径。"); return; }

            string templateDir;
            if (!ResolveTemplateDir(out templateDir)) return;

            string lnk = TemplateLnkPath(templateDir);

            // 关键保护: 模板目录已有内容 → 绝不静默覆盖, 让用户选择如何处理。
            if (DirHasContent(templateDir))
            {
                int choice = ChoiceDialog.Show(this, "模板已存在",
                    "检测到模板已存在, 且其中已有内容:\r\n" + templateDir +
                    "\r\n\r\n为避免覆盖你已做好的模板, 请选择如何处理:",
                    new string[]
                    {
                        "保留并打开 (继续制作 / 更新模板)",
                        "清空重建 (删除现有内容, 慎选)",
                        "取消"
                    }, 1);

                if (choice == 0)
                {
                    // 保留内容: 刷新快捷方式后直接打开, 不动模板里的任何文件。
                    if (!CreateTemplateShortcut(templateDir, chrome)) return;
                    Log("已保留现有模板, 快捷方式已就绪: " + lnk);
                    LaunchTemplate(false);
                    return;
                }
                if (choice == 1)
                {
                    // 清空重建: 永久删除现有内容, 二次强确认。
                    if (MessageBox.Show(this,
                            "【危险】将永久删除以下模板目录中的全部内容, 不可恢复:\r\n" + templateDir +
                            "\r\n\r\n请先确认该模板对应的 Chrome 已【完全关闭】" +
                            "(否则文件被占用会删除失败)。\r\n\r\n确定清空重建吗?",
                            "清空重建确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                        return;
                    // 防御性安全再校验: 绝不删根目录/系统目录/真实 Chrome 数据目录。
                    if (IsRootPath(templateDir) || LooksLikeSensitivePath(templateDir))
                    { Warn("出于安全考虑, 拒绝清空该目录。"); return; }
                    try { ForceDeleteDirectory(templateDir); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this,
                            "清空模板失败 (可能 Chrome 仍打开占用文件): " + ex.Message,
                            "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    Log("已清空模板目录, 准备重建: " + templateDir);
                    // 落到下方“全新创建”继续。
                }
                else
                {
                    return; // 取消 / 关闭
                }
            }

            // ---- 全新创建 (空目录 或 清空重建后) ----
            try { Directory.CreateDirectory(templateDir); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "创建模板文件夹失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!CreateTemplateShortcut(templateDir, chrome)) return;

            Log("模板文件夹就绪: " + templateDir);
            Log("已创建模板快捷方式: " + lnk);

            if (MessageBox.Show(this,
                    "模板已创建:\r\n" + templateDir + "\r\n\r\n" +
                    "是否立即打开 Chrome 开始制作模板?\r\n" +
                    "(登录账号、安装扩展、调好设置后, 关闭 Chrome 即可。)",
                    "创建完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                LaunchTemplate(false);
        }

        // 打开模板: 直接启动 chrome --user-data-dir=模板目录, 进入制作/更新状态。
        // fromButton=true 由"打开模板"按钮调用, 文件夹缺失时先征求确认再创建。
        private void LaunchTemplate(bool fromButton)
        {
            string chrome = txtChrome.Text.Trim();
            if (chrome.Length == 0 || !File.Exists(chrome))
            { Warn("未找到 chrome.exe, 请点击“探测”或“浏览”指定正确路径。"); return; }

            string templateDir;
            if (!ResolveTemplateDir(out templateDir)) return;

            if (!Directory.Exists(templateDir))
            {
                if (fromButton &&
                    !Confirm("模板文件夹尚不存在:\r\n" + templateDir + "\r\n\r\n是否现在创建并打开?"))
                    return;
                try { Directory.CreateDirectory(templateDir); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "创建模板文件夹失败: " + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(chrome);
                psi.Arguments = "--user-data-dir=\"" + templateDir + "\"";
                psi.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(chrome));
                psi.UseShellExecute = true;
                Process.Start(psi);
                Log("已打开模板 Chrome: " + templateDir + "  (制作/更新完成后请关闭 Chrome)");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开模板失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 打开模板文件夹: 在资源管理器中定位并高亮模板快捷方式, 方便手动双击;
        // 快捷方式不存在时退回打开其所在目录。
        private void OpenTemplateFolder()
        {
            string templateDir;
            if (!ResolveTemplateDir(out templateDir)) return;

            try
            {
                string lnk = TemplateLnkPath(templateDir);
                if (File.Exists(lnk))
                {
                    Process.Start("explorer.exe", "/select,\"" + lnk + "\"");
                    return;
                }
                // 没有快捷方式: 打开其所在的父目录(存在则), 否则打开模板目录本身。
                string parent = Path.GetDirectoryName(lnk);
                string toOpen = (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    ? parent : templateDir;
                if (!Directory.Exists(toOpen)) Directory.CreateDirectory(toOpen);
                ProcessStartInfo psi = new ProcessStartInfo(toOpen);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开模板文件夹失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------- 生成核心 ----------
        private void OnGenerate()
        {
            string chrome = txtChrome.Text.Trim();
            string prefix = txtPrefix.Text.Trim();
            int start = (int)numStart.Value;
            int end = (int)numEnd.Value;
            int total = end - start + 1;
            bool overwrite = rbOverwrite.Checked;
            bool skip = !overwrite;
            bool useTemplate = chkUseTemplate.Checked;
            bool excludeCache = chkExcludeCache.Checked;

            string userData, shortcutDir, templateDir;
            try { userData = Path.GetFullPath(txtUserData.Text.Trim()).TrimEnd('\\'); }
            catch { Warn("数据目录路径无效。"); return; }
            try { shortcutDir = Path.GetFullPath(txtShortcut.Text.Trim()).TrimEnd('\\'); }
            catch { Warn("快捷方式目录路径无效。"); return; }
            templateDir = txtTemplate.Text.Trim().TrimEnd('\\');

            if (chrome.Length == 0 || !File.Exists(chrome))
            { Warn("未找到 chrome.exe, 请点击“探测”或“浏览”指定正确路径。"); return; }
            if (prefix.Length == 0) { Warn("名称前缀不能为空。"); return; }
            if (HasInvalidNameChar(prefix))
            { Warn("名称前缀含有非法字符 (\\ / : * ? \" < > |), 请修改后重试。"); return; }
            if (txtUserData.Text.Trim().Length == 0 || txtShortcut.Text.Trim().Length == 0)
            { Warn("数据目录和快捷方式目录都必须填写。"); return; }
            if (IsRootPath(userData))
            { Warn("数据目录不能是盘符根目录, 请指定一个子文件夹 (例如 F:\\Chrome_UserData)。"); return; }
            if (start > end) { Warn("起始编号不能大于结束编号。"); return; }
            if (PathsEqual(userData, shortcutDir))
            {
                if (!Confirm("数据目录与快捷方式目录相同, 快捷方式会混入分身数据目录。确定继续?")) return;
            }

            if (useTemplate)
            {
                if (templateDir.Length == 0 || !Directory.Exists(templateDir) || !DirHasContent(templateDir))
                { Warn("模板目录无效或为空: " + templateDir +
                    "\r\n请先把配置好的 UserData 文件夹放好, 再勾选“使用模板”。"); return; }
                if (IsAncestorOrEqual(templateDir, userData))
                { Warn("模板目录不能是数据目录本身或其父级/祖先, 否则会无限递归复制到自身。"); return; }
                if (!Confirm("使用模板前, 请确认该模板对应的 Chrome 已【完全关闭】,\r\n" +
                             "否则 Cookies 等文件被占用, 登录态无法复制。\r\n\r\n是否继续?")) return;
                if (overwrite)
                {
                    if (LooksLikeSensitivePath(userData))
                    { Warn("数据目录位于系统目录或真实 Chrome 用户数据目录下, 为防止误删已拒绝“覆盖”。\r\n" +
                        "请把数据目录改到一个独立的空文件夹 (例如 F:\\Chrome_UserData)。"); return; }
                    if (MessageBox.Show(this,
                            "【危险】覆盖模式将删除以下目录中已存在的分身, 再用模板重写:\r\n" +
                            userData + "\r\n范围: " + prefix + start + " .. " + prefix + end +
                            "  (最多 " + total + " 个)\r\n\r\n其中已有的登录数据将永久丢失且不可恢复！\r\n\r\n确定要覆盖吗?",
                            "覆盖确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                }
            }

            try { Directory.CreateDirectory(userData); Directory.CreateDirectory(shortcutDir); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "创建目录失败: " + ex.Message, "无法生成",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string workingDir = Path.GetDirectoryName(Path.GetFullPath(chrome));
            int created = 0, skipped = 0, failed = 0;
            int tplCopied = 0, tplSkipped = 0;
            int totalLocked = 0, profilesLocked = 0;

            SetBusy(true);
            txtLog.Clear();
            progress.Minimum = 0; progress.Maximum = total; progress.Value = 0;
            Log("开始生成: " + prefix + start + " .. " + prefix + end + "  (共 " + total + " 个)" +
                (useTemplate ? "  [模板模式]" : ""));

            object shell = null;
            try
            {
                shell = CreateShell();
                int done = 0;
                for (int n = start; n <= end; n++)
                {
                    if (this.IsDisposed) break;

                    string name = prefix + n;
                    string target = Path.Combine(userData, name);
                    string lnkPath = Path.Combine(shortcutDir, name + ".lnk");
                    string args = "--user-data-dir=\"" + target + "\"";
                    bool itemBroken = false;

                    if (useTemplate)
                    {
                        try
                        {
                            if (IsAncestorOrEqual(target, templateDir) || IsAncestorOrEqual(templateDir, target))
                            {
                                tplSkipped++;
                                Log("跳过模板复制 (模板与目标存在包含关系, 避免递归): " + name);
                            }
                            else
                            {
                                bool dataExists = DirHasContent(target);
                                if (dataExists && skip)
                                {
                                    tplSkipped++;
                                    Log("跳过模板复制 (分身数据已存在): " + name);
                                }
                                else
                                {
                                    if (dataExists && overwrite)
                                    {
                                        if (!IsAncestorOrEqual(userData, target) || PathsEqual(userData, target))
                                            throw new InvalidOperationException("安全检查未通过, 拒绝删除: " + target);
                                        lblProgress.Text = "清理 " + name + " …";
                                        Application.DoEvents();
                                        ForceDeleteDirectory(target);
                                    }
                                    lblProgress.Text = "复制模板 -> " + name + " …";
                                    Application.DoEvents();
                                    int errs = 0;
                                    int files = CopyTemplate(templateDir, target, excludeCache, ref errs);
                                    tplCopied++;
                                    if (errs > 0) { totalLocked += errs; profilesLocked++; }
                                    Log("已复制模板 -> " + name + "  (" + files + " 个文件" +
                                        (errs > 0 ? ", " + errs + " 个被占用/失败未复制" : "") + ")");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++; itemBroken = true;
                            Log("模板复制失败: " + name + "  -> " + ex.Message);
                        }
                    }

                    if (itemBroken)
                    {
                        Log("  → 因数据步骤失败, 已跳过该分身的快捷方式。");
                    }
                    else
                    {
                        try
                        {
                            if (File.Exists(lnkPath) && skip)
                            { skipped++; Log("跳过快捷方式 (已存在): " + name + ".lnk"); }
                            else
                            {
                                CreateShortcut(shell, lnkPath, chrome, args, workingDir, "Chrome");
                                created++; Log("已生成快捷方式: " + name + ".lnk");
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++; Log("快捷方式失败: " + name + ".lnk  -> " + ex.Message);
                        }
                    }

                    done++;
                    progress.Value = done;
                    lblProgress.Text = done + " / " + total;
                    Application.DoEvents();
                }

                Log("");
                if (useTemplate) Log("模板复制: " + tplCopied + " 个, 跳过 " + tplSkipped + " 个。");
                Log("快捷方式: 新建 " + created + " 个, 跳过 " + skipped + " 个。失败 " + failed + " 个。");
                if (totalLocked > 0)
                    Log("⚠ 有 " + profilesLocked + " 个分身共 " + totalLocked +
                        " 个文件被占用/失败未复制, 可能缺登录态。请彻底关闭 Chrome 后, 对这些分身用『覆盖』重跑。");

                // 桌面入口
                if (chkDesktop.Checked)
                {
                    string dname = txtDesktop.Text.Trim();
                    if (dname.Length == 0 || HasInvalidNameChar(dname))
                        Log("跳过桌面入口: 名称为空或含非法字符。");
                    else
                    {
                        try { Log(CreateDesktopEntry(shell, shortcutDir, dname)); }
                        catch (Exception ex) { Log("桌面入口创建失败: " + ex.Message); }
                    }
                }

                lblProgress.Text = "完成 " + done + " / " + total;

                // 留存本次配置, 并记一条历史 (两次 CollectSnapshot: 避免当前配置与历史条目共用同一对象)
                settings.Current = CollectSnapshot();
                settings.AddHistory(CollectSnapshot(), created);
                ConfigStore.Save(settings);

                bool anyWarn = (failed > 0) || (totalLocked > 0);
                string summary = (anyWarn ? "生成完成 (有警告)\r\n\r\n" : "生成完成！\r\n\r\n");
                if (useTemplate)
                    summary += "复制模板: " + tplCopied + " 个\r\n跳过分身: " + tplSkipped + " 个\r\n";
                summary += "新建快捷方式: " + created + "\r\n跳过快捷方式: " + skipped + "\r\n失败: " + failed + "\r\n";
                if (chkDesktop.Checked) summary += "桌面入口: " + txtDesktop.Text.Trim() + "\r\n";
                if (totalLocked > 0)
                    summary += "\r\n⚠ " + profilesLocked + " 个分身共 " + totalLocked +
                        " 个文件被占用未复制(可能缺登录态),\r\n请彻底关闭 Chrome 后对这些分身用“覆盖”重跑。\r\n";
                summary += "\r\n是否打开快捷方式文件夹？";

                if (MessageBox.Show(this, summary, anyWarn ? "完成 (有警告)" : "完成",
                        MessageBoxButtons.YesNo,
                        anyWarn ? MessageBoxIcon.Warning : MessageBoxIcon.Information) == DialogResult.Yes)
                    OpenOutputFolder();
            }
            finally
            {
                ReleaseShell(shell);
                SetBusy(false);
            }
        }

        private void Warn(string msg)
        {
            MessageBox.Show(this, msg, "无法生成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private bool Confirm(string msg)
        {
            return MessageBox.Show(this, msg, "确认", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private void SetBusy(bool isBusy)
        {
            busy = isBusy;
            SetEnabledRec(this, isBusy);
            btnGenerate.Text = isBusy ? "生成中…" : "一键生成";
            this.UseWaitCursor = isBusy;
            if (!isBusy) UpdateTemplateEnabled();
        }

        private void SetEnabledRec(Control parent, bool isBusy)
        {
            foreach (Control c in parent.Controls)
            {
                if (c == txtLog || c == progress || c == lblProgress) { }
                else if (c is CaptionButton) { }
                else if (c is TextBox || c is NumericUpDown || c is Button ||
                         c is RadioButton || c is CheckBox)
                    c.Enabled = !isBusy;
                if (c.Controls.Count > 0) SetEnabledRec(c, isBusy);
            }
        }

        private void Log(string msg)
        {
            txtLog.AppendText(msg + "\r\n");
        }

        // ---------- 模板复制 ----------
        private static int CopyTemplate(string src, string dst, bool excludeCache, ref int errors)
        {
            int count = 0, processed = 0;
            CopyDirRec(src, dst, excludeCache, ref count, ref errors, ref processed);
            return count;
        }

        private static void CopyDirRec(string src, string dst, bool excludeCache,
                                       ref int count, ref int errors, ref int processed)
        {
            try { Directory.CreateDirectory(dst); }
            catch { errors++; return; }

            FileInfo[] files; DirectoryInfo[] subs;
            try
            {
                DirectoryInfo dir = new DirectoryInfo(src);
                files = dir.GetFiles(); subs = dir.GetDirectories();
            }
            catch { errors++; return; }

            foreach (FileInfo f in files)
            {
                if (IsExcludedFile(f.Name)) continue;
                processed++;
                try
                {
                    string d = Path.Combine(dst, f.Name);
                    f.CopyTo(d, true);
                    try { File.SetAttributes(d, File.GetAttributes(d) & ~FileAttributes.ReadOnly); }
                    catch { }
                    count++;
                }
                catch { errors++; }
                if ((processed % 100) == 0) Application.DoEvents();
            }

            foreach (DirectoryInfo sub in subs)
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (excludeCache && IsCacheDir(sub.Name)) continue;
                try { CopyDirRec(sub.FullName, Path.Combine(dst, sub.Name),
                        excludeCache, ref count, ref errors, ref processed); }
                catch { errors++; }
            }
        }

        private static void ForceDeleteDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            try { ClearReadOnlyRec(new DirectoryInfo(dir)); } catch { }
            Directory.Delete(dir, true);
        }

        private static void ClearReadOnlyRec(DirectoryInfo d)
        {
            try { d.Attributes = FileAttributes.Directory; } catch { }
            foreach (FileInfo f in d.GetFiles()) { try { f.Attributes = FileAttributes.Normal; } catch { } }
            foreach (DirectoryInfo sub in d.GetDirectories())
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                ClearReadOnlyRec(sub);
            }
        }

        private static bool IsExcludedFile(string name)
        {
            foreach (string f in ExcludedFiles)
                if (string.Equals(f, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsCacheDir(string name)
        {
            foreach (string c in CacheDirNames)
                if (string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool DirHasContent(string dir)
        {
            try
            {
                return Directory.Exists(dir) &&
                    (Directory.GetFiles(dir).Length > 0 || Directory.GetDirectories(dir).Length > 0);
            }
            catch { return false; }
        }

        // ---------- 路径工具 ----------
        private static bool IsRootPath(string fullTrimmed)
        {
            try
            {
                if (fullTrimmed.Length <= 2) return true;
                string root = Path.GetPathRoot(fullTrimmed);
                if (string.IsNullOrEmpty(root)) return false;
                return string.Equals(fullTrimmed.TrimEnd('\\'), root.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsAncestorOrEqual(string ancestor, string path)
        {
            try
            {
                string a = Path.GetFullPath(ancestor).TrimEnd('\\');
                string p = Path.GetFullPath(path).TrimEnd('\\');
                if (string.Equals(a, p, StringComparison.OrdinalIgnoreCase)) return true;
                return p.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool LooksLikeSensitivePath(string full)
        {
            string[] sysDirs = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            foreach (string s in sysDirs)
                if (!string.IsNullOrEmpty(s) && IsAncestorOrEqual(s, full)) return true;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                string realChrome = Path.Combine(local, @"Google\Chrome\User Data");
                if (IsAncestorOrEqual(realChrome, full) || IsAncestorOrEqual(full, realChrome)) return true;
            }
            return false;
        }

        private static bool HasInvalidNameChar(string s)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char b in bad)
                if (s.IndexOf(b) >= 0) return true;
            return false;
        }

        private static bool PathsEqual(string a, string b)
        {
            try
            {
                string na = Path.GetFullPath(a).TrimEnd('\\');
                string nb = Path.GetFullPath(b).TrimEnd('\\');
                return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // ---------- COM 建快捷方式 ----------
        private static object CreateShell()
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("无法创建 WScript.Shell COM 对象。");
            return Activator.CreateInstance(shellType);
        }

        private static void ReleaseShell(object shell)
        {
            if (shell == null) return;
            try { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); } catch { }
        }

        private static void CreateShortcut(object shell, string lnkPath, string target,
                                           string args, string workingDir, string description)
        {
            Type shellType = shell.GetType();
            object shortcut = shellType.InvokeMember("CreateShortcut",
                BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            try
            {
                Type scType = shortcut.GetType();
                SetProp(scType, shortcut, "TargetPath", target);
                SetProp(scType, shortcut, "Arguments", args);
                SetProp(scType, shortcut, "WorkingDirectory", workingDir);
                SetProp(scType, shortcut, "Description", description);
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            }
        }

        private static void SetProp(Type t, object obj, string name, object value)
        {
            t.InvokeMember(name, BindingFlags.SetProperty, null, obj, new object[] { value });
        }
    }
}
