using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading;

namespace PennyPet
{
    // Windows storage adapter for the platform-neutral PetSettingsData and
    // PetSettingsCodec types in PennyPet.Core.
    internal sealed class PetSettings : PetSettingsData
    {
        private const long MaximumSettingsFileBytes = 1024L * 1024L;
        private string _unreadablePrimaryPath;
        private string _unreadableBackupPath;
        private readonly PersistenceWriter<SettingsWriteRequest> _writer;
        private readonly SynchronizationContext _uiContext;

        internal PetSettings(Func<SettingsWriteRequest, PersistenceResult> write = null)
        {
            _uiContext = SynchronizationContext.Current;
            _writer = new PersistenceWriter<SettingsWriteRequest>(write ?? WriteSnapshot);
            _writer.Failed += delegate(object sender, PersistenceFailedEventArgs e)
            {
                EventHandler<PersistenceFailedEventArgs> handler = SaveFailed;
                if (handler == null) return;
                if (_uiContext == null || SynchronizationContext.Current == _uiContext)
                    handler(this, e);
                else _uiContext.Post(delegate { handler(this, e); }, null);
            };
        }

        internal event EventHandler<PersistenceFailedEventArgs> SaveFailed;

        internal bool HasUnsavedChanges { get { return _writer.IsDirty; } }
        internal bool HasPendingSaves { get { return _writer.HasPending; } }
        internal Exception LastSaveError { get { return _writer.LastError; } }

        private static string FilePath
        {
            get
            {
                return Path.Combine(WindowsDataPaths.PennyPetDirectory,
                    "settings.ini");
            }
        }

        public static PetSettings Load()
        {
            string primary = FilePath;
            if (File.Exists(primary) || File.Exists(primary + ".bak"))
                return LoadFromFile(primary);
            PetSettings migrated = TryMigrateLegacySettings();
            return migrated ?? new PetSettings();
        }

        private static PetSettings TryMigrateLegacySettings()
        {
            foreach (string legacyPath in LegacySettingsPaths())
            {
                if (!File.Exists(legacyPath)) continue;
                PetSettings legacy;
                Exception error;
                if (TryLoadSingleFile(legacyPath, out legacy, out error))
                {
                    // One-time copy-forward. The legacy file stays untouched.
                    legacy.SaveToFile(FilePath);
                    return legacy;
                }
                ApplicationDiagnostics.ReportNonFatal(
                    "settings-legacy-skip", error);
            }
            return null;
        }

        private static string[] LegacySettingsPaths()
        {
            string local = WindowsDataPaths.LocalApplicationDataDirectory;
            return new string[]
            {
                Path.Combine(local, "FishPet", "settings.ini"),
                Path.Combine(local, "ShanYingPet", "settings.ini")
            };
        }

        internal static PetSettings LoadFromFile(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return new PetSettings();
            PetSettings settings;
            Exception primaryError;
            if (TryLoadSingleFile(filePath, out settings, out primaryError))
                return settings;
            ApplicationDiagnostics.ReportNonFatal("settings-load-primary",
                primaryError);

            string backupPath = filePath + ".bak";
            Exception backupError = null;
            if (File.Exists(backupPath) && TryLoadSingleFile(backupPath,
                out settings, out backupError))
            {
                // The recovered values are safe to use. Preserve the unreadable
                // primary before the next atomic save replaces it.
                settings._unreadablePrimaryPath = Path.GetFullPath(filePath);
                return settings;
            }
            if (File.Exists(backupPath))
                ApplicationDiagnostics.ReportNonFatal("settings-load-backup",
                    backupError);

            settings = new PetSettings();
            settings._unreadablePrimaryPath = Path.GetFullPath(filePath);
            if (File.Exists(backupPath))
                settings._unreadableBackupPath = Path.GetFullPath(backupPath);
            return settings;
        }

        private static bool TryLoadSingleFile(string filePath,
            out PetSettings settings, out Exception error)
        {
            settings = null;
            error = null;
            try
            {
                if (new FileInfo(filePath).Length > MaximumSettingsFileBytes)
                    throw new InvalidDataException("Settings file is too large.");
                PetSettingsData parsed = PetSettingsCodec.Parse(
                    File.ReadAllLines(filePath, Encoding.UTF8));
                settings = new PetSettings();
                settings.CopyFrom(parsed);
            }
            catch (Exception caught)
            {
                settings = null;
                error = caught;
                return false;
            }
            return true;
        }

        public PersistenceResult Save()
        {
            return SaveToFile(FilePath);
        }

        internal PersistenceResult SaveToFile(string filePath)
        {
            return _writer.Enqueue(CaptureWrite(filePath)).GetAwaiter().GetResult();
        }

        internal void SaveAsync()
        {
            _writer.Enqueue(CaptureWrite(FilePath), coalesce: true);
        }

        internal PersistenceResult WaitForPendingSaves()
        {
            return WaitForPendingSaves(TimeSpan.FromSeconds(5));
        }

        internal PersistenceResult WaitForPendingSaves(TimeSpan timeout)
        {
            return _writer.Flush(timeout);
        }

        private SettingsWriteRequest CaptureWrite(string filePath)
        {
            // Capture on the model thread. The worker never enumerates mutable
            // settings or reminder items, including during a retry.
            return new SettingsWriteRequest(filePath, PetSettingsCodec.Serialize(this));
        }

        private PersistenceResult WriteSnapshot(SettingsWriteRequest request)
        {
            try
            {
                if (!PreserveUnreadableSources(request.FilePath))
                    throw new IOException(
                        "Unreadable settings could not be preserved safely.");
                AtomicTextFile.WriteAllLines(request.FilePath, request.Lines, true);
                return PersistenceResult.Success();
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal("settings-save", error);
                return PersistenceResult.Failure(error);
            }
        }

        private bool PreserveUnreadableSources(string destinationPath)
        {
            string destination = Path.GetFullPath(destinationPath);
            bool protectsPrimary = !String.IsNullOrEmpty(_unreadablePrimaryPath) &&
                String.Equals(destination, _unreadablePrimaryPath,
                    StringComparison.OrdinalIgnoreCase);
            bool protectsBackup = !String.IsNullOrEmpty(_unreadableBackupPath) &&
                String.Equals(destination + ".bak", _unreadableBackupPath,
                    StringComparison.OrdinalIgnoreCase);
            if (!protectsPrimary && !protectsBackup) return true;
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                if (protectsPrimary && File.Exists(_unreadablePrimaryPath))
                    File.Copy(_unreadablePrimaryPath,
                        _unreadablePrimaryPath + ".corrupt-" + stamp, false);
                if (protectsBackup && File.Exists(_unreadableBackupPath))
                    File.Copy(_unreadableBackupPath,
                        _unreadableBackupPath + ".corrupt-" + stamp, false);
                _unreadablePrimaryPath = null;
                _unreadableBackupPath = null;
                return true;
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "settings-preserve-unreadable", error);
                return false;
            }
        }

    }
    internal sealed class SettingsWriteRequest
    {
        internal readonly string FilePath;
        internal readonly IReadOnlyList<string> Lines;

        internal SettingsWriteRequest(string filePath, List<string> lines)
        {
            FilePath = filePath;
            Lines = lines.AsReadOnly();
        }
    }
}
