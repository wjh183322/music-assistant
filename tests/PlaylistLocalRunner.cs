using System;
using System.IO;
using System.Linq;
using MusicAssistant;

static class PlaylistLocalRunner {
 static int Main(string[] args){try{
  int count=0;PlaylistTests.Run((condition,name)=>{if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name);});
  if(args.Length>1){var plan=PublicPlaylist.ParseNetEasePlan(File.ReadAllText(args[0]),"https://music.163.com/playlist?id=local-fixture");foreach(string path in Directory.GetFiles(args[1],"netease-songs*.txt"))foreach(var pair in PublicPlaylist.ParseNetEaseSongs(File.ReadAllText(path)))plan.Details[pair.Key]=pair.Value;var result=PublicPlaylist.CompleteNetEase(plan);Console.WriteLine(Json.Write(new{LocalFixture=true,Count=result.Tracks.Count,SourceTrackCount=result.SourceTrackCount,MissingDetailsCount=result.MissingDetailsCount,Notice=result.ImportNotice}));}
  Console.WriteLine("Local playlist checks passed: "+count);return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}}
}
