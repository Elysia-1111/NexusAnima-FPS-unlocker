using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NexusAnima240
{
    internal static class TrayMenuAppearance
    {
        internal static ContextMenuStrip Create(Action open,Action exit)
        {
            var menu=new ContextMenuStrip
            {
                ShowImageMargin=true,ShowCheckMargin=false,Font=Ui.Font(13),
                BackColor=Ui.Panel,ForeColor=Ui.Text,DropShadowEnabled=true,
                Padding=new Padding(Ui.S(5),Ui.S(6),Ui.S(5),Ui.S(6)),
                ImageScalingSize=new Size(Ui.S(16),Ui.S(16)),
                Renderer=new TrayMenuRenderer()
            };
            Bitmap openImage=DrawIcon(false),exitImage=DrawIcon(true);
            var openItem=new ToolStripMenuItem("打开工具",openImage,delegate {open();});
            var exitItem=new ToolStripMenuItem("退出",exitImage,delegate {exit();});
            foreach(var item in new[] {openItem,exitItem})
            {
                item.AutoSize=false;item.Size=new Size(Ui.S(156),Ui.S(37));
                item.ImageScaling=ToolStripItemImageScaling.None;
                item.Padding=new Padding(0);item.TextAlign=ContentAlignment.MiddleLeft;
            }
            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator {AutoSize=false,Height=Ui.S(9)});
            menu.Items.Add(exitItem);
            menu.Opened+=delegate
            {
                using(var path=Ui.Rounded(new RectangleF(0,0,menu.Width,menu.Height),Ui.S(10)))
                {
                    Region previous=menu.Region;menu.Region=new Region(path);
                    if(previous!=null)previous.Dispose();
                }
            };
            menu.Disposed+=delegate {openImage.Dispose();exitImage.Dispose();};
            return menu;
        }
        private static Bitmap DrawIcon(bool power)
        {
            var bitmap=new Bitmap(Ui.S(16),Ui.S(16));
            using(var graphics=Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.ScaleTransform(Ui.Scale,Ui.Scale);
                using(var pen=new Pen(power ? Ui.Muted : Ui.Mint,1.5f))
                {
                    pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                    if(power)
                    {graphics.DrawArc(pen,new RectangleF(2.5f,3,11,11),-50,280);graphics.DrawLine(pen,8,1.5f,8,7.5f);}
                    else
                    {
                        using(var frame=Ui.Rounded(new RectangleF(1.5f,2.5f,13,11),2))graphics.DrawPath(pen,frame);
                        graphics.DrawLine(pen,2,6,14,6);graphics.DrawLine(pen,4,4.3f,4.4f,4.3f);
                    }
                }
            }
            return bitmap;
        }
    }

    internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        internal TrayMenuRenderer() : base(new ProfessionalColorTable {UseSystemColors=false}) {RoundedEdges=true;}
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {using(var brush=new SolidBrush(Ui.Panel))e.Graphics.FillRectangle(brush,e.AffectedBounds);}
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {using(var brush=new SolidBrush(Ui.Panel))e.Graphics.FillRectangle(brush,e.AffectedBounds);}
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=Ui.Rounded(new RectangleF(0.5f,0.5f,e.ToolStrip.Width-1,e.ToolStrip.Height-1),Ui.S(10)))
            using(var pen=new Pen(Ui.Line))e.Graphics.DrawPath(pen,path);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if(!e.Item.Selected||!e.Item.Enabled)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=Ui.Rounded(new RectangleF(Ui.S(2),Ui.S(1),e.Item.Width-Ui.S(4),e.Item.Height-Ui.S(2)),Ui.S(7)))
            using(var brush=new SolidBrush(Color.FromArgb(52,75,86)))e.Graphics.FillPath(brush,path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor=e.Item.Enabled ? (e.Item.Selected ? Ui.Mint : Ui.Text) : Ui.Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using(var pen=new Pen(Ui.Line))e.Graphics.DrawLine(pen,Ui.S(9),e.Item.Height/2,e.Item.Width-Ui.S(9),e.Item.Height/2);
        }
    }
}
