// File-format implementation references and licenses: THIRD_PARTY.md / licenses.
// Keys remain local and are never sent to the playlist readers or included in receipts.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MusicAssistant {
 public sealed class MissingKeyException:Exception {public MissingKeyException(string message):base(message){}}
 public sealed class DecodedSource {
  public string AudioPath; public string CoverPath; public string Kind; public string SongId; public string Platform;
  public Dictionary<string,object> Metadata=new Dictionary<string,object>();
  public Dictionary<string,string> Tags=new Dictionary<string,string>();
  public void Apply(Track track) {
   if(!string.IsNullOrEmpty(SongId)&&!string.IsNullOrEmpty(track.Platform)&&track.Platform!=Platform)throw new InvalidDataException("下载文件所属平台与歌单不一致，请选择原平台对应文件");
   if(!string.IsNullOrEmpty(SongId)&&!string.IsNullOrEmpty(track.SongId)&&track.Platform==Platform) {
    bool sourceNumeric=SongId.All(char.IsDigit),trackNumeric=track.SongId.All(char.IsDigit);
    if(sourceNumeric==trackNumeric&&SongId!=track.SongId)throw new InvalidDataException("下载文件的歌曲 ID 与歌单记录不同，请关联原歌单对应歌曲");
    if(sourceNumeric&&!trackNumeric&&!string.IsNullOrEmpty(track.AlternateSongId)&&SongId!=track.AlternateSongId)throw new InvalidDataException("下载文件的数字歌曲 ID 与歌单记录不同");
   }
   if(string.IsNullOrEmpty(track.Platform))track.Platform=Platform;if(string.IsNullOrEmpty(track.SongId))track.SongId=SongId??"";
   string name; if(Tags.TryGetValue("title",out name)&&(string.IsNullOrEmpty(track.Title)||track.Title==Path.GetFileNameWithoutExtension(track.SourcePath)||track.MetadataUnavailable)){track.Title=name;track.MetadataUnavailable=false;}
   if(Tags.TryGetValue("artist",out name)&&string.IsNullOrEmpty(track.Artist))track.Artist=name;
   if(Tags.TryGetValue("album",out name)&&string.IsNullOrEmpty(track.Album))track.Album=name;
  }
 }
 public static class SourceDecoder {
  static readonly byte[] NcmCore=Encoding.ASCII.GetBytes("hzHRAmso5kInbaxW");
  static readonly byte[] NcmMeta=new byte[]{0x23,0x31,0x34,0x6c,0x6a,0x6b,0x5f,0x21,0x5c,0x5d,0x26,0x30,0x55,0x3c,0x27,0x28};
  static readonly byte[] LegacyTable={0xc3,0x4a,0xd6,0xca,0x90,0x67,0xf7,0x52,0xd8,0xa1,0x66,0x62,0x9f,0x5b,0x09,0x00,0xc3,0x5e,0x95,0x23,0x9f,0x13,0x11,0x7e,0xd8,0x92,0x3f,0xbc,0x90,0xbb,0x74,0x0e,0xc3,0x47,0x74,0x3d,0x90,0xaa,0x3f,0x51,0xd8,0xf4,0x11,0x84,0x9f,0xde,0x95,0x1d,0xc3,0xc6,0x09,0xd5,0x9f,0xfa,0x66,0xf9,0xd8,0xf0,0xf7,0xa0,0x90,0xa1,0xd6,0xf3};
  public static bool IsWrapped(string path) {string ext=Path.GetExtension(path).ToLowerInvariant();return ext==".ncm"||ext.StartsWith(".qmc")||ext.StartsWith(".mflac")||ext.StartsWith(".mgg")||ext==".mmp4"||ext==".tkm";}
  static byte[] Exact(BinaryReader reader,int length) {if(length<0||length>16*1024*1024)throw new InvalidDataException("封装块长度无效");byte[] value=reader.ReadBytes(length);if(value.Length!=length)throw new EndOfStreamException("文件未下载完整或封装块被截断");return value;}
  static int Length(BinaryReader reader,int max) {uint length=reader.ReadUInt32();if(length>max||length>reader.BaseStream.Length-reader.BaseStream.Position)throw new InvalidDataException("封装块长度超出文件范围");return (int)length;}
  static byte[] AesDecrypt(byte[] data,byte[] key) {if(data.Length==0||data.Length%16!=0)throw new InvalidDataException("NCM AES 块长度无效");using(var aes=Aes.Create()){aes.Key=key;aes.Mode=CipherMode.ECB;aes.Padding=PaddingMode.PKCS7;using(var decryptor=aes.CreateDecryptor())return decryptor.TransformFinalBlock(data,0,data.Length);}}
  public static DecodedSource Decode(string source,string staging,CancellationToken ct) {
   Directory.CreateDirectory(staging);ct.ThrowIfCancellationRequested();
   var result=Path.GetExtension(source).Equals(".ncm",StringComparison.OrdinalIgnoreCase)?DecodeNcm(source,staging,ct):DecodeQmc(source,staging,ct);
   string extension=Sniff(result.AudioPath);string declared=Json.Str(result.Metadata,"format");if(result.Kind=="NCM"&&(declared=="mp3"||declared=="flac")&&extension!="."+declared)throw new InvalidDataException("NCM 载荷容器与元信息的格式标识不一致");string path=Path.Combine(staging,"payload"+extension);File.Move(result.AudioPath,path);result.AudioPath=path;return result;
  }
  static string Sniff(string path) {
   byte[] h=new byte[32];using(var f=File.OpenRead(path)){int count=f.Read(h,0,h.Length);if(count<4)throw new InvalidDataException("恢复出的音频载荷为空或被截断");}
   string first=Encoding.ASCII.GetString(h,0,4);
   if(first=="fLaC")return ".flac";if(first=="OggS")return ".ogg";if(first=="RIFF"&&Encoding.ASCII.GetString(h,8,4)=="WAVE")return ".wav";
   if(Encoding.ASCII.GetString(h,4,4)=="ftyp")return ".m4a";
   if(Encoding.ASCII.GetString(h,0,3)=="ID3"||(h[0]==255&&(h[1]&0xe0)==0xe0))return ".mp3";
   throw new InvalidDataException("恢复载荷未识别为有效音频；可能是密钥不匹配、文件不完整或尚未支持的封装版本");
  }
  static DecodedSource DecodeNcm(string source,string folder,CancellationToken ct) {
   var result=new DecodedSource{Kind="NCM",Platform="网易云音乐",AudioPath=Path.Combine(folder,"raw-audio.bin")};
   using(var input=File.OpenRead(source))using(var reader=new BinaryReader(input)) {
    if(Encoding.ASCII.GetString(Exact(reader,8))!="CTENFDAM")throw new InvalidDataException("NCM 文件头无效");Exact(reader,2);
    byte[] encryptedKey=Exact(reader,Length(reader,65536));for(int i=0;i<encryptedKey.Length;i++)encryptedKey[i]^=0x64;
    byte[] headerKey=AesDecrypt(encryptedKey,NcmCore);byte[] prefix=Encoding.ASCII.GetBytes("neteasecloudmusic");
    if(headerKey.Length<=17||!headerKey.Take(prefix.Length).SequenceEqual(prefix))throw new InvalidDataException("NCM 音频密钥前缀无效");
    byte[] key=headerKey.Skip(17).ToArray();byte[] box=Enumerable.Range(0,256).Select(i=>(byte)i).ToArray();int last=0;
    for(int i=0;i<256;i++){int next=(box[i]+last+key[i%key.Length])&255;byte swap=box[i];box[i]=box[next];box[next]=swap;last=next;}
    byte[] encryptedMeta=Exact(reader,Length(reader,4*1024*1024));
    if(encryptedMeta.Length>0) {
     for(int i=0;i<encryptedMeta.Length;i++)encryptedMeta[i]^=0x63;
     string data=Encoding.ASCII.GetString(encryptedMeta);const string marker="163 key(Don't modify):";
     if(!data.StartsWith(marker))throw new InvalidDataException("NCM 元信息前缀无效");
     ParseNcmMetadata(data.Substring(marker.Length),result);
    }
    uint crc=reader.ReadUInt32();Exact(reader,1);uint allocated=reader.ReadUInt32();int imageSize=Length(reader,16*1024*1024);
    byte[] image=Exact(reader,imageSize);
    if(allocated!=0&&allocated<imageSize)throw new InvalidDataException("NCM 封面分配长度小于实际长度");
    long extra=allocated==0?0:allocated-imageSize;if(extra>16*1024*1024||extra>input.Length-input.Position)throw new InvalidDataException("NCM 封面填充长度无效");input.Seek(extra,SeekOrigin.Current);
    if(image.Length>0) {string ext=image.Length>=8&&image[0]==0x89&&image[1]==0x50&&image[2]==0x4e?".png":image.Length>=3&&image[0]==255&&image[1]==216?".jpg":null;if(ext==null)throw new InvalidDataException("NCM 封面编码无法保真写入目标格式");result.CoverPath=Path.Combine(folder,"container-cover"+ext);File.WriteAllBytes(result.CoverPath,image);}
    result.Metadata["containerCrc32"]=crc;
    long offset=0;using(var output=new FileStream(result.AudioPath,FileMode.CreateNew,FileAccess.Write)) {
     byte[] buffer=new byte[65536];int n;while((n=input.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();for(int i=0;i<n;i++){int j=(int)((offset+i+1)&255);buffer[i]^=box[(box[j]+box[(box[j]+j)&255])&255];}output.Write(buffer,0,n);offset+=n;}
    }
   }
   return result;
  }
  static void AddTag(DecodedSource result,string destination,string source) {string value=Json.Str(result.Metadata,source);if(value.Length>0)result.Tags[destination]=value;}
  static void ParseNcmMetadata(string encoded,DecodedSource result) {
   string metadata=new UTF8Encoding(false,true).GetString(AesDecrypt(Convert.FromBase64String(encoded),NcmMeta));
   if(!metadata.StartsWith("music:"))throw new InvalidDataException("NCM 元信息类型尚未支持");
   result.Metadata=Json.Object(metadata.Substring(6));result.SongId=Json.Str(result.Metadata,"musicId");
   AddTag(result,"title","musicName");AddTag(result,"album","album");AddTag(result,"track","trackNumber");
   if(!string.IsNullOrEmpty(result.SongId))result.Tags["netease_id"]=result.SongId;
   object artists;if(result.Metadata.TryGetValue("artist",out artists)){var names=new List<string>();foreach(var row in (IEnumerable)artists){var values=((IEnumerable)row).Cast<object>().ToArray();if(values.Length>0)names.Add(Convert.ToString(values[0]));}result.Tags["artist"]=string.Join(" / ",names);}
  }
  public static DecodedSource ReadIdentity(string source) {
   if(!IsWrapped(source))return null;
   using(var input=File.OpenRead(source))using(var reader=new BinaryReader(input)) {
    if(Path.GetExtension(source).Equals(".ncm",StringComparison.OrdinalIgnoreCase)) {
     if(Encoding.ASCII.GetString(Exact(reader,8))!="CTENFDAM")throw new InvalidDataException("NCM 头无效");Exact(reader,2);Exact(reader,Length(reader,65536));byte[] bytes=Exact(reader,Length(reader,4*1024*1024));
     for(int i=0;i<bytes.Length;i++)bytes[i]^=0x63;string text=Encoding.ASCII.GetString(bytes);const string marker="163 key(Don't modify):";
     var result=new DecodedSource{Kind="NCM",Platform="网易云音乐"};if(bytes.Length>0){if(!text.StartsWith(marker))throw new InvalidDataException("NCM 元数据头无效");ParseNcmMetadata(text.Substring(marker.Length),result);}return result;
    }
    var qq=new DecodedSource{Platform="QQ音乐",Kind="QMC"};if(input.Length<16)return qq;input.Seek(-16,SeekOrigin.End);byte[] tail=Exact(reader,16);
    if(Encoding.ASCII.GetString(tail,8,8)=="musicex\0") {uint size=BitConverter.ToUInt32(tail,0);if(BitConverter.ToUInt32(tail,4)!=1||size<192||size>=input.Length||size>16*1024*1024)throw new InvalidDataException("musicex 版本/长度无效");input.Position=input.Length-size;byte[] footer=Exact(reader,192);qq.SongId=Utf16Field(footer,12,60);qq.Kind="musicex V1";qq.Metadata["resourceFilename"]=Utf16Field(footer,72,68);return qq;}
    string flag=Encoding.ASCII.GetString(tail,12,4);if(flag=="QTag"||flag=="STag"){uint size=ReadBe(tail,8);if(size==0||size>65536||size+8>=input.Length)throw new InvalidDataException("QQ 标识长度无效");input.Position=input.Length-size-8;string[] fields=Encoding.UTF8.GetString(Exact(reader,(int)size)).Split(',');qq.SongId=flag=="QTag"?(fields.Length>1?fields[1]:""):(fields.Length>0?fields[0]:"");qq.Kind=flag;}return qq;
   }
  }
  static DecodedSource DecodeQmc(string source,string folder,CancellationToken ct) {
   var result=new DecodedSource{Kind="QMC1",Platform="QQ音乐",AudioPath=Path.Combine(folder,"raw-audio.bin")};
   using(var input=File.OpenRead(source))using(var reader=new BinaryReader(input)) {
    long audioLength=input.Length;if(audioLength<16)throw new InvalidDataException("QQ 封装文件过短");string supplied=ReadEKey(source);string ekey=supplied;
    input.Seek(-16,SeekOrigin.End);byte[] trailer=Exact(reader,16);
    if(Encoding.ASCII.GetString(trailer,8,8)=="musicex\0") {
     uint footerSize=BitConverter.ToUInt32(trailer,0),version=BitConverter.ToUInt32(trailer,4);
     if(version!=1||footerSize<192||footerSize>16*1024*1024||footerSize>=input.Length)throw new InvalidDataException("musicex 尾部版本/长度尚未支持");
     audioLength=input.Length-footerSize;input.Position=audioLength;byte[] footer=Exact(reader,(int)footerSize);
     result.SongId=Utf16Field(footer,0x0c,60);string resource=Utf16Field(footer,0x48,68);result.Kind="musicex V1";result.Metadata["resourceFilename"]=resource;result.Metadata["songMid"]=result.SongId;
     if(result.SongId.Length==0||resource.Length==0)throw new InvalidDataException("musicex 尾部缺少资源标识");
     if(string.IsNullOrEmpty(ekey))throw new MissingKeyException("该 musicex 文件没有内嵌密钥；选择歌曲后点击「提供解码密钥」，输入该文件对应的 ekey。不会读取客户端登录信息。");
    } else {
     string marker=Encoding.ASCII.GetString(trailer,12,4);
     if(marker=="QTag"||marker=="STag") {
      uint footerSize=ReadBe(trailer,8);if(footerSize==0||footerSize>65536||footerSize+8>=input.Length)throw new InvalidDataException("QQ 尾部标记长度无效");
      audioLength=input.Length-footerSize-8;input.Position=audioLength;string text=new UTF8Encoding(false,true).GetString(Exact(reader,(int)footerSize));string[] fields=text.Split(',');
      if(marker=="QTag") {if(fields.Length<2)throw new InvalidDataException("QTag 字段不完整");if(string.IsNullOrEmpty(ekey))ekey=fields[0];result.SongId=fields[1];}
      else {if(fields.Length>0)result.SongId=fields[0];if(string.IsNullOrEmpty(ekey))throw new MissingKeyException("STag 文件未包含密钥，请提供该文件对应的 ekey");}
      result.Kind=marker;result.Metadata["songId"]=result.SongId;
     } else {
      uint keyLength=BitConverter.ToUInt32(trailer,12);
      if(keyLength>0&&keyLength<=65536&&keyLength+4<input.Length) {
       long begin=input.Length-keyLength-4;input.Position=begin;byte[] bytes=Exact(reader,(int)keyLength);string candidate=Encoding.ASCII.GetString(bytes).TrimEnd('\0');
       // A bounded length alone is not sufficient to distinguish a footer from random audio bytes.
       if(candidate.Length>=16&&candidate.All(c=>char.IsLetterOrDigit(c)||c=='+'||c=='/'||c=='=')) {if(string.IsNullOrEmpty(ekey))ekey=candidate;audioLength=begin;result.Kind="QMC2 内嵌密钥";}
      }
     }
    }
    string extension=Path.GetExtension(source).ToLowerInvariant();QmcCipher cipher=null;
    if(!string.IsNullOrWhiteSpace(ekey)) {cipher=new QmcCipher(ParseEKey(ekey));result.Kind+=(cipher.IsRc4?" / RC4":" / Map");}
    else if(extension.StartsWith(".mflac")||extension.StartsWith(".mgg")||extension==".mmp4")throw new MissingKeyException("该 QQ 文件未提供可读取的内嵌密钥，请提供对应 ekey；原文件保留");
    input.Position=0;long offset=0;using(var output=new FileStream(result.AudioPath,FileMode.CreateNew,FileAccess.Write)) {
     byte[] buffer=new byte[65536];while(offset<audioLength){ct.ThrowIfCancellationRequested();int n=input.Read(buffer,0,(int)Math.Min(buffer.Length,audioLength-offset));if(n==0)throw new EndOfStreamException("QQ 音频载荷被截断");if(cipher==null)LegacyXor(offset,buffer,n);else cipher.Transform(offset,buffer,0,n);output.Write(buffer,0,n);offset+=n;}
    }
   }
   if(!string.IsNullOrEmpty(result.SongId))result.Tags[result.SongId.All(char.IsDigit)?"qqmusic_id":"qqmusic_mid"]=result.SongId;
   return result;
  }
  static string ReadEKey(string source) {string path=source+".ekey";if(!File.Exists(path))return "";if(new FileInfo(path).Length>65536)throw new InvalidDataException("ekey 配套文件过大");string text=File.ReadAllText(path,Encoding.UTF8).Trim().Trim('\0');if(text.StartsWith("{"))text=Json.Str(Json.Object(text),"ekey");return text;}
  static string Utf16Field(byte[] data,int offset,int length) {if(offset+length>data.Length)throw new InvalidDataException("musicex 字段越界");string text=new UnicodeEncoding(false,false,true).GetString(data,offset,length);int zero=text.IndexOf('\0');return zero<0?text:text.Substring(0,zero);}
  public static void LegacyXor(long offset,byte[] buffer,int length) {for(int i=0;i<length;i++){long pos=offset+i;if(pos>0x7fff)pos%=0x7fff;long index=pos&0x7f;if(index>0x3f)index=(0x80-index)&0x3f;buffer[i]^=LegacyTable[index];}}
  public static byte[] ParseEKey(string ekey) {
   byte[] value=Convert.FromBase64String(ekey.Trim().Trim('\0'));byte[] prefix=Encoding.ASCII.GetBytes("QQMusic EncV2,Key:");
   if(value.Take(prefix.Length).SequenceEqual(prefix)) {byte[] stage=value.Skip(prefix.Length).ToArray();stage=TeaDecrypt(stage,Encoding.ASCII.GetBytes("386ZJY!@#*$%^&)("));stage=TeaDecrypt(stage,Encoding.ASCII.GetBytes("**#!(#$%&^a1cZ,T"));value=Convert.FromBase64String(Encoding.ASCII.GetString(stage));}
   if(value.Length<24||value.Length>65536)throw new InvalidDataException("ekey 长度无效");byte[] header=value.Take(8).ToArray();byte[] mask={0x69,0x56,0x46,0x38,0x2b,0x20,0x15,0x0b};byte[] tea=new byte[16];for(int i=0;i<8;i++){tea[i*2]=mask[i];tea[i*2+1]=header[i];}
   return header.Concat(TeaDecrypt(value.Skip(8).ToArray(),tea)).ToArray();
  }
  public static byte[] TeaDecrypt(byte[] encrypted,byte[] key) {
   if(encrypted.Length<16||encrypted.Length%8!=0||key.Length!=16)throw new InvalidDataException("QQ TEA 密钥块长度无效");
   uint[] k=new uint[4];for(int i=0;i<4;i++)k[i]=ReadBe(key,i*4);byte[] decoded=new byte[encrypted.Length];byte[] previous=new byte[8];
   for(int offset=0;offset<encrypted.Length;offset+=8) {
    byte[] block=new byte[8];for(int i=0;i<8;i++)block[i]=(byte)(encrypted[offset+i]^previous[i]);
    uint y=ReadBe(block,0),z=ReadBe(block,4),sum=0xe3779b90;
    unchecked {for(int round=0;round<16;round++){z-=((y<<4)+k[2])^(y+sum)^((y>>5)+k[3]);y-=((z<<4)+k[0])^(z+sum)^((z>>5)+k[1]);sum-=0x9e3779b9;}}
    WriteBe(block,0,y);WriteBe(block,4,z);previous=(byte[])block.Clone();
    for(int i=0;i<8;i++)decoded[offset+i]=(byte)(block[i]^(offset==0?0:encrypted[offset-8+i]));
   }
   int start=1+(decoded[0]&7)+2,end=decoded.Length-7;if(start>end||decoded.Skip(end).Any(b=>b!=0))throw new InvalidDataException("QQ TEA 填充校验失败，密钥可能不匹配");return decoded.Skip(start).Take(end-start).ToArray();
  }
  static uint ReadBe(byte[] data,int offset) {return ((uint)data[offset]<<24)|((uint)data[offset+1]<<16)|((uint)data[offset+2]<<8)|data[offset+3];}
  static void WriteBe(byte[] data,int offset,uint value) {data[offset]=(byte)(value>>24);data[offset+1]=(byte)(value>>16);data[offset+2]=(byte)(value>>8);data[offset+3]=(byte)value;}
 }
 public sealed class QmcCipher {
  readonly byte[] key,state;readonly uint hash;public bool IsRc4 {get;private set;}
  public QmcCipher(byte[] key):this(key,key.Length>300){}
  public QmcCipher(byte[] key,bool useRc4) {
   if(key.Length<8||key.Length>65536)throw new InvalidDataException("QQ 音频密钥长度无效");this.key=(byte[])key.Clone();IsRc4=useRc4;
   state=new byte[key.Length];for(int i=0;i<state.Length;i++)state[i]=(byte)i;int j=0;for(int i=0;i<key.Length;i++){j=(j+state[i]+key[i])%key.Length;byte swap=state[i];state[i]=state[j];state[j]=swap;}
   uint value=1;foreach(byte b in key){if(b==0)continue;uint next=unchecked(value*b);if(next==0||next<=value)break;value=next;}hash=value;
  }
  ulong SegmentKey(long id,byte seed) {double divisor=(id+1)*(double)seed;double result=hash/divisor*100.0;return double.IsInfinity(result)||result>=ulong.MaxValue?ulong.MaxValue:(ulong)result;}
  public void Transform(long offset,byte[] data,int start,int count) {
   if(!IsRc4){for(int i=0;i<count;i++){long pos=offset+i;if(pos>0x7fff)pos%=0x7fff;int index=(int)((pos*pos+71214)%key.Length),rotation=(index+4)&7;data[start+i]^=(byte)((key[index]<<rotation)|(key[index]>>rotation));}return;}
   int cursor=start,left=count;long current=offset;
   if(current<128){int n=(int)Math.Min(left,128-current);for(int i=0;i<n;i++){byte b=key[(int)(current%key.Length)];data[cursor++]^=key[(int)(SegmentKey(current,b)%(ulong)key.Length)];current++;left--;}}
   while(left>0) {
    long segment=current/5120;int intra=(int)(current%5120),n=Math.Min(left,5120-intra);int seedIndex=(int)(segment&511);
    if(seedIndex>=key.Length)throw new InvalidDataException("QQ RC4 密钥不足 512 字节");
    int discard=(int)(SegmentKey(segment,key[seedIndex])&511)+intra;byte[] s=(byte[])state.Clone();int j=0,k=0;
    for(int i=0;i<discard+n;i++){j=(j+1)%s.Length;k=(k+s[j])%s.Length;byte swap=s[j];s[j]=s[k];s[k]=swap;byte mask=s[(s[j]+s[k])%s.Length];if(i>=discard)data[cursor++]^=mask;}
    current+=n;left-=n;
   }
  }
 }
}
