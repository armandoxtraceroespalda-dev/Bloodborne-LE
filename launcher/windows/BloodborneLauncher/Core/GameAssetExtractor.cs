using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
namespace BloodborneLauncher.Core;

/// <summary>Derives local launcher artwork from the user's dump; never edits the source files.</summary>
public static class GameAssetExtractor
{
    public static readonly string[] RequiredFiles = { "background.png", "menu-frame.png", "menu-underline.png", "menu-glow-half.png", "menu-book-leaf.png", "menu-book-panel.png", "BloodborneMenu.ttf", "game-icon.png", "game-icon.ico" };
    public readonly record struct Step(int Percent, string Message);
    public static string? FindGameRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Path.GetFullPath(path);
            if (string.Equals(Path.GetFileName(path), "dvdroot_ps4", StringComparison.OrdinalIgnoreCase))
                path = Path.GetDirectoryName(path)!;
            var menu = Path.Combine(path, "dvdroot_ps4", "menu");
            return File.Exists(Path.Combine(path, "eboot.bin")) &&
                File.Exists(Path.Combine(menu, "title.tpf.dcx")) &&
                File.Exists(Path.Combine(menu, "common.tpf.dcx")) && FindFont(menu) != null ? path : null;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { return null; }
    }
    private static string? FindFont(string menu)
    {
        var preferred = new[] { "engus", "enggb", "spaes", "spaar", "jpnjp", "rusru" }
            .Select(locale => Path.Combine(menu, locale, "font.gfx")).FirstOrDefault(File.Exists);
        return preferred ?? (Directory.Exists(menu) ? Directory.EnumerateDirectories(menu)
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => Path.Combine(p, "font.gfx")).FirstOrDefault(File.Exists) : null);
    }
    private static string HashFile(string path)
    {
        using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file));
    }
    public static bool IsReady(string output)
    {
        try
        {
            Span<byte> signature = stackalloc byte[8];
            foreach (var name in RequiredFiles)
            {
                var path = Path.Combine(output, name);
                if (!File.Exists(path) || new FileInfo(path).Length < 32) return false;
                using var stream = File.OpenRead(path);stream.ReadExactly(signature);
                if (name.EndsWith(".png", StringComparison.Ordinal) && !signature.SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return false;
                if (name.EndsWith(".ico", StringComparison.Ordinal) && !signature[..6].SequenceEqual(new byte[] {0,0,1,0,1,0})) return false;
                if (name.EndsWith(".ttf", StringComparison.Ordinal) && !signature[..4].SequenceEqual(new byte[] {0,1,0,0})) return false;
            }
            var manifest = Path.Combine(output, "assets-manifest.json");
            if (File.Exists(manifest))
            {
                var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest));
                foreach (var name in RequiredFiles)
                    if (hashes == null || !hashes.TryGetValue(name, out var expected) ||
                        !string.Equals(expected, HashFile(Path.Combine(output, name)), StringComparison.OrdinalIgnoreCase))
                        return false;
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }
    public static void Extract(string game, string output, IProgress<Step>? progress = null, CancellationToken cancel = default)
    {
        game = FindGameRoot(game) ?? throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Select the Bloodborne folder containing eboot.bin and dvdroot_ps4/menu."));
        if (IsReady(output)) { progress?.Report(new(100, "Ready"));return; }
        var menu = Path.Combine(game, "dvdroot_ps4", "menu");
        Directory.CreateDirectory(output);
        var staging = Path.Combine(output, ".extract-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        void Report(int value, string text) { cancel.ThrowIfCancellationRequested();progress?.Report(new(value, text)); }
        try
        {
            Report(5, "Reading title artwork");
            var titleTextures = Textures(Path.Combine(menu, "title.tpf.dcx"), "MENU_Title_00001", "MENU_Title_00100");
            var title = titleTextures["MENU_Title_00001"];
            WritePng(title, 0, 0, 1920, 1024, Path.Combine(staging, "background.png"), false);
            var icon = titleTextures["MENU_Title_00100"];
            WritePng(icon, 0, 0, icon.Width, icon.Height, Path.Combine(staging, "game-icon.png"), false);
            WriteIcon(Path.Combine(staging, "game-icon.png"), Path.Combine(staging, "game-icon.ico"), icon.Width, icon.Height);
            Report(30, "Reading menu artwork");
            var commonTextures = Textures(Path.Combine(menu, "common.tpf.dcx"), "MENU_Common_00001", "MENU_Common_00002", "MENU_Common_00003");
            var common = commonTextures["MENU_Common_00003"];
            WritePng(common, 0, 50, 480, 58, Path.Combine(staging, "menu-frame.png"), true);
            // The independent double-stroke underline, not the ornate System-menu frame above it.
            WritePng(common, 0, 230, 184, 20, Path.Combine(staging, "menu-underline.png"), true);
            WritePng(common, 7, 813, 360, 108, Path.Combine(staging, "menu-glow-half.png"), true);
            // The native renderer mirrors this spine and feathers its inner edge.
            WritePng(commonTextures["MENU_Common_00001"], 0, 1088, 240, 936, Path.Combine(staging, "menu-book-leaf.png"), false);
            WritePng(commonTextures["MENU_Common_00002"], 0, 0, 1152, 680, Path.Combine(staging, "menu-book-panel.png"), false);
            Report(55, "Preparing the menu font");
            MenuFontConverter.Convert(FindFont(menu)!, Path.Combine(staging, "BloodborneMenu.ttf"), cancel);
            Report(90, "Checking local assets");
            if (!IsReady(staging)) throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("The extracted menu assets are incomplete."));
            var hashes = RequiredFiles.ToDictionary(n => n, n => HashFile(Path.Combine(staging,n)).ToLowerInvariant());
            File.WriteAllText(Path.Combine(staging, "assets-manifest.json"), JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
            cancel.ThrowIfCancellationRequested();
            foreach (var name in RequiredFiles.Append("assets-manifest.json"))
                File.Move(Path.Combine(staging, name), Path.Combine(output, name), overwrite: true);
            Report(100, "Ready");
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }
    private sealed record Pixels(int Width, int Height, ColorRgba32[] Data);
    private static uint U32(byte[] b,int at) { if(at<0 || at>b.Length-4) throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Truncated menu data."));return BitConverter.ToUInt32(b,at); }
    private static ushort U16(byte[] b,int at) { if(at<0 || at>b.Length-2) throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Truncated menu data."));return BitConverter.ToUInt16(b,at); }
    private static Dictionary<string, Pixels> Textures(string path, params string[] wanted)
    {
        var b = File.ReadAllBytes(path);
        if (!b.AsSpan(0,Math.Min(4,b.Length)).SequenceEqual("TPF\0"u8))
        {
            if (b.Length < 80 || !b.AsSpan(0,4).SequenceEqual("DCX\0"u8) || !Encoding.ASCII.GetString(b,0,64).Contains("DFLT"))
                throw new InvalidDataException(BloodborneLauncher.Core.UiText.Format("Unsupported menu archive: {0}",path));
            byte[]? unpacked = null;
            for (int off = 64; off < Math.Min(128,b.Length-2); off++)
            {
                if(b[off] != 0x78) continue;
                try
                {
                    using var source = new MemoryStream(b,off,b.Length-off);
                    using var z = new ZLibStream(source,CompressionMode.Decompress);
                    using var target = new MemoryStream();var buffer = new byte[65536];int count;
                    while((count=z.Read(buffer))>0) { target.Write(buffer,0,count);if(target.Length>128*1024*1024) throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Menu archive is too large.")); }
                    var candidate=target.ToArray();if(candidate.AsSpan(0,Math.Min(4,candidate.Length)).SequenceEqual("TPF\0"u8)) { unpacked=candidate;break; }
                }
                catch (InvalidDataException) { }
            }
            b=unpacked ?? throw new InvalidDataException(BloodborneLauncher.Core.UiText.Format("Could not decompress {0}",path));
        }
        if(b.Length<16 || b[12]!=4) throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Expected a PS4 menu texture package."));
        int textures=checked((int)U32(b,8));if(textures>8192)throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid texture count."));
        var result = new Dictionary<string, Pixels>(StringComparer.Ordinal);
        for(int i=0;i<textures;i++)
        {
            int at=checked(16+i*36),nameAt=checked((int)U32(b,at+24)),end=nameAt;
            while(end<=b.Length-2 && U16(b,end)!=0)end+=2;
            if(end>b.Length-2)throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid texture name."));
            var name = Encoding.Unicode.GetString(b,nameAt,end-nameAt);
            if(!wanted.Contains(name, StringComparer.Ordinal))continue;
            int offset=checked((int)U32(b,at)),size=checked((int)U32(b,at+4)),width=U16(b,at+12),height=U16(b,at+14);
            uint dxgi=U32(b,at+32);int stride=dxgi==71?8:dxgi==98?16:throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Unsupported menu texture compression."));
            if(width<1 || height<1 || width>8192 || height>8192 || offset<0 || size<0 || offset>b.Length-size)throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid menu texture bounds."));
            int bw=(width+3)/4,bh=(height+3)/4;var linear=new byte[checked(bw*bh*stride)];
            for(int y=0;y<bh;y++)for(int x=0;x<bw;x++)
            {
                int morton=0;for(int bit=0;bit<3;bit++)morton|=((x>>bit)&1)<<(2*bit)|((y>>bit)&1)<<(2*bit+1);
                int source=checked((((y/8)*((bw+7)/8)+x/8)*64+morton)*stride);
                if(source>size-stride)throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Truncated swizzled menu texture."));
                Buffer.BlockCopy(b,offset+source,linear,(y*bw+x)*stride,stride);
            }
            result.Add(name, new Pixels(width,height,new BcDecoder().DecodeRaw(linear,width,height,dxgi==71?CompressionFormat.Bc1:CompressionFormat.Bc7)));
        }
        foreach(var name in wanted)
            if(!result.ContainsKey(name))throw new InvalidDataException(BloodborneLauncher.Core.UiText.Format("Menu texture not found: {0}",name));
        return result;
    }
    private static void WritePng(Pixels pixels,int left,int top,int width,int height,string path,bool additive)
    {
        if(left<0 || top<0 || left+width>pixels.Width || top+height>pixels.Height)throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid menu crop."));
        using var compressed=new MemoryStream();
        using(var z=new ZLibStream(compressed,CompressionLevel.Optimal,leaveOpen:true))
        {
            var row=new byte[1+width*4];
            for(int y=0;y<height;y++)
            {
                for(int x=0;x<width;x++)
                {
                    var c=pixels.Data[(top+y)*pixels.Width+left+x];int at=1+x*4;
                    int alpha=additive?Math.Max(c.r,Math.Max(c.g,c.b)):c.a;
                    row[at]=additive?(byte)(alpha==0?0:Math.Round(c.r*255.0/alpha)):c.r;
                    row[at+1]=additive?(byte)(alpha==0?0:Math.Round(c.g*255.0/alpha)):c.g;
                    row[at+2]=additive?(byte)(alpha==0?0:Math.Round(c.b*255.0/alpha)):c.b;
                    row[at+3]=(byte)alpha;
                }
                z.Write(row);
            }
        }
        using var png=File.Create(path);png.Write(new byte[]{137,80,78,71,13,10,26,10});
        var header=new byte[13];BE(header,0,(uint)width);BE(header,4,(uint)height);header[8]=8;header[9]=6;
        Chunk(png,"IHDR",header);Chunk(png,"IDAT",compressed.ToArray());Chunk(png,"IEND",Array.Empty<byte>());
    }
    // Windows accepts PNG-backed ICO entries; preserve the original icon pixels and alpha.
    private static void WriteIcon(string pngPath, string path, int width, int height)
    {
        if(width is < 1 or > 256 || height is < 1 or > 256)
            throw new InvalidDataException(BloodborneLauncher.Core.UiText.T("Invalid title icon size."));
        var png = File.ReadAllBytes(pngPath);
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((byte)(width == 256 ? 0 : width)); writer.Write((byte)(height == 256 ? 0 : height));
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(png.Length); writer.Write(22); writer.Write(png);
    }
    private static void BE(byte[] b,int at,uint value) { b[at]=(byte)(value>>24);b[at+1]=(byte)(value>>16);b[at+2]=(byte)(value>>8);b[at+3]=(byte)value; }
    private static void Chunk(Stream stream,string kind,byte[] data)
    {
        var word=new byte[4];BE(word,0,(uint)data.Length);stream.Write(word);
        var bytes=Encoding.ASCII.GetBytes(kind);stream.Write(bytes);stream.Write(data);uint crc=0xFFFFFFFF;
        foreach(byte b in bytes.Concat(data)) { crc^=b;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xEDB88320u:0); }
        BE(word,0,crc^0xFFFFFFFF);stream.Write(word);
    }
}
