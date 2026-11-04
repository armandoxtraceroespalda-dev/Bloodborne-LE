using BloodborneLauncher.Core;
using System.Text.Json.Nodes;

namespace BloodborneLauncher.Native.Backend;

public sealed partial class ProfileSession
{
    public string? SaveLocationMigrationError { get; private set; }
    internal void ClearSaveMigrationError() => SaveLocationMigrationError = null;

    internal void MigrateLegacySaves()
    {
        SaveLocationMigrationError = null;
        try
        {
            if (Str("native_user_dir_selected") == "true" || !LegacySaveLocation.IsLegacy(Str("user_dir"))) return;
            if (SaveStore.GameProcessRunning())
                throw new IOException(UiText.T("Close the game first: saves can't be changed while it runs."));
            string? migrated = LegacySaveLocation.Migrate(Str("user_dir"), Path.Combine(Paths.DataDir, "user"));
            if (migrated == null) return;
            Set("user_dir", migrated, true);
            // Persist even if the player closes the launcher without pressing Play.
            // Merge this key only; unrelated settings and pending edits stay intact.
            SavePreview();
            var settings = ReadJson(ConfigPath);
            settings["user_dir"] = JsonValue.Create(migrated);
            AtomicJson(ConfigPath, settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SaveLocationMigrationError = error.Message;
        }
    }
}
