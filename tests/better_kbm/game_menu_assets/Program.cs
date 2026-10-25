using System;
using System.IO;
using System.Linq;
using System.Text;
using System.IO.Compression;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using BloodborneLauncher.Core;
class Program
{
 static void Check(bool b,string s){if(!b)throw new Exception(s);}
 static void Main(string[] args)
 {
  if(args.Length==2 && args[0]=="--real")
  {
   string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(Path.Combine(root,"dvdroot_ps4/menu/engus"));
   File.WriteAllBytes(Path.Combine(root,"eboot.bin"),new byte[]{1});
   File.WriteAllBytes(Path.Combine(root,"dvdroot_ps4/menu/engus/font.gfx"),FontFixture());
   GameAssetExtractor.Extract(root,Path.Combine(root,"gui"));
   Check(GameAssetExtractor.IsReady(Path.Combine(root,"gui")),"Real PS4 title/common archives must extract successfully");
   Console.WriteLine("PASS: real title/common archives, including original 80x80 title icon.");return;
  }
  var half=new byte[]{1,2,3,4, 5,6,7,8, 9,10,11,12, 21,22,23,24};
  var joined=MenuPixels.MirrorHighlight(half,2,2);
  Check(joined.SequenceEqual(new byte[]{1,2,3,4,5,6,7,8,5,6,7,8,1,2,3,4,9,10,11,12,21,22,23,24,21,22,23,24,9,10,11,12}),"Full highlight must mirror each row with identical adjacent center pixels");
  Check(half[0]==1 && half[^1]==24,"Highlight cache must not modify source pixels");
  var padded=new byte[]{1,2,3,10,4,5,6,30,7,8,9,20,0,0,0,0,0,0,0,0};
  var prepared=MenuPixels.PrepareHighlight(padded,5,1,out int fullWidth);
  Check(fullWidth==4 && prepared.SequenceEqual(new byte[]{1,2,3,10,4,5,6,30,4,5,6,30,1,2,3,10}),"Trailing transparent crop columns must not become a dark center band");
  var sandbox=Path.Combine(Path.GetTempPath(),"bb-menu-assets-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(sandbox);
  try { Run(sandbox); }
  finally { Directory.Delete(sandbox,true); }
 }
 static void Run(string sandbox)
 {
  string root=Path.Combine(sandbox,"game");Directory.CreateDirectory(Path.Combine(root,"dvdroot_ps4/menu/engus"));
  File.WriteAllBytes(Path.Combine(root,"eboot.bin"),new byte[]{1});File.WriteAllBytes(Path.Combine(root,"dvdroot_ps4/menu/engus/font.gfx"),FontFixture());
  var encoder=new BcEncoder();encoder.OutputOptions.GenerateMipMaps=false;encoder.OutputOptions.Format=CompressionFormat.Bc7;
  var rgba=Enumerable.Repeat(new byte[]{140,130,100,255},16).SelectMany(x=>x).ToArray();byte[] block=encoder.EncodeToRawBytes(rgba,4,4,PixelFormat.Rgba32)[0];
  Tpf(Path.Combine(root,"dvdroot_ps4/menu/common.tpf.dcx"),"MENU_Common_00003",1024,1024,98,block);
  string commonPath=Path.Combine(root,"dvdroot_ps4/menu/common.tpf.dcx");
  Tpf(commonPath+".book","MENU_Common_00001",2048,2048,98,block);
  JoinTpf(commonPath,commonPath+".book");File.Delete(commonPath+".book");
  Tpf(Path.Combine(root,"dvdroot_ps4/menu/title.tpf.dcx"),"MENU_Title_00001",2048,1024,71,new byte[]{255,255,255,255,0,0,0,0});
  string titlePath=Path.Combine(root,"dvdroot_ps4/menu/title.tpf.dcx");
  Tpf(titlePath+".icon","MENU_Title_00100",96,96,71,new byte[]{255,255,255,255,0,0,0,0});
  JoinTpf(titlePath,titlePath+".icon");File.Delete(titlePath+".icon");
  string output=Path.Combine(sandbox,"gui");if(Directory.Exists(output))Directory.Delete(output,true);
  Check(GameAssetExtractor.FindGameRoot(root)==root,"Root validation");Check(GameAssetExtractor.FindGameRoot(Path.Combine(root,"dvdroot_ps4"))==root,"dvdroot normalization");
  var sourceHashes=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))));
  Check(!GameAssetExtractor.IsReady(output),"Empty cache");GameAssetExtractor.Extract(root,output);
  foreach(var entry in sourceHashes)Check(entry.Value==System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(entry.Key))),"Extraction must not modify game files");
  VerifyPixels(Path.Combine(output,"background.png"),516,292);
  var ico=File.ReadAllBytes(Path.Combine(output,"game-icon.ico"));
  Check(ico.Take(6).SequenceEqual(new byte[]{0,0,1,0,1,0}) && ico[6]==96 && ico[7]==96,"ICO directory must describe the extracted icon");
  Check(BitConverter.ToInt32(ico,18)==22 && BitConverter.ToInt32(ico,14)==ico.Length-22,"ICO payload bounds");
  Check(ico.Skip(22).SequenceEqual(File.ReadAllBytes(Path.Combine(output,"game-icon.png"))),"ICO must preserve the native icon PNG exactly");
  VerifyDimensions(Path.Combine(output,"menu-underline.png"),184,20);
  VerifyDimensions(Path.Combine(output,"menu-sidebar-underline.png"),350,22);
  VerifyDimensions(Path.Combine(output,"menu-book-leaf.png"),472,936);
  // An old GUI cache upgrades without regenerating or overwriting unrelated art.
  var priorArt=new[]{"background.png","menu-frame.png","menu-underline.png","menu-glow-half.png","BloodborneMenu.ttf","game-icon.png","game-icon.ico"}
      .ToDictionary(name=>name,name=>File.ReadAllBytes(Path.Combine(output,name)));
  var priorDates=priorArt.Keys.ToDictionary(name=>name,name=>File.GetLastWriteTimeUtc(Path.Combine(output,name)));
  File.Delete(Path.Combine(output,"menu-sidebar-underline.png"));File.Delete(Path.Combine(output,"menu-book-leaf.png"));
  Check(!GameAssetExtractor.IsReady(output),"Prior GUI cache requires the new book crops");
  GameAssetExtractor.Extract(root,output);
  foreach(var entry in priorArt) {
   Check(File.ReadAllBytes(Path.Combine(output,entry.Key)).SequenceEqual(entry.Value),"Book upgrade preserves existing GUI bytes");
   Check(File.GetLastWriteTimeUtc(Path.Combine(output,entry.Key))==priorDates[entry.Key],"Book upgrade must not rewrite existing GUI files");
  }
  Check(GameAssetExtractor.IsReady(output),"Book upgrade refreshes its complete manifest");
  Check(GameAssetExtractor.IsReady(output),"Complete cache");var time=File.GetLastWriteTimeUtc(Path.Combine(output,"background.png"));GameAssetExtractor.Extract(root,output);Check(time==File.GetLastWriteTimeUtc(Path.Combine(output,"background.png")),"Ready cache must not extract again");
  File.Delete(Path.Combine(output,"game-icon.ico"));
  Check(!GameAssetExtractor.IsReady(output),"Previous-version caches without the icon must upgrade automatically");
  GameAssetExtractor.Extract(root,output);Check(GameAssetExtractor.IsReady(output),"Cache upgrade must regenerate the icon and underline");
  File.WriteAllText(Path.Combine(output,"menu-frame.png"),"broken");Check(!GameAssetExtractor.IsReady(output),"Broken cache detection");GameAssetExtractor.Extract(root,output);Check(GameAssetExtractor.IsReady(output),"Repair broken cache");
  Check(!Directory.EnumerateDirectories(output,".extract-*").Any(),"Staging cleanup");
  var cancel=new System.Threading.CancellationToken(true);try{GameAssetExtractor.Extract(root,output+"-cancel",cancel:cancel);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
  Check(!Directory.EnumerateDirectories(output+"-cancel",".extract-*").Any(),"Cancelled staging cleanup");
  Console.WriteLine("PASS: BC1/BC7 PS4 tile extraction, DFLT/DCX, PNG generation, Unicode font, root validation, cache skip/repair and cancellation cleanup.");
 }
 static void VerifyDimensions(string path,int width,int height)
 {
  var png=File.ReadAllBytes(path);
  int BE(int at)=>(png[at]<<24)|(png[at+1]<<16)|(png[at+2]<<8)|png[at+3];
  Check(BE(16)==width && BE(20)==height,"Native underline crop dimensions");
 }
 static void JoinTpf(string path,string extra)
 {
  byte[] Unpack(string p){using var file=File.OpenRead(p);file.Position=76;using var z=new ZLibStream(file,CompressionMode.Decompress);using var raw=new MemoryStream();z.CopyTo(raw);return raw.ToArray();}
  var packages=new[]{Unpack(path),Unpack(extra)};int length=88+packages.Sum(p=>p.Length-52);
  var result=new byte[length];packages[0].AsSpan(0,16).CopyTo(result);BitConverter.GetBytes(2).CopyTo(result,8);int cursor=88;
  for(int i=0;i<packages.Length;i++)
  {
   var source=packages[i];int at=16+i*36,delta=cursor-52;
   source.AsSpan(16,36).CopyTo(result.AsSpan(at));
   BitConverter.GetBytes(BitConverter.ToInt32(source,16)+delta).CopyTo(result,at);
   BitConverter.GetBytes(BitConverter.ToInt32(source,40)+delta).CopyTo(result,at+24);
   source.AsSpan(52).CopyTo(result.AsSpan(cursor));cursor+=source.Length-52;
  }
  using var file=File.Create(path);var header=new byte[76];Encoding.ASCII.GetBytes("DCX\0").CopyTo(header,0);Encoding.ASCII.GetBytes("DFLT").CopyTo(header,32);file.Write(header);
  using var z=new ZLibStream(file,CompressionLevel.Fastest);z.Write(result);
 }
 static void VerifyPixels(string path,int x,int y)
 {
  var png=File.ReadAllBytes(path);int at=8,width=0;using var compressed=new MemoryStream();
  int BE(int p)=>(png[p]<<24)|(png[p+1]<<16)|(png[p+2]<<8)|png[p+3];
  while(at<png.Length){int n=BE(at);string kind=Encoding.ASCII.GetString(png,at+4,4);if(kind=="IHDR")width=BE(at+8);if(kind=="IDAT")compressed.Write(png,at+8,n);at+=12+n;}
  compressed.Position=0;using var z=new ZLibStream(compressed,CompressionMode.Decompress);using var raw=new MemoryStream();z.CopyTo(raw);var data=raw.ToArray();
  int pixel=y*(1+width*4)+1+x*4,r=(x/4)%32,g=(y/4)%64,b=(x/4+y/4)%32;
  Check(Math.Abs(data[pixel]-((r<<3)|(r>>2)))<=1 && Math.Abs(data[pixel+1]-((g<<2)|(g>>4)))<=1 && Math.Abs(data[pixel+2]-((b<<3)|(b>>2)))<=1,"Nonuniform PS4 block positions must untile correctly");
 }
 static byte[] FontFixture()
 {
  var bits=new System.Collections.Generic.List<int>();
  void U(int value,int count){for(int i=count-1;i>=0;i--)bits.Add((value>>i)&1);}
  U(1,4);U(0,4);U(0,1);U(3,5);U(1,5);U(0,1);U(0,1);U(1,1);
  foreach(var delta in new[]{(1000,0),(0,-1000),(-1000,1000)})
  {U(1,1);U(1,1);U(9,4);U(1,1);U(delta.Item1,11);U(delta.Item2,11);}
  U(0,1);U(0,5);while(bits.Count%8!=0)bits.Add(0);
  var shape=new byte[bits.Count/8];for(int i=0;i<bits.Count;i++)shape[i/8]|=(byte)(bits[i]<<(7-i%8));
  var codes=new ushort[]{84,233,1071,26085};using var font=new MemoryStream();
  using(var w=new BinaryWriter(font,Encoding.UTF8,true))
  {
   w.Write((ushort)1);w.Write((byte)0x8C);w.Write((byte)0);w.Write((byte)4);w.Write(Encoding.ASCII.GetBytes("Test"));w.Write((ushort)codes.Length);
   for(int i=0;i<=codes.Length;i++)w.Write((uint)((codes.Length+1)*4+i*shape.Length));
   foreach(var code in codes)w.Write(shape);foreach(var code in codes)w.Write(code);
   w.Write((short)16400);w.Write((short)4080);w.Write((short)0);foreach(var code in codes)w.Write((short)10240);
   foreach(var code in codes)w.Write(new byte[]{8,0});w.Write((ushort)0);
  }
  using var swf=new MemoryStream();using var b=new BinaryWriter(swf);
  b.Write(Encoding.ASCII.GetBytes("GFX"));b.Write((byte)8);b.Write((uint)(8+6+6+font.Length+2));b.Write(new byte[]{8,0});b.Write((ushort)256);b.Write((ushort)1);
  b.Write((ushort)((75<<6)|63));b.Write((uint)font.Length);b.Write(font.ToArray());b.Write((ushort)0);return swf.ToArray();
 }
 static void Tpf(string path,string name,int w,int h,int dxgi,byte[] block)
 {
  byte[] n=Encoding.Unicode.GetBytes(name+"\0");int dataAt=(52+n.Length+15)/16*16,len=w/4*(h/4)*block.Length;var tpf=new byte[dataAt+len];
  void U32(int at,int v)=>BitConverter.GetBytes(v).CopyTo(tpf,at);void U16(int at,int v)=>BitConverter.GetBytes((ushort)v).CopyTo(tpf,at);
  Encoding.ASCII.GetBytes("TPF\0").CopyTo(tpf,0);U32(8,1);tpf[12]=4;U32(16,dataAt);U32(20,len);U16(28,w);U16(30,h);U32(40,52);U32(48,dxgi);n.CopyTo(tpf,52);
  for(int at=dataAt;at<tpf.Length;at+=block.Length)block.CopyTo(tpf,at);
  if(dxgi==71)
   for(int ty=0;ty<h/32;ty++)for(int tx=0;tx<w/32;tx++)for(int slot=0;slot<64;slot++)
   {
    int x=tx*8+(slot&1)+((slot>>2)&1)*2+((slot>>4)&1)*4;
    int y=ty*8+((slot>>1)&1)+((slot>>3)&1)*2+((slot>>5)&1)*4;
    ushort color=(ushort)((x%32)<<11 | (y%64)<<5 | (x+y)%32);
    int at=dataAt+((ty*(w/32)+tx)*64+slot)*8;
    BitConverter.GetBytes(color).CopyTo(tpf,at);BitConverter.GetBytes(color).CopyTo(tpf,at+2);
   }
  using var file=File.Create(path);var header=new byte[76];Encoding.ASCII.GetBytes("DCX\0").CopyTo(header,0);Encoding.ASCII.GetBytes("DFLT").CopyTo(header,32);file.Write(header);using var z=new ZLibStream(file,CompressionLevel.Fastest);z.Write(tpf);
 }
}
