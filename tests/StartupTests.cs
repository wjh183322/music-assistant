using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using MusicAssistant;

static class StartupTests {
 public static void Run(string testRoot,Action<bool,string> check) {
  string root=Path.Combine(testRoot,"startup-storage");Directory.CreateDirectory(root);
  string preferred=Path.Combine(root,"preferred"),fallback=Path.Combine(root,"fallback");
  var normal=DataDirectory.Resolve(new[]{preferred,fallback});check(normal.Root==preferred&&!normal.IsFallback,"正常启动创建并实际读写默认数据目录");
  check(!Directory.GetFiles(preferred,".write-check-*").Any(),"启动读写探测清理自身临时文件");
  var source=new Store(preferred);source.Add(new HistoryEntry{Key="persisted",Title="保留的处理记录",SongId="123",Platform="QQ音乐",AudioHash="audio",Profile="24/96",RelativePath="Library/example.flac"});source.SaveSettings();
  // Reproduce real Windows write denial only in a fresh, bounded synthetic test directory.
  string absolute=Path.GetFullPath(preferred),bound=Path.GetFullPath(testRoot).TrimEnd('\\')+"\\";
  if(!absolute.StartsWith(bound,StringComparison.OrdinalIgnoreCase))throw new Exception("权限测试目录超出测试工作区");
  var info=new DirectoryInfo(preferred);DirectorySecurity original=info.GetAccessControl();DirectorySecurity denied=info.GetAccessControl();
  denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.Write|FileSystemRights.Delete,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Deny));
  try {
   info.SetAccessControl(denied);bool writeDenied=false;try{DataDirectory.ProbeWritable(preferred);}catch(UnauthorizedAccessException){writeDenied=true;}check(writeDenied,"复现默认目录拒绝写入（真实 Windows 权限，合成目录）");
   var relocated=DataDirectory.Resolve(new[]{preferred,fallback});check(relocated.Root==fallback&&relocated.IsFallback,"默认目录拒绝访问时自动选择可写备用目录");
   var restored=new Store(relocated.Root);check(restored.History.Count==1&&restored.History[0].Key=="persisted","备用目录迁移保留已有成功历史");
   restored.Add(new HistoryEntry{Key="new-record",Title="新记录",AudioHash="new",Profile="16/44"});
  }finally{info.SetAccessControl(original);}
  var restarted=DataDirectory.Resolve(new[]{preferred,fallback});check(restarted.Root==fallback&&new Store(restarted.Root).History.Count==2,"默认权限恢复后重启仍使用已有记录，避免历史切换丢失");
  string collision=Path.Combine(root,"file-not-directory");File.WriteAllText(collision,"existing file");var alternative=DataDirectory.Resolve(new[]{collision,Path.Combine(root,"file-fallback")});check(alternative.IsFallback&&File.ReadAllText(collision)=="existing file","同名文件阻碍目录创建时安全回退，保留原文件");
  string invalid=Path.Combine(root,"corrupt-primary");Directory.CreateDirectory(invalid);File.WriteAllText(Path.Combine(invalid,"history.json"),"broken");
  bool corrupt=false;try{var location=DataDirectory.Resolve(new[]{invalid,Path.Combine(root,"corrupt-fallback")});new Store(location.Root);}catch{corrupt=true;}check(corrupt&&File.ReadAllText(Path.Combine(invalid,"history.json"))=="broken","损坏历史不会被权限回退机制清空或隐藏");
 }
}
