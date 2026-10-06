using System;
using System.Collections.Generic;
using System.Linq;
using MusicAssistant;

static class PlaylistTests {
 public static void Run(Action<bool,string> check) {
  var plan=PublicPlaylist.ParseNetEasePlan(Json.Write(new{code=200,playlist=new{name="测试完整歌单",trackCount=3,trackIds=new[]{new{id=11},new{id=22},new{id=33}},tracks=new[]{new{id=11,name="第一首",ar=new[]{new{name="歌手一"}},al=new{name="专辑一"}}}}}),"https://music.163.com/playlist?id=1");
  check(plan.Ids.SequenceEqual(new[]{"11","22","33"})&&plan.Details.Count==1,"网易预览只有部分详情时仍保留完整 ID 列表");
  var details=PublicPlaylist.ParseNetEaseSongs(Json.Write(new{code=200,songs=new[]{new{id=33,name="第三首",artists=new[]{new{name="歌手三"}},album=new{name="专辑三"}},new{id=22,name="第二首",artists=new[]{new{name="歌手二"}},album=new{name="专辑二"}}}}));foreach(var pair in details)plan.Details[pair.Key]=pair.Value;
  var playlist=PublicPlaylist.CompleteNetEase(plan);check(playlist.Tracks.Select(t=>t.SongId).SequenceEqual(new[]{"11","22","33"}),"详情返回顺序变化时按原歌单 ID 顺序组装");
  check(playlist.Tracks[1].Artist=="歌手二"&&playlist.Tracks[1].Album=="专辑二"&&playlist.MissingDetailsCount==0,"批量歌曲详情填充标题、歌手、专辑");
  check(string.IsNullOrEmpty(playlist.ImportNotice)&&playlist.SourceTrackCount==3,"数量一致时不会产生错误的部分歌单提示");
  var unavailable=PublicPlaylist.ParseNetEasePlan(Json.Write(new{code=200,playlist=new{name="部分详情未公开",trackCount=3,trackIds=new[]{new{id=11},new{id=22}},tracks=new object[0]}}),"https://music.163.com/playlist?id=2");
  unavailable.Details["11"]=new Track{Title="可见歌曲",Artist="歌手",SongId="11"};var partial=PublicPlaylist.CompleteNetEase(unavailable);
  check(partial.Tracks.Count==2&&partial.SourceTrackCount==3&&partial.ImportNotice.Contains("差 1 首"),"平台总数与公开 ID 不一致时明确说明缺口，不伪造歌曲");
  check(partial.Tracks[1].SongId=="22"&&partial.Tracks[1].MetadataUnavailable&&partial.MissingDetailsCount==1,"详情不可见但 ID 已知的歌曲保留位置和原 ID");
  bool privateDenied=false;try{PublicPlaylist.ParseNetEasePlan(Json.Write(new{code=200,playlist=new{name="需登录",trackCount=5,trackIds=new object[0]}}),"https://music.163.com/playlist?id=3");}catch(System.IO.InvalidDataException){privateDenied=true;}check(privateDenied,"没有公开 ID 的非空歌单不被当作空歌单导入");
  check(PublicPlaylist.NetEaseId(new Uri("https://music.163.com/playlist?id=123&uct2=opaque_share_value"))=="123","分享追踪参数不影响歌单 ID 识别");
  check(PublicPlaylist.NetEaseId(new Uri("https://music.163.com/#/playlist?id=456"))=="456","网易 fragment 分享链接使用相同完整读取流程");
  bool invalid=false;try{PublicPlaylist.ParseNetEasePlan(Json.Write(new{code=200,playlist=new{name="无效",trackCount=1,trackIds=new[]{new{id="not-a-song-id"}}}}),"https://music.163.com/playlist?id=4");}catch(System.IO.InvalidDataException){invalid=true;}check(invalid,"无效 ID 无法进入详情查询 URL");
 }
}
