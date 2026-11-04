using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    bool _testedSettingsChecked;
    void OfferNativeTestedSettings()
    {
        if (_testedSettingsChecked) return;
        _testedSettingsChecked = true;
        var settings = _profile.SourceSettings();
        if (!TestedSettings.ShouldAsk(_profile.Ini, settings)) return;
        _profile.EditSource((state, _) => TestedSettings.MarkAsked(state));
        if (Confirm(UiText.T("Use the settings tested for this engine version? Your current settings will be backed up first.")))
            ApplyNativeTestedSettings();
    }
    void ApplyNativeTestedSettings()
    {
        _profile.EditSource((settings, ini) => { TestedSettings.Use(ini, settings); TestedSettings.MarkAsked(settings); });
        _pageRows = null;
        Rebuild();
    }
    void DrawNativeTestedSettings(float x, float width, ref float y)
    {
        PageGroup("Tested settings", x, width, ref y);
        var buttons = new List<PageButton>
        {
            new("Use tested settings", "tested-use", () => Run(() =>
            {
                if (Confirm(UiText.T("Use the settings tested for this engine version? Your current settings will be backed up first."))) ApplyNativeTestedSettings();
            }), "\uE73E")
        };
        if (TestedSettings.SavedFile() != null) buttons.Add(new("Restore my settings", "tested-restore", () => Run(() =>
        {
            if (!Confirm(UiText.T("Restore your saved settings?"))) return;
            _profile.EditSource((settings, ini) => TestedSettings.Restore(ini, settings));
            _pageRows = null; Rebuild();
        }), "\uE7A7"));
        ReferenceCard("Tested settings", "Use the settings tested for this engine version. Your previous settings are saved so you can restore them.", "\uE73E", x, width, ref y, buttons);
    }
}
