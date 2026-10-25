using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace BloodborneLauncher.Core;

/// <summary>Read local game-exported atlases once; crop with WPF, without repainting them.</summary>
public static class MenuAssets
{
    private static readonly Dictionary<string, BitmapSource?> Images = new(StringComparer.OrdinalIgnoreCase);
    public static ImageSource? Background { get; private set; }
    private static int _backgroundWidth;
    public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "gui");
    public static IReadOnlyList<string> MissingAssets { get; private set; } = Array.Empty<string>();
    public static string Diagnostic => "Asset folder: " + DirectoryPath + "\n" +
        (MissingAssets.Count == 0 ? "All menu assets loaded." : "Missing: " + string.Join(", ", MissingAssets));

    /// <summary>Upgrade the earlier asset pack without overwriting locally edited gui files.</summary>
    public static void PrepareDirectory()
    {
        var previous = Path.Combine(AppContext.BaseDirectory, "Assets", "Bloodborne");
        if (!Directory.Exists(previous)) return;
        foreach (var name in new[] { "background.png", "menu-frame.png", "menu-underline.png",
                     "menu-glow-half.png", "menu-sidebar-underline.png", "menu-book-leaf.png", "menu-book-panel.png", "BloodborneMenu.ttf", "game-icon.png", "game-icon.ico", "assets-manifest.json" })
        {
            var source = Path.Combine(previous, name);
            var target = Path.Combine(DirectoryPath, name);
            if (!File.Exists(source) || File.Exists(target)) continue;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.Copy(source, target, overwrite: false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { Workspace.Current.Log(BloodborneLauncher.Core.UiText.Format("Menu asset migration failed: {0}",e.Message), LogLevel.Warn); }
        }
    }
    private static BitmapSource? Load(string name)
    {
        if (Images.TryGetValue(name, out var cached)) return cached;
        var path = Path.Combine(DirectoryPath, name + ".png");
        BitmapSource? result = null;
        if (!File.Exists(path))LauncherDiagnostics.Record("Asset not found: "+path,LogLevel.Warn);
        if (File.Exists(path))
        {
            try
            {
                var image=MenuBitmapLoader.Load(path,name.Equals("background",StringComparison.OrdinalIgnoreCase));
                result=image;
                LauncherDiagnostics.Record($"Asset loaded: {path}; {image.PixelWidth}x{image.PixelHeight}; {image.Format}",LogLevel.Info);
                var alpha=MenuBitmapLoader.SampleAlpha(image);
                LauncherDiagnostics.Record($"Asset alpha sample: {name}; min={alpha.Min}; max={alpha.Max} (0=transparent, 255=opaque)",LogLevel.Info);
            }
            catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or FormatException or UnauthorizedAccessException)
            { LauncherDiagnostics.Record("Asset decode failed: "+path+Environment.NewLine+e,LogLevel.Bad); }
        }
        Images[name] = result;return result;
    }
    private static BitmapSource? Crop(string name, int x, int y, int width, int height)
    {
        var image = Load(name);
        if (image == null || x < 0 || y < 0 || x + width > image.PixelWidth || y + height > image.PixelHeight) return null;
        var crop = new CroppedBitmap(image, new Int32Rect(x, y, width, height));crop.Freeze();
        return MenuBitmapLoader.Detach(crop);
    }
    private static void DiscardAtlases()
    {
        foreach(var key in Images.Keys.Where(key=>key.StartsWith("MENU_",StringComparison.Ordinal)).ToArray())Images.Remove(key);
    }
    internal static long RetainedPixelBytes => (Background is BitmapSource background ? (long)background.PixelWidth*background.PixelHeight*4 : 0) +
        Images.Values.Where(image=>image!=null).Distinct().Sum(image=>(long)image!.PixelWidth*image.PixelHeight*4);

    private static bool Brush(string key, BitmapSource? image, double opacity = 1)
    {
        if (image == null) return false;
        var brush = new ImageBrush(image) { Stretch = Stretch.Fill, Opacity = opacity };brush.Freeze();
        Application.Current.Resources[key] = brush;
        return true;
    }
    private static BitmapSource JoinGlow(BitmapSource half)
    {
        var image=new FormatConvertedBitmap(half,PixelFormats.Bgra32,null,0);image.Freeze();
        int stride=image.PixelWidth*4;var pixels=new byte[stride*image.PixelHeight];image.CopyPixels(pixels,stride,0);
        var joined=MenuPixels.PrepareHighlight(pixels,image.PixelWidth,image.PixelHeight,out int joinedWidth);
        var result=BitmapSource.Create(joinedWidth,image.PixelHeight,96,96,PixelFormats.Bgra32,null,joined,joinedWidth*4);
        result.Freeze();return result;
    }
    public static ImageSource? LoadBackground(int width)
    {
        if(Background!=null && _backgroundWidth==width)return Background;
        var path=Path.Combine(DirectoryPath,"background.png");
        Background=File.Exists(path)?MenuBitmapLoader.Load(path,true,width):Crop("MENU_Title_00001",0,0,1920,1024);
        DiscardAtlases();
        _backgroundWidth=width;
        return Background;
    }
    public static void Install()
    {
        var missing = new List<string>();
        // Decode after HWND creation, when the actual monitor DPI is available.
        if (!File.Exists(Path.Combine(DirectoryPath,"background.png")) && !File.Exists(Path.Combine(DirectoryPath,"MENU_Title_00001.png"))) missing.Add("background.png");
        if (!Brush("MenuFrameBrush", Load("menu-frame") ?? Crop("MENU_Common_00003", 0, 50, 480, 58)))
            missing.Add("menu-frame.png");
        if (!Brush("MenuUnderlineBrush", Load("menu-underline") ?? Crop("MENU_Common_00003", 0, 230, 184, 20)))
            missing.Add("menu-underline.png");
        if (!Brush("MenuSidebarUnderlineBrush", Load("menu-sidebar-underline")))
            missing.Add("menu-sidebar-underline.png");
        // Brush alpha avoids an intermediate full-window group-opacity surface.
        if (!Brush("MenuBookLeafBrush", Load("menu-book-leaf"),0.25))
            missing.Add("menu-book-leaf.png");
        if (!Brush("MenuBookPanelBrush", Load("menu-book-panel"),0.375))
            missing.Add("menu-book-panel.png");
        // GameWindowIcon loads game-icon.ico for the game process; the menu never decodes it.
        var half=Load("menu-glow-half") ?? Crop("MENU_Common_00003", 7, 813, 360, 108);
        if (half==null) missing.Add("menu-glow-half.png");
        else { Brush("MenuGlowHalfBrush",half);Brush("MenuGlowBrush",JoinGlow(half)); }
        if (!LauncherAppearance.HasLocalFont) missing.Add("BloodborneMenu.ttf");
        DiscardAtlases();
        MissingAssets = missing.AsReadOnly();
        Workspace.Current.Log(BloodborneLauncher.Core.UiText.Format("Bloodborne UI 48. {0}",Diagnostic.Replace("\n", " ")),
            missing.Count == 0 ? LogLevel.Info : LogLevel.Warn);
        LauncherDiagnostics.Record(Diagnostic,missing.Count==0?LogLevel.Good:LogLevel.Bad);
        if(missing.Any(name=>name.EndsWith(".png",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException(BloodborneLauncher.Core.UiText.Format("The native menu assets could not be loaded.\n{0}\nDetails: {1}",Diagnostic,LauncherDiagnostics.LogPath));
    }
}
