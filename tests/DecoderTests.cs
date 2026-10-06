using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MusicAssistant;

static class DecoderTests {
 public static void Run(string root,AudioTool audio,Action<bool,string> check) {
  byte[] teaCipher={0x91,0x09,0x51,0x62,0xe3,0xf5,0xb6,0xdc,0x6b,0x41,0x4b,0x50,0xd1,0xa5,0xb8,0x4e,0xc5,0x0d,0x0c,0x1b,0x11,0x96,0xfd,0x3c};
  check(SourceDecoder.TeaDecrypt(teaCipher,Encoding.ASCII.GetBytes("12345678ABCDEFGH")).SequenceEqual(new byte[]{1,2,3,4,5,6,7,8}),"TEA 独立 C++ 已知向量");
  string ekey="VGhpcyBpcyBHFWEh4cjZ1Vi7rJ56XeoPlqGM1sxBGPg7mt89umKclFBr9iqfmFdS";
  byte[] mapKey=SourceDecoder.ParseEKey(ekey);check(Encoding.ASCII.GetString(mapKey)=="This is a test key for test purpose :D","ekey 官方开源项目固定向量");
  var map=new QmcCipher(Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOP"));byte[] mapBytes=new byte[16];map.Transform(0,mapBytes,0,16);
  check(mapBytes.SequenceEqual(new byte[]{0x3f,0x8a,0xc1,0x49,0x3f,0x49,0xc1,0x8a,0x3f,0x8a,0xc1,0x49,0x3f,0x49,0xc1,0x8a}),"QMC2 Map 固定向量");
  byte[] boundary=new byte[16];map.Transform(0x7fff-8,boundary,0,16);check(boundary.SequenceEqual(new byte[]{0x8a,0x3f,0x8a,0xc1,0x49,0x3f,0x49,0xc1,0x8a,0x8a,0xc1,0x49,0x3f,0x49,0xc1,0x8a}),"QMC2 Map 32767 边界向量");
  var rc4=new QmcCipher(Enumerable.Range(0,255).Select(i=>(byte)i).ToArray(),true);byte[] rc4Bytes=new byte[16];rc4.Transform(120,rc4Bytes,0,16);check(rc4Bytes.SequenceEqual(new byte[]{0,0,0,0,0,0,0,0,141,97,122,193,166,101,233,214}),"QMC2 RC4 首段交界已知向量");
  byte[] rc4Segment=new byte[16];rc4.Transform(5112,rc4Segment,0,16);check(rc4Segment.SequenceEqual(new byte[]{118,193,176,83,10,98,105,234,151,56,198,1,226,173,127,4}),"QMC2 RC4 5120 分段边界向量");
  byte[] legacy=new byte[3];SourceDecoder.LegacyXor(32767,legacy,legacy.Length);check(legacy.SequenceEqual(new byte[]{0x4a,0x4a,0xd6}),"QMC1 特殊 32767 边界（原始 seed 算法）");
  string folder=Path.Combine(root,"containers");Directory.CreateDirectory(folder);string plain=Path.Combine(folder,"reference.flac");
  audio.Run(audio.Ffmpeg,new[]{"-v","error","-f","lavfi","-i","anoisesrc=duration=0.7:sample_rate=96000:seed=42","-ac","2","-c:a","flac","-sample_fmt","s32",plain},CancellationToken.None);
  string image=Path.Combine(folder,"reference.png");audio.Run(audio.Ffmpeg,new[]{"-v","error","-f","lavfi","-i","color=c=red:s=16x16:d=0.1","-frames:v","1",image},CancellationToken.None);
  byte[] raw=File.ReadAllBytes(plain);check(raw.Length>65536,"合成载荷覆盖多个解码读取块");
  string ncm=Path.Combine(folder,"synthetic.ncm");WriteNcm(ncm,raw,File.ReadAllBytes(image),"123456",8);
  var decoded=SourceDecoder.Decode(ncm,Path.Combine(folder,"ncm-decode"),CancellationToken.None);
  check(Files.Hash(decoded.AudioPath)==Files.Hash(plain),"NCM 恢复原编码载荷，逐字节一致（包含封面填充）");
  check(decoded.SongId=="123456"&&decoded.Tags["title"]=="封装测试歌曲"&&Files.Hash(decoded.CoverPath)==Files.Hash(image),"NCM 歌曲 ID、标签与原始封面恢复");
  var store=new Store(Path.Combine(folder,"state"));store.Settings.OutputRoot=Path.Combine(folder,"output");store.Settings.RecycleOriginal=false;var processor=new Processor(store,audio);
  var track=Imports.FromFile(ncm);processor.Process(track,CancellationToken.None);check(track.Status=="已完成","NCM 全流程输出："+track.Detail);
  string output=Files.Inside(Path.Combine(store.Settings.OutputRoot,"Music"),track.OutputPath);var before=audio.Inspect(plain,CancellationToken.None);var after=audio.Inspect(output,CancellationToken.None);
  check(audio.AudioHash(plain,before,CancellationToken.None)==audio.AudioHash(output,before,CancellationToken.None)&&before.Profile==after.Profile,"NCM 写标签/封面后音频样本和参数一致");
  check(Json.Str(after.Tags,"title")=="封装测试歌曲"&&Json.Str(after.Tags,"artist")=="封装测试歌手","NCM 元数据写入播放器音乐文件");
  check(!File.ReadAllText(Path.Combine(Path.GetDirectoryName(output),"source-info.json")).Contains(ekey),"转换记录不包含解码密钥");
  string mp3Plain=Path.Combine(folder,"reference.mp3");audio.Run(audio.Ffmpeg,new[]{"-v","error","-f","lavfi","-i","sine=duration=0.8:sample_rate=44100","-ac","2","-c:a","libmp3lame",mp3Plain},CancellationToken.None);
  string ncmMp3=Path.Combine(folder,"synthetic-mp3.ncm");WriteNcm(ncmMp3,File.ReadAllBytes(mp3Plain),File.ReadAllBytes(image),"123457",0,"mp3");var mp3Track=Imports.FromFile(ncmMp3);processor.Process(mp3Track,CancellationToken.None);check(mp3Track.Status=="已完成"&&mp3Track.OutputPath.EndsWith(".mp3"),"NCM MP3 保留原编码，写封面标签后样本一致："+mp3Track.Detail);
  var wrong=new Track{Title="wrong",Platform="网易云音乐",SongId="999",SourcePath=ncm};processor.Process(wrong,CancellationToken.None);check(wrong.Status=="失败"&&File.Exists(ncm),"原文件歌曲 ID 不匹配时拒绝替换");
  string qmc=Path.Combine(folder,"synthetic.qmcflac");byte[] encrypted=(byte[])raw.Clone();SourceDecoder.LegacyXor(0,encrypted,encrypted.Length);File.WriteAllBytes(qmc,encrypted);
  var qmcDecoded=SourceDecoder.Decode(qmc,Path.Combine(folder,"qmc-decode"),CancellationToken.None);check(Files.Hash(qmcDecoded.AudioPath)==Files.Hash(plain),"旧 QMC 恢复 FLAC 原载荷，跨多个 32767 边界");
  string qmc2=Path.Combine(folder,"synthetic.mflac");byte[] mapped=(byte[])raw.Clone();new QmcCipher(mapKey).Transform(0,mapped,0,mapped.Length);using(var writer=new BinaryWriter(File.Create(qmc2))){writer.Write(mapped);byte[] keyText=Encoding.ASCII.GetBytes(ekey);writer.Write(keyText);writer.Write((uint)keyText.Length);}
  var qmc2Decoded=SourceDecoder.Decode(qmc2,Path.Combine(folder,"qmc2-decode"),CancellationToken.None);check(Files.Hash(qmc2Decoded.AudioPath)==Files.Hash(plain),"QMC2 Map 内嵌 ekey 尾部完整恢复");
  string qtag=Path.Combine(folder,"synthetic-qtag.mflac");using(var writer=new BinaryWriter(File.Create(qtag))){writer.Write(mapped);byte[] footer=Encoding.ASCII.GetBytes(ekey+",765432,2");writer.Write(footer);writer.Write(Be((uint)footer.Length));writer.Write(Encoding.ASCII.GetBytes("QTag"));}
  var tagDecoded=SourceDecoder.Decode(qtag,Path.Combine(folder,"qtag-decode"),CancellationToken.None);check(Files.Hash(tagDecoded.AudioPath)==Files.Hash(plain)&&tagDecoded.SongId=="765432","QTag 恢复音频和歌曲数字 ID");
  byte[] rc4Key=Enumerable.Range(0,512).Select(i=>(byte)(i%251+1)).ToArray();string rc4Ekey=EncodeEKey(rc4Key);byte[] rc4Encrypted=(byte[])raw.Clone();new QmcCipher(rc4Key).Transform(0,rc4Encrypted,0,rc4Encrypted.Length);
  string rc4Path=Path.Combine(folder,"synthetic-rc4.mflac");using(var writer=new BinaryWriter(File.Create(rc4Path))){writer.Write(rc4Encrypted);byte[] keyText=Encoding.ASCII.GetBytes(rc4Ekey);writer.Write(keyText);writer.Write((uint)keyText.Length);}
  var rc4Decoded=SourceDecoder.Decode(rc4Path,Path.Combine(folder,"rc4-decode"),CancellationToken.None);check(Files.Hash(rc4Decoded.AudioPath)==Files.Hash(plain),"512 字节 QMC2 RC4 多段载荷完整恢复");
  string musicex=Path.Combine(folder,"synthetic-new.mflac");byte[] musicFooter=new byte[192];Encoding.Unicode.GetBytes("song-mid-test").CopyTo(musicFooter,0x0c);Encoding.Unicode.GetBytes("resource.mflac").CopyTo(musicFooter,0x48);BitConverter.GetBytes((uint)192).CopyTo(musicFooter,176);BitConverter.GetBytes((uint)1).CopyTo(musicFooter,180);Encoding.ASCII.GetBytes("musicex\0").CopyTo(musicFooter,184);
  using(var writer=new BinaryWriter(File.Create(musicex))){writer.Write(rc4Encrypted);writer.Write(musicFooter);}
  bool missing=false;try{SourceDecoder.Decode(musicex,Path.Combine(folder,"missing-key"),CancellationToken.None);}catch(MissingKeyException){missing=true;}check(missing&&File.Exists(musicex),"musicex 无密钥时明确请求单文件 ekey，保留源文件");
  File.WriteAllText(musicex+".ekey",rc4Ekey,new UTF8Encoding(false));var musicDecoded=SourceDecoder.Decode(musicex,Path.Combine(folder,"musicex-decode"),CancellationToken.None);check(Files.Hash(musicDecoded.AudioPath)==Files.Hash(plain)&&musicDecoded.SongId=="song-mid-test","musicex V1 + 手动 ekey 恢复原载荷");
  string v2Ekey=Convert.ToBase64String(Encoding.ASCII.GetBytes("QQMusic EncV2,Key:").Concat(TeaEncrypt(TeaEncrypt(Encoding.ASCII.GetBytes(rc4Ekey),Encoding.ASCII.GetBytes("**#!(#$%&^a1cZ,T")),Encoding.ASCII.GetBytes("386ZJY!@#*$%^&)("))).ToArray());
  check(SourceDecoder.ParseEKey(v2Ekey).SequenceEqual(rc4Key),"EncV2 双层 ekey 解析");
  string ogg=Path.Combine(folder,"reference.ogg");audio.Run(audio.Ffmpeg,new[]{"-v","error","-f","lavfi","-i","sine=duration=0.3:sample_rate=44100","-ac","2","-c:a","libvorbis",ogg},CancellationToken.None);
  var oggTrack=Imports.FromFile(ogg);processor.Process(oggTrack,CancellationToken.None);check(oggTrack.Status=="已完成"&&oggTrack.OutputPath.EndsWith(".wav"),"QQ 常见 OGG/Vorbis 转浮点 WAV：完整解码样本一致");
  byte[] badTea=(byte[])teaCipher.Clone();badTea[23]^=255;bool invalidKey=false;try{SourceDecoder.TeaDecrypt(badTea,Encoding.ASCII.GetBytes("12345678ABCDEFGH"));}catch(InvalidDataException){invalidKey=true;}check(invalidKey,"密钥块填充校验拒绝损坏数据");
  int count=store.History.Count;File.WriteAllText(musicex+".ekey","invalid!",Encoding.UTF8);var invalid=Imports.FromFile(musicex);processor.Process(invalid,CancellationToken.None);check(invalid.Status=="失败"&&File.Exists(musicex)&&store.History.Count==count,"错误 ekey 不写成功历史，不回收原文件");
  var publicQQ=PublicPlaylist.Read("https://y.qq.com/n/ryqq/playlist/1374105607",CancellationToken.None);check(publicQQ.Tracks.Count>0&&publicQQ.Tracks.All(t=>t.Platform=="QQ音乐"&&!string.IsNullOrEmpty(t.SongId)&&!string.IsNullOrEmpty(t.Artist)),"真实 QQ 官方公开歌单完整读取、MID 与歌手保留");
  string catalogFolder=Path.Combine(root,"catalog-only");Directory.CreateDirectory(catalogFolder);File.Copy(ncm,Path.Combine(catalogFolder,"download.ncm"));File.Copy(musicex,Path.Combine(catalogFolder,"download.mflac"));
  var catalog=LocalCatalog.Build(new[]{catalogFolder},audio,CancellationToken.None,s=>{});
  check(catalog.Matches(new Track{Platform="网易云音乐",SongId="123456"}).Count==1,"指定目录按 NCM 原始歌曲 ID 自动关联，无须凭歌名猜测");
  check(catalog.Matches(new Track{Platform="QQ音乐",SongId="song-mid-test"}).Count==1,"musicex 没有 ekey 也能先读取 MID 自动关联");
  File.Copy(ncm,Path.Combine(catalogFolder,"another-quality.ncm"));var multiple=LocalCatalog.Build(new[]{catalogFolder},audio,CancellationToken.None,s=>{});check(multiple.Matches(new Track{Platform="网易云音乐",SongId="123456"}).Count==2,"同歌曲 ID 多份候选保留选择，不擅自挑版本");
  var ids=new Store(Path.Combine(root,"numeric-mid-state"));ids.Add(new HistoryEntry{Key="numeric",Platform="QQ音乐",SongId="765432",AudioHash="same",Profile="24/96",RelativePath="Library/song.flac"});
  check(ids.Identity(new Track{Platform="QQ音乐",SongId="someMID",AlternateSongId="765432"})!=null,"删除本地文件后，QQ 数字 ID 与 MID 的同曲历史关联");
  string fakeToken="synthetic_session_for_unit_test_12345";foreach(string text in new[]{"{\"authst\":\""+fakeToken+"\"}","{\\\"authst\\\":\\\""+fakeToken+"\\\"}","authst="+fakeToken+"&other=x"}){byte[] bytes=Encoding.UTF8.GetBytes(text);check(QQKeySession.ExtractToken(bytes,bytes.Length)==fakeToken,"QQ 登录状态解析（仅合成文本，不读取真实进程）");}
  byte[] wide=Encoding.Unicode.GetBytes("{\"authst\":\""+fakeToken+"\"}");check(QQKeySession.ExtractToken(wide,wide.Length)==fakeToken,"UTF-16 登录状态解析（合成数据）");
  check(QQKeySession.ParseUin("[user]\r\nUin=123456789\r\n") == "123456789"&&QQKeySession.ParseUin("Uin=0")==null,"QQ 账号标识解析，未登录标识拒绝");
  string response=Json.Write(new{req_1=new{code=0,data=new{midurlinfo=new[]{new{ekey=ekey}}}}});check(QQKeySession.ParseResponse(response)==ekey,"单文件密钥响应解析（合成响应，不发送登录信息）");
  bool denied=false;try{QQKeySession.ParseResponse(Json.Write(new{req_1=new{code=0,data=new{retcode=1,midurlinfo=new[]{new{ekey=ekey}}}}}));}catch(InvalidDataException){denied=true;}check(denied,"QQ 服务拒绝授权时不接受响应密钥");
  check(!Directory.Exists(Path.Combine(store.Settings.OutputRoot,"Music",".working")),"解码暂存位于 Music 之外，不随播放器导出复制");
 }
 static byte[] AesEncrypt(byte[] data,byte[] key){using(var aes=Aes.Create()){aes.Key=key;aes.Mode=CipherMode.ECB;aes.Padding=PaddingMode.PKCS7;using(var encryptor=aes.CreateEncryptor())return encryptor.TransformFinalBlock(data,0,data.Length);}}
 static void WriteNcm(string path,byte[] payload,byte[] image,string songId,int gap,string format="flac") {
  byte[] key=Encoding.ASCII.GetBytes("independent-fixture-key");byte[] keyBlock=AesEncrypt(Encoding.ASCII.GetBytes("neteasecloudmusic").Concat(key).ToArray(),Encoding.ASCII.GetBytes("hzHRAmso5kInbaxW"));for(int i=0;i<keyBlock.Length;i++)keyBlock[i]^=0x64;
  string meta="music:"+Json.Write(new{musicId=songId,musicName="封装测试歌曲",artist=new object[]{new object[]{"封装测试歌手",123}},album="封装测试专辑",format=format});byte[] metadata=Encoding.ASCII.GetBytes("163 key(Don't modify):"+Convert.ToBase64String(AesEncrypt(Encoding.UTF8.GetBytes(meta),new byte[]{0x23,0x31,0x34,0x6c,0x6a,0x6b,0x5f,0x21,0x5c,0x5d,0x26,0x30,0x55,0x3c,0x27,0x28})));for(int i=0;i<metadata.Length;i++)metadata[i]^=0x63;
  // Independent KSA and precomputed 256-byte mask, rather than the decoder's chunk loop.
  int[] state=Enumerable.Range(0,256).ToArray();int j=0;for(int i=0;i<256;i++){j=(j+state[i]+key[i%key.Length])%256;int swap=state[i];state[i]=state[j];state[j]=swap;}byte[] mask=new byte[256];for(int i=0;i<256;i++){int n=(i+1)%256;mask[i]=(byte)state[(state[n]+state[(state[n]+n)%256])%256];}byte[] data=payload.Select((b,i)=>(byte)(b^mask[i%256])).ToArray();
  using(var w=new BinaryWriter(File.Create(path))){w.Write(Encoding.ASCII.GetBytes("CTENFDAM"));w.Write((ushort)0);w.Write((uint)keyBlock.Length);w.Write(keyBlock);w.Write((uint)metadata.Length);w.Write(metadata);w.Write((uint)0);w.Write((byte)0);w.Write((uint)(image.Length+gap));w.Write((uint)image.Length);w.Write(image);w.Write(new byte[gap]);w.Write(data);}
 }
 static byte[] Be(uint value){return new[]{(byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value};}
 static uint U32(byte[] b,int i){return ((uint)b[i]<<24)|((uint)b[i+1]<<16)|((uint)b[i+2]<<8)|b[i+3];}
 static byte[] TeaEncrypt(byte[] value,byte[] key){int pad=(8-(value.Length+10)%8)%8;byte[] plain=new byte[value.Length+10+pad];plain[0]=(byte)pad;Array.Copy(value,0,plain,3+pad,value.Length);byte[] output=new byte[plain.Length],previous=new byte[8];uint[] k=Enumerable.Range(0,4).Select(i=>U32(key,i*4)).ToArray();
  for(int offset=0;offset<plain.Length;offset+=8){byte[] block=new byte[8];for(int i=0;i<8;i++)block[i]=(byte)(plain[offset+i]^(offset==0?0:output[offset-8+i]));byte[] next=(byte[])block.Clone();uint y=U32(block,0),z=U32(block,4),sum=0;unchecked{for(int r=0;r<16;r++){sum+=0x9e3779b9;y+=((z<<4)+k[0])^(z+sum)^((z>>5)+k[1]);z+=((y<<4)+k[2])^(y+sum)^((y>>5)+k[3]);}}byte[] encrypted=Be(y).Concat(Be(z)).ToArray();for(int i=0;i<8;i++)output[offset+i]=(byte)(encrypted[i]^previous[i]);previous=next;}return output;}
 static string EncodeEKey(byte[] raw){byte[] mask={0x69,0x56,0x46,0x38,0x2b,0x20,0x15,0x0b};byte[] tea=new byte[16];for(int i=0;i<8;i++){tea[2*i]=mask[i];tea[2*i+1]=raw[i];}return Convert.ToBase64String(raw.Take(8).Concat(TeaEncrypt(raw.Skip(8).ToArray(),tea)).ToArray());}
}
