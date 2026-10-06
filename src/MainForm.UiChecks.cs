using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MusicAssistant {
 public sealed partial class MainForm {
  public List<string> RunUiChecks(){var results=new List<string>();Action<bool,string> check=(condition,message)=>{if(!condition)throw new InvalidOperationException("界面检查失败："+message);results.Add(message);};
   Switch("歌单任务");songSearch.Text="海风";Application.DoEvents();check(VisibleTracks().Count==1&&Selected!=null&&Selected.Title=="海风","搜索后歌曲操作准确对应当前可见歌曲");
   platformFilter.SelectedItem="QQ音乐";Application.DoEvents();check(VisibleTracks().Count==0&&Selected==null,"平台筛选无结果时不误操作隐藏歌曲");
   songSearch.Text="";platformFilter.SelectedIndex=0;statusTab="需补齐";ApplyFilters();Application.DoEvents();check(VisibleTracks().Count==2,"待下载与缺密钥歌曲可单独筛选");
   var target=VisibleTracks()[1];SelectCard(target);SetView(false);check(Selected==target,"卡片切换列表时保留选中歌曲");SetView(true);check(Selected==target,"列表切回卡片时保留选中歌曲");
   statusTab="全部";ApplyFilters();link.Text="https://music.163.com/playlist?id=123";Switch("处理历史");Switch("设置与帮助");Switch("歌单任务");Application.DoEvents();check(link.Text=="https://music.163.com/playlist?id=123"&&!link.IsDisposed&&!grid.IsDisposed,"跨页面切换保留输入与可复用控件");
   Size=new Size(1060,740);Application.DoEvents();var read=Descendants(content).OfType<Button>().First(c=>c.Text=="读取歌单");check(read.Bounds.Bottom<=read.Parent.ClientSize.Height&&read.Bounds.Right<=read.Parent.ClientSize.Width,"最小窗口下读取按钮完整可见");
   check(run.Bounds.Bottom<=run.Parent.ClientSize.Height&&run.Bounds.Right<=run.Parent.ClientSize.Width,"最小窗口下处理按钮完整可见");
   Switch("处理历史");Application.DoEvents();var open=Descendants(content).OfType<Button>().First(c=>c.Text=="打开记录目录");check(open.Bounds.Bottom<=open.Parent.ClientSize.Height,"历史页面操作按钮没有被布局裁切");
   Switch("设置与帮助");Switch("歌单任务");Application.DoEvents();check(VisibleTracks().Count==4&&!grid.IsDisposed,"重复导航后任务列表仍可正常使用");
   ShowLargePreview();Application.DoEvents();var tracks=VisibleTracks();
   check(tracks.Count==993&&grid.Rows.Count==993&&metricItems[0].Value=="993","分页仍保留完整歌单、列表与总数");
   check(albumWall.Controls.Count==60&&albumPage==0&&!pagePrevious.Enabled&&pageNext.Enabled,"大歌单首屏只创建60张卡片");
   check(albumWall.Controls[0].Bounds.IntersectsWith(albumWall.ClientRectangle)&&albumWall.DisplayRectangle.Height<32767,"大歌单首屏卡片可见且布局高度安全");
   check(pageNext.Bounds.Bottom<=pageNext.Parent.ClientSize.Height&&pageCaption.Width>200,"最小窗口下页码与翻页按钮完整可见");
   var seen=new List<Track>();for(int page=0;page<17;page++){Application.DoEvents();seen.AddRange(albumWall.Controls.OfType<AlbumTile>().Select(t=>t.Track));check(albumWall.Controls.Count<=60&&albumWall.Controls[0].Bounds.IntersectsWith(albumWall.ClientRectangle),"分页 "+(page+1)+" 的卡片完整且首张可见");if(page<16)pageNext.PerformClick();}
   check(seen.SequenceEqual(tracks),"逐页浏览993首顺序一致，无丢失或重复");
   check(albumWall.Controls.Count==33&&albumPage==16&&!pageNext.Enabled&&pagePrevious.Enabled&&pageCaption.Text.Contains("961–993"),"尾页显示剩余33首并禁止越界翻页");
   ChangeAlbumPage(1);check(albumPage==16,"尾页不会跳出歌单范围");
   SelectCard(tracks[992]);SetView(false);check(Selected==tracks[992]&&grid.Rows.Count==993,"尾页歌曲切换列表保持选择且列表显示完整歌单");
   grid.CurrentCell=grid.Rows[120].Cells[0];SetView(true);Application.DoEvents();check(albumPage==2&&Selected==tracks[120]&&albumWall.Controls.OfType<AlbumTile>().Any(t=>t.Track==tracks[120]&&t.Selected),"列表中的跨页歌曲回到对应卡片页");
   SetView(false);grid.CurrentCell=grid.Rows[177].Cells[0];SetView(true);Application.DoEvents();check(Selected==tracks[177]&&albumWall.Controls.OfType<AlbumTile>().First(t=>t.Track==tracks[177]).Bounds.IntersectsWith(albumWall.ClientRectangle),"从列表切回卡片时自动滚动到页内靠后的选中歌曲");SelectCard(tracks[120]);
   albumWall.AutoScrollPosition=new Point(0,270);Application.DoEvents();var beforeScroll=albumWall.AutoScrollPosition;var beforeTile=albumWall.Controls[0];tracks[120].Status="处理中";BindGrid();Application.DoEvents();check(albumWall.Controls[0]==beforeTile&&albumWall.AutoScrollPosition==beforeScroll&&albumPage==2,"处理进度刷新复用卡片并保持页码与滚动位置");
   pagePrevious.PerformClick();Application.DoEvents();check(albumPage==1&&Selected==tracks[60]&&albumWall.AutoScrollPosition==Point.Empty,"翻页重置滚动并将操作对应到新页面歌曲");
   songSearch.Text="测试歌曲0993";Application.DoEvents();check(VisibleTracks().Count==1&&albumPage==0&&Selected==tracks[992]&&albumWall.Controls.Count==1&&!pageNext.Enabled,"搜索覆盖全歌单并定位末尾歌曲");
   songSearch.Text="不存在的模拟歌曲";Application.DoEvents();check(Selected==null&&albumWall.Controls.Count==0&&emptyState.Visible&&!cardSurface.Visible,"无结果时显示空状态且无法误操作旧歌曲");
   songSearch.Clear();statusTab="已完成";ApplyFilters();Application.DoEvents();check(VisibleTracks().Count==1&&Selected==tracks[992]&&albumPage==0,"状态筛选覆盖全部页面");
   statusTab="全部";ApplyFilters();platformFilter.SelectedItem="QQ音乐";Application.DoEvents();check(VisibleTracks().Count==496&&albumWall.Controls.Count==60&&albumPage==0,"平台筛选保留全歌单匹配结果并重置页码");
   platformFilter.SelectedIndex=0;ChangeAlbumPage(16);Switch("处理历史");Switch("歌单任务");Application.DoEvents();check(albumWall.Controls.Count==60&&albumWall.Controls[0].Bounds.IntersectsWith(albumWall.ClientRectangle)&&!albumWall.IsDisposed,"大歌单重复导航后仍显示卡片");
   return results;
  }
  public void ShowLargePreview(){if(!previewMode)throw new InvalidOperationException("模拟歌单仅用于独立预览");var p=new Playlist{Name="千首歌单 · 模拟界面测试"};for(int i=1;i<=993;i++)p.Tracks.Add(new Track{Title="测试歌曲"+i.ToString("D4"),Artist="示例歌手",Platform=i%2==0?"QQ音乐":"网易云音乐",SongId=i.ToString(),Status=i==993?"已完成":"待处理",Detail="模拟数据 · 保留源音质"});playlists.Clear();playlists.Add(p);songSearch.Clear();platformFilter.SelectedIndex=0;statusTab="全部";ResetAlbumPage();Switch("歌单任务");}
  static IEnumerable<Control> Descendants(Control control){foreach(Control child in control.Controls){yield return child;foreach(var item in Descendants(child))yield return item;}}
 }
}
