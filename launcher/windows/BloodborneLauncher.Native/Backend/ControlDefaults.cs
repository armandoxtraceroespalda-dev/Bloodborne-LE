using BloodborneLauncher.Core;

namespace BloodborneLauncher.Native.Backend;
internal static class ControlDefaults
{
    public static void Reset(ProfileSession profile, string device, bool all)
    {
        if (device is not ("keyboard_mouse" or "controller") || device != profile.InputDevice)
            throw new InvalidOperationException("The input device changed; reopen the reset confirmation.");
        if (all)
        {
            // PageCatalog exposes only the current device's settings. Saved profile
            // files and the other device's custom settings/bindings are not deleted.
            foreach (var row in PageCatalog.Build(3, profile))
                if (row.Kind != OptionKind.Action && row.Key != "native-profile")
                    profile.Set(row.Key, row.Default, row.Setting);
            profile.EditSource((settings, ini) =>
            {
                ControlProfiles.Apply(settings, ini, ControlProfiles.BuiltIns.First(value => value.InputDevice == device));
                if (device == "controller") EngineSwitches.Set(settings,
                    EngineSwitches.All.First(value => value.Env == "BB_GAMEPAD"), "");
            });
            if (device == "keyboard_mouse") profile.Set("stick_release_delay_ms", "0");
        }
        else
        {
            profile.EditSource((settings, ini) =>
            {
                if (device == "controller") PadMap.ResetAll(ini);
                else
                {
                    var defaults = ControlProfiles.BuiltIns.First(value => value.Name == ControlProfiles.DarkSouls3Name);
                    foreach (var action in InputMap.Controls)
                        Bindings.Set(ini, action, InputMap.Split(defaults.Bindings.GetValueOrDefault(action.Name, action.Default)));
                }
            });
        }
    }
}
