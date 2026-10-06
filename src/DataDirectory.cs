using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MusicAssistant {
 public sealed class DataLocation {
  public string Root {get;set;} public string Notice {get;set;}
  public bool IsFallback {get;set;}
 }
 public static class DataDirectory {
  static readonly string[] StateFiles={"history.json","settings.json","tasks.json","history.json.bak","settings.json.bak","tasks.json.bak"};
  const string Marker="storage-location.json";
  public static DataLocation ResolveDefault() {
   return Resolve(new[]{
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MusicAssistant"),
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"MusicAssistant"),
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","MusicAssistant"),
    Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"UserData")});
  }
  public static DataLocation Resolve(IEnumerable<string> paths) {
   var roots=paths.Where(p=>!string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
   if(roots.Count==0)throw new IOException("没有可用的数据目录候选");
   // Prefer the previously used directory over an empty default, so a later permissions change
   // does not make successful-history records disappear after restart.
   var occupied=roots.Where(HasState).ToList();
   var marked=roots.Where(p=>File.Exists(Path.Combine(p,Marker))).ToList();
   var order=marked.Where(HasState).Concat(occupied).Concat(marked).Concat(roots).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
   var failures=new List<string>();
   foreach(string root in order) {
    try {ProbeWritable(root);}catch(UnauthorizedAccessException){failures.Add(root+"：无法读写");continue;}catch(IOException ex){failures.Add(root+"："+ex.Message);continue;}
    // Copy readable records into an available directory before opening Store. A damaged or
    // inaccessible old record stops migration rather than silently starting an empty history.
    if(!HasState(root))foreach(string old in occupied.Where(p=>!string.Equals(p,root,StringComparison.OrdinalIgnoreCase)))Migrate(old,root);
    bool fallback=!string.Equals(root,roots[0],StringComparison.OrdinalIgnoreCase);
    Files.AtomicText(Path.Combine(root,Marker),Json.Write(new{Root=root,InitializedAt=DateTimeOffset.Now.ToString("o")}));
    return new DataLocation{Root=root,IsFallback=fallback,Notice=fallback?"记录保存在备用目录："+root+"。后续启动继续使用此目录，已有可读记录已保留。":"记录目录："+root};
   }
   throw new IOException("找不到可读写的数据目录。请把程序解压到可写目录后重试。\n"+string.Join("\n",failures));
  }
  static bool HasState(string root){return StateFiles.Take(3).Any(name=>File.Exists(Path.Combine(root,name)));}
  public static void ProbeWritable(string root) {
   Directory.CreateDirectory(root);string path=Path.Combine(root,".write-check-"+Guid.NewGuid().ToString("N")+".tmp");
   try {Files.AtomicText(path,"first");Files.AtomicText(path,"second");if(File.ReadAllText(path,Encoding.UTF8)!="second")throw new IOException("数据目录读写检查失败");}
   finally {if(File.Exists(path))File.Delete(path);if(File.Exists(path+".bak"))File.Delete(path+".bak");}
  }
  static void Migrate(string source,string destination) {
   var snapshots=new Dictionary<string,string>();
   foreach(string name in StateFiles) {
    string path=Path.Combine(source,name);if(!File.Exists(path))continue;string text=File.ReadAllText(path,Encoding.UTF8);
    if(name.EndsWith(".json",StringComparison.OrdinalIgnoreCase)) {
     object parsed=Json.Serializer().DeserializeObject(text);if(parsed==null)throw new InvalidDataException("原数据记录为空，未切换到空白记录："+path);
     if(name=="history.json"&&Json.Read<List<HistoryEntry>>(text)==null)throw new InvalidDataException("原历史格式无效："+path);
     if(name=="settings.json"&&Json.Read<Settings>(text)==null)throw new InvalidDataException("原设置格式无效："+path);
     if(name=="tasks.json"&&Json.Read<List<Playlist>>(text)==null)throw new InvalidDataException("原任务格式无效："+path);
    }
    snapshots[name]=text;
   }
   // Avoid mixing two existing histories without a user decision.
   foreach(var pair in snapshots){string target=Path.Combine(destination,pair.Key);if(File.Exists(target)&&File.ReadAllText(target,Encoding.UTF8)!=pair.Value)throw new IOException("检测到两份不同的数据记录，请先保留并合并记录："+source+" 与 "+destination);}
   foreach(var pair in snapshots){string target=Path.Combine(destination,pair.Key);if(!File.Exists(target))Files.AtomicText(target,pair.Value);}
  }
 }
}
