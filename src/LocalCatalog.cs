using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MusicAssistant {
 public sealed class CatalogFile {public string Path;public string Platform;public string SongId;public string AlternateSongId;public string Quality;}
 public sealed class LocalCatalog {
  readonly List<CatalogFile> files=new List<CatalogFile>();
  public static LocalCatalog Build(IEnumerable<string> folders,AudioTool audio,CancellationToken ct,Action<string> log) {
   var catalog=new LocalCatalog();var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(string folder in folders) {
    if(!Directory.Exists(folder)){log("指定目录不存在："+folder);continue;}
    try {foreach(string path in Imports.EnumerateAudio(folder)) {
     ct.ThrowIfCancellationRequested();if(!visited.Add(Path.GetFullPath(path)))continue;
     try {
      var item=new CatalogFile{Path=path};string declared=path+".track.json";
      if(File.Exists(declared)){var row=Json.Read<Track>(File.ReadAllText(declared,Encoding.UTF8));if(row!=null){item.Platform=Imports.NormalizePlatform(row.Platform);item.SongId=row.SongId;item.AlternateSongId=row.AlternateSongId;item.Quality=row.Quality;}}
      else if(SourceDecoder.IsWrapped(path)){var identity=SourceDecoder.ReadIdentity(path);if(identity!=null){item.Platform=identity.Platform;item.SongId=identity.SongId;}}
      else {var probe=audio.Inspect(path,ct);string netease=Json.Str(probe.Tags,"netease_id"),mid=Json.Str(probe.Tags,"qqmusic_mid"),id=Json.Str(probe.Tags,"qqmusic_id");if(netease.Length>0){item.Platform="网易云音乐";item.SongId=netease;}else if(mid.Length>0||id.Length>0){item.Platform="QQ音乐";item.SongId=mid.Length>0?mid:id;item.AlternateSongId=id;}}
      if(!string.IsNullOrWhiteSpace(item.SongId)&&!string.IsNullOrWhiteSpace(item.Platform))catalog.files.Add(item);
     }catch(OperationCanceledException){throw;}catch(Exception ex){log("目录索引跳过「"+System.IO.Path.GetFileName(path)+"」："+ex.Message);}
    }}catch(OperationCanceledException){throw;}catch(Exception ex){log("目录读取未完成："+folder+" — "+ex.Message);}
   }
   log("指定目录索引完成："+visited.Count+" 个音乐文件，"+catalog.files.Count+" 个具备歌曲 ID。");return catalog;
  }
  public List<string> Matches(Track track) {
   return files.Where(f=>f.Platform==track.Platform&&!string.IsNullOrEmpty(track.SongId)&&(f.SongId==track.SongId||(!string.IsNullOrEmpty(track.AlternateSongId)&&f.SongId==track.AlternateSongId)||(!string.IsNullOrEmpty(f.AlternateSongId)&&f.AlternateSongId==track.SongId))&&(string.IsNullOrEmpty(track.Quality)||string.IsNullOrEmpty(f.Quality)||f.Quality==track.Quality)).Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  }
 }
}
