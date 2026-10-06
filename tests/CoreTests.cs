using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MusicAssistant;

static class CoreTests {
 static int passed;static string root;static AudioTool audio;
 static void Assert(bool condition,string message){if(!condition)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
 static string Make(string name,string codec,int rate,int bits){string path=Path.Combine(root,name);audio.Run(audio.Ffmpeg,new[]{"-v","error","-nostdin","-n","-f","lavfi","-i","sine=frequency=440:duration=0.3:sample_rate="+rate,"-ac","2","-c:a",codec,"-metadata","title=测试歌曲","-metadata","artist=测试歌手","-metadata","album=测试专辑",path},CancellationToken.None);return path;}
 static void Main(string[] args){try{root=Path.Combine(Path.GetFullPath(args.Length>0?args[0]:".test-output"),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);audio=new AudioTool(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools"));Assert(audio.Available,"音频组件可用");Run();Console.WriteLine("ALL PASSED: "+passed+" — "+root);}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}}
 static void Run(){
  StartupTests.Run(root,Assert);
  PlaylistTests.Run(Assert);
  Assert(Files.SafeName("CON")=="_CON"&&Files.SafeName("a/b:c")=="a_b_c","Windows 文件名和保留名称");
  bool traversal=false;try{Files.Inside(root,"../outside");}catch(InvalidDataException){traversal=true;}Assert(traversal,"阻止导出目录穿越");
  Assert(AudioTool.Quote("x\\")=="\"x\\\\\""&&AudioTool.Quote("a\"b")=="\"a\\\"b\"","进程参数转义（不经过 shell）");
  var store=new Store(Path.Combine(root,"state"));store.Settings.OutputRoot=Path.Combine(root,"export");store.Settings.RecycleOriginal=false;store.Settings.PreferFlac=true;store.SaveSettings();
  string source=Make("24bit.wav","pcm_s24le",96000,24);File.WriteAllText(Path.ChangeExtension(source,".lrc"),"[00:00.00]中文歌词\n",new UTF8Encoding(false));
  var t=new Track{Title="测试歌曲",Artist="测试歌手",Platform="QQ音乐",SongId="song-1",SourcePath=source,Quality="24/96"};var processor=new Processor(store,audio);processor.Process(t,CancellationToken.None);
  Assert(t.Status=="已完成","24 位 WAV 转 FLAC 成功："+t.Detail);Assert(File.Exists(source),"关闭替换时保留源文件");
  string output=Files.Inside(Path.Combine(store.Settings.OutputRoot,"Music"),t.OutputPath);var originalProbe=audio.Inspect(source,CancellationToken.None);var result=audio.Inspect(output,CancellationToken.None);
  Assert(result.Bits==24&&result.Rate==96000&&result.Channels==2,"采样率、位深、声道不变");Assert(audio.AudioHash(source,originalProbe,CancellationToken.None)==audio.AudioHash(output,originalProbe,CancellationToken.None),"转换前后完整解码样本一致");
  Assert(File.Exists(Path.ChangeExtension(output,".lrc"))&&File.Exists(Path.Combine(Path.GetDirectoryName(output),"source-info.json")),"歌词及完整源信息归档");Assert(Json.Str(result.Tags,"title")=="测试歌曲","标签保留");
  var cross=new Track{Title=t.Title,Artist=t.Artist,SourcePath=source,Platform="网易云音乐",SongId="song-2",Quality="24/96"};processor.Process(cross,CancellationToken.None);Assert(cross.Status=="历史重复"&&cross.OutputPath==t.OutputPath,"跨平台精确音频去重，共享文件");Assert(store.History.Any(h=>h.Platform=="网易云音乐"&&h.SongId=="song-2"),"记录跨平台身份别名");
  // Only remove synthetic test output under the exact test workspace.
  Assert(Path.GetFullPath(output).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"测试删除目标在临时测试目录内");File.Delete(output);
  var reloaded=new Store(store.Root);var historyTrack=new Track{Title=t.Title,Platform=t.Platform,SongId=t.SongId,Quality=t.Quality};new Processor(reloaded,audio).Process(historyTrack,CancellationToken.None);Assert(historyTrack.Status=="历史重复"&&historyTrack.OutputPath==t.OutputPath,"本地输出删除、程序重启后仍按历史去重");
  string canonical=t.OutputPath;t.Force=true;processor.Process(t,CancellationToken.None);Assert(t.Status=="已完成"&&t.OutputPath==canonical&&File.Exists(output),"强制重新处理恢复原固定路径，不破坏旧歌单引用");
  var p1=new Playlist{Name="中文歌单一",Tracks=new List<Track>{t,cross}};var p2=new Playlist{Name="中文歌单二",Tracks=new List<Track>{historyTrack}};string playlist=PlaylistWriter.Write(p1,store.Settings.OutputRoot);string second=PlaylistWriter.Write(p2,store.Settings.OutputRoot);
  string[] lines=File.ReadAllLines(playlist,Encoding.UTF8);Assert(lines.Count(l=>l.StartsWith("../Library/"))==2&&File.ReadAllText(second).Contains("../"+t.OutputPath),"多个歌单引用同一历史文件并保留顺序");
  string ncm=Path.Combine(root,"protected.ncm");File.WriteAllText(ncm,"test");var protectedTrack=Imports.FromFile(ncm);int before=store.History.Count;processor.Process(protectedTrack,CancellationToken.None);Assert(protectedTrack.Status=="失败"&&File.Exists(ncm)&&store.History.Count==before,"损坏封装文件保留、不写成功历史");
  string bad=Path.Combine(root,"broken.flac");File.WriteAllText(bad,"broken");var broken=Imports.FromFile(bad);processor.Process(broken,CancellationToken.None);Assert(broken.Status=="失败"&&File.Exists(bad)&&store.History.Count==before,"损坏输入保留、不写成功历史");
  string source32=Make("32bit.wav","pcm_s32le",48000,32);var bit32=Imports.FromFile(source32);processor.Process(bit32,CancellationToken.None);Assert(bit32.Status=="已完成"&&bit32.OutputPath.EndsWith(".wav"),"32 位源保留 WAV，不降为 24 位 FLAC");
  string floatSource=Make("float.wav","pcm_f32le",48000,32);var floating=Imports.FromFile(floatSource);processor.Process(floating,CancellationToken.None);Assert(floating.Status=="已完成"&&floating.OutputPath.EndsWith(".wav"),"浮点 WAV 精度保留，完整样本校验");
  string mp3Source=Make("audio.mp3","libmp3lame",44100,0);var mp3=Imports.FromFile(mp3Source);processor.Process(mp3,CancellationToken.None);Assert(mp3.Status=="已完成"&&Files.Hash(Files.Inside(Path.Combine(store.Settings.OutputRoot,"Music"),mp3.OutputPath))==Files.Hash(mp3Source),"已有 MP3 原样保留，不二次有损编码");
  string alacSource=Make("audio.m4a","alac",44100,16);var alac=Imports.FromFile(alacSource);processor.Process(alac,CancellationToken.None);Assert(alac.Status=="已完成"&&alac.OutputPath.EndsWith(".flac"),"ALAC 无损整理为 FLAC");
  string cover=Path.Combine(root,"picture.png");audio.Run(audio.Ffmpeg,new[]{"-v","error","-f","lavfi","-i","color=c=blue:s=32x32:d=0.1","-frames:v","1",cover},CancellationToken.None);
  string covered=Path.Combine(root,"covered.flac");audio.Run(audio.Ffmpeg,new[]{"-v","error","-i",source,"-i",cover,"-map","0:a","-map","1:v","-c:a","flac","-c:v","copy","-disposition:v","attached_pic","-metadata","title=有封面的歌曲",covered},CancellationToken.None);
  var coverTrack=Imports.FromFile(covered);processor.Process(coverTrack,CancellationToken.None);Assert(coverTrack.Status=="已完成","含内嵌封面的 FLAC 处理："+coverTrack.Detail);string coverOutput=Files.Inside(Path.Combine(store.Settings.OutputRoot,"Music"),coverTrack.OutputPath);Assert(Files.Hash(Path.Combine(Path.GetDirectoryName(coverOutput),"cover.png"))==Files.Hash(cover),"提取内嵌 PNG 封面，内容原样保留");
  string invalidLyricsSource=Make("invalid-lyrics.wav","pcm_s16le",32000,16);File.WriteAllBytes(Path.ChangeExtension(invalidLyricsSource,".lrc"),new byte[]{255,254,13});var invalidLyrics=Imports.FromFile(invalidLyricsSource);int lyricHistory=store.History.Count;processor.Process(invalidLyrics,CancellationToken.None);Assert(invalidLyrics.Status=="失败"&&File.Exists(invalidLyricsSource)&&store.History.Count==lyricHistory,"无法保留歌词时停止提交并保留源文件");
  var cts=new CancellationTokenSource();cts.Cancel();bool cancelled=false;try{processor.Process(Imports.FromFile(source),cts.Token);}catch(OperationCanceledException){cancelled=true;}Assert(cancelled&&File.Exists(source),"取消任务不删除源文件");
  var rates=new Store(Path.Combine(root,"identity"));rates.Add(new HistoryEntry{Key="a",Platform="QQ音乐",SongId="same",AudioHash="a",Profile="16/44",Quality="standard"});rates.Add(new HistoryEntry{Key="b",Platform="QQ音乐",SongId="same",AudioHash="b",Profile="24/96",Quality="hires"});Assert(rates.Identity(new Track{Platform="QQ音乐",SongId="same"})==null,"不同品质历史不能静默合并");
  Assert(rates.Identity(new Track{Platform="QQ音乐",SongId="same",Quality="hires"}).Key=="b","显式品质选择对应历史");
  string csv=Path.Combine(root,"list.csv");File.WriteAllText(csv,"歌名,歌手,平台,歌曲ID,文件路径\r\n\"带,逗号\",测试歌手,QQ音乐,abc,24bit.wav\r\n",new UTF8Encoding(true));var imported=Imports.Load(csv);Assert(imported.Tracks.Count==1&&imported.Tracks[0].Title=="带,逗号"&&imported.Tracks[0].SourcePath==source,"CSV 中文标题、引号及相对路径解析");
  string html="<title>公开歌单 - 歌单 - 网易云音乐</title><span id=\"playlist-track-count\">2</span><ul class=\"f-hide\"><li><a href=\"/song?id=1\">甲 &amp; 乙</a></li><li><a href=\"/song?id=2\">丙</a></li></ul>";var publicList=PublicPlaylist.Parse(html,"https://music.163.com/playlist?id=1");Assert(publicList.Tracks.Count==2&&publicList.Tracks[0].SongId=="1"&&publicList.Tracks[0].Title=="甲 & 乙","读取公开网页歌曲 ID、顺序与转义名称");
  bool partial=false;try{PublicPlaylist.Parse(html.Replace(">2</span>",">3</span>"),"https://music.163.com/playlist?id=1");}catch(InvalidDataException){partial=true;}Assert(partial,"拒绝把不完整网页当完整歌单");
  bool host=false;try{Imports.OfficialLink("https://y.qq.com.evil.example/list");}catch(InvalidDataException){host=true;}Assert(host,"分享链接严格限制官方主机");
  Assert(Imports.OfficialLink("https://music.163.com/#/playlist?id=1").Host=="music.163.com","网易带 fragment 分享链接接受");
  bool corrupt=false;File.WriteAllText(Path.Combine(rates.Root,"history.json"),"broken");try{new Store(rates.Root);}catch{corrupt=true;}Assert(corrupt,"损坏历史不静默清空");
  string recyclable=Path.Combine(root,"recycle-synthetic.txt");File.WriteAllText(recyclable,"synthetic recycle test");Recycle.Send(recyclable);Assert(!File.Exists(recyclable),"Windows 强制回收操作（仅合成测试文件）");
  store.Settings.RecycleOriginal=true;string autoRecycleSource=Make("auto-recycle.wav","pcm_s16le",22050,16);var autoRecycle=Imports.FromFile(autoRecycleSource);processor.Process(autoRecycle,CancellationToken.None);Assert(autoRecycle.Status=="已完成"&&!File.Exists(autoRecycleSource)&&File.Exists(Files.Inside(Path.Combine(store.Settings.OutputRoot,"Music"),autoRecycle.OutputPath)),"输出提交与历史持久保存后才回收合成源音乐");
  var live=PublicPlaylist.Read("https://music.163.com/#/playlist?id=3778678",CancellationToken.None);Assert(live.Tracks.Count>0&&live.Tracks.All(item=>item.Platform=="网易云音乐"&&!string.IsNullOrEmpty(item.SongId)),"真实网易官方公开歌单读取与 fragment 规范化");
  DecoderTests.Run(root,audio,Assert);
 }
}
