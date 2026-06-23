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
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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

    public class MainForm : Form
    {
        private const int MaxCount = 10000;

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

        public MainForm()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.ResizeRedraw, true); // 整窗缩放重绘, 杜绝自绘控件残影
            BuildUi();
            SetDefaults();
            // 设默认值后再挂"基本目录/名称"联动 (避免初始化时误触发)
            txtBase.TextChanged += delegate { DeriveFromBaseName(); };
            txtName.TextChanged += delegate { DeriveFromBaseName(); };
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
            this.ClientSize = new Size(720, 800);
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Theme.BgWindow;
            this.StartPosition = FormStartPosition.CenterScreen;

            BuildTitleBar();

            // 卡片1: 基础配置 (含 基本目录 / 名称 两个联动源)
            CardPanel c1 = MakeCard(16, 56, 688, 224);
            AddTitle(c1, "基础配置", 18, 12);
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
            CardPanel c3 = MakeCard(16, 446, 688, 100);
            AddTitle(c3, "模板（可选）", 18, 12);
            chkUseTemplate = AddCheck(c3, "使用模板", 18, 44, 90);
            chkUseTemplate.CheckedChanged += delegate { UpdateTemplateEnabled(); };
            txtTemplate = AddInput(c3, 110, 42, 392);
            btnBrowseTemplate = AddBrowse(c3, "浏览", 510, 42, 162, delegate { BrowseFolder(txtTemplate); });
            chkExcludeCache = AddCheck(c3, "排除缓存目录（复制更快、更省空间；登录态/扩展/设置保留）", 110, 74, 540);
            chkExcludeCache.Checked = true;

            // 操作按钮
            btnGenerate = new PillButton();
            btnGenerate.Text = "一键生成";
            btnGenerate.Primary = true;
            btnGenerate.BackColor = Theme.BgWindow;
            btnGenerate.Font = new Font(this.Font.FontFamily, 10.5F, FontStyle.Bold);
            btnGenerate.SetBounds(16, 558, 200, 40);
            btnGenerate.Click += delegate { OnGenerate(); };
            this.Controls.Add(btnGenerate);

            PillButton btnOpen = new PillButton();
            btnOpen.Text = "打开输出文件夹"; btnOpen.Primary = false; btnOpen.BackColor = Theme.BgWindow;
            btnOpen.SetBounds(228, 558, 150, 40);
            btnOpen.Click += delegate { OpenOutputFolder(); };
            this.Controls.Add(btnOpen);

            PillButton btnDesktopBtn = new PillButton();
            btnDesktopBtn.Text = "建桌面入口"; btnDesktopBtn.Primary = false; btnDesktopBtn.BackColor = Theme.BgWindow;
            btnDesktopBtn.SetBounds(390, 558, 150, 40);
            btnDesktopBtn.Click += delegate { OnCreateDesktopEntry(); };
            this.Controls.Add(btnDesktopBtn);

            // 卡片4: 实时日志
            CardPanel c4 = MakeCard(16, 610, 688, 150);
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
            progress.SetBounds(16, 770, 540, 18);
            this.Controls.Add(progress);

            lblProgress = new Label();
            lblProgress.SetBounds(566, 769, 140, 20);
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
            this.MinimumSize = new Size(720, 720);
            this.ClientSize = new Size(944, 824);
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
                    TextRenderer.DrawText(g, "v2.0", vf,
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
            bool on = chkUseTemplate.Checked;
            txtTemplate.Enabled = on;
            btnBrowseTemplate.Enabled = on;
            chkExcludeCache.Enabled = on;
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
            base.OnFormClosing(e);
        }

        // ---------- 默认值 (全部写死在 exe 中, 不读写任何配置文件) ----------
        private void SetDefaults()
        {
            string baseDir = @"D:\Chrome_Matrix_Browser";
            string name = "Github";
            txtBase.Text = baseDir;
            txtName.Text = name;
            txtChrome.Text = DetectChrome();
            txtUserData.Text = Path.Combine(baseDir, name + "_Multiple", name + "_UserData");
            txtShortcut.Text = Path.Combine(baseDir, name + "_Multiple", name + "_ShortCuts");
            txtTemplate.Text = Path.Combine(baseDir, name + "_Multiple", name + "_Template_UserData");
            txtPrefix.Text = name + "_";
            txtDesktop.Text = "Github集合";
            numStart.Value = 1;
            numEnd.Value = 20;
            rbSkip.Checked = true;
            rbOverwrite.Checked = false;
            chkUseTemplate.Checked = false;
            chkExcludeCache.Checked = true;
            chkDesktop.Checked = true;
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
            { Warn("数据目录不能是盘符根目录, 请指定一个子文件夹 (例如 D:\\Chrome_UserData)。"); return; }
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
                        "请把数据目录改到一个独立的空文件夹 (例如 D:\\Chrome_UserData)。"); return; }
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
