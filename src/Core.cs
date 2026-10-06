using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MusicAssistant {
 public sealed class Track {
  public string Title {get;set;} public string Artist {get;set;} public string Album {get;set;}
  public string Platform {get;set;} public string SongId {get;set;} public string Quality {get;set;}
  public string AlternateSongId {get;set;}
  public string SourcePath {get;set;} public string Status {get;set;} public string Detail {get;set;}
  public string OutputPath {get;set;} public bool Force {get;set;}
  public string HistoryKey {get;set;}
  public bool MetadataUnavailable {get;set;}
  public Track() { Title=Artist=Album=Platform=SongId=Quality=SourcePath=OutputPath=Detail=""; Status="待处理"; }
 }
 public sealed class Playlist {
  public string Name {get;set;} public string Link {get;set;} public List<Track> Tracks {get;set;}
  public int SourceTrackCount {get;set;} public int SourceIdCount {get;set;} public int MissingDetailsCount {get;set;} public string ImportNotice {get;set;}
  public Playlist() {Name="新歌单"; Link=""; Tracks=new List<Track>();}
 }
 public sealed class HistoryEntry {
  public string Key {get;set;} public string Title {get;set;} public string Artist {get;set;}
  public string Album {get;set;} public string Platform {get;set;} public string SongId {get;set;}
  public string AlternateSongId {get;set;}
  public string Quality {get;set;} public string AudioHash {get;set;} public string Profile {get;set;}
  public string RelativePath {get;set;} public string ProcessedAt {get;set;}
 }
 public sealed class Settings {
  public string OutputRoot {get;set;} public List<string> SourceFolders {get;set;}
  public bool RecycleOriginal {get;set;} public bool PreferFlac {get;set;}
  public Settings() {OutputRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),"音乐助手导出"); SourceFolders=new List<string>(); RecycleOriginal=true; PreferFlac=false;}
 }
 public static class Json {
  public static JavaScriptSerializer Serializer() {return new JavaScriptSerializer {MaxJsonLength=32*1024*1024, RecursionLimit=100};}
  public static string Write(object value) {return Serializer().Serialize(value);}
  public static T Read<T>(string text) {return Serializer().Deserialize<T>(text);}
  public static Dictionary<string,object> Object(string text) {return Read<Dictionary<string,object>>(text);}
  public static string Str(Dictionary<string,object> d,string key) {object v; return d!=null && d.TryGetValue(key,out v) && v!=null ? Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture):"";}
  public static int Num(Dictionary<string,object> d,string key) {int n; int.TryParse(Str(d,key),out n);return n;}
 }
 public static class Files {
  public static string Hash(string path) {using(var s=File.OpenRead(path)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
  public static string HashText(string value) {using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
  public static string SafeName(string value) {
   var chars=Path.GetInvalidFileNameChars(); string name=new string((value??"").Select(c=>chars.Contains(c)||char.IsControl(c)?'_':c).ToArray()).Trim().TrimEnd('.',' ');
   if(name.Length>64) name=name.Substring(0,64).TrimEnd('.',' '); if(name.Length==0) name="未命名";
   string stem=name.Split('.')[0].ToUpperInvariant(); if(new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem)) name="_"+name;
   return name;
  }
  public static string ShortName(string value,int length) {string name=SafeName(value);if(name.Length<=length)return name;int end=length;if(char.IsHighSurrogate(name[end-1]))end--;return name.Substring(0,end).TrimEnd('.',' ');}
  public static void AtomicText(string path,string text) {
   Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try {File.WriteAllText(temp,text,new UTF8Encoding(false)); if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);}
   finally {if(File.Exists(temp)) File.Delete(temp);}
  }
  public static string Inside(string root,string relative) {
   string absoluteRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   string target=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
   if(!target.StartsWith(absoluteRoot,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("路径超出导出目录"); return target;
  }
 }
 public sealed class Store {
  public string Root {get;private set;} public Settings Settings {get;private set;} public List<HistoryEntry> History {get;private set;}
  public Store(string root) {
   Root=root; Directory.CreateDirectory(root);
   Settings=File.Exists(Path.Combine(root,"settings.json")) ? Json.Read<Settings>(File.ReadAllText(Path.Combine(root,"settings.json"),Encoding.UTF8)):new Settings();
   History=File.Exists(Path.Combine(root,"history.json")) ? Json.Read<List<HistoryEntry>>(File.ReadAllText(Path.Combine(root,"history.json"),Encoding.UTF8)):new List<HistoryEntry>();
   if(Settings==null||History==null) throw new InvalidDataException("记录文件无效；请先从 .bak 恢复，避免丢失去重历史。");
  }
  public void SaveSettings() {Files.AtomicText(Path.Combine(Root,"settings.json"),Json.Write(Settings));}
  public void SaveHistory() {Files.AtomicText(Path.Combine(Root,"history.json"),Json.Write(History));}
  public void Add(HistoryEntry entry) {History.RemoveAll(h=>h.Key==entry.Key);History.Add(entry);try {SaveHistory();} catch {History.Remove(entry);throw;}}
  public HistoryEntry Identity(Track t) {
   if(t.Force)return null;
   if(!string.IsNullOrEmpty(t.HistoryKey)) {var remembered=History.LastOrDefault(h=>h.Key==t.HistoryKey);if(remembered!=null)return remembered;}
   if(string.IsNullOrWhiteSpace(t.SongId)||string.IsNullOrWhiteSpace(t.Platform)) return null;
   var matches=History.Where(h=>h.Platform==t.Platform&&(h.SongId==t.SongId||(!string.IsNullOrEmpty(t.AlternateSongId)&&h.SongId==t.AlternateSongId)||(!string.IsNullOrEmpty(h.AlternateSongId)&&h.AlternateSongId==t.SongId))&&(string.IsNullOrWhiteSpace(t.Quality)||h.Quality==t.Quality)).ToList();
   return matches.Select(h=>h.AudioHash+"|"+h.Profile).Distinct().Count()==1 ? matches.LastOrDefault():null;
  }
  public HistoryEntry Exact(string audioHash,string profile,Track t) {
   if(t.Force)return null;
   return History.LastOrDefault(h=>h.AudioHash==audioHash&&h.Profile==profile&&Normalize(h.Title)==Normalize(t.Title)&&Normalize(h.Artist)==Normalize(t.Artist));
  }
  static string Normalize(string value) {return (value??"").Trim().ToLowerInvariant();}
 }
 public static class PlaylistWriter {
  public static string Write(Playlist list,string root) {
   var sb=new StringBuilder("#EXTM3U\n");
   foreach(var t in list.Tracks.Where(t=>!string.IsNullOrEmpty(t.OutputPath))) {
    // Relative paths are resolved from Music/Playlists on the device, not from the PC.
    if(!t.OutputPath.StartsWith("Library/",StringComparison.Ordinal)||t.OutputPath.Contains("..")) throw new InvalidDataException("歌单引用路径不合法");
    sb.Append("#EXTINF:-1,").Append((t.Artist+" - "+t.Title).Replace("\r"," ").Replace("\n"," ")).Append('\n');
    sb.Append("../").Append(t.OutputPath.Replace('\\','/')).Append('\n');
   }
   string id=Files.HashText(list.Link+"|"+list.Name).Substring(0,8);
   string path=Files.Inside(Path.Combine(root,"Music"),"Playlists/"+Files.SafeName(list.Name)+"_"+id+".m3u"); Files.AtomicText(path,sb.ToString());return path;
  }
 }
}
