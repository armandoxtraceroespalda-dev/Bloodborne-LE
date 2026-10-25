using System.Globalization;
using System.Text;
using BloodborneLauncher.Core;
namespace BloodborneLauncher.Native.Backend;
internal sealed record SettingResult(int Page, string Key, string Title, string Path, string Device = "", int MoreTab = 2,
    string Fallback = "")
{
    public string Match { get; } = SettingsSearchIndex.Normalize(Title + " " + Path + " " + Key);
}
internal sealed class SettingsSearchIndex
{
    readonly List<SettingResult> _entries = new();
    public int Count => _entries.Count;
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        string decomposed;
        try { decomposed=text.Normalize(NormalizationForm.FormD); } catch(ArgumentException) { decomposed=text; }
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(c));
        return builder.ToString();
    }
    public static SettingsSearchIndex Build(ProfileSession profile, bool cheats)
    {
        var index = new SettingsSearchIndex();
        void Add(int page, string key, string title, string section = "", string device = "", int tab = 2, string fallback = "")
        {
            string path = UiText.T(PageCatalog.Titles[page]);
            if (section.Length > 0) path += " › " + UiText.T(section);
            if (device.Length > 0) path += " › " + UiText.T(device == "controller" ? "Gamepad" : "Keyboard/Mouse");
            index._entries.Add(new(page, key, UiText.T(title), path, device, tab, fallback));
        }
        foreach (var row in GraphicsSchema.Build()) Add(1,row.Key,row.Title,row.Section,
            fallback: row.Visible=="dlss" ? "upscaler" : row.Visible=="custom" ? "preset" : row.Gate ?? "");
        Add(1,"gpu_id","Rendering GPU"); Add(1,"recommend","Recommended settings");
        foreach (int page in new[]{2,4,5,6,7,9})
        {
            if (page==9 && !cheats) continue;
            foreach(var row in PageCatalog.Build(page,profile))
            {
                string title=row.Key=="online_username"?"Account name":row.Key=="online_enabled"?"Play online":
                    row.Key=="action:online-link"?"The Hunter's Dream account":row.Title;
                Add(page,row.Key,title,row.Section);
            }
        }
        foreach(string device in new[]{"keyboard_mouse","controller"})
            foreach(var row in PageCatalog.Build(3,profile,device)) Add(3,row.Key,row.Title,row.Section,device);
        Add(5,"online_pool","Server");
        Add(3,"binding-search","Keybindings");
        Add(3,"controls-reset-all","Reset all");Add(3,"reset-bindings","Reset","Keybindings");
        Add(3,PreferredGamepads.Key,"Preferred gamepad",device:"controller");
        foreach(var action in InputMap.Controls) Add(3,"bind-"+action.Name,action.Label,"Keybindings");
        Add(8,"native-engine-preset","Engine preset","Engine");
        foreach(var row in EngineSwitches.All)
            if(EngineSwitches.Groups.Contains(row.Group) && row.Group!=EngineSwitches.Graphics && row.Env!="BB_GAMEPAD")
                Add(8,row.Key,row.Title,row.Group);
        foreach(var item in new[]{("more-mods","Mods",0),("more-log","Log",1),("more-engine","Engine",2)})
            Add(8,item.Item1,item.Item2,tab:item.Item3);
        foreach(var item in new[]{("trophy-sound-file","Sound file"),("trophy-notification-test","Try it"),
            ("trophy-log-folder","Achievement log"),("trophy-progress-reset","Reset achievement progress")})
            Add(6,item.Item1,item.Item2,"Notification");
        return index;
    }
    public IReadOnlyList<SettingResult> Find(string query, int limit=8)
    {
        string normalized=Normalize(query.Trim());
        if(normalized.Length==0)return Array.Empty<SettingResult>();
        string[] words=normalized.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var matches=new List<(SettingResult Result,int Score)>();
        foreach(var entry in _entries)
        {
            if(!words.All(word=>entry.Match.Contains(word,StringComparison.Ordinal)))continue;
            string title=Normalize(entry.Title);
            int score=title==normalized?0:title.StartsWith(normalized,StringComparison.Ordinal)?1:title.Contains(normalized,StringComparison.Ordinal)?2:3;
            matches.Add((entry,score));
        }
        matches.Sort((a,b)=>{int score=a.Score.CompareTo(b.Score);return score!=0?score:StringComparer.CurrentCulture.Compare(a.Result.Title,b.Result.Title);});
        return matches.Take(limit).Select(m=>m.Result).ToArray();
    }
}
