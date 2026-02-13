using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(Schedule1SaveManagerPro.SaveManagerMod), "Schedule 1 Save Manager Pro", "1.0.0", "TVGS Modding Community")]
[assembly: MelonGame(null, "Schedule I")]

namespace Schedule1SaveManagerPro
{
    public class SaveManagerMod : MelonMod
    {
        private const string SaveRootPreference = "SaveRootPath";
        private const string ToggleKeyPreference = "ToggleKey";

        private bool _showWindow;
        private Rect _windowRect = new Rect(60, 60, 760, 520);
        private Vector2 _snapshotScroll;
        private string _newSnapshotName = string.Empty;
        private string _status = "Ready.";
        private string _newWorldStatus = "Idle.";

        private string? _pendingRestoreSnapshotPath;
        private string? _pendingDeleteSnapshotPath;
        private DateTime _worldCreateStartedAtUtc;
        private string? _worldCreateBackupRoot;
        private Dictionary<string, DateTime> _worldStateBeforeCreate = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private bool _watchWorldCreate;

        private MelonPreferences_Category _prefs = null!;
        private MelonPreferences_Entry<string> _saveRootPath = null!;
        private MelonPreferences_Entry<string> _toggleKey = null!;

        private string SnapshotsRoot => Path.Combine(NormalizedSaveRoot(), "Snapshots");

        public override void OnInitializeMelon()
        {
            _prefs = MelonPreferences.CreateCategory("Schedule1SaveManagerPro", "Schedule 1 Save Manager Pro");
            _saveRootPath = _prefs.CreateEntry(
                SaveRootPreference,
                DetectDefaultSaveRoot(),
                description: "Root directory containing game saves."
            );
            _toggleKey = _prefs.CreateEntry(ToggleKeyPreference, KeyCode.F6.ToString(), description: "UI toggle key (Unity KeyCode string).");

            EnsureDirectories();
            MelonLogger.Msg($"Save Manager loaded. Save root: {NormalizedSaveRoot()}");
        }

        public override void OnGUI()
        {
            if (ResolveToggleKey(out var key)
                && Event.current != null
                && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == key)
            {
                _showWindow = !_showWindow;
                Event.current.Use();
            }

            if (!_showWindow)
            {
                return;
            }

            GUI.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.92f);
            GUI.Box(_windowRect, "Schedule 1 Save Manager Pro v1.0.0");
            GUILayout.BeginArea(new Rect(_windowRect.x + 8, _windowRect.y + 24, _windowRect.width - 16, _windowRect.height - 32));
            DrawWindow();
            GUILayout.EndArea();
        }

        private void DrawWindow()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("MelonLoader save tools: unlimited snapshots + restore + delete");
            GUILayout.Space(8);

            DrawPathControls();
            DrawCreateControls();
            DrawSnapshotList();
            DrawActionConfirmation();

            GUILayout.Space(8);
            GUILayout.Label($"Status: {_status}");
            GUILayout.Label($"World Create: {_newWorldStatus}");
            GUILayout.Label($"Tip: {_toggleKey.Value} toggles this window.");
            GUILayout.EndVertical();

        }

        private void DrawPathControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Save Root", GUILayout.Width(80));

            var editedRoot = GUILayout.TextField(_saveRootPath.Value);
            if (editedRoot != _saveRootPath.Value)
            {
                _saveRootPath.Value = editedRoot;
            }

            if (GUILayout.Button("Apply", GUILayout.Width(80)))
            {
                EnsureDirectories();
                _status = "Updated save root path.";
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            {
                _status = "Refreshed snapshot list.";
            }

            GUILayout.EndHorizontal();
            GUILayout.Label($"Snapshots Folder: {SnapshotsRoot}");
            GUILayout.Space(8);
        }

        private void DrawCreateControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("New Snapshot", GUILayout.Width(95));
            _newSnapshotName = GUILayout.TextField(_newSnapshotName);

            if (GUILayout.Button("Create", GUILayout.Width(100)))
            {
                CreateSnapshot(_newSnapshotName);
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (GUILayout.Button("New World (No Override)", GUILayout.Width(200)))
            {
                PrepareSafeNewWorldFlow();
            }
            GUILayout.Space(10);
        }

        public override void OnUpdate()
        {
            if (_watchWorldCreate)
            {
                TryFinalizeSafeWorldCreate();
            }
        }

        private void DrawSnapshotList()
        {
            var snapshots = GetSnapshots();
            GUILayout.Label($"Existing Snapshots ({snapshots.Length}):");

            _snapshotScroll = GUILayout.BeginScrollView(_snapshotScroll, GUILayout.Height(290));
            foreach (var snapshot in snapshots)
            {
                GUILayout.BeginHorizontal("box");
                GUILayout.Label(snapshot.Name, GUILayout.Width(300));
                GUILayout.Label(snapshot.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"), GUILayout.Width(170));
                GUILayout.Label(ToReadableSize(GetDirectorySize(snapshot.FullName)), GUILayout.Width(85));

                if (GUILayout.Button("Restore", GUILayout.Width(90)))
                {
                    _pendingRestoreSnapshotPath = snapshot.FullName;
                    _pendingDeleteSnapshotPath = null;
                }

                if (GUILayout.Button("Delete", GUILayout.Width(90)))
                {
                    _pendingDeleteSnapshotPath = snapshot.FullName;
                    _pendingRestoreSnapshotPath = null;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        private void DrawActionConfirmation()
        {
            if (_pendingRestoreSnapshotPath == null && _pendingDeleteSnapshotPath == null)
            {
                return;
            }

            GUILayout.Space(8);
            GUILayout.BeginVertical("box");

            if (_pendingRestoreSnapshotPath != null)
            {
                GUILayout.Label($"Restore '{Path.GetFileName(_pendingRestoreSnapshotPath)}'? This replaces current save files.");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Confirm Restore", GUILayout.Width(150)))
                {
                    RestoreSnapshot(_pendingRestoreSnapshotPath);
                    _pendingRestoreSnapshotPath = null;
                }
                if (GUILayout.Button("Cancel", GUILayout.Width(100)))
                {
                    _pendingRestoreSnapshotPath = null;
                }
                GUILayout.EndHorizontal();
            }

            if (_pendingDeleteSnapshotPath != null)
            {
                GUILayout.Label($"Delete '{Path.GetFileName(_pendingDeleteSnapshotPath)}'? This cannot be undone.");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Confirm Delete", GUILayout.Width(150)))
                {
                    DeleteSnapshot(_pendingDeleteSnapshotPath);
                    _pendingDeleteSnapshotPath = null;
                }
                if (GUILayout.Button("Cancel", GUILayout.Width(100)))
                {
                    _pendingDeleteSnapshotPath = null;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private bool ResolveToggleKey(out KeyCode key)
        {
            var configured = (_toggleKey.Value ?? string.Empty).Trim();
            if (Enum.TryParse(configured, true, out key))
            {
                return true;
            }

            key = KeyCode.F6;
            return false;
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(NormalizedSaveRoot());
            Directory.CreateDirectory(SnapshotsRoot);
        }

        private DirectoryInfo[] GetSnapshots()
        {
            EnsureDirectories();
            return new DirectoryInfo(SnapshotsRoot)
                .GetDirectories()
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .ToArray();
        }

        private void CreateSnapshot(string proposedName)
        {
            try
            {
                EnsureDirectories();

                var snapshotName = string.IsNullOrWhiteSpace(proposedName)
                    ? $"Save-{DateTime.Now:yyyyMMdd-HHmmss}"
                    : SanitizeFolderName(proposedName.Trim());

                if (string.IsNullOrWhiteSpace(snapshotName))
                {
                    _status = "Snapshot name is invalid.";
                    return;
                }

                var destination = Path.Combine(SnapshotsRoot, snapshotName);
                if (Directory.Exists(destination))
                {
                    _status = "Snapshot already exists. Choose another name.";
                    return;
                }

                var entries = Directory.GetFileSystemEntries(NormalizedSaveRoot())
                    .Where(entry => !Path.GetFileName(entry).Equals("Snapshots", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (entries.Length == 0)
                {
                    _status = "No save files found to snapshot.";
                    return;
                }

                Directory.CreateDirectory(destination);
                foreach (var entry in entries)
                {
                    CopyEntry(entry, Path.Combine(destination, Path.GetFileName(entry)));
                }

                _status = $"Created snapshot '{snapshotName}'.";
                _newSnapshotName = string.Empty;
            }
            catch (Exception ex)
            {
                _status = $"Create failed: {ex.Message}";
                MelonLogger.Error(ex.ToString());
            }
        }

        private void RestoreSnapshot(string snapshotPath)
        {
            try
            {
                EnsureDirectories();

                foreach (var entry in Directory.GetFileSystemEntries(NormalizedSaveRoot()))
                {
                    if (Path.GetFileName(entry).Equals("Snapshots", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }

                foreach (var entry in Directory.GetFileSystemEntries(snapshotPath))
                {
                    CopyEntry(entry, Path.Combine(NormalizedSaveRoot(), Path.GetFileName(entry)));
                }

                _status = $"Restored snapshot '{Path.GetFileName(snapshotPath)}'.";
            }
            catch (Exception ex)
            {
                _status = $"Restore failed: {ex.Message}";
                MelonLogger.Error(ex.ToString());
            }
        }

        private void DeleteSnapshot(string snapshotPath)
        {
            try
            {
                var name = Path.GetFileName(snapshotPath);
                Directory.Delete(snapshotPath, true);
                _status = $"Deleted snapshot '{name}'.";
            }
            catch (Exception ex)
            {
                _status = $"Delete failed: {ex.Message}";
                MelonLogger.Error(ex.ToString());
            }
        }

        private static void CopyEntry(string sourcePath, string destinationPath)
        {
            if (Directory.Exists(sourcePath))
            {
                CopyDirectory(sourcePath, destinationPath);
                return;
            }

            File.Copy(sourcePath, destinationPath, true);
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destinationFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destinationFile, true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                var destinationSubdirectory = Path.Combine(destinationDir, Path.GetFileName(directory));
                CopyDirectory(directory, destinationSubdirectory);
            }
        }

        private string NormalizedSaveRoot()
        {
            var configured = (_saveRootPath.Value ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            return DetectDefaultSaveRoot();
        }

        private static string DetectDefaultSaveRoot()
        {
            var localLow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Low", "TVGS");
            var scheduleIPath = Path.Combine(localLow, "Schedule I");
            if (Directory.Exists(scheduleIPath))
            {
                return scheduleIPath;
            }

            var schedule1Path = Path.Combine(localLow, "Schedule 1");
            return schedule1Path;
        }

        private static string SanitizeFolderName(string folderName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(folderName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static long GetDirectorySize(string directoryPath)
        {
            try
            {
                return Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path).Length)
                    .Sum();
            }
            catch
            {
                return 0L;
            }
        }

        private static string ToReadableSize(long bytes)
        {
            var sizes = new List<string> { "B", "KB", "MB", "GB" };
            double length = bytes;
            var order = 0;
            while (length >= 1024 && order < sizes.Count - 1)
            {
                order++;
                length /= 1024;
            }

            return $"{length:0.#} {sizes[order]}";
        }

        private void PrepareSafeNewWorldFlow()
        {
            try
            {
                EnsureDirectories();
                var worldDirs = GetWorldDirectories();
                _worldStateBeforeCreate = worldDirs.ToDictionary(path => Path.GetFileName(path), path => Directory.GetLastWriteTimeUtc(path), StringComparer.OrdinalIgnoreCase);

                _worldCreateBackupRoot = Path.Combine(SnapshotsRoot, $"PreCreateWorlds-{DateTime.Now:yyyyMMdd-HHmmss}");
                Directory.CreateDirectory(_worldCreateBackupRoot);
                foreach (var dir in worldDirs)
                {
                    CopyDirectory(dir, Path.Combine(_worldCreateBackupRoot, Path.GetFileName(dir)));
                }

                _watchWorldCreate = true;
                _worldCreateStartedAtUtc = DateTime.UtcNow;
                _newWorldStatus = "Prepared backup; open New World screen now.";

                if (!TryOpenWorldCreationScreen())
                {
                    _newWorldStatus = "Prepared backup. Could not auto-open menu, click New World manually.";
                }
            }
            catch (Exception ex)
            {
                _newWorldStatus = $"Prepare failed: {ex.Message}";
                MelonLogger.Error(ex.ToString());
            }
        }

        private void TryFinalizeSafeWorldCreate()
        {
            try
            {
                var worldDirs = GetWorldDirectories();
                if (worldDirs.Count == 0)
                {
                    return;
                }

                var newWorldFound = worldDirs.Any(path => !_worldStateBeforeCreate.ContainsKey(Path.GetFileName(path)));
                if (newWorldFound)
                {
                    _watchWorldCreate = false;
                    _newWorldStatus = "New world created as additional slot.";
                    return;
                }

                if ((DateTime.UtcNow - _worldCreateStartedAtUtc).TotalSeconds < 2)
                {
                    return;
                }

                var changed = worldDirs
                    .Select(path => new { Path = path, Name = Path.GetFileName(path), Time = Directory.GetLastWriteTimeUtc(path) })
                    .Where(x => _worldStateBeforeCreate.TryGetValue(x.Name, out var before) && x.Time > before.AddSeconds(1))
                    .OrderByDescending(x => x.Time)
                    .FirstOrDefault();

                if (changed == null || string.IsNullOrWhiteSpace(_worldCreateBackupRoot))
                {
                    return;
                }

                var nextName = GetNextWorldName(worldDirs.Select(Path.GetFileName));
                var nextPath = Path.Combine(NormalizedSaveRoot(), nextName);
                CopyDirectory(changed.Path, nextPath);

                var backupOriginal = Path.Combine(_worldCreateBackupRoot, changed.Name);
                if (Directory.Exists(backupOriginal))
                {
                    Directory.Delete(changed.Path, true);
                    CopyDirectory(backupOriginal, changed.Path);
                }

                _watchWorldCreate = false;
                _newWorldStatus = $"Moved new world to '{nextName}' and restored '{changed.Name}'.";
            }
            catch (Exception ex)
            {
                _watchWorldCreate = false;
                _newWorldStatus = $"Finalize failed: {ex.Message}";
                MelonLogger.Error(ex.ToString());
            }
        }

        private List<string> GetWorldDirectories()
        {
            return Directory.GetDirectories(NormalizedSaveRoot())
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    if (name.Equals("Snapshots", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return name.IndexOf("world", StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .ToList();
        }

        private static string GetNextWorldName(IEnumerable<string> existingNames)
        {
            var used = new HashSet<int>();
            foreach (var name in existingNames)
            {
                var digits = new string(name.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var n))
                {
                    used.Add(n);
                }
            }

            var next = 1;
            while (used.Contains(next))
            {
                next++;
            }

            return $"World{next}";
        }

        private bool TryOpenWorldCreationScreen()
        {
            var candidates = new[]
            {
                "OpenWorldCreation",
                "OpenCreateWorld",
                "OpenNewWorld",
                "OnClickNewWorld",
                "NewGame"
            };

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    foreach (var methodName in candidates)
                    {
                        var method = type.GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                        if (method == null)
                        {
                            continue;
                        }

                        var instances = UnityEngine.Object.FindObjectsOfType(type);
                        foreach (var instance in instances)
                        {
                            try
                            {
                                method.Invoke(instance, null);
                                return true;
                            }
                            catch
                            {
                                // keep trying candidates
                            }
                        }
                    }
                }
            }

            return false;
        }
    }
}
