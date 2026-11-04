using System.Globalization;
using BloodborneLauncher.Core;
using BloodborneLauncher.Native.Backend;
using Rect = Vortice.Mathematics.Rect;

namespace BloodborneLauncher.Native;
internal sealed partial class NativeWindow
{
    SaveEditorSession? _saveEditor;
    SaveCatalog? _saveCatalog;
    List<(SavePlace Place, SaveCharacter Character)>? _editorCharacters;
    readonly Dictionary<string, string> _editorFields = new();
    readonly HashSet<string> _editorInvalid = new();
    string _editorSection = "Character", _editorContent = "All", _editorSearch = "", _editorProblem = "";
    string _editorFeedback = "";
    int _editorGeneration;
    bool _editorLoading;
    const int EditorRows = 40;

    void ReleaseNativeSaveEditor()
    {
        ++_editorGeneration;
        _editorLoading = false;
        _editorCharacters = null;
        _saveEditor = null;
        _saveCatalog = null;
        _editorFields.Clear();
        _editorInvalid.Clear();
        _editorProblem = "";
        _editorFeedback = "";
        _editorSearch = "";
    }

    bool LeaveNativeSaveEditor() => _saveEditor?.Changes is not > 0 || Confirm(UiText.T("Discard unsaved changes?"));

    void LoadNativeSaveEditor()
    {
        if (_editorLoading || _busy) return;
        _editorLoading = true;
        int generation = ++_editorGeneration;
        var settings = _profile.SourceSettings();
        string locale = GameLanguages.Text(settings) == "20" ? "es-419" : GameLanguages.MenuLocale(GameLanguages.Text(settings)).Replace('_', '-');
        Background(async () =>
        {
            var result = await Task.Run(() =>
            {
                var found = SaveEditorSession.Places(settings).SelectMany(p => SaveStore.Characters(p).Select(c => (Place: p, Character: c))).ToList();
                var catalog = SaveCatalog.TryLoad(GameEnvironment.GameDir(settings), locale, out string? problem);
                return (found, catalog, problem);
            });
            return () =>
            {
                if (generation != _editorGeneration || _page != 8 || _moreTab != 3) return;
                _editorLoading = false;
                _editorCharacters = result.found;
                _saveCatalog = result.catalog;
                _editorProblem = result.problem ?? "";
                int selected = SaveStore.DefaultCharacter(result.found.Select(f => f.Character).ToList(), PlayerProfiles.Current(settings));
                if (selected >= 0) OpenNativeSaveCharacter(selected);
                Rebuild();
            };
        });
    }

    void OpenNativeSaveCharacter(int index)
    {
        if (_editorCharacters == null || index < 0 || index >= _editorCharacters.Count) return;
        var selected = _editorCharacters[index];
        _saveEditor = SaveEditorSession.Open(selected.Place, selected.Character);
        _editorFields.Clear();
        _editorInvalid.Clear();
    }

    void SelectSaveEditorChoice(string key, string value)
    {
        if (key == "save-editor:character")
        {
            if (!LeaveNativeSaveEditor()) return;
            OpenNativeSaveCharacter(int.Parse(value, CultureInfo.InvariantCulture));
        }
        else if (key == "save-editor:section") _editorSection = value;
        else if (key == "save-editor:content") _editorContent = value;
        _editorSearch = "";
        _scroll = 0;
        Rebuild();
    }

    void EditorChoice(string label, string key, string value, Choice[] choices, float x, float width, ref float y)
    {
        var option = new Option("", label, "save-editor:" + key, OptionKind.Choice, Choices: choices, Default: value);
        var box = new Rect(x, y, width, 54 * _scale);
        DrawOption(option, box);
        y += 58 * _scale;
    }

    void EditorNote(string text, float x, float width, ref float y, bool translate = true)
    {
        float height = WrappedHeight(text, 16, width - 20 * _scale, translate, true);
        Wrapped(text, new Rect(x + 10 * _scale, y, width - 20 * _scale, height), 16, Muted, translate, true);
        y += height + 14 * _scale;
    }

    void EditorNumber(string name, string key, int value, int min, int max, Action<SaveSlot, int> edit, float x, float width, ref float y)
    {
        float fieldWidth = Math.Min(width * .4f, 180 * _scale);
        TextAt(name, new Rect(x + 10 * _scale, y, width - fieldWidth - 30 * _scale, 36 * _scale), 18);
        var field = new Rect(x + width - fieldWidth - 10 * _scale, y, fieldWidth, 36 * _scale);
        string text = _editorFields.TryGetValue(key, out var typed) ? typed : value.ToString(CultureInfo.InvariantCulture);
        QueueNativeText("save-editor-field:" + key, field, text, false, false, next =>
        {
            _editorFields[key] = next;
            if (int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number >= min && number <= max)
            {
                try { _saveEditor!.Edit(slot => edit(slot, number)); _editorInvalid.Remove(key); }
                catch (ArgumentException) { _editorInvalid.Add(key); }
            }
            else _editorInvalid.Add(key);
            Rebuild();
        });
        y += 48 * _scale;
    }

    void EditorRun(Action action)
    {
        _editorFeedback = "";
        try { action(); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _editorFeedback = error.Message;
        }
        Rebuild();
    }

    void SaveNativeEditor()
    {
        RequireClosedSaveGame();
        if (_saveEditor == null || _saveEditor.Changes == 0 || _editorInvalid.Count > 0) return;
        if (!Confirm(UiText.T("Save changes? A backup will be made first."))) return;
        _saveEditor.Commit();
        _editorFields.Clear();
        Rebuild();
    }

    void DrawNativeSaveEditor(float x, float width, ref float y)
    {
        if (_editorCharacters == null)
        {
            LoadNativeSaveEditor();
            EditorNote("Loading...", x, width, ref y);
            return;
        }
        if (SaveGameIsRunning())
        {
            EditorNote("Close the game first: saves can't be changed while it runs.", x, width, ref y);
            return;
        }
        if (_editorCharacters.Count == 0)
        {
            EditorNote("No characters found. Create a character in the game first.", x, width, ref y);
            Button("Refresh", new Rect(x + 10 * _scale, y, 180 * _scale, 38 * _scale), "editor-refresh-empty", () => { ReleaseNativeSaveEditor(); Rebuild(); });
            y += 50 * _scale;
            return;
        }
        if (_editorFeedback.Length > 0) EditorNote(_editorFeedback, x, width, ref y, false);
        var session = _saveEditor!;
        int index = _editorCharacters.FindIndex(p => p.Character.File == session.Character.File);
        EditorChoice("Character", "character", index.ToString(CultureInfo.InvariantCulture), _editorCharacters.Select((f, i) => Choice.Literal($"{f.Character.Name} · {f.Character.Level} · {f.Place.Profile}", i.ToString(CultureInfo.InvariantCulture))).ToArray(), x, width, ref y);
        float toolbar = y;
        Button("Save", new Rect(x + 10 * _scale, toolbar, 140 * _scale, 38 * _scale), "editor-save", () => EditorRun(SaveNativeEditor), icon: "\uE74E");
        Button("Discard", new Rect(x + 158 * _scale, toolbar, 150 * _scale, 38 * _scale), "editor-discard", () => EditorRun(() =>
        {
            if (!LeaveNativeSaveEditor()) return;
            session.Discard(); _editorFields.Clear(); _editorInvalid.Clear(); Rebuild();
        }), icon: "\uE7A7");
        Button("Refresh", new Rect(x + 316 * _scale, toolbar, 150 * _scale, 38 * _scale), "editor-refresh", () => EditorRun(() =>
        {
            if (!LeaveNativeSaveEditor()) return;
            ReleaseNativeSaveEditor(); Rebuild();
        }), icon: "\uE72C");
        y += 50 * _scale;
        if (_editorInvalid.Count > 0) EditorNote("Enter a number within the allowed range before saving.", x, width, ref y);
        EditorChoice("Section", "section", _editorSection, new[] { "Character", "Items", "Weapons & gear", "Bosses", "Lamps", "Revert" }.Select(v => new Choice(v, v)).ToArray(), x, width, ref y);
        if (_editorSection is not ("Character" or "Revert"))
        {
            EditorChoice("Content", "content", _editorContent, new[] { "All", "Base game", "DLC" }.Select(v => new Choice(v, v)).ToArray(), x, width, ref y);
            QueueNativeText("editor-search", new Rect(x + 10 * _scale, y, width - 20 * _scale, 36 * _scale), _editorSearch, false, false, value => { _editorSearch = value; Rebuild(); });
            y += 48 * _scale;
        }
        var slot = session.Slot;
        bool Match(string name) => name.Contains(_editorSearch, StringComparison.CurrentCultureIgnoreCase);
        bool Content(bool dlc) => _editorContent == "All" || (_editorContent == "DLC") == dlc;
        string Name(uint id) => _saveCatalog?.ItemName(id) ?? id.ToString(CultureInfo.InvariantCulture);
        switch (_editorSection)
        {
            case "Character":
                EditorNote($"{slot.Name} · {UiText.T("Level")} {slot.Level}", x, width, ref y, false);
                EditorNumber("Blood echoes", "echoes", slot.Echoes, 0, SaveSlot.MaxEchoes, (s, v) => s.SetEchoes(v), x, width, ref y);
                EditorNumber("Insight", "insight", slot.Insight, 0, SaveSlot.MaxInsight, (s, v) => s.SetInsight(v), x, width, ref y);
                foreach (SaveStat stat in Enum.GetValues<SaveStat>())
                    EditorNumber(stat.ToString(), "stat-" + stat, slot.Stat(stat), 1, SaveSlot.MaxStat, (s, v) => s.SetStat(stat, v), x, width, ref y);
                break;
            case "Items":
                PageGroup("What you have", x, width, ref y);
                var owned = slot.Items.Where(i => i.Category == 4 && Content(SaveWorld.IsDlcGoods(i.Id)) && Match(Name(i.ItemId))).ToList();
                foreach (var item in owned.Take(EditorRows))
                {
                    var goods = _saveCatalog?.GoodsById.GetValueOrDefault(item.Id);
                    int max = goods == null ? item.Quantity : Math.Max(item.Quantity, item.List == SaveList.Storage ? goods.MaxStored : goods.MaxHeld);
                    EditorNumber(Name(item.ItemId) + (item.List == SaveList.Storage ? " · " + UiText.T("In storage") : ""), $"item-{item.List}-{item.Position}", item.Quantity, 1, Math.Max(1, max), (s, v) => s.SetQuantity(item, v), x, width, ref y);
                }
                EditorLimit(owned.Count, x, width, ref y);
                PageGroup("Give an item", x, width, ref y);
                if (_saveCatalog == null) { EditorNote(_editorProblem, x, width, ref y, false); break; }
                TextAt("Quantity", new Rect(x + 10 * _scale, y, width * .6f, 36 * _scale), 18);
                QueueNativeText("editor-give-count", new Rect(x + width * .65f, y, width * .3f, 36 * _scale), _editorFields.GetValueOrDefault("give-count", "1"), false, false,
                    value => _editorFields["give-count"] = value);
                y += 48 * _scale;
                var available = _saveCatalog.Goods.Where(g => Content(g.Dlc) && Match(g.Name)).ToList();
                foreach (var goods in available.Take(EditorRows))
                    ReferenceCard(goods.Name, goods.KeyItem ? "Key item" : UiText.T("Give") + " 1", "", x, width, ref y,
                        new[] { new PageButton(goods.KeyItem ? "Unlock" : "Give", "editor-give-" + goods.Id, () => EditorRun(() =>
                        {
                            var have = session.Slot.Items.FirstOrDefault(i => i.Category == 4 && i.Id == goods.Id && i.List != SaveList.Storage);
                            if (!int.TryParse(_editorFields.GetValueOrDefault("give-count", "1"), NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1)
                                throw new ArgumentException(UiText.T("Enter a number within the allowed range before saving."));
                            count = Math.Max(0, Math.Min(count, goods.MaxHeld - (have?.Quantity ?? 0)));
                            if (count > 0) session.Edit(s => s.AddGoods(goods.Id, count, goods.Sort, goods.KeyItem));
                            _editorFeedback = UiText.T("Give") + ": " + goods.Name + " × " + count.ToString(CultureInfo.InvariantCulture);
                            _editorFields.Clear(); _editorInvalid.Clear(); Rebuild();
                        }), "\uE710") });
                EditorLimit(available.Count, x, width, ref y);
                break;
            case "Weapons & gear":
                PageGroup("What you have", x, width, ref y);
                var gear = slot.Items.Where(i => ((i.Category == 0 && i.Id >= 2000000) || (i.Category == 1 && i.Id < 900000)) && Content(i.Category == 0 ? SaveWorld.IsDlcWeapon(i.Id) : SaveWorld.IsDlcArmor(i.Id)) && Match(Name(i.ItemId))).ToList();
                foreach (var item in gear.Take(EditorRows)) ReferenceCard(Name(item.ItemId), item.List == SaveList.Storage ? "In storage" : "Carried", "", x, width, ref y, Array.Empty<PageButton>());
                EditorLimit(gear.Count, x, width, ref y);
                PageGroup("Give a weapon or armor", x, width, ref y);
                if (_saveCatalog == null) { EditorNote(_editorProblem, x, width, ref y, false); break; }
                var equipment = _saveCatalog.Weapons.Select(w => (w.Name, w.Dlc, Give: (Action<SaveSlot>)(s => s.AddWeapon(w.Id, w.Durability, w.Sort, w.GemSlots))))
                    .Concat(_saveCatalog.Armor.Select(a => (a.Name, a.Dlc, Give: (Action<SaveSlot>)(s => s.AddArmor(a.Id, a.Durability, a.Sort)))))
                    .Where(g => Match(g.Name) && Content(g.Dlc)).OrderBy(g => g.Name).ToList();
                int gearId = 0;
                foreach (var equipmentItem in equipment.Take(EditorRows))
                {
                    var selected = equipmentItem;
                    ReferenceCard(selected.Name, selected.Dlc ? "DLC" : "Base game", "", x, width, ref y,
                        new[] { new PageButton("Give", "editor-gear-" + gearId++, () => EditorRun(() => { session.Edit(selected.Give); _editorFields.Clear(); _editorInvalid.Clear(); Rebuild(); }), "\uE710") });
                }
                EditorLimit(equipment.Count, x, width, ref y);
                break;
            case "Bosses":
                foreach (var boss in SaveWorld.Bosses.Where(b => Content(SaveWorld.IsDlcBoss(b)) && Match(_saveCatalog?.NpcName(b.NameId, b.Name) ?? b.Name)))
                {
                    var selected = boss;
                    ReferenceCard(_saveCatalog?.NpcName(boss.NameId, boss.Name) ?? boss.Name, SaveWorld.State(slot, boss).ToString(), "", x, width, ref y, new[]
                    {
                        new PageButton("Mark defeated", "boss-defeat-" + boss.Defeated, () => EditorRun(() => { session.Edit(s => SaveWorld.MarkDefeated(s, selected)); Rebuild(); })),
                        new PageButton("Fight again", "boss-fight-" + boss.Defeated, () => EditorRun(() => { session.Edit(s => SaveWorld.FightAgain(s, selected)); Rebuild(); })),
                        new PageButton("Lock again", "boss-lock-" + boss.Defeated, () => EditorRun(() => { session.Edit(s => SaveWorld.LockAgain(s, selected)); Rebuild(); }))
                    });
                }
                break;
            case "Lamps":
                if (_saveCatalog == null) { EditorNote(_editorProblem, x, width, ref y, false); break; }
                foreach (var lamp in _saveCatalog.Lamps.Where(l => Content(l.Dlc) && Match(l.Name)))
                {
                    bool lit = slot.Flag(lamp.Flag);
                    var selected = lamp;
                    ReferenceCard(lamp.Name, lamp.Area, "", x, width, ref y, new[]
                    {
                        new PageButton(lit ? "Extinguish" : "Light", "lamp-" + lamp.Flag, () => EditorRun(() => { session.Edit(s => s.SetFlag(selected.Flag, !lit)); Rebuild(); }))
                    }, true);
                }
                break;
            case "Revert":
                EditorNote("Reverting restores every save in this profile. The current saves are backed up first.", x, width, ref y);
                foreach (var backup in SaveStore.Backups(session.Place))
                {
                    var selected = backup;
                    ReferenceCard(backup.When.ToString("g"), session.Place.Profile, "", x, width, ref y, new[]
                    {
                        new PageButton("Revert to this", "editor-revert-" + backup.Dir, () => EditorRun(() =>
                        {
                            RequireClosedSaveGame();
                            if (!Confirm(UiText.T("Revert to this backup?"))) return;
                            SaveStore.Revert(session.Place, selected); ReleaseNativeSaveEditor(); Rebuild();
                        }), "\uE7A7")
                    }, true);
                }
                break;
        }
    }

    void EditorLimit(int count, float x, float width, ref float y)
    {
        if (count > EditorRows) EditorNote(UiText.T("Search to narrow the list.") + $" ({EditorRows}/{count})", x, width, ref y, false);
    }
}
