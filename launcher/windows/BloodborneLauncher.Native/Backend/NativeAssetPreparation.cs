using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Platform;

namespace BloodborneLauncher.Native.Backend;

/// <summary>Extract cold game buffers in a disposable process, keeping the visible launcher small.</summary>
internal static class NativeAssetPreparation
{
    internal const string WorkerArgument="--extract-launcher-assets";
    internal static bool Valid(string path)
    {
        try
        {
            using var file=File.OpenRead(path);
            if(file.Length is <32 or >67108864)return false;
            Span<byte> header=stackalloc byte[24];file.ReadExactly(header);
            if(path.EndsWith(".png",StringComparison.OrdinalIgnoreCase))
            {
                if(!header[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return false;
                int width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..]);
                int height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..]);
                return width is >0 and <=8192 && height is >0 and <=8192;
            }
            if(path.EndsWith(".ttf",StringComparison.OrdinalIgnoreCase))return header[..4].SequenceEqual(new byte[]{0,1,0,0});
            if(path.EndsWith(".ico",StringComparison.OrdinalIgnoreCase))return header[..4].SequenceEqual(new byte[]{0,0,1,0}) && header[4]!=0;
            return false;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){return false;}
    }
    internal static bool IsReady(string output)=>GameAssetExtractor.RequiredFiles.All(name=>Valid(Path.Combine(output,name)));
    internal static void InstallPrepared(string staging,string output)
    {
        if(!GameAssetExtractor.IsReady(staging))throw new InvalidDataException(UiText.T("The extracted menu assets are incomplete."));
        Directory.CreateDirectory(output);
        foreach(var name in GameAssetExtractor.RequiredFiles)
        {
            string target=Path.Combine(output,name);
            // Locally customized assets are valid player data, not disposable build output.
            if(!Valid(target))File.Move(Path.Combine(staging,name),target,true);
        }
        string sounds=Path.Combine(staging,"sounds");
        if(Directory.Exists(sounds))
            foreach(var file in Directory.EnumerateFiles(sounds,"*.wav"))
            {
                string target=Path.Combine(output,"sounds",Path.GetFileName(file));
                if(GameMenuSoundExtractor.ValidClip(target))continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Move(file,target,true);
            }
        var hashes=new Dictionary<string,string>();
        foreach(var name in GameAssetExtractor.RequiredFiles)
        {using var file=File.OpenRead(Path.Combine(output,name));hashes[name]=Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();}
        string manifest=Path.Combine(staging,"installed-manifest.json");
        File.WriteAllText(manifest,JsonSerializer.Serialize(hashes));
        File.Move(manifest,Path.Combine(output,"assets-manifest.json"),true);
    }
    internal static int RunWorker(string game,string output)
    {
        try
        {
            var progress=new Writer();GameAssetExtractor.Extract(game,output,new ArtworkProgress(progress));
            try
            {
                if(GameMenuSoundExtractor.FindBanks(game).Length>0)
                {
                    progress.Report(new(92,"Preparing menu sounds"));
                    GameMenuSoundExtractor.Extract(game,output);
                    NativeBackSound.Ensure(game,output,CancellationToken.None);
                }
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
            {Console.Error.WriteLine("Optional menu audio: "+e.Message);}
            progress.Report(new(100,"Ready"));return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e.GetBaseException().Message);return 2;}
    }
    sealed class Writer:IProgress<GameAssetExtractor.Step>
    {public void Report(GameAssetExtractor.Step step)=>Console.Out.WriteLine(JsonSerializer.Serialize(step));}
    sealed class ArtworkProgress(IProgress<GameAssetExtractor.Step> output):IProgress<GameAssetExtractor.Step>
    {public void Report(GameAssetExtractor.Step step)=>output.Report(new(step.Percent*9/10,step.Percent==100?"Preparing menu sounds":step.Message));}

    internal static async Task PrepareAsync(string game,string output,IProgress<GameAssetExtractor.Step> progress,CancellationToken cancel)
    {
        Directory.CreateDirectory(output);
        string staging=Path.Combine(output,".setup-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var command=new ProcessStartInfo(Environment.ProcessPath??throw new IOException(UiText.T("The launcher executable path is unavailable.")))
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            if(Path.GetFileNameWithoutExtension(command.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
                command.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            command.ArgumentList.Add(WorkerArgument);command.ArgumentList.Add(game);command.ArgumentList.Add(staging);
            using var process=new Process{StartInfo=command};
            cancel.ThrowIfCancellationRequested();
            if(!process.Start())throw new IOException(UiText.T("Could not start the local asset worker."));
            var diagnostics=ReadErrors(process.StandardError);
            try
            {
                while(await process.StandardOutput.ReadLineAsync(cancel) is {} line)
                {
                    if(line.Length>4096)continue;
                    try{var step=JsonSerializer.Deserialize<GameAssetExtractor.Step>(line);if(step.Message!=null && step.Percent is >=0 and <=100)progress.Report(step);}
                    catch(JsonException){}
                }
                await process.WaitForExitAsync(cancel);
                string errors=await diagnostics;
                if(process.ExitCode!=0)throw new IOException(UiText.Format("Local menu asset extraction failed: {0}",errors.Trim()));
                cancel.ThrowIfCancellationRequested();InstallPrepared(staging,output);
            }
            finally
            {
                if(!process.HasExited)
                {
                    try{process.Kill(entireProcessTree:true);}catch(InvalidOperationException) when(process.HasExited){}
                    await process.WaitForExitAsync();
                }
                await diagnostics;
            }
        }
        finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    static async Task<string> ReadErrors(StreamReader source)
    {
        var text=new StringBuilder();var buffer=new char[2048];int count;
        while((count=await source.ReadAsync(buffer))>0)if(text.Length<8192)text.Append(buffer,0,Math.Min(count,8192-text.Length));
        return text.ToString();
    }
}
