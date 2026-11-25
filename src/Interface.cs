using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NexusAnima240
{
    internal static class Ui
    {
        internal static float Scale=1;
        internal static readonly Color Background=Color.FromArgb(30,32,52);
        internal static readonly Color Panel=Color.FromArgb(40,44,66);
        internal static readonly Color Text=Color.FromArgb(242,249,250);
        internal static readonly Color Muted=Color.FromArgb(158,173,190);
        internal static readonly Color Mint=Color.FromArgb(116,228,194);
        internal static readonly Color Blue=Color.FromArgb(145,178,243);
        internal static readonly Color Line=Color.FromArgb(63,77,94);
        internal static Icon GameIcon;
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        internal static void Initialize()
        {
            try { Scale=GetDpiForSystem()/96f; } catch { Scale=1; }
            using (var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("NexusAnima.ico"))
                if (input!=null) using (var icon=new Icon(input,new Size(256,256))) GameIcon=(Icon)icon.Clone();
        }
        internal static int S(float size) { return (int)Math.Round(size*Scale); }
        internal static Font Font(float size,bool bold=false) { return new Font("Microsoft YaHei UI",size*Scale,bold ? FontStyle.Bold : FontStyle.Regular,GraphicsUnit.Pixel); }
        internal static Font NumberFont(float size) { return new Font("Segoe UI",size*Scale,FontStyle.Regular,GraphicsUnit.Pixel); }
        internal static Rectangle Rect(int x,int y,int w,int h) { return new Rectangle(S(x),S(y),S(w),S(h)); }
        internal static GraphicsPath Rounded(RectangleF bounds,float radius)
        {
            var path=new GraphicsPath(); float d=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
            path.AddArc(bounds.Left,bounds.Top,d,d,180,90); path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
            path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90); path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
        }
        internal static void StyleWindow(Form form)
        {
            try { int dark=1,corner=2,border=ColorTranslator.ToWin32(Line); DwmSetWindowAttribute(form.Handle,20,ref dark,4); DwmSetWindowAttribute(form.Handle,33,ref corner,4); DwmSetWindowAttribute(form.Handle,34,ref border,4); } catch { }
        }
        internal static Label Label(string text,int x,int y,int w,int h,float size,Color color,bool bold=false)
        { return new Label {Text=text,Bounds=Rect(x,y,w,h),Font=Font(size,bold),ForeColor=color,BackColor=Color.Transparent}; }
        internal static bool HasGame()
        { var processes=Process.GetProcessesByName("NexusAnima"); bool present=processes.Length!=0; foreach(var p in processes)p.Dispose(); return present; }
    }

    internal sealed class SoftButton : Button
    {
        internal bool Primary;
        private bool hovered;
        internal SoftButton(string text,Rectangle bounds,bool primary)
        {
            Text=text; Bounds=bounds; Primary=primary; FlatStyle=FlatStyle.Flat;
            FlatAppearance.BorderSize=0; Font=Ui.Font(primary ? 17 : 13,true); Cursor=Cursors.Hand;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered=true;Invalidate();base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered=false;Invalidate();base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var background=new SolidBrush(Parent.BackColor))g.FillRectangle(background,ClientRectangle);
            var rect=new RectangleF(1,1,Width-2,Height-2);
            using(var shape=Ui.Rounded(rect,Ui.S(Primary ? 16 : 12)))
            {
                if(Primary && Enabled)
                { using(var brush=new LinearGradientBrush(rect,hovered ? Color.FromArgb(147,240,214) : Ui.Mint,hovered ? Color.FromArgb(169,196,255) : Ui.Blue,15f))g.FillPath(brush,shape); }
                else
                { using(var brush=new SolidBrush(hovered && Enabled ? Color.FromArgb(53,63,83) : Ui.Panel))g.FillPath(brush,shape); using(var pen=new Pen(Ui.Line))g.DrawPath(pen,shape); }
            }
            Color foreground=Primary && Enabled ? Color.FromArgb(25,48,57) : Enabled ? Ui.Text : Ui.Muted;
            TextRenderer.DrawText(g,Text,Font,ClientRectangle,foreground,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            if(Focused && Enabled) { using(var pen=new Pen(Color.FromArgb(150,255,255,255),Ui.S(1)))using(var focus=Ui.Rounded(new RectangleF(Ui.S(4),Ui.S(4),Width-Ui.S(8),Height-Ui.S(8)),Ui.S(12)))g.DrawPath(pen,focus); }
        }
    }

    internal sealed class IconButton : Button
    {
        private readonly string kind;
        private bool hover;
        internal IconButton(string type,Rectangle bounds)
        {
            kind=type;Bounds=bounds;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;TabStop=true;
            AccessibleName=type=="settings" ? "设置" : type=="close" ? "关闭" : "最小化";
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        }
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent.BackColor);
            if(hover)using(var brush=new SolidBrush(Ui.Panel))using(var r=Ui.Rounded(new RectangleF(0,0,Width-1,Height-1),Ui.S(10)))g.FillPath(brush,r);
            g.TranslateTransform(Width/2f,Height/2f);g.ScaleTransform(Ui.Scale,Ui.Scale);
            using(var pen=new Pen(Enabled ? (hover ? Ui.Text : Ui.Muted) : Ui.Line,1.6f))
            {
                pen.StartCap=pen.EndCap=LineCap.Round;
                if(kind=="close"){g.DrawLine(pen,-4,-4,4,4);g.DrawLine(pen,4,-4,-4,4);}
                else if(kind=="minimize")g.DrawLine(pen,-5,3,5,3);
                else {g.DrawEllipse(pen,-5,-5,10,10);g.DrawEllipse(pen,-1.5f,-1.5f,3,3);for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(pen,(float)Math.Cos(a)*5,(float)Math.Sin(a)*5,(float)Math.Cos(a)*7,(float)Math.Sin(a)*7);}}
            }
        }
    }

    internal class ModernForm : Form
    {
        protected ModernForm(int width,int height)
        {
            AutoScaleMode=AutoScaleMode.None;ClientSize=new Size(Ui.S(width),Ui.S(height));
            BackColor=Ui.Background;ForeColor=Ui.Text;Font=Ui.Font(14);FormBorderStyle=FormBorderStyle.None;
            StartPosition=FormStartPosition.CenterScreen;Icon=Ui.GameIcon;DoubleBuffered=true;MaximizeBox=false;
            SetStyle(ControlStyles.ResizeRedraw,true);
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Ui.StyleWindow(this);}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.Background);
            using(var pen=new Pen(Color.FromArgb(70,Ui.Line)))using(var r=Ui.Rounded(new RectangleF(0.5f,0.5f,Width-1,Height-1),Ui.S(12)))e.Graphics.DrawPath(pen,r);
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if(message.Msg==0x84 && message.Result.ToInt32()==1)
            {
                long pos=message.LParam.ToInt64();var point=PointToClient(new Point((short)(pos&0xffff),(short)((pos>>16)&0xffff)));
                if(point.Y<Ui.S(82) && GetChildAtPoint(point)==null)message.Result=new IntPtr(2);
            }
        }
    }

    internal sealed class MainForm : ModernForm
    {
        private readonly AppSettings settings;
        private readonly TextBox fps;
        private readonly SoftButton start;
        private readonly IconButton settingsButton;
        private readonly Label status;
        private readonly System.Windows.Forms.Timer timer;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private bool trayTransition;
        private Session session;
        private bool closing,starting;
        private bool invalid;
        private readonly bool autoStart;
        private readonly ToolTip tips=new ToolTip();

        internal MainForm(bool auto,int initialFps) : base(520,410)
        {
            autoStart=auto;Text="崩坏：因缘精灵 FPS解锁工具";settings=AppSettings.Load();
            settings.GamePath=GameLocator.Resolve(settings.GamePath);settings.FrameRate=initialFps;
            var icon=new PictureBox {Bounds=Ui.Rect(28,27,46,46),SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.Transparent,Image=Ui.GameIcon==null ? null : Ui.GameIcon.ToBitmap()};Controls.Add(icon);
            Controls.Add(Ui.Label("崩坏：因缘精灵",87,25,285,32,22,Ui.Text,true));
            Controls.Add(Ui.Label("FPS解锁工具",89,60,200,22,12,Ui.Muted));
            settingsButton=new IconButton("settings",Ui.Rect(383,27,32,32));Controls.Add(settingsButton);
            var minimize=new IconButton("minimize",Ui.Rect(421,27,32,32));Controls.Add(minimize);
            var close=new IconButton("close",Ui.Rect(459,27,32,32));Controls.Add(close);
            tips.SetToolTip(settingsButton,"设置");tips.SetToolTip(minimize,"收起到系统托盘");tips.SetToolTip(close,"关闭并恢复游戏帧率");
            settingsButton.Click+=delegate {ShowSettings();};minimize.Click+=delegate {HideToTray();};close.Click+=delegate {Close();};
            var caption=Ui.Label("目标帧率",49,127,130,22,13,Ui.Muted);caption.BackColor=Ui.Panel;Controls.Add(caption);
            fps=new TextBox {AccessibleName="目标帧率",Bounds=Ui.Rect(100,151,225,79),Text=initialFps.ToString(),TextAlign=HorizontalAlignment.Center,Font=Ui.NumberFont(61),BorderStyle=BorderStyle.None,BackColor=Ui.Panel,ForeColor=Ui.Text,MaxLength=10,TabIndex=0};Controls.Add(fps);
            var suffix=Ui.Label("FPS",329,184,80,28,18,Ui.Mint);suffix.BackColor=Ui.Panel;Controls.Add(suffix);
            var hint=Ui.Label("1 – 240  ·  任意整数",49,238,300,20,11,Ui.Muted);hint.BackColor=Ui.Panel;Controls.Add(hint);
            fps.KeyPress+=delegate(object sender,KeyPressEventArgs e){if(!Char.IsControl(e.KeyChar)&&!Char.IsDigit(e.KeyChar))e.Handled=true;};
            fps.TextChanged+=delegate {invalid=false;Invalidate();};
            start=new SoftButton("启动游戏",Ui.Rect(28,292,464,57),true){TabIndex=1};Controls.Add(start);
            start.Click+=delegate {StartGame();};AcceptButton=start;
            status=Ui.Label(GameLocator.IsGame(settings.GamePath) ? "游戏路径已识别 · 启动后自动收起到托盘" : "未找到游戏 · 请在设置中选择游戏路径",28,369,464,23,11,Ui.Muted);
            status.TextAlign=ContentAlignment.MiddleCenter;Controls.Add(status);
            trayMenu=TrayMenuAppearance.Create(ShowFromTray,Close);
            trayIcon=new NotifyIcon {Icon=Ui.GameIcon ?? SystemIcons.Application,Text=Text,ContextMenuStrip=trayMenu,Visible=false};
            trayIcon.MouseClick+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left)ShowFromTray();};
            timer=new System.Windows.Forms.Timer {Interval=100};timer.Tick+=delegate {Poll();};timer.Start();
            Shown+=delegate {ActiveControl=start;fps.SelectionLength=0;if(autoStart)BeginInvoke(new Action(StartGame));};
            FormClosing+=delegate
            {
                closing=true;timer.Stop();
                int value;if(Int32.TryParse(fps.Text,out value)&&value>=1&&value<=Profile.MaxFps)settings.FrameRate=value;
                try{settings.Save();}catch(Exception e){Files.Log(e.Message);}
                if(session!=null)
                {
                    session.Stop();
                    if(!session.Restored && session.Lease!=null && session.Lease.WasWritten)
                        MessageBox.Show(this,"恢复帧率未完成，请正常退出并重新打开游戏。","因缘精灵",MessageBoxButtons.OK,MessageBoxIcon.Information);
                    session.Dispose();session=null;
                }
            };
        }
        private void HideToTray()
        {
            if(closing||IsDisposed||trayIcon==null||trayTransition)return;
            trayTransition=true;
            try
            {
                // Keep the message loop and session timer running while removing the main window.
                trayIcon.Text=session!=null && session.Active ? Text+" · "+settings.FrameRate+" FPS" : Text;
                trayIcon.Visible=true;
                Hide();ShowInTaskbar=false;
                WindowState=FormWindowState.Normal;
                Files.Log("Window hidden to system tray.");
            }
            finally{trayTransition=false;}
        }
        private void ShowFromTray()
        {
            if(closing||IsDisposed||trayTransition)return;
            trayTransition=true;
            try
            {
                ShowInTaskbar=true;WindowState=FormWindowState.Normal;
                Show();BringToFront();Activate();
                trayIcon.Visible=false;
                Files.Log("Window restored from system tray.");
            }
            finally{trayTransition=false;}
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if(WindowState==FormWindowState.Minimized && trayIcon!=null)HideToTray();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                if(trayIcon!=null){trayIcon.Visible=false;trayIcon.Dispose();}
                if(trayMenu!=null)trayMenu.Dispose();
                if(timer!=null)timer.Dispose();
                tips.Dispose();
            }
            base.Dispose(disposing);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            var rect=Ui.Rect(28,108,464,163);
            using(var path=Ui.Rounded(rect,Ui.S(20)))
            {using(var fill=new SolidBrush(Ui.Panel))g.FillPath(fill,path);using(var pen=new Pen(invalid ? Color.FromArgb(238,164,178) : Ui.Line,Ui.S(1)))g.DrawPath(pen,path);}
            using(var brush=new LinearGradientBrush(Ui.Rect(48,108,150,2),Ui.Mint,Color.FromArgb(0,Ui.Blue),0f))g.FillRectangle(brush,Ui.Rect(48,108,150,2));
        }
        private void ShowSettings()
        {
            if(starting||session!=null)return;
            using(var dialog=new SettingsForm(settings)){dialog.ShowDialog(this);}
            status.Text=GameLocator.IsGame(settings.GamePath) ? "游戏路径已就绪 · 启动后自动收起到托盘" : "请在设置中选择 NexusAnima.exe";
        }
        private void StartGame()
        {
            if(starting||session!=null)return;
            int value;
            if(!Int32.TryParse(fps.Text,out value)||value<1||value>Profile.MaxFps)
            {invalid=true;status.Text="请输入 1–240 之间的整数";Invalidate();fps.Focus();return;}
            if(Ui.HasGame()){status.Text="请先正常退出游戏，再从这里启动";return;}
            settings.GamePath=GameLocator.Resolve(settings.GamePath);
            if(!GameLocator.IsGame(settings.GamePath)){ShowSettings();if(!GameLocator.IsGame(settings.GamePath))return;}
            try
            {
                settings.FrameRate=value;settings.Save();
                if(!Program.IsAdmin)
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--launch-auto --fps "+value){UseShellExecute=true,Verb="runas",WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory});
                    Close();return;
                }
                starting=true;start.Enabled=false;fps.ReadOnly=true;settingsButton.Enabled=false;start.Text="正在启动…";
                session=Session.Launch(settings.GamePath,value,delegate(string message){status.Text="正在等待游戏准备就绪…";Files.Log(message);},delegate {Application.DoEvents();return closing;});
                if(closing){session.Dispose();session=null;return;}
                session.Start();start.Text="游戏运行中";UpdateSessionStatus();
                HideToTray();
            }
            catch(Exception e)
            {
                if(session!=null){session.Dispose();session=null;}
                Files.Log(e.ToString());
                if(!closing)
                {
                    if(trayIcon.Visible)ShowFromTray();
                    var win32=e as System.ComponentModel.Win32Exception;
                    status.Text=win32!=null && win32.NativeErrorCode==1223 ? "已取消管理员确认" :
                        e.Message.Contains("指纹") || e.Message.Contains("版本") || e.Message.Contains("特征") ? "游戏版本不匹配 · 请更新工具后重试" :
                        e.Message.Contains("拒绝") || (win32!=null && win32.NativeErrorCode==5) ? "游戏访问被拒绝 · 请退出游戏后重试" :
                        "启动未完成 · 将鼠标移至此处查看原因";
                    tips.SetToolTip(status,e.Message);start.Text="启动游戏";start.Enabled=true;fps.ReadOnly=false;settingsButton.Enabled=true;
                }
            }
            finally{starting=false;}
        }
        private void Poll()
        {
            if(starting||session==null)return;
            try
            {
                if(!session.Alive){session.Stop();session.Dispose();session=null;start.Text="启动游戏";start.Enabled=true;fps.ReadOnly=false;settingsButton.Enabled=true;status.Text="游戏已退出 · 已恢复默认状态";trayIcon.Text=Text;return;}
                session.Tick();
                UpdateSessionStatus();
            }
            catch(Exception e)
            {
                Files.Log(e.ToString());string restore=session.Stop();session.Dispose();session=null;
                start.Text="启动游戏";start.Enabled=true;fps.ReadOnly=false;settingsButton.Enabled=true;
                status.Text="解锁已停止 · 将鼠标移至此处查看原因";tips.SetToolTip(status,e.Message+"\n"+restore);
                trayIcon.Text=Text;
                if(trayIcon.Visible)ShowFromTray();
            }
        }
        private void UpdateSessionStatus()
        {
            if(session==null || !session.Active)return;
            status.Text="目标 "+session.Lease.Target+" FPS · 关闭工具将恢复游戏设定";
            trayIcon.Text=Text+" · "+session.Lease.Target+" FPS";
        }
        internal void RenderPreview(string path)
        {
            Show();Application.DoEvents();
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path,ImageFormat.Png);}
            using(var dialog=new SettingsForm(settings)){dialog.Show();Application.DoEvents();using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(Path.GetDirectoryName(path),"settings-preview.png"),ImageFormat.Png);}dialog.Close();}
            Close();
        }
    }

    internal sealed class SettingsForm : ModernForm
    {
        private readonly AppSettings settings;
        private readonly TextBox pathText;
        private readonly Label pathStatus;
        internal SettingsForm(AppSettings value) : base(520,370)
        {
            settings=value;Text="设置 · 因缘精灵";StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;
            Controls.Add(Ui.Label("设置",28,27,270,34,23,Ui.Text,true));
            var close=new IconButton("close",Ui.Rect(459,27,32,32));close.Click+=delegate {Close();};Controls.Add(close);
            Controls.Add(Ui.Label("游戏路径",29,90,180,25,14,Ui.Text,true));
            pathStatus=Ui.Label(GameLocator.IsGame(settings.GamePath) ? "已识别游戏位置" : "自动识别失败，请手动选择",29,119,450,22,11,Ui.Muted);Controls.Add(pathStatus);
            pathText=new TextBox {AccessibleName="游戏路径",Text=settings.GamePath??"",Bounds=Ui.Rect(42,156,434,45),Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Ui.Panel,ForeColor=Ui.Text,Font=Ui.Font(12),WordWrap=true};Controls.Add(pathText);
            var auto=new SoftButton("自动识别",Ui.Rect(28,216,223,40),false);Controls.Add(auto);
            var browse=new SoftButton("选择游戏文件",Ui.Rect(265,216,227,40),false);Controls.Add(browse);
            auto.Click+=delegate
            {
                string found=GameLocator.Detect();
                if(found==null){pathStatus.Text="未找到游戏，请选择 NexusAnima.exe";return;}
                SavePath(found,"已自动识别游戏位置");
            };
            browse.Click+=delegate
            {
                using(var dialog=new OpenFileDialog {Title="选择 NexusAnima.exe",Filter="因缘精灵 (NexusAnima.exe)|NexusAnima.exe",CheckFileExists=true,Multiselect=false})
                {
                    if(GameLocator.IsGame(settings.GamePath))dialog.InitialDirectory=Path.GetDirectoryName(settings.GamePath);
                    if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                    if(!GameLocator.IsGame(dialog.FileName)){pathStatus.Text="请选择游戏目录中的 NexusAnima.exe";return;}
                    SavePath(dialog.FileName,"已保存游戏路径");
                }
            };
            Controls.Add(Ui.Label("开发者",29,307,120,23,12,Ui.Muted));
            var developer=Ui.Label("MissPinkElf",250,303,242,29,18,Ui.Mint,true);developer.TextAlign=ContentAlignment.MiddleRight;Controls.Add(developer);
        }
        private void SavePath(string path,string message)
        {try{settings.GamePath=path;settings.Save();pathText.Text=path;pathStatus.Text=message;}catch(Exception e){pathStatus.Text="路径保存失败";Files.Log(e.Message);}}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var r=Ui.Rounded(Ui.Rect(28,146,464,60),Ui.S(12)))
            {using(var b=new SolidBrush(Ui.Panel))e.Graphics.FillPath(b,r);using(var p=new Pen(Ui.Line))e.Graphics.DrawPath(p,r);}
            using(var p=new Pen(Ui.Line))e.Graphics.DrawLine(p,Ui.S(28),Ui.S(285),Ui.S(492),Ui.S(285));
        }
    }
}
