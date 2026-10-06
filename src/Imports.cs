using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace MusicAssistant {
 public static class Imports {
  public static readonly string[] AudioExtensions={".mp3",".flac",".wav",".m4a",".aac",".aif",".aiff",".ape",".dsf",".dff",".wma",".wv",".ogg",".opus",".ncm",".qmc0",".qmc2",".qmc3",".qmcflac",".qmcogg",".mflac",".mgg",".mflac0",".mgg1",".mggl",".mmp4",".tkm"};
  public static bool IsAudio(string path) {string e=Path.GetExtension(path).ToLowerInvariant();return AudioExtensions.Contains(e)||e.StartsWith(".qmc")||e.StartsWith(".mflac")||e.StartsWith(".mgg");}
  public static Playlist Load(string path) {
   string ext=Path.GetExtension(path).ToLowerInvariant();
   if(ext==".json") {var p=Json.Read<Playlist>(File.ReadAllText(path,Encoding.UTF8)); if(p==null||p.Tracks==null)throw new InvalidDataException("JSON 需要 Name 和 Tracks 字段"); foreach(var t in p.Tracks) Resolve(t,Path.GetDirectoryName(path)); return p;}
   var list=new Playlist{Name=Path.GetFileNameWithoutExtension(path)};
   if(ext==".csv"||ext==".tsv") {
    using(var parser=new TextFieldParser(path,Encoding.UTF8,true)) {
     parser.TextFieldType=FieldType.Delimited;parser.SetDelimiters(ext==".tsv"?"\t":",");parser.HasFieldsEnclosedInQuotes=true;
     string[] header=parser.ReadFields();if(header==null)throw new InvalidDataException("歌单为空");
     while(!parser.EndOfData) {var row=parser.ReadFields();var t=new Track();
      for(int i=0;i<header.Length&&i<row.Length;i++) Set(t,header[i].Trim().TrimStart('\ufeff').ToLowerInvariant(),row[i]);
      if(t.Title.Length==0&&t.SourcePath.Length==0)continue; Resolve(t,Path.GetDirectoryName(path));list.Tracks.Add(t);
     }
    }
   } else if(ext==".m3u"||ext==".m3u8") {
    string name=""; foreach(string raw in File.ReadAllLines(path,Encoding.UTF8)) {
     string line=raw.Trim().TrimStart('\ufeff'); if(line.StartsWith("#EXTINF:")) {int comma=line.IndexOf(',');name=comma>=0?line.Substring(comma+1):"";continue;}
     if(line.Length==0||line.StartsWith("#"))continue;
     if(Uri.IsWellFormedUriString(line,UriKind.Absolute)&&!line.StartsWith("file:",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("仅支持本地音乐 M3U，不下载网络流");
     var t=new Track {SourcePath=line.StartsWith("file:",StringComparison.OrdinalIgnoreCase)?new Uri(line).LocalPath:line,Title=name};name="";Resolve(t,Path.GetDirectoryName(path));list.Tracks.Add(t);
    }
   } else if(IsAudio(path)) {list.Tracks.Add(FromFile(path));}
   else throw new InvalidDataException("支持 CSV、TSV、JSON、M3U 或音乐文件");
   return list;
  }
  static void Set(Track t,string field,string value) {
   switch(field) {case "title":case "name":case "歌名":case "歌曲":t.Title=value;break;
    case "artist":case "歌手":t.Artist=value;break;case "album":case "专辑":t.Album=value;break;
    case "platform":case "平台":t.Platform=NormalizePlatform(value);break;case "songid":case "id":case "歌曲id":t.SongId=value;break;
    case "quality":case "音质":t.Quality=value;break;case "sourcepath":case "path":case "文件路径":t.SourcePath=value;break;}
  }
  public static string NormalizePlatform(string value) {string v=(value??"").Trim().ToLowerInvariant();return v.Contains("网易")||v=="netease"?"网易云音乐":v.Contains("qq")?"QQ音乐":value;}
  static void Resolve(Track t,string folder) {if(!string.IsNullOrEmpty(t.SourcePath)) {t.SourcePath=Path.GetFullPath(Path.IsPathRooted(t.SourcePath)?t.SourcePath:Path.Combine(folder,t.SourcePath));if(string.IsNullOrWhiteSpace(t.Title))t.Title=Path.GetFileNameWithoutExtension(t.SourcePath);} }
  public static Track FromFile(string path) {return new Track {SourcePath=Path.GetFullPath(path),Title=Path.GetFileNameWithoutExtension(path)};}
  public static IEnumerable<string> EnumerateAudio(string folder) {var pending=new Stack<string>();pending.Push(folder);while(pending.Count>0){string current=pending.Pop();if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)continue;foreach(string file in Directory.GetFiles(current))if(IsAudio(file))yield return file;foreach(string child in Directory.GetDirectories(current))pending.Push(child);}}
  public static Playlist FromFolder(string folder) {return new Playlist{Name=Path.GetFileName(folder.TrimEnd('\\')),Tracks=EnumerateAudio(folder).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).Select(FromFile).ToList()};}
  public static Uri OfficialLink(string text) {
   var match=Regex.Match(text??"",@"https?://[^\s<>""，]+",RegexOptions.IgnoreCase);Uri uri;
   if(!match.Success||!Uri.TryCreate(match.Value,UriKind.Absolute,out uri))throw new InvalidDataException("请粘贴完整官方分享链接");
   string host=uri.Host.ToLowerInvariant();if(!(host=="music.163.com"||host.EndsWith(".music.163.com",StringComparison.Ordinal)||host=="163cn.tv"||host=="y.qq.com"||host.EndsWith(".y.qq.com",StringComparison.Ordinal)))throw new InvalidDataException("只接受 QQ 音乐或网易云音乐官方分享链接");
   return uri;
  }
 }
}
