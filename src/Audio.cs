using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MusicAssistant {
 public sealed class Probe {
  public string Raw; public Dictionary<string,object> Audio; public Dictionary<string,object> Tags;
  public string Codec {get {return Json.Str(Audio,"codec_name");}}
  public int Rate {get {return Json.Num(Audio,"sample_rate");}} public int Channels {get {return Json.Num(Audio,"channels");}}
  public int Bits {get {int n=Json.Num(Audio,"bits_per_raw_sample");if(n==0)n=Json.Num(Audio,"bits_per_sample");if(n==0){string f=Json.Str(Audio,"sample_fmt");n=f.StartsWith("s16")?16:f.StartsWith("s32")?32:f.StartsWith("dbl")?64:f.StartsWith("flt")?32:0;}return n;}}
  public bool Floating {get {string f=Json.Str(Audio,"sample_fmt");return f.StartsWith("flt")||f.StartsWith("dbl");}}
  public string Profile {get {return Rate+"|"+Channels+"|"+Bits+"|"+(Floating?"float":"integer");}}
 }
 public sealed class AudioTool {
  public string Ffmpeg {get;private set;} public string Ffprobe {get;private set;}
  public AudioTool(string tools) {Ffmpeg=Path.Combine(tools,"ffmpeg.exe");Ffprobe=Path.Combine(tools,"ffprobe.exe");}
  public bool Available {get {return File.Exists(Ffmpeg)&&File.Exists(Ffprobe);}}
  // Windows CRT quoting, including trailing backslashes; no shell invocation.
  public static string Quote(string value) {
   var s=new StringBuilder("\"");int slashes=0;foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='"'){s.Append('\\',slashes*2+1).Append(c);slashes=0;continue;}s.Append('\\',slashes).Append(c);slashes=0;}return s.Append('\\',slashes*2).Append('"').ToString();
  }
  public string Run(string executable,IEnumerable<string> args,CancellationToken ct) {
   if(!File.Exists(executable))throw new FileNotFoundException("缺少音频组件，请运行 setup-audio.ps1",executable);
   var info=new ProcessStartInfo(executable,string.Join(" ",args.Select(Quote))) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
   using(var p=Process.Start(info)) {
    var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();var timer=Stopwatch.StartNew();
    while(!p.WaitForExit(100)) {if(ct.IsCancellationRequested||timer.Elapsed.TotalMinutes>30){try{p.Kill();}catch{}p.WaitForExit();ct.ThrowIfCancellationRequested();throw new TimeoutException("音频处理超时，原文件保留");}}
    string result=stdout.GetAwaiter().GetResult();string error=stderr.GetAwaiter().GetResult();ct.ThrowIfCancellationRequested();
    if(p.ExitCode!=0)throw new InvalidDataException("音频组件失败："+(error.Length>1000?error.Substring(error.Length-1000):error));return result;
   }
  }
  public Probe Inspect(string path,CancellationToken ct) {
   string raw=Run(Ffprobe,new[]{"-v","error","-show_streams","-show_format","-of","json",path},ct);
   var data=Json.Object(raw);object streamsValue;if(!data.TryGetValue("streams",out streamsValue))throw new InvalidDataException("找不到音频流");
   var streams=((IEnumerable)streamsValue).Cast<object>().OfType<Dictionary<string,object>>().Where(s=>Json.Str(s,"codec_type")=="audio").ToList();
   if(streams.Count!=1)throw new InvalidDataException("只处理单音频流文件，避免丢失其他音轨");
   var tags=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);object fmtValue;
   if(data.TryGetValue("format",out fmtValue)){var fmt=fmtValue as Dictionary<string,object>;object ts;if(fmt!=null&&fmt.TryGetValue("tags",out ts))foreach(var pair in (Dictionary<string,object>)ts)tags[pair.Key]=pair.Value;}
   object streamTags;if(streams[0].TryGetValue("tags",out streamTags))foreach(var pair in (Dictionary<string,object>)streamTags)tags[pair.Key]=pair.Value;
   return new Probe{Raw=raw,Audio=streams[0],Tags=tags};
  }
  public string AudioHash(string path,Probe probe,CancellationToken ct) {
   if(probe.Codec.StartsWith("dsd"))return Files.Hash(path); // DSD is kept byte-for-byte, never converted to PCM.
   string pcm=probe.Floating?"pcm_f64le":"pcm_s32le";
   if(!probe.Floating&&probe.Bits>32)throw new InvalidDataException("无法对超过 32 位整数音频保证精确校验");
   return Run(Ffmpeg,new[]{"-v","error","-xerror","-err_detect","explode","-i",path,"-map","0:a:0","-c:a",pcm,"-f","hash","-hash","sha256","-"},ct).Trim();
  }
  public string Extension(Probe p,string source,bool preferFlac) {
   if(p.Channels<1||p.Channels>2)throw new InvalidDataException("播放器兼容规则仅验证了单声道/双声道；多声道保留源文件并跳过");
   string ext=Path.GetExtension(source).ToLowerInvariant();string c=p.Codec;int bitrate=Json.Num(p.Audio,"bit_rate");
   if(c.StartsWith("dsd")) {if(!(ext==".dsf"||ext==".dff")||!new[]{352800,705600,1411200}.Contains(p.Rate))throw new InvalidDataException("DSD 参数/容器未确认兼容");return ext;}
   if(c=="mp3") {if(!new[]{32000,44100,48000}.Contains(p.Rate)||bitrate<32000||bitrate>320000)throw new InvalidDataException("MP3 参数超出金砖二代规格");return ".mp3";}
   if(c=="aac") {
    string profile=Json.Str(p.Audio,"profile");bool he=profile.StartsWith("HE");
    if(p.Rate<8000||p.Rate>48000||bitrate<(he?32000:16000)||bitrate>(he?144000:320000))throw new InvalidDataException("AAC 参数超出金砖二代规格或码率未知");
    if(ext==".aac"||ext==".m4a"||ext==".mp4")return ext==".mp4"?".m4a":ext;
    return ".m4a";
   }
   if(c=="wmav2") {if(p.Rate!=44100||bitrate<32000||bitrate>192000)throw new InvalidDataException("WMA 参数超出播放器规格");return ".wma";}
   if(p.Rate<8000||p.Rate>384000)throw new InvalidDataException("采样率超出播放器支持范围；不降采样");
   if((c=="vorbis"||c=="opus")&&p.Floating&&p.Bits==32)return ".wav"; // preserve the decoded float samples, never re-encode to MP3.
   bool pcm=c.StartsWith("pcm_");bool lossless=pcm||new[]{"flac","alac","ape","wavpack"}.Contains(c);
   if(!lossless)throw new InvalidDataException("源编码不在已验证兼容范围；不重新有损编码");
   if(p.Bits!=16&&p.Bits!=24&&p.Bits!=32)throw new InvalidDataException("位深超出已验证范围；不降低位深");
   if(p.Bits==32) {if(pcm&&(ext==".wav"||ext==".aif"||ext==".aiff"))return ext;if(pcm)return ".wav";throw new InvalidDataException("32 位压缩音频不能直接转为 24 位 FLAC");}
   if((preferFlac||c=="ape"||c=="wavpack")&&!p.Floating)return ".flac";
   if(c=="flac")return ".flac";if(c=="alac")return ".m4a";
   if(pcm){if(ext==".wav"||ext==".aif"||ext==".aiff")return ext;return ".wav";}
   return ".flac";
  }
  public void Export(string source,string output,Probe p,string extension,CancellationToken ct,DecodedSource container=null) {
   string original=Path.GetExtension(source).ToLowerInvariant();
   bool extra=container!=null&&(container.Tags.Count>0||!string.IsNullOrEmpty(container.CoverPath));
   if(!extra&&(original==extension||(original==".mp4"&&extension==".m4a"))) {File.Copy(source,output,false);return;}
   string codec=(extension==".flac"&&p.Codec!="flac")?"flac":extension==".wav"?(p.Floating?"pcm_f32le":p.Bits==32?"pcm_s32le":p.Bits==24?"pcm_s24le":"pcm_s16le"):"copy";
   var args=new List<string>{"-v","error","-xerror","-nostdin","-n","-i",source};
   bool embed=container!=null&&!string.IsNullOrEmpty(container.CoverPath)&&(extension==".flac"||extension==".mp3"||extension==".m4a");if(embed)args.AddRange(new[]{"-i",container.CoverPath});
   args.AddRange(new[]{"-map","0:a:0","-map_metadata","0","-c:a",codec});
   // Non-audio metadata is also archived in the sidecar. Copy embedded cover where the muxer supports it.
   if(extension==".flac"||extension==".mp3"||extension==".m4a")args.AddRange(new[]{"-map","0:v?","-c:v","copy"});
   if(embed)args.AddRange(new[]{"-map","1:v:0","-disposition:v","attached_pic"});
   if(container!=null)foreach(var tag in container.Tags){args.Add("-metadata");args.Add(tag.Key+"="+tag.Value);}
   if(codec=="flac")args.AddRange(new[]{"-sample_fmt",p.Bits==16?"s16":"s32","-compression_level","8"});
   args.Add(output);Run(Ffmpeg,args,ct);
  }
  public void ExportCovers(string source,string folder,Probe p,CancellationToken ct) {
   var data=Json.Object(p.Raw);object value;if(!data.TryGetValue("streams",out value))return;
   int index=0;foreach(var s in ((IEnumerable)value).Cast<object>().OfType<Dictionary<string,object>>()) {
    if(Json.Str(s,"codec_type")!="video")continue;object disposition;var d=s.TryGetValue("disposition",out disposition)?disposition as Dictionary<string,object>:null;
    if(Json.Num(d,"attached_pic")!=1)throw new InvalidDataException("文件含有非封面视频流，不能保证信息完整");
    string c=Json.Str(s,"codec_name");if(c!="mjpeg"&&c!="png")throw new InvalidDataException("封面编码未验证，源文件保留");
    string path=Path.Combine(folder,"cover"+(index++==0?"":"_"+index)+(c=="png"?".png":".jpg"));
    Run(Ffmpeg,new[]{"-v","error","-nostdin","-n","-i",source,"-map","0:"+Json.Str(s,"index"),"-c:v","copy","-frames:v","1",path},ct);
    if(!File.Exists(path)||new FileInfo(path).Length==0)throw new InvalidDataException("封面提取失败");
   }
  }
 }
 public sealed class Processor {
  readonly Store store; readonly AudioTool audio; public Action<string> Log;
  public Processor(Store store,AudioTool audio) {this.store=store;this.audio=audio;Log=s=>{};}
  public static bool Protected(string path) {return SourceDecoder.IsWrapped(path);}
  public void Process(Track t,CancellationToken ct) {
   ct.ThrowIfCancellationRequested();t.OutputPath="";
   if(string.IsNullOrEmpty(t.SourcePath)||!File.Exists(t.SourcePath)) {
    var old=store.Identity(t);if(old!=null){Reuse(t,old,"历史记录复用（不检查播放器）");return;}
    t.Status="待官方下载";t.Detail="缺少文件或关联；请官方下载后选择对应文件。多个品质历史不会自动合并。";return;
   }
   string source=Path.GetFullPath(t.SourcePath);
   string temp=null,decodeFolder=null;bool committed=false;string sourceHash="";string relative="";
   try {
    t.Status="校验中";
    using(var guard=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)) {
     string music=Path.Combine(store.Settings.OutputRoot,"Music");
     if(source.StartsWith(Path.GetFullPath(music).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("源文件在导出库内；请使用原下载文件，避免替换导出结果");
     string input=source;DecodedSource container=null;
     if(Protected(source)) {decodeFolder=Path.Combine(store.Settings.OutputRoot,".working","decode_"+Guid.NewGuid().ToString("N"));container=SourceDecoder.Decode(source,decodeFolder,ct);container.Apply(t);input=container.AudioPath;}
     var probe=audio.Inspect(input,ct);string title=Json.Str(probe.Tags,"title"),artist=Json.Str(probe.Tags,"artist"),album=Json.Str(probe.Tags,"album");
     if(string.IsNullOrWhiteSpace(t.Title)||t.Title==Path.GetFileNameWithoutExtension(source))if(title.Length>0)t.Title=title;
     if(string.IsNullOrWhiteSpace(t.Artist))t.Artist=artist;if(string.IsNullOrWhiteSpace(t.Album))t.Album=album;
     string extension=audio.Extension(probe,input,store.Settings.PreferFlac);string hash=audio.AudioHash(input,probe,ct);
     var existing=store.Exact(hash,probe.Profile,t);
     if(existing!=null){RecordAlias(t,existing);Reuse(t,existing,"音频内容与参数一致，复用历史路径");return;}
     sourceHash=Files.Hash(source);string folder=Files.ShortName(t.Artist,24)+" - "+Files.ShortName(t.Title,32)+"_"+Files.HashText(hash+"|"+probe.Profile+"|"+t.Title+"|"+t.Artist).Substring(0,20);
     string file=Files.SafeName(t.Title)+extension;relative="Library/"+folder+"/"+file;
     string destination=Files.Inside(music,relative);
     temp=Path.Combine(store.Settings.OutputRoot,".working",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);string output=Path.Combine(temp,file);
     audio.Export(input,output,probe,extension,ct,container);var result=audio.Inspect(output,ct);
     if(result.Rate!=probe.Rate||result.Channels!=probe.Channels||result.Bits!=probe.Bits||result.Floating!=probe.Floating)throw new InvalidDataException("音频参数变化，取消替换源文件");
     if(audio.AudioHash(output,probe,ct)!=hash)throw new InvalidDataException("解码音频校验不一致，取消替换源文件");
     long size=new FileInfo(output).Length;if(size>=4L*1024*1024*1024)throw new InvalidDataException("输出文件达到 4 GB，播放器无法播放");
     audio.ExportCovers(input,temp,probe,ct);
     if(container!=null&&!string.IsNullOrEmpty(container.CoverPath)) {string cover=Path.Combine(temp,Path.GetFileName(container.CoverPath));File.Copy(container.CoverPath,cover,false);if(Files.Hash(cover)!=Files.Hash(container.CoverPath))throw new InvalidDataException("封装封面完整性校验失败");}
     PreserveSidecars(source,output,temp);
     string[] coverCandidates={Path.Combine(temp,"container-cover.jpg"),Path.Combine(temp,"container-cover.png"),Path.Combine(temp,"cover.jpg"),Path.Combine(temp,"cover.png"),Path.ChangeExtension(output,".jpg"),Path.ChangeExtension(output,".jpeg"),Path.ChangeExtension(output,".png")};
     string playerCover=coverCandidates.FirstOrDefault(File.Exists);if(playerCover!=null) {string coverTarget=Path.Combine(temp,folder+Path.GetExtension(playerCover));File.Copy(playerCover,coverTarget,false);if(Files.Hash(playerCover)!=Files.Hash(coverTarget))throw new InvalidDataException("播放器外置封面校验失败");}
     Files.AtomicText(Path.Combine(temp,"source-info.json"),Json.Write(new{SourcePath=source,SourceSha256=sourceHash,RecoveredPayloadSha256=container==null?null:Files.Hash(input),Protection=container==null?null:container.Kind,ContainerMetadata=container==null?null:container.Metadata,AudioHash=hash,Profile=probe.Profile,SourceProbe=Json.Object(probe.Raw),Platform=t.Platform,SongId=t.SongId,Quality=t.Quality,ProcessedAt=DateTimeOffset.Now.ToString("o")}));
     ct.ThrowIfCancellationRequested();string destDir=Path.GetDirectoryName(destination);Directory.CreateDirectory(Path.GetDirectoryName(destDir));
     if(Directory.Exists(destDir)) {
      if(!t.Force)throw new IOException("同内容导出目录已存在，请先检查或选择重新处理；不会覆盖");
      if(File.Exists(destination)) {var existingProbe=audio.Inspect(destination,ct);if(existingProbe.Profile!=probe.Profile||audio.AudioHash(destination,probe,ct)!=hash)throw new IOException("固定路径已有不同音频，保留两份源文件，请先检查");}
      // Restore the same canonical path so previous device playlists remain valid.
      // Archive conflicting metadata instead of overwriting files from an earlier run.
      foreach(string pending in Directory.GetFiles(temp)) {
       string target=Path.Combine(destDir,Path.GetFileName(pending));
       if(File.Exists(target)) {if(Files.Hash(pending)==Files.Hash(target))continue;if(target==destination)continue;target=Path.Combine(destDir,Path.GetFileNameWithoutExtension(pending)+"_archive_"+Guid.NewGuid().ToString("N").Substring(0,12)+Path.GetExtension(pending));}
       File.Move(pending,target);
      }
     } else {Directory.Move(temp,destDir);temp=null;}
     committed=true;
     var entry=new HistoryEntry {Key=Guid.NewGuid().ToString("N"),Title=t.Title,Artist=t.Artist,Album=t.Album,Platform=t.Platform,SongId=t.SongId,AlternateSongId=t.AlternateSongId,Quality=t.Quality,AudioHash=hash,Profile=probe.Profile,RelativePath=relative,ProcessedAt=DateTimeOffset.Now.ToString("o")};
     store.Add(entry);t.HistoryKey=entry.Key;t.OutputPath=relative;t.Status="已完成";t.Detail=(container==null?"":container.Kind+" 载荷已恢复；")+"音频、参数及附加信息已校验";t.Force=false;
    }
    if(store.Settings.RecycleOriginal) {
     try {if(ct.IsCancellationRequested){t.Detail+="；已取消，源文件保留";}else if(Files.Hash(source)!=sourceHash){t.Detail+="；源文件发生变化，保留";}else {Recycle.Send(source);t.Detail+="；原文件已移入回收站";}}
     catch(Exception ex){t.Detail+="；回收未完成，源文件保留："+ex.Message;}
    }
   } catch(OperationCanceledException) {t.Status="已取消";t.Detail=committed?"输出已生成；源文件保留，请检查历史":"原文件保留";throw;}
   catch(MissingKeyException ex){t.Status="需要密钥";t.Detail=ex.Message+"；原文件保留";}
   catch(Exception ex){t.Status="失败";t.Detail=ex.Message+(committed?"；输出已生成但提交未完成，原文件保留":"；原文件保留");}
   finally {CleanupWorking(temp);CleanupWorking(decodeFolder);Log(t.Title+"："+t.Status+" — "+t.Detail);}
  }
  void CleanupWorking(string path) {if(path==null||!Directory.Exists(path))return;try {string working=Path.GetFullPath(Path.Combine(store.Settings.OutputRoot,".working")).TrimEnd('\\')+"\\";if(Path.GetFullPath(path).StartsWith(working,StringComparison.OrdinalIgnoreCase)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0)Directory.Delete(path,true);}catch{}}
  void RecordAlias(Track t,HistoryEntry e) {
   if(string.IsNullOrWhiteSpace(t.SongId)||store.History.Any(h=>h.Platform==t.Platform&&h.SongId==t.SongId&&h.AudioHash==e.AudioHash&&h.Profile==e.Profile))return;
   store.Add(new HistoryEntry {Key=Guid.NewGuid().ToString("N"),Title=t.Title,Artist=t.Artist,Album=t.Album,Platform=t.Platform,SongId=t.SongId,AlternateSongId=t.AlternateSongId,Quality=t.Quality,AudioHash=e.AudioHash,Profile=e.Profile,RelativePath=e.RelativePath,ProcessedAt=DateTimeOffset.Now.ToString("o")});
  }
  static void Reuse(Track t,HistoryEntry entry,string reason) {t.HistoryKey=entry.Key;t.OutputPath=entry.RelativePath;t.Status="历史重复";t.Detail=reason;}
  static void PreserveSidecars(string source,string output,string temp) {
   string stem=Path.Combine(Path.GetDirectoryName(source),Path.GetFileNameWithoutExtension(source));
   foreach(string ext in new[]{".lrc",".txt",".jpg",".jpeg",".png"}) {string side=stem+ext;if(!File.Exists(side))continue;string target=Path.Combine(temp,Path.GetFileNameWithoutExtension(output)+ext);
    if(ext==".lrc") {byte[] bytes=File.ReadAllBytes(side);string text;
     try {text=new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){throw new InvalidDataException("歌词不是有效 UTF-8；请转换歌词编码后重试，原文件保留");}
     byte[] converted=new UTF8Encoding(false).GetBytes(text.TrimStart('\ufeff'));if(converted.Length>512*1024)throw new InvalidDataException("歌词超过播放器 512 KB 限制");File.WriteAllBytes(target,converted);
     File.WriteAllBytes(Path.Combine(temp,"source-lyrics.lrc"),bytes);
    } else {File.Copy(side,target,false);if(Files.Hash(side)!=Files.Hash(target))throw new InvalidDataException("配套文件校验失败");}
   }
  }
 }
}
