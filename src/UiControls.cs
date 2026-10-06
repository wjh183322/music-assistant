using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MusicAssistant {
 public static class UiTheme {
  public static readonly Color Accent=Color.FromArgb(236,98,78),Ink=Color.FromArgb(41,48,60),Muted=Color.FromArgb(139,150,165),Line=Color.FromArgb(233,237,242),Background=Color.FromArgb(247,248,250),Soft=Color.FromArgb(255,240,235);
  public static GraphicsPath Round(RectangleF rect,float radius){var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(rect.Width,rect.Height));if(d<=0){p.AddRectangle(rect);return p;}p.AddArc(rect.X,rect.Y,d,d,180,90);p.AddArc(rect.Right-d,rect.Y,d,d,270,90);p.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);p.AddArc(rect.X,rect.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
  public static Color StatusColor(string status){if(status=="已完成")return Color.FromArgb(77,139,106);if(status=="历史重复")return Color.FromArgb(107,125,157);if(status=="失败")return Color.FromArgb(204,91,86);if(status=="需要密钥"||status=="待官方下载"||status=="待官方确认")return Color.FromArgb(177,137,73);return Muted;}
  public static Color StatusFill(string status){if(status=="已完成")return Color.FromArgb(237,247,240);if(status=="历史重复")return Color.FromArgb(240,243,250);if(status=="失败")return Color.FromArgb(255,240,238);if(status=="需要密钥"||status=="待官方下载"||status=="待官方确认")return Color.FromArgb(255,247,232);return Color.FromArgb(244,246,249);}
 }
 public class SurfacePanel:Panel {
  public int Radius {get;set;}public Color BorderColor {get;set;}public Color SurfaceColor {get;set;}
  public SurfacePanel(){Radius=12;BorderColor=UiTheme.Line;SurfaceColor=Color.White;DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=UiTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),Radius))using(var fill=new SolidBrush(SurfaceColor))using(var pen=new Pen(BorderColor)){e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(pen,path);}}
 }
 public sealed class SoftButton:Button {
  public bool Primary {get;set;}public bool Chosen {get;set;}public bool Quiet {get;set;}public string Icon {get;set;}public int CornerRadius {get;set;}public bool LeftAligned {get;set;}
  bool hover;
  public SoftButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;CornerRadius=8;Cursor=Cursors.Hand;DoubleBuffered=true;BackColor=Color.White;ForeColor=UiTheme.Ink;SetStyle(ControlStyles.ResizeRedraw,true);}
  protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?UiTheme.Background:Parent.BackColor);
   Color fill=Primary?(hover?Color.FromArgb(219,82,63):UiTheme.Accent):Chosen?UiTheme.Soft:Quiet?(hover?Color.FromArgb(245,247,250):Color.White):(hover?Color.FromArgb(252,253,254):Color.White);
   Color text=Primary?Color.White:Chosen?UiTheme.Accent:ForeColor;if(!Enabled){fill=Primary?Color.FromArgb(245,192,182):Color.FromArgb(249,250,251);text=Color.FromArgb(171,178,188);}
   using(var path=UiTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),CornerRadius))using(var b=new SolidBrush(fill)){e.Graphics.FillPath(b,path);if(!Primary&&!Chosen&&!Quiet)using(var p=new Pen(UiTheme.Line))e.Graphics.DrawPath(p,path);}
   int iconWidth=string.IsNullOrEmpty(Icon)?0:22;int start=LeftAligned?14:Math.Max(10,(Width-TextRenderer.MeasureText(Text,Font).Width-iconWidth)/2);
   if(iconWidth>0)UiIcons.Draw(e.Graphics,Icon,new Rectangle(start,(Height-17)/2,17,17),text);
   TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(start+iconWidth,0,Math.Max(0,Width-start-iconWidth-8),Height),text,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
   if(Focused&&ShowFocusCues)using(var pen=new Pen(UiTheme.Accent)){pen.DashStyle=DashStyle.Dot;using(var p=UiTheme.Round(new RectangleF(3,3,Width-7,Height-7),Math.Max(2,CornerRadius-2)))e.Graphics.DrawPath(pen,p);}
  }
 }
 public static class UiIcons {
  public static void Draw(Graphics g,string kind,Rectangle rect,Color color){var state=g.Save();g.TranslateTransform(rect.X,rect.Y);g.ScaleTransform(rect.Width/24f,rect.Height/24f);g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=new Pen(color,1.6f)){p.StartCap=p.EndCap=LineCap.Round;p.LineJoin=LineJoin.Round;
   switch(kind){
    case "music":g.DrawLine(p,15,4,15,16);g.DrawLine(p,15,4,21,6);g.DrawEllipse(p,8,15,7,5);break;
    case "grid":for(int x=3;x<18;x+=11)for(int y=3;y<18;y+=11)g.DrawRectangle(p,x,y,7,7);break;
    case "list":for(int y=5;y<22;y+=6){g.DrawEllipse(p,3,y-1,1,1);g.DrawLine(p,8,y,21,y);}break;
    case "folder":g.DrawLines(p,new[]{new Point(3,7),new Point(3,4),new Point(9,4),new Point(12,7),new Point(21,7),new Point(21,20),new Point(3,20),new Point(3,7)});break;
    case "history":g.DrawArc(p,4,4,16,16,205,310);g.DrawLines(p,new[]{new Point(3,4),new Point(3,10),new Point(8,10)});g.DrawLine(p,12,8,12,13);g.DrawLine(p,12,13,16,15);break;
    case "settings":g.DrawEllipse(p,7,7,10,10);for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(p,(float)(12+8*Math.Cos(a)),(float)(12+8*Math.Sin(a)),(float)(12+10*Math.Cos(a)),(float)(12+10*Math.Sin(a)));}break;
    case "search":g.DrawEllipse(p,4,3,12,12);g.DrawLine(p,14,14,21,21);break;
    case "download":g.DrawLine(p,12,3,12,15);g.DrawLines(p,new[]{new Point(7,11),new Point(12,16),new Point(17,11)});g.DrawLines(p,new[]{new Point(4,18),new Point(4,21),new Point(20,21),new Point(20,18)});break;
    case "link":g.DrawArc(p,3,7,12,10,80,265);g.DrawArc(p,10,7,12,10,260,265);g.DrawLine(p,9,12,16,12);break;
    case "plus":g.DrawLine(p,12,5,12,19);g.DrawLine(p,5,12,19,12);break;
    case "more":using(var b=new SolidBrush(color))for(int i=5;i<22;i+=7)g.FillEllipse(b,i-1,11,2,2);break;
    case "play":using(var b=new SolidBrush(color))g.FillPolygon(b,new[]{new Point(7,4),new Point(20,12),new Point(7,20)});break;
    case "stop":g.DrawRectangle(p,6,6,12,12);break;
    case "check":g.DrawLines(p,new[]{new Point(5,12),new Point(10,17),new Point(20,6)});break;
    case "shield":g.DrawPolygon(p,new[]{new Point(12,2),new Point(20,6),new Point(18,16),new Point(12,22),new Point(6,16),new Point(4,6)});g.DrawLines(p,new[]{new Point(8,11),new Point(11,14),new Point(16,9)});break;
    case "key":g.DrawEllipse(p,3,3,10,10);g.DrawLines(p,new[]{new Point(12,12),new Point(21,21),new Point(21,16),new Point(17,16)});break;
    case "chevron":g.DrawLines(p,new[]{new Point(9,6),new Point(15,12),new Point(9,18)});break;
    default:g.DrawEllipse(p,4,4,16,16);break;
   }}g.Restore(state);}
 }
 public sealed class BrandMark:Control {
  public BrandMark(){DoubleBuffered=true;Size=new Size(44,44);}
  protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=UiTheme.Round(new RectangleF(1,1,Width-2,Height-2),13))using(var b=new LinearGradientBrush(ClientRectangle,Color.FromArgb(255,150,118),UiTheme.Accent,65f))e.Graphics.FillPath(b,p);UiIcons.Draw(e.Graphics,"music",new Rectangle(8,7,29,29),Color.White);}
 }
 public sealed class MetricTile:Control {
  public string Caption {get;set;}public string Value {get;set;}public string Icon {get;set;}public Color Tint {get;set;}
  public MetricTile(){DoubleBuffered=true;Tint=UiTheme.Accent;Value="0";BackColor=UiTheme.Background;}
  protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=UiTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),11))using(var b=new SolidBrush(Color.White))using(var pen=new Pen(UiTheme.Line)){g.FillPath(b,p);g.DrawPath(pen,p);}UiIcons.Draw(g,Icon,new Rectangle(16,18,18,18),Tint);using(var f=new Font("Microsoft YaHei UI",9))TextRenderer.DrawText(g,Caption,f,new Rectangle(44,15,Width-53,23),UiTheme.Muted,TextFormatFlags.EndEllipsis);using(var f=new Font("Segoe UI",22,FontStyle.Bold))TextRenderer.DrawText(g,Value,f,new Rectangle(15,39,Width-25,36),UiTheme.Ink,TextFormatFlags.EndEllipsis);}
 }
 public sealed class AlbumTile:Control {
  public Track Track {get;private set;}public bool Selected {get;set;}public Action<Track> Activate;public Action<Track,Control> Options;
  readonly Color color;Image cover;
  public AlbumTile(Track track,string coverPath,int ordinal){Track=track;DoubleBuffered=true;Size=new Size(194,248);Margin=new Padding(0,0,15,15);Cursor=Cursors.Hand;AccessibleName=track.Title;AccessibleRole=AccessibleRole.ListItem;
   Color[] colors={Color.FromArgb(210,148,129),Color.FromArgb(131,162,156),Color.FromArgb(154,151,177),Color.FromArgb(162,183,196),Color.FromArgb(188,165,130)};color=colors[ordinal%colors.Length];
   if(coverPath!=null)try{using(var s=File.OpenRead(coverPath))using(var source=Image.FromStream(s))cover=new Bitmap(source);}catch{cover=null;}
  }
  protected override void Dispose(bool disposing){if(disposing&&cover!=null){cover.Dispose();cover=null;}base.Dispose(disposing);}
  protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button==MouseButtons.Right||e.Y>205&&e.X>160){if(Options!=null)Options(Track,this);}else if(Activate!=null)Activate(Track);}
  protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(UiTheme.Background);Rectangle artwork=new Rectangle(0,0,Width-1,178);
   using(var path=UiTheme.Round(artwork,10)){var state=g.Save();g.SetClip(path);if(cover!=null)g.DrawImage(cover,artwork);else {using(var brush=new LinearGradientBrush(artwork,ControlPaint.Light(color,.3f),ControlPaint.Dark(color,.06f),55f))g.FillRectangle(brush,artwork);using(var brush=new SolidBrush(Color.FromArgb(28,Color.White)))g.FillEllipse(brush,Width-80,-40,150,150);using(var brush=new SolidBrush(Color.FromArgb(52,35,40,46)))g.FillEllipse(brush,52,36,94,94);using(var pen=new Pen(Color.FromArgb(55,Color.White),1))for(int r=25;r<47;r+=7)g.DrawEllipse(pen,99-r,83-r,r*2,r*2);using(var brush=new SolidBrush(Color.FromArgb(220,Color.White)))g.FillEllipse(brush,92,76,14,14);}
    using(var brush=new LinearGradientBrush(artwork,Color.FromArgb(0,23,30,44),Color.FromArgb(145,23,30,44),90f)){brush.WrapMode=WrapMode.TileFlipXY;g.FillRectangle(brush,artwork);}
    using(var f=new Font("Microsoft YaHei UI",11,FontStyle.Bold))TextRenderer.DrawText(g,Track.Title,f,new Rectangle(12,124,Width-24,25),Color.White,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    using(var f=new Font("Microsoft YaHei UI",8))TextRenderer.DrawText(g,string.IsNullOrEmpty(Track.Artist)?"等待本地文件信息":Track.Artist,f,new Rectangle(12,152,Width-24,19),Color.FromArgb(233,237,242),TextFormatFlags.EndEllipsis);
    string status=Track.Status;using(var p=UiTheme.Round(new RectangleF(10,10,82,23),5))using(var b=new SolidBrush(UiTheme.StatusFill(status)))g.FillPath(b,p);using(var f=new Font("Microsoft YaHei UI",8))TextRenderer.DrawText(g,status,f,new Rectangle(14,13,76,18),UiTheme.StatusColor(status),TextFormatFlags.EndEllipsis);g.Restore(state);
   }
   if(Selected)using(var pen=new Pen(UiTheme.Accent,2))using(var path=UiTheme.Round(new RectangleF(1,1,Width-3,176),10))g.DrawPath(pen,path);
   using(var f=new Font("Microsoft YaHei UI",8))TextRenderer.DrawText(g,string.IsNullOrEmpty(Track.Platform)?"本地音乐":Track.Platform,f,new Rectangle(2,187,Width-8,20),UiTheme.Muted,TextFormatFlags.EndEllipsis);
   using(var f=new Font("Microsoft YaHei UI",8))TextRenderer.DrawText(g,string.IsNullOrEmpty(Track.Detail)?"保留源音质 · 等待处理":Track.Detail,f,new Rectangle(2,212,Width-35,29),UiTheme.Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);UiIcons.Draw(g,"more",new Rectangle(Width-24,214,18,18),UiTheme.Muted);
  }
 }
}
