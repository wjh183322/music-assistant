// Optional, explicitly invoked session reader. No session information is persisted.
// Format references: QM Unlock (MIT), licenses/QMUnlock-MIT.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MusicAssistant {
 public sealed class QQKeySession:IDisposable {
  string uin,token;
  QQKeySession(string uin,string token){this.uin=uin;this.token=token;}
  public static QQKeySession Open(CancellationToken ct) {
   string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Tencent","QQMusic","QQMusicServiceConfig.ini");
   if(!File.Exists(path)||new FileInfo(path).Length>1024*1024)throw new InvalidDataException("未找到 QQ 音乐当前登录配置；请先在桌面 QQ 音乐中登录");
   string uin=ParseUin(File.ReadAllText(path,Encoding.UTF8));if(uin==null)throw new InvalidDataException("QQ 音乐登录配置没有有效账号标识");
   var timer=Stopwatch.StartNew();int session=Process.GetCurrentProcess().SessionId;
   foreach(string name in new[]{"QQMusic","QQMusicDesktop","QQMusicHelper","QQMusicCloud","QQMusicService"}) {
    foreach(var process in Process.GetProcessesByName(name))using(process) {
     ct.ThrowIfCancellationRequested();if(process.SessionId!=session)continue;
     string token=ReadSessionToken(process.Id,ct,timer);if(token!=null)return new QQKeySession(uin,token);
     if(timer.Elapsed.TotalSeconds>20)break;
    }
    if(timer.Elapsed.TotalSeconds>20)break;
   }
   throw new InvalidDataException("未读取到当前 QQ 音乐登录状态。请打开并登录桌面客户端后重试；权限不一致或客户端版本变化时可改用单文件 ekey。不会自动提权。");
  }
  public static string ParseUin(string content){var match=Regex.Match(content,@"(?m)^\s*Uin\s*=\s*([0-9]{1,20})\s*$");return match.Success&&match.Groups[1].Value!="0"?match.Groups[1].Value:null;}
  public static string ExtractToken(byte[] bytes,int count) {
   const string pattern=@"(?:\\?""authst\\?""\s*[:=]\s*\\?""?|authst=)([A-Za-z0-9_\-]{21,2048}={0,2})";
   var ascii=Regex.Match(Encoding.ASCII.GetString(bytes,0,count),pattern);if(ascii.Success)return ascii.Groups[1].Value;
   var wide=Regex.Match(Encoding.Unicode.GetString(bytes,0,count&~1),pattern);return wide.Success?wide.Groups[1].Value:null;
  }
  static string ReadSessionToken(int pid,CancellationToken ct,Stopwatch timer) {
   IntPtr handle=OpenProcess(0x0400|0x0010,false,pid);if(handle==IntPtr.Zero)return null;
   try {ulong address=0;long scanned=0;byte[] carry=new byte[0];
    while(address<0x00007fffffffffff&&scanned<512L*1024*1024&&timer.Elapsed.TotalSeconds<=20) {
     ct.ThrowIfCancellationRequested();MemoryInfo info;UIntPtr size=VirtualQueryEx(handle,new IntPtr((long)address),out info,new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryInfo))));if(size==UIntPtr.Zero)break;
     ulong region=info.RegionSize.ToUInt64(),begin=(ulong)info.BaseAddress.ToInt64();if(region==0||begin+region<=address)break;
     if(info.State==0x1000&&(info.Protect&0x100)==0&&(info.Protect&0xff)!=0x01) {
      for(ulong offset=0;offset<region&&scanned<512L*1024*1024&&timer.Elapsed.TotalSeconds<=20;offset+=1024*1024) {
       ct.ThrowIfCancellationRequested();int count=(int)Math.Min(1024*1024,region-offset);byte[] buffer=new byte[count];UIntPtr read;
       ReadProcessMemory(handle,new IntPtr((long)(begin+offset)),buffer,new UIntPtr((uint)count),out read);int actual=(int)Math.Min((ulong)count,read.ToUInt64());scanned+=count;
       if(actual==0){carry=new byte[0];continue;}byte[] joined=new byte[carry.Length+actual];Buffer.BlockCopy(carry,0,joined,0,carry.Length);Buffer.BlockCopy(buffer,0,joined,carry.Length,actual);
       string token=ExtractToken(joined,joined.Length);Array.Clear(buffer,0,buffer.Length);if(token!=null){Array.Clear(joined,0,joined.Length);return token;}
       carry=joined.Skip(Math.Max(0,joined.Length-2048)).ToArray();Array.Clear(joined,0,joined.Length);
      }
     }
     address=begin+region;carry=new byte[0];
    }
   }finally{CloseHandle(handle);}return null;
  }
  public string Fetch(string source,CancellationToken ct) {
   if(token==null)throw new ObjectDisposedException("QQKeySession");var file=SourceDecoder.ReadIdentity(source);
   string filename=file==null?"":Json.Str(file.Metadata,"resourceFilename"),mid=file==null?"":file.SongId;
   if(file==null||file.Kind!="musicex V1"||!Regex.IsMatch(filename,@"^[A-Za-z0-9_.-]+\.(mflac|mgg|mmp4)$",RegexOptions.IgnoreCase)||!Regex.IsMatch(mid??"",@"^[A-Za-z0-9]{10,32}$"))throw new InvalidDataException("只为可确认资源标识的 musicex V1 已下载文件请求密钥");
   var payload=new{comm=new{authst=token,ct="19",cv="1859",uin=uin,tmeLoginType="3"},req_1=new{module="music.vkey.GetEVkey",method="CgiGetEVkey",param=new{filename=new[]{filename},guid="10000",songmid=new[]{mid},songtype=new[]{1},uin=uin,loginflag=1,platform="27",ctx=1}}};
   ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
   var request=(HttpWebRequest)WebRequest.Create("https://u.y.qq.com/cgi-bin/musicu.fcg");request.Method="POST";request.ContentType="application/json; charset=utf-8";request.AllowAutoRedirect=false;request.Timeout=20000;request.ReadWriteTimeout=20000;request.UserAgent="QQMusic/20 MusicAssistant/0.2";
   byte[] body=Encoding.UTF8.GetBytes(Json.Write(payload));request.ContentLength=body.Length;
   using(ct.Register(()=>request.Abort()))try{using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);Array.Clear(body,0,body.Length);
    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)) {var sb=new StringBuilder();char[] buffer=new char[4096];int n;while((n=reader.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();sb.Append(buffer,0,n);if(sb.Length>1024*1024)throw new InvalidDataException("QQ 密钥服务响应过大");}string key=ParseResponse(sb.ToString());SourceDecoder.ParseEKey(key);return key;}
   }catch(WebException){ct.ThrowIfCancellationRequested();throw new IOException("QQ 官方密钥服务请求未成功；未保存登录信息，请稍后重试");}catch(OperationCanceledException){throw;}catch(InvalidDataException){throw;}catch(Exception){throw new InvalidDataException("QQ 密钥响应无法校验，请刷新登录状态后重试");}finally{Array.Clear(body,0,body.Length);}
  }
  public static string ParseResponse(string json) {
   var data=Json.Object(json);object reqValue;if(Json.Num(data,"code")!=0||!data.TryGetValue("req_1",out reqValue))throw new InvalidDataException("QQ 未返回当前歌曲密钥，请刷新客户端登录状态");
   var req=reqValue as Dictionary<string,object>;object dataValue;if(req==null||Json.Num(req,"code")!=0||!req.TryGetValue("data",out dataValue))throw new InvalidDataException("QQ 未授权返回当前歌曲密钥，请检查当前账号和文件权限");
   var inner=dataValue as Dictionary<string,object>;object rows;if(inner==null||Json.Num(inner,"retcode")!=0||!inner.TryGetValue("midurlinfo",out rows))throw new InvalidDataException("QQ 未返回文件的 ekey");
   var row=((IEnumerable)rows).Cast<object>().OfType<Dictionary<string,object>>().FirstOrDefault();string key=Json.Str(row,"ekey");if(string.IsNullOrEmpty(key)||Json.Num(row,"errcode")!=0)throw new InvalidDataException("QQ 未返回文件的 ekey，当前登录状态或客户端版本可能不适用");return key;
  }
  public void Dispose(){token=null;uin=null;}
  [StructLayout(LayoutKind.Sequential)]struct MemoryInfo{public IntPtr BaseAddress,AllocationBase;public uint AllocationProtect;public ushort PartitionId,Padding;public UIntPtr RegionSize;public uint State,Protect,Type;}
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(IntPtr process,IntPtr address,[Out]byte[] bytes,UIntPtr size,out UIntPtr read);
  [DllImport("kernel32.dll")]static extern UIntPtr VirtualQueryEx(IntPtr process,IntPtr address,out MemoryInfo info,UIntPtr size);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 }
}
