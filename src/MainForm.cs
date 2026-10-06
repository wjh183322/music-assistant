using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MusicAssistant {
 public sealed partial class MainForm:Form {
  readonly Store store;readonly AudioTool audio;readonly List<Playlist> playlists=new List<Playlist>();
  readonly Color ink=UiTheme.Ink,muted=UiTheme.Muted,accent=UiTheme.Accent,background=UiTheme.Background;
  readonly Panel content=new Panel();readonly ComboBox listSelector=new ComboBox();readonly DataGridView grid=new DataGridView();readonly TextBox log=new TextBox();
  readonly TextBox link=new TextBox(),output=new TextBox(),historySearch=new TextBox();readonly Label stats=new Label(),subtitle=new Label();readonly ProgressBar progress=new ProgressBar();
  readonly CheckBox recycle=new CheckBox(),flac=new CheckBox();readonly ListBox sourceDirs=new ListBox();readonly DataGridView historyGrid=new DataGridView();
  readonly Button run=new SoftButton(),cancel=new SoftButton();readonly List<Control> mutable=new List<Control>();
  CancellationTokenSource cancellation;bool busy;string view="任务";
  public bool SkipTaskSaveOnClose {get;set;}
  [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern IntPtr SendMessage(IntPtr handle,uint message,IntPtr parameter,string text);
  public MainForm(Store store,bool preview) {
   this.store=store;audio=new AudioTool(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools"));
   link.HandleCreated+=(s,e)=>SendMessage(link.Handle,0x1501,new IntPtr(1),"粘贴 QQ / 网易云歌单分享链接");
   historySearch.HandleCreated+=(s,e)=>SendMessage(historySearch.Handle,0x1501,new IntPtr(1),"搜索歌曲、歌手或平台");
   songSearch.HandleCreated+=(s,e)=>SendMessage(songSearch.Handle,0x1501,new IntPtr(1),"搜索歌曲、歌手或平台");
   Text="音乐助手 0.5 · Walkman Library";Size=new Size(1320,920);MinimumSize=new Size(1060,740);StartPosition=FormStartPosition.CenterScreen;BackColor=background;Font=new Font("Microsoft YaHei UI",9f);ForeColor=ink;
   previewMode=preview;InitializeShell();
   FormClosing+=(s,e)=>{if(busy){cancellation.Cancel();e.Cancel=true;Append("正在停止；完成当前提交后即可关闭。");}else if(!SkipTaskSaveOnClose)SaveTasks();};
   string taskPath=Path.Combine(store.Root,"tasks.json");if(File.Exists(taskPath))playlists.AddRange(Json.Read<List<Playlist>>(File.ReadAllText(taskPath,Encoding.UTF8))??new List<Playlist>());
   if(preview)Demo();BuildTasks();
  }
  Label Label(string text,float size,Color color,int width,int height) {return new Label{Text=text,Font=new Font("Microsoft YaHei UI",size,size>=16?FontStyle.Bold:FontStyle.Regular),ForeColor=color,BackColor=Color.Transparent,Width=width,Height=height,AutoSize=false,TextAlign=ContentAlignment.MiddleLeft};}
  Button Button(string text,Action action,int width,bool primary) {
   var b=new SoftButton{Text=text,Width=width,Height=36,Primary=primary,ForeColor=ink,Icon=ButtonIcon(text),Margin=new Padding(0,0,8,0)};
   b.Click+=(s,e)=>Safe(action);return b;
  }
  void Safe(Action action){try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"音乐助手",MessageBoxButtons.OK,MessageBoxIcon.Information);}}
  FlowLayoutPanel Row(int height){return new FlowLayoutPanel{Dock=DockStyle.Fill,Height=height,WrapContents=false,FlowDirection=FlowDirection.LeftToRight,Padding=new Padding(0,5,0,0)};}
  void RefreshLists(){int selected=listSelector.SelectedIndex;listSelector.Items.Clear();foreach(var p in playlists)listSelector.Items.Add(p.Name);if(playlists.Count>0)listSelector.SelectedIndex=Math.Max(0,Math.Min(selected,playlists.Count-1));else BindGrid();RefreshSidebar();}
  void SelectionChanged(object s,EventArgs e){if(buildingUi)return;cardSelected=null;BindGrid();RefreshSidebar();}
  void GridDoubleClick(object s,DataGridViewCellEventArgs e){if(e.RowIndex>=0)BindSource();}
  void GridTooltip(object s,DataGridViewCellToolTipTextNeededEventArgs e){if(e.RowIndex>=0){var t=grid.Rows[e.RowIndex].DataBoundItem as Track;if(t!=null)e.ToolTipText=t.Detail+"\n"+t.SourcePath;}}
  Playlist Current {get{return listSelector.SelectedIndex>=0&&listSelector.SelectedIndex<playlists.Count?playlists[listSelector.SelectedIndex]:null;}}
  Track Selected {get{return showCards?cardSelected:(grid.CurrentRow==null?null:grid.CurrentRow.DataBoundItem as Track);}}
  void BindGrid(){if(buildingUi)return;var visible=VisibleTracks();grid.DataSource=new BindingList<Track>(visible);BuildAlbumWall(visible);UpdateStats();UpdateStage(visible.Count);}
  void UpdateStats(){UpdateCounters();}
  void Add(Playlist p){playlists.Add(p);RefreshLists();listSelector.SelectedIndex=playlists.Count-1;SaveTasks();Append("已导入「"+p.Name+"」，"+p.Tracks.Count+" 首。歌曲顺序按原列表保留。");if(!string.IsNullOrEmpty(p.ImportNotice))Append("读取说明："+p.ImportNotice);}
  void ImportFile(){using(var dialog=new OpenFileDialog{Filter="歌单文件|*.csv;*.tsv;*.json;*.m3u;*.m3u8",Multiselect=true})if(dialog.ShowDialog(this)==DialogResult.OK)foreach(string file in dialog.FileNames)Add(Imports.Load(file));}
  void ImportFolder(){using(var dialog=new FolderBrowserDialog{Description="选择明确允许处理的音乐目录（包括子目录）"})if(dialog.ShowDialog(this)==DialogResult.OK)Add(Imports.FromFolder(dialog.SelectedPath));}
  void ChooseSourceFolder(){using(var dialog=new FolderBrowserDialog{Description="选择官方下载目录，只读取你指定目录内的音乐标识"})if(dialog.ShowDialog(this)==DialogResult.OK){if(!store.Settings.SourceFolders.Contains(dialog.SelectedPath))store.Settings.SourceFolders.Add(dialog.SelectedPath);store.SaveSettings();Append("已指定下载目录："+dialog.SelectedPath+"；开始处理时按歌曲 ID 自动关联。");}}
  void ImportAudio(){using(var dialog=new OpenFileDialog{Filter="所有文件|*.*",Multiselect=true})if(dialog.ShowDialog(this)==DialogResult.OK){var p=new Playlist{Name="本地音乐 "+DateTime.Now.ToString("MM-dd HHmm")};foreach(string file in dialog.FileNames)if(Imports.IsAudio(file))p.Tracks.Add(Imports.FromFile(file));if(p.Tracks.Count>0)Add(p);}}
  void BindSource(){if(busy)return;var t=Selected;if(t==null)return;using(var dialog=new OpenFileDialog{Title="选择「"+t.Title+"」对应的官方下载文件",Filter="所有音乐文件|*.*"})if(dialog.ShowDialog(this)==DialogResult.OK){t.SourcePath=dialog.FileName;t.Status="待处理";t.Detail="已手动关联："+dialog.FileName;BindGrid();SaveTasks();}}
  void ForceTrack(){var t=Selected;if(t==null)return;t.Force=true;t.Status="待重新处理";t.OutputPath="";t.Detail="本次忽略历史；需要提供本地源文件";BindGrid();SaveTasks();}
  void ProvideKey(){var t=Selected;if(t==null)return;if(!File.Exists(t.SourcePath))throw new InvalidDataException("请先关联这首歌的下载文件，或运行一次目录关联");if(!SourceDecoder.IsWrapped(t.SourcePath)||Path.GetExtension(t.SourcePath).Equals(".ncm",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("该文件无需提供 QQ ekey");string key=Prompt("提供该文件的 ekey（仅保存在源文件旁，不上传）","",true);if(string.IsNullOrWhiteSpace(key))return;SourceDecoder.ParseEKey(key);Files.AtomicText(t.SourcePath+".ekey",key.Trim());t.Status="待处理";t.Detail="已保存单文件 ekey；开始处理时校验是否匹配";BindGrid();SaveTasks();}
  void OpenSong(){var t=Selected;if(t==null)return;string url=null;if(t.Platform=="网易云音乐"&&t.SongId.All(char.IsDigit)&&t.SongId.Length>0)url="https://music.163.com/song?id="+t.SongId;else if(t.Platform=="QQ音乐"&&t.SongId.Length>0&&t.SongId.All(char.IsLetterOrDigit))url="https://y.qq.com/n/ryqq/songDetail/"+Uri.EscapeDataString(t.SongId);if(url==null)throw new InvalidDataException("该条目没有可打开的原始平台歌曲 ID，请使用官方歌单页面");Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
  async void FetchQQKeys(){if(busy||Current==null)return;
   try {var pending=new List<Track>();foreach(var t in Current.Tracks.Where(t=>File.Exists(t.SourcePath)&&SourceDecoder.IsWrapped(t.SourcePath)&&t.Status!="已完成"&&t.Status!="历史重复")) {try {var identity=SourceDecoder.ReadIdentity(t.SourcePath);if(identity!=null&&identity.Kind=="musicex V1"){identity.Apply(t);pending.Add(t);}}catch(Exception ex){Append("文件标识未确认："+t.Title+" — "+ex.Message);}}
    if(pending.Count==0){MessageBox.Show(this,"当前歌单没有已关联、待处理的 musicex V1 文件。请先运行一次处理以自动关联下载文件。","QQ 单文件密钥");return;}
    string message="仅为当前歌单中 "+pending.Count+" 首已下载的 musicex 歌曲获取密钥。\n\n本操作会读取本机 QQ 音乐当前登录状态，并将必要登录信息发送给 QQ 官方密钥服务。登录令牌不写入设置、历史或日志；得到的单文件 ekey 保存在源文件旁。\n\n是否为这一批文件继续？";
    if(MessageBox.Show(this,message,"使用当前 QQ 音乐登录状态",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return;
    SetBusy(true);cancellation=new CancellationTokenSource();progress.Maximum=pending.Count;progress.Value=0;Append("读取当前 QQ 音乐登录状态，仅用于本批文件密钥……");
    using(var session=await Task.Run(()=>QQKeySession.Open(cancellation.Token))) {
     foreach(var t in pending) {cancellation.Token.ThrowIfCancellationRequested();try{string key=await Task.Run(()=>session.Fetch(t.SourcePath,cancellation.Token));Files.AtomicText(t.SourcePath+".ekey",key);t.Status="待处理";t.Detail="QQ 官方服务已返回单文件密钥，可开始处理";Append(t.Title+"：单文件密钥已取得");}catch(OperationCanceledException){throw;}catch(Exception ex){t.Status="需要密钥";t.Detail="获取未完成："+ex.Message;Append(t.Title+"："+t.Detail);}progress.Value++;BindGrid();SaveTasks();}
    }
    Append("本批密钥请求结束。点击开始处理即可转换；未取得密钥的文件保留。");
   }catch(OperationCanceledException){Append("已停止密钥请求，成功取得的单文件密钥保留。");}catch(Exception ex){MessageBox.Show(this,ex.Message,"QQ 密钥获取未完成");Append("登录状态或密钥服务不可用："+ex.Message);}finally{SetBusy(false);BindGrid();SaveTasks();}
  }
  void RenamePlaylist(){var p=Current;if(p==null)return;string name=Prompt("歌单名称",p.Name);if(!string.IsNullOrWhiteSpace(name)){p.Name=name.Trim();RefreshLists();SaveTasks();}}
  void RemovePlaylist(){var p=Current;if(p==null)return;playlists.Remove(p);RefreshLists();SaveTasks();}
  void SavePlaylist(){var p=Current;if(p==null)return;using(var d=new SaveFileDialog{Filter="完整歌单 JSON|*.json",FileName=Files.SafeName(p.Name)+".json"})if(d.ShowDialog(this)==DialogResult.OK)Files.AtomicText(d.FileName,Json.Write(p));}
  async void ReadLink(){if(busy)return;try{var uri=Imports.OfficialLink(link.Text);SetBusy(true);cancellation=new CancellationTokenSource();Append("读取官方公开歌单页面……");var p=await Task.Run(()=>PublicPlaylist.Read(uri.AbsoluteUri,cancellation.Token));Add(p);}catch(OperationCanceledException){Append("已停止读取。");}catch(Exception ex){MessageBox.Show(this,ex.Message,"歌单读取",MessageBoxButtons.OK,MessageBoxIcon.Information);Append("读取未完成："+ex.Message);}finally{SetBusy(false);}}
  void OpenOfficial(){var p=Current;string url=link.Text;if(string.IsNullOrWhiteSpace(url)&&p!=null)url=p.Link;Process.Start(new ProcessStartInfo(Imports.OfficialLink(url).AbsoluteUri){UseShellExecute=true});}
  async void RunClick(object sender,EventArgs e){if(busy)return;if(playlists.Sum(p=>p.Tracks.Count)==0){Append("请先导入歌曲。");return;}if(!audio.Available){Safe(()=>{throw new IOException("缺少音频组件，请运行 setup-audio.ps1");});return;}
   try{store.SaveSettings();SetBusy(true);cancellation=new CancellationTokenSource();int total=playlists.Sum(p=>p.Tracks.Count),done=0;progress.Maximum=Math.Max(1,total);progress.Value=0;
    var processor=new Processor(store,audio);processor.Log=text=>UI(()=>Append(text));
    Append("读取指定目录的歌曲标识……");var catalog=await Task.Run(()=>LocalCatalog.Build(store.Settings.SourceFolders,audio,cancellation.Token,text=>UI(()=>Append(text))));
    foreach(var p in playlists) {
     foreach(var t in p.Tracks){cancellation.Token.ThrowIfCancellationRequested();t.Status="处理中";BindGrid();await Task.Run(()=>{TryIdAssociation(t,catalog);processor.Process(t,cancellation.Token);});done++;progress.Value=Math.Min(done,total);BindGrid();SaveTasks();}
     string playlistPath=PlaylistWriter.Write(p,store.Settings.OutputRoot);Append("已生成播放列表："+playlistPath);
     string report=Path.Combine(store.Settings.OutputRoot,"Reports",Files.SafeName(p.Name)+"_"+Files.HashText(p.Link+"|"+p.Name).Substring(0,8)+".json");Files.AtomicText(report,Json.Write(p));
    }
    Append("处理结束。请把导出 Music 目录的内容按原结构合并复制到播放器 Music 目录。历史复用不会核实设备文件。");
   }catch(OperationCanceledException){Append("已停止；成功记录已保存，未完成项可重新运行。");}catch(Exception ex){Append("批次中断："+ex.Message);MessageBox.Show(this,ex.Message,"处理未完成");}finally{SetBusy(false);BindGrid();SaveTasks();}
  }
  void TryIdAssociation(Track t,LocalCatalog catalog) {
   if(File.Exists(t.SourcePath)||string.IsNullOrEmpty(t.SongId)||store.Identity(t)!=null)return;
   var matches=catalog.Matches(t);
   if(matches.Count==1){t.SourcePath=matches[0];t.Detail="通过原始歌曲 ID 自动关联";}else if(matches.Count>1)t.Detail="同歌曲 ID 有多份文件，请手动关联版本/品质";
  }
  void CancelClick(object s,EventArgs e){if(cancellation!=null)cancellation.Cancel();}
  void SetBusy(bool value){busy=value;foreach(var c in mutable)c.Enabled=!value;run.Enabled=!value;cancel.Enabled=value;UseWaitCursor=value;}
  void UI(Action action){if(IsDisposed)return;if(InvokeRequired)BeginInvoke(action);else action();}
  void Append(string text){log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);log.SelectionStart=log.Text.Length;log.ScrollToCaret();if(activityCaption!=null){activityCaption.Text=text;activityCaption.Invalidate();}}
  void SaveTasks(){Files.AtomicText(Path.Combine(store.Root,"tasks.json"),Json.Write(playlists));}
  void OpenFolder(string folder){Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo("explorer.exe",AudioTool.Quote(folder)){UseShellExecute=true});}
  static string Prompt(string title,string current,bool secret=false){using(var f=new Form{Text=title,Width=560,Height=170,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false}){var t=new TextBox{Text=current,Left=18,Top=20,Width=505,UseSystemPasswordChar=secret,MaxLength=65536};var ok=new Button{Text="保存",Left=425,Top=65,Width=95,DialogResult=DialogResult.OK};f.Controls.Add(t);f.Controls.Add(ok);f.AcceptButton=ok;return f.ShowDialog()==DialogResult.OK?t.Text:null;}}
  void Demo(){var p=new Playlist{Name="午后与夜晚 · 示例歌单"};p.Tracks.Add(new Track{Title="远山",Artist="示例歌手",Platform="网易云音乐",Status="已完成",Detail="NCM → FLAC，24 bit / 96 kHz 校验通过"});p.Tracks.Add(new Track{Title="海风",Artist="示例歌手",Platform="网易云音乐",Status="历史重复",Detail="复用历史路径，无须重复下载"});p.Tracks.Add(new Track{Title="归途（现场版）",Artist="示例乐队",Platform="QQ音乐",Status="待官方下载",Detail="官方下载完成后，按原歌曲 ID 关联"});p.Tracks.Add(new Track{Title="星河",Artist="示例歌手",Platform="QQ音乐",Status="需要密钥",Detail="musicex V1，可主动授权 QQ 客户端取密钥"});playlists.Clear();playlists.Add(p);}
 }
}
