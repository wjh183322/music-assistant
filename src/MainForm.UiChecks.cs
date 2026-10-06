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
   Switch("设置与帮助");Switch("歌单任务");Application.DoEvents();check(VisibleTracks().Count==4&&!grid.IsDisposed,"重复导航后任务列表仍可正常使用");return results;
  }
  static IEnumerable<Control> Descendants(Control control){foreach(Control child in control.Controls){yield return child;foreach(var item in Descendants(child))yield return item;}}
 }
}
