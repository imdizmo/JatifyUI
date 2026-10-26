using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using static Jatify.Campfire.CampfireUi;

namespace Jatify.Campfire
{
    // Manage saves from the Jatify design (stDialog "saves"): Worlds / Characters, one card per save
    // (name, where, size, when, its backups); a card opens to list its backups (Restore) and its actions. Every file
    // operation is Valheim's own (SaveSystem.Delete, MoveSource, RestoreBackup, run off the main thread like Valheim's
    // menu does), and each of those is guarded by SaveBackup, which copies the files away first.
    internal static class SavesView
    {
        private const float W = 640f;
        private static Window _win;
        private static RectTransform _tabs, _list, _hint;
        private static SaveDataType _type = SaveDataType.World;
        private static SaveWithBackups _sel;
        private static int _backup = -1;
        private static readonly HashSet<string> _open = new HashSet<string>();
        private static bool _busy;

        internal static bool Shown => _win != null && _win.Shown;
        internal static void Hide() { if (_win != null) _win.Show(false); }

        internal static void Open(bool worlds)
        {
            if (_win == null || _win.Root == null) Build();
            _type = worlds ? SaveDataType.World : SaveDataType.Character;
            _sel = null; _backup = -1;
            _win.Show(true);
            Reload();
        }

        private static void Build()
        {
            _win = new Window(CharScreen.Root, "saves", W, 860f, () => { _win.Show(false); CharScreen.Show(); });
            _win.SetTitle("Manage saves");
            _win.Buttons("Close", extra: "Refresh", onExtra: Reload);
            _tabs = Place(Node(_win.Body, "tabs"), 0f, 0f, 280f, 30f);
            _list = JatifyLook.Scroll(_win.Body, "list", 0f, 42f, _win.BodyW, _win.BodyH - 42f - 34f);
            _hint = Place(Node(_win.Body, "hint"), 0f, _win.BodyH - 26f, _win.BodyW, 26f);
        }

        private static void Reload()
        {
            Busy("Reading saves…", () => SaveSystem.ForceRefreshCache(), Paint);
        }

        private static void Paint()
        {
            if (_win == null || _win.Root == null) return;
            Clear(_tabs);
            Seg(_tabs, 0f, 0f, 280f, new[] { "Worlds", "Characters" }, _type == SaveDataType.World ? 0 : 1,
                i => { _type = i == 0 ? SaveDataType.World : SaveDataType.Character; _sel = null; _backup = -1; Paint(); });
            Clear(_list);
            float w = _win.BodyW, y = 0f;
            var saves = SaveSystem.GetSavesByType(_type).OrderByDescending(s => s.IsDeleted ? DateTime.MinValue : s.PrimaryFile.LastModified).ToList();
            if (Fejd.ShowOnly != null) saves = saves.Where(s => Fejd.ShowOnly.Contains(s.Name.ToLowerInvariant())).ToList();   // dev screenshots: the same names as the lists
            foreach (var s in saves)
            {
                var save = s;
                bool open = _open.Contains(s.Name);
                var files = s.BackupFiles;
                var src = s.IsDeleted ? files.FirstOrDefault()?.m_source ?? FileHelpers.FileSource.Local : s.PrimaryFile.m_source;
                var card = Box(_list, s.Name, C("raised"), open ? C("line2") : LineSoft, 10); card.raycastTarget = false;
                var top = Box(card.transform, "head", NoColor, NoColor, 1); Place(top.rectTransform, 0f, 0f, w, 62f);
                On(top, () => { if (!_open.Remove(save.Name)) _open.Add(save.Name); Paint(); });
                Text(top.transform, s.IsDeleted ? s.Name + " (deleted)" : s.Name, "title", 19f, White, 12f, 5f, w - 150f, 30f);
                var sub = s.IsDeleted ? "Deleted · backups only"
                    : $"{(src.IsCloud() ? "Cloud" : "Local")} · {Size(s.PrimaryFile.Size)}{(files.Length > 0 ? $" ({Size(s.SizeWithBackups)} with backups)" : "")} · {When(s.PrimaryFile.LastModified)}";
                Text(top.transform, sub, "lore", 13f, C("mute"), 12f, 32f, w - 150f, 24f);
                Text(top.transform, $"{files.Length} backup{(files.Length == 1 ? "" : "s")}", "mono", 12f, C("dim"), w - 140f, 0f, 116f, 62f, TextAlignmentOptions.MidlineRight);
                var chev = Img(top.transform, Glyphs.Triangle, C("mute"), "open"); At(chev.rectTransform, 1f, 0.5f, -10f, 0f, 8f, 8f);
                chev.rectTransform.localEulerAngles = new Vector3(0f, 0f, open ? 180f : -90f);
                float h = 62f;
                if (open)
                {
                    // its backups (.cf-backs): name, size, when, then Move / Restore / Delete
                    for (int i = 0; i < files.Length; i++)
                    {
                        int bi = i;
                        var f = files[i];
                        var line = Img(card.transform, null, C("line2"), "line"); Place(line.rectTransform, 12f, h, 2f, 32f);
                        Text(card.transform, f.Name, "mono", 12f, C("dim"), 24f, h, w - 24f - 290f, 32f);
                        Text(card.transform, $"{Size(f.Size)} · {When(f.LastModified)}", "mono", 11f, C("mute"), w - 290f - 4f - 120f, h, 120f, 32f, TextAlignmentOptions.MidlineRight);
                        float bx = w - 12f;
                        bx -= 60f; Secondary(card.transform, "Delete", bx, h + 2f, 60f, 28f, () => { Pick(save, bi); Delete(f); }, !_busy, 12f);
                        bx -= 74f; Secondary(card.transform, "Restore", bx, h + 2f, 70f, 28f, () => { Pick(save, bi); Restore(f); }, !_busy, 12f);
                        if (CanMove(f)) { bx -= 104f; Secondary(card.transform, MoveLabel(f), bx, h + 2f, 100f, 28f, () => { Pick(save, bi); Move(f); }, !_busy, 12f); }
                        h += 36f;
                    }
                    if (files.Length == 0) { Text(card.transform, "No backups yet.", "lore", 13f, C("mute"), 12f, h - 4f, w - 24f, 24f); h += 26f; }
                    // the save's own actions (.cf-acts)
                    if (!s.IsDeleted)
                    {
                        var f = s.PrimaryFile;
                        var acts = Place(Node(card.transform, "acts"), 12f, h + 4f, w - 24f, 34f);
                        var items = new List<(string, Action, bool)>();
                        if (CanMove(f)) items.Add((MoveLabel(f), () => { Pick(save, -1); Move(f); }, !_busy));
                        items.Add(("Open folder", () => OpenFolder(f), true));
                        items.Add(("Delete", () => { Pick(save, -1); Delete(f); }, !_busy));
                        Acts(acts, 0f, w - 24f, false, items.ToArray());
                        h += 4f + 34f;
                    }
                    h += 12f;
                }
                Place(card.rectTransform, 0f, y, w, h);
                y += h + 6f;
            }
            if (saves.Count == 0) Para(_list, "No saves here.", "lore", 14f, C("mute"), 2f, 4f, w, 30f);
            _list.sizeDelta = new Vector2(0f, y);
            PaintHint();
        }

        private static void Pick(SaveWithBackups save, int backup) { _sel = save; _backup = backup; }
        private static bool CanMove(SaveFile f) => FileHelpers.CloudStorageSupportedAndEnabled || !f.m_source.IsNotCloud();
        private static string MoveLabel(SaveFile f) => f.m_source.IsNotCloud() ? "Move to cloud" : "Move to local";

        // "Today 19:42", "Yesterday", "Oct 12", "2025-10-12"
        private static string When(DateTime t)
        {
            var today = DateTime.Today;
            if (t.Date == today) return $"Today {t:HH:mm}";
            if (t.Date == today.AddDays(-1)) return "Yesterday";
            return t.Year == today.Year ? t.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture) : t.ToString("yyyy-MM-dd");
        }

        private static void PaintHint()
        {
            Clear(_hint);
            var text = "Before any delete, move or restore a copy goes to campfire-backups";
            if (FileHelpers.CloudStorageSupportedAndEnabled)
            {
                ulong used = FileHelpers.GetTotalCloudUsage(), cap = FileHelpers.GetTotalCloudCapacity();
                text = (cap > 0 ? $"Cloud {Size(used)} of {Size(cap)}" : "Cloud") + " · before any delete, move or restore a copy goes to campfire-backups";
            }
            Text(_hint, text, "mono", 11.5f, C("mute"), 0f, 0f, _win.BodyW, 26f);
        }

        // The folder the file is in (local saves), else Valheim's save folder: read only, nothing is touched.
        private static void OpenFolder(SaveFile f)
        {
            string dir = null;
            try
            {
                var p = f.IsChunked ? f.ChunkedDirectory : f.AllPaths.FirstOrDefault();
                if (!string.IsNullOrEmpty(p))
                {
                    var full = (Path.IsPathRooted(p) ? p : Path.Combine(SaveBackup.DataDir, p.TrimStart('/', '\\'))).TrimEnd('/', '\\');
                    dir = Path.GetDirectoryName(full);
                }
            }
            catch (Exception e) { Log.Warn("open folder: " + e.Message); }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = SaveBackup.DataDir;
            Application.OpenURL("file://" + dir);
            CharScreen.Toast($"Opened {dir}");
        }

        // ---- actions (Valheim's own, on a worker thread like its menu) ----------------------------

        private static void Delete(SaveFile f)
        {
            Action go = () => Busy("Deleting…", () => SaveSystem.Delete(f), Done);
            if (_backup >= 0) CharScreen.Confirm("Delete backup", $"Delete the backup {f.Name}? A copy goes to campfire-backups first.", "Delete", go);
            else CharScreen.ConfirmByName("Delete save", $"{_sel.Name} will be deleted, its backups stay.", _sel.Name, go);
        }

        private static void Restore(SaveFile backup)
        {
            CharScreen.Confirm("Restore backup", _sel.IsDeleted ? $"Bring {_sel.Name} back from {backup.Name}?"
                : $"Replace {_sel.Name} with {backup.Name}? The current save is copied to campfire-backups first.", "Restore",
                () => Busy("Restoring…", () => { var r = SaveSystem.RestoreBackup(backup); if (r != SaveSystem.RestoreBackupResult.Success) CharScreen.Toast($"Restore failed: {r}"); }, Done));
        }

        private static void Move(SaveFile f)
        {
            var dest = f.m_source.IsNotCloud() ? FileHelpers.FileSource.Cloud : FileHelpers.FileSource.Local;
            if (_sel.AllFiles.Any(o => o != f && o.m_source == dest && o.Name == f.Name && o.IsChunked == f.IsChunked)) { CharScreen.Toast($"{f.Name} already exists there"); return; }
            if (SaveSystem.IsCorrupt(f)) { CharScreen.Toast($"{f.Name} is corrupt and can't be moved"); return; }
            Busy("Moving…", () =>
            {
                if (!SaveSystem.MoveSource(f, _backup >= 0, dest, out bool quota)) CharScreen.Toast(quota ? "Not enough Steam cloud space" : $"Moving {f.Name} failed");
            }, Done);
        }

        private static void Done()
        {
            SaveSystem.ForceRefreshCache();
            _sel = null; _backup = -1;
            Paint();
        }

        private static void Busy(string what, Action work, Action after)
        {
            if (_busy) return;
            _busy = true;
            CharScreen.Toast(what);
            var bw = new BackgroundWorker();
            bw.DoWork += (_, __) => work();
            bw.RunWorkerCompleted += (_, e) =>
            {
                _busy = false;
                if (e.Error != null) { Log.Warn(e.Error.ToString()); CharScreen.Toast("That failed: " + e.Error.Message); }
                after();
            };
            bw.RunWorkerAsync();
        }

        private static string Size(ulong b) => b >= 1 << 20 ? $"{b / 1048576.0:0.0} MB" : b >= 1 << 10 ? $"{b / 1024.0:0.0} KB" : $"{b} B";
    }
}
