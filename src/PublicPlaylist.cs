using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MusicAssistant {
 public static class PublicPlaylist {
  public static Playlist Read(string link,CancellationToken ct) {
   Uri uri=Imports.OfficialLink(link);
   if(uri.Host=="music.163.com"&&uri.Fragment.StartsWith("#/"))uri=new Uri("https://music.163.com"+uri.Fragment.Substring(1));
   ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
   for(int redirect=0;redirect<5;redirect++) {
    string qqId=QQId(uri);if(qqId!=null)return ReadQQ(qqId,uri.AbsoluteUri,ct);
    ct.ThrowIfCancellationRequested();var request=(HttpWebRequest)WebRequest.Create(uri);request.AllowAutoRedirect=false;request.Timeout=20000;request.ReadWriteTimeout=20000;request.UserAgent="MusicAssistant/0.1 (personal playlist reader)";
    using(ct.Register(()=>request.Abort())) {
     try {using(var response=(HttpWebResponse)request.GetResponse()) {
      if((int)response.StatusCode>=300&&(int)response.StatusCode<400) {uri=Imports.OfficialLink(new Uri(uri,response.Headers["Location"]).AbsoluteUri);continue;}
      using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)) {
       var sb=new StringBuilder();char[] buffer=new char[8192];int n;while((n=reader.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();sb.Append(buffer,0,n);if(sb.Length>8*1024*1024)throw new InvalidDataException("网页超出读取大小限制");}
       return Parse(sb.ToString(),uri.AbsoluteUri);
      }
     }} catch(WebException){ct.ThrowIfCancellationRequested();throw;}
    }
   }
   throw new InvalidDataException("分享链接重定向过多，请使用完整歌单链接");
  }
  public static Playlist Parse(string html,string link) {
   var p=new Playlist{Link=link};var title=Regex.Match(html,@"<title[^>]*>(.*?)</title>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
   p.Name=WebUtility.HtmlDecode(title.Success?title.Groups[1].Value:"导入歌单").Replace(" - 歌单 - 网易云音乐","").Trim();
   bool netease=new Uri(link).Host.EndsWith(".163.com",StringComparison.OrdinalIgnoreCase);
   if(netease) {
    // Read the visible public song list, not the encrypted pre-data or private download APIs.
    var list=Regex.Match(html,@"<ul\s+class=[""']f-hide[""'][^>]*>(.*?)</ul>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
    foreach(Match song in Regex.Matches(list.Success?list.Groups[1].Value:"",@"<a\s+href=[""']/song\?id=(\d+)[""'][^>]*>(.*?)</a>",RegexOptions.Singleline))p.Tracks.Add(new Track{Title=WebUtility.HtmlDecode(Regex.Replace(song.Groups[2].Value,"<[^>]+>","")),Platform="网易云音乐",SongId=song.Groups[1].Value});
    var count=Regex.Match(html,@"id=[""']playlist-track-count[""'][^>]*>\s*(\d+)");
    if(count.Success&&int.Parse(count.Groups[1].Value)!=p.Tracks.Count)throw new InvalidDataException("网页只提供了部分歌曲；不会把部分结果当作完整歌单，请改用完整歌单文件");
   } else {
    foreach(Match script in Regex.Matches(html,@"<script[^>]*type=[""']application/ld\+json[""'][^>]*>(.*?)</script>",RegexOptions.Singleline|RegexOptions.IgnoreCase)) {
     try {ReadStructured(Json.Object(script.Groups[1].Value),p);}catch(ArgumentException){}catch(InvalidOperationException){}
    }
   }
   if(p.Tracks.Count==0)throw new InvalidDataException("官方网页没有提供可读取的完整歌曲列表（可能需要登录或动态加载）。请在官方客户端打开，改用 CSV/JSON/M3U 导入；软件不会调用未验证的下载接口。");
   if(p.Name.Length==0)p.Name="导入歌单";return p;
  }
  static void ReadStructured(Dictionary<string,object> data,Playlist p) {
   if(Json.Str(data,"@type")!="MusicPlaylist")return;object tracks;if(!data.TryGetValue("track",out tracks))return;
   object name;if(data.TryGetValue("name",out name))p.Name=Convert.ToString(name);
   var rows=tracks as IEnumerable;if(rows==null)return;
   foreach(object raw in rows) {var row=raw as Dictionary<string,object>;if(row==null)continue;var id=Regex.Match(Json.Str(row,"url"),@"(?:songDetail/|songmid=)([a-zA-Z0-9]+)");if(!id.Success)continue;
    p.Tracks.Add(new Track{Title=Json.Str(row,"name"),Platform="QQ音乐",SongId=id.Groups[1].Value});}
   int count=Json.Num(data,"numTracks");if(count==0||count!=p.Tracks.Count){p.Tracks.Clear();throw new InvalidDataException("公开结构化歌单缺少完整数量，不能确认读取完整");}
  }
  static string QQId(Uri uri) {
   if(!(uri.Host=="y.qq.com"||uri.Host=="c.y.qq.com"||uri.Host=="i.y.qq.com"))return null;
   if(!(uri.AbsolutePath.Contains("playlist")||uri.AbsolutePath.Contains("taoge")||uri.AbsolutePath.Contains("ucc_getcdinfo")))return null;
   var match=Regex.Match(uri.AbsoluteUri,@"(?:/playlist/|[?&](?:id|disstid)=)(\d+)(?=[&#/.]|$)");return match.Success?match.Groups[1].Value:null;
  }
  static Playlist ReadQQ(string id,string original,CancellationToken ct) {
   // Public playlist metadata only. No account cookies, login tokens, vkeys or download URLs.
   string url="https://c.y.qq.com/qzone/fcg-bin/fcg_ucc_getcdinfo_byids_cp.fcg?type=1&json=1&utf8=1&onlysong=0&disstid="+id+"&format=json&g_tk=5381&loginUin=0&hostUin=0";
   string response=FetchQQ(url,ct);return ParseQQ(response,original);
  }
  static string FetchQQ(string url,CancellationToken ct) {
   var request=(HttpWebRequest)WebRequest.Create(url);request.AllowAutoRedirect=false;request.Timeout=20000;request.ReadWriteTimeout=20000;request.UserAgent="MusicAssistant/0.2";request.Referer="https://y.qq.com/";
   using(ct.Register(()=>request.Abort()))try{using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){
    if(response.StatusCode!=HttpStatusCode.OK)throw new InvalidDataException("QQ 官方公开歌单服务未返回成功状态");
    var sb=new StringBuilder();char[] buffer=new char[8192];int n;while((n=reader.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();sb.Append(buffer,0,n);if(sb.Length>16*1024*1024)throw new InvalidDataException("QQ 歌单超过读取大小限制");}return sb.ToString();
   }}catch(WebException){ct.ThrowIfCancellationRequested();throw;}
  }
  public static Playlist ParseQQ(string text,string link) {
   var data=Json.Object(text);if(Json.Num(data,"code")!=0)throw new InvalidDataException("QQ 歌单不可公开读取、已删除或需要登录；请使用官方导出的歌单文件");
   object lists;if(!data.TryGetValue("cdlist",out lists))throw new InvalidDataException("QQ 公开歌单响应缺少列表");
   var info=((IEnumerable)lists).Cast<object>().OfType<Dictionary<string,object>>().FirstOrDefault();if(info==null)throw new InvalidDataException("QQ 歌单没有公开内容");
   var result=new Playlist{Name=Json.Str(info,"dissname"),Link=link};object songs;if(!info.TryGetValue("songlist",out songs))throw new InvalidDataException("QQ 响应未提供歌曲列表");
   foreach(var song in ((IEnumerable)songs).Cast<object>().OfType<Dictionary<string,object>>()) {
    string mid=Json.Str(song,"songmid");if(mid.Length==0)throw new InvalidDataException("QQ 歌曲缺少原始 MID，不能按歌名替代");
    var names=new List<string>();object singers;if(song.TryGetValue("singer",out singers))foreach(var singer in ((IEnumerable)singers).Cast<object>().OfType<Dictionary<string,object>>())names.Add(Json.Str(singer,"name"));
    result.Tracks.Add(new Track{Title=WebUtility.HtmlDecode(Json.Str(song,"songname")),Artist=string.Join(" / ",names),Album=WebUtility.HtmlDecode(Json.Str(song,"albumname")),Platform="QQ音乐",SongId=mid,AlternateSongId=Json.Str(song,"songid")});
   }
   int expected=Json.Num(info,"total_song_num");if(expected==0)expected=Json.Num(info,"songnum");
   if(result.Tracks.Count==0||expected!=result.Tracks.Count)throw new InvalidDataException("QQ 服务返回的歌曲数不完整（读取 "+result.Tracks.Count+" / 总计 "+expected+"），不会把部分结果当完整歌单");
   return result;
  }
 }
}
