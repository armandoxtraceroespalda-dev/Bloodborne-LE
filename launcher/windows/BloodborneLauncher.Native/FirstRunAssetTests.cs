using System.Buffers.Binary;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;

namespace BloodborneLauncher.Native;
internal static class FirstRunAssetTests
{
    internal static void Run(string temporary)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);Console.WriteLine("PASS "+message);}
        string root=Path.Combine(temporary,"first-run-assets"),staging=Path.Combine(root,"staging"),output=Path.Combine(root,"gui");
        Directory.CreateDirectory(staging);Directory.CreateDirectory(output);
        Check(!NativeAssetPreparation.IsReady(output),"fresh installs require extraction before the themed renderer starts");
        foreach(var name in GameAssetExtractor.RequiredFiles)
        {
            byte[] bytes=new byte[40];
            if(name.EndsWith(".png"))
            {
                new byte[]{137,80,78,71,13,10,26,10}.CopyTo(bytes,0);
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16),240);BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20),936);
            }
            else if(name.EndsWith(".ico"))new byte[]{0,0,1,0,1,0}.CopyTo(bytes,0);
            else new byte[]{0,1,0,0}.CopyTo(bytes,0);
            File.WriteAllBytes(Path.Combine(staging,name),bytes);
        }
        byte[] custom=File.ReadAllBytes(Path.Combine(staging,"background.png"));custom[^1]=99;
        File.WriteAllBytes(Path.Combine(output,"background.png"),custom);
        File.WriteAllText(Path.Combine(output,"menu-book-panel.png"),"broken");
        File.WriteAllText(Path.Combine(output,"player-custom-file.txt"),"keep");
        NativeAssetPreparation.InstallPrepared(staging,output);
        Check(NativeAssetPreparation.IsReady(output),"prepared assets fill missing files and replace invalid cache entries");
        Check(File.ReadAllBytes(Path.Combine(output,"background.png")).SequenceEqual(custom),"first-run repair preserves valid customized launcher artwork");
        Check(File.ReadAllText(Path.Combine(output,"player-custom-file.txt"))=="keep","asset repair leaves unrelated local files intact");
        Check(GameAssetExtractor.IsReady(output),"installed cache manifest matches preserved and newly extracted assets");

        string settings=Path.Combine(root,"settings.json"),ini=Path.Combine(root,"not-created.ini");
        File.WriteAllText(settings,"{\"future\":{\"value\":7},\"game_dir\":\"before\"}");
        var session=new ProfileSession(new[]{"--config",settings,"--ini",ini,"--assets",output,"--profile",Path.Combine(root,"profile")});
        session.RememberGameRoot("saved-game-root");
        var saved=ProfileSession.ReadJson(settings);
        Check(saved["game_dir"]?.ToString()=="saved-game-root" && saved["future"]?["value"]?.ToString()=="7" && !File.Exists(ini),
            "first-run game selection persists without an engine INI or overwriting other settings");
    }
}
