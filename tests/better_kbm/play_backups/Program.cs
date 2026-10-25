using BloodborneLauncher.Core;
static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
var root=Path.Combine(Path.GetTempPath(),"better-kbm-play-"+Guid.NewGuid());Directory.CreateDirectory(root);
try {
 var settings=new LauncherSettings(root);var source=SaveBackups.SaveData(settings);Directory.CreateDirectory(source);
 var save=Path.Combine(source,"save.dat");File.WriteAllText(save,"a");
 Check(SaveBackups.CreateDuringPlay(settings)==null,"Active writes must settle first");
 File.SetLastWriteTimeUtc(save,DateTime.UtcNow.AddMinutes(-1));
 File.WriteAllText(save+".lantern-work","pending");
 Check(SaveBackups.CreateDuringPlay(settings)==null,"Pending transaction must block backup");File.Delete(save+".lantern-work");
 var made=SaveBackups.CreateDuringPlay(settings);Check(made!=null,"Quiet save must be backed up");
 Check(File.ReadAllText(Path.Combine(made!,"savedata","save.dat"))=="a","Backup content");
 Check(SaveBackups.List(settings).Single().MadeDuringPlay,"Play backup marker");
 Check(SaveBackups.CreateDuringPlay(settings)==null,"Unchanged saves must not duplicate");
 // Retention has independent budgets for startup and in-play copies.
 Directory.Delete(made!,true);
 for(int i=1;i<=12;++i)Directory.CreateDirectory(Path.Combine(SaveBackups.Folder(settings),$"2020-01-{i:00}_00-00-00"));
 for(int i=1;i<=8;++i)Directory.CreateDirectory(Path.Combine(SaveBackups.Folder(settings),$"2020-02-{i:00}_00-00-00_play"));
 made=SaveBackups.CreateDuringPlay(settings);Check(made!=null,"Changed history must allow backup");
 var all=SaveBackups.List(settings);Check(all.Count(x=>x.MadeDuringPlay)==6,"Keep six play copies");Check(all.Count(x=>!x.MadeDuringPlay)==10,"Keep ten startup copies");
 Check(File.ReadAllText(save)=="a","Source remains unchanged");
 Console.WriteLine("PASS: quiet saves, pending-write exclusion, actual copy, unchanged deduplication, independent retention and source preservation.");
}finally{Directory.Delete(root,true);}
namespace BloodborneLauncher.Core {
 public record LauncherSettings(string Root);
 public static class GameEnvironment {public static string UserDir(LauncherSettings s)=>Path.Combine(s.Root,"user");public static string SavesRoot(LauncherSettings s)=>s.Root;}
 public static class PlayerProfiles {public static string Current(LauncherSettings s)=>"default";}
 public static class UiText {public static string T(string s)=>s;}
}
