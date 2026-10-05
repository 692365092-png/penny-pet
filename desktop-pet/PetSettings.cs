using System;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading;

namespace PennyPet
{
    // Windows storage adapter for the platform-neutral PetSettingsData and
    // PetSettingsCodec types in PennyPet.Core.
    internal sealed class PetSettings : PetSettingsData, IPersistenceRetryTarget
    {
        private const long MaximumSettingsFileBytes = 1024L * 1024L;
        private string _unreadablePrimaryPath;
        private string _unreadableBackupPath;
        private readonly PersistenceWriter<SettingsWriteRequest> _writer;
        private readonly SynchronizationContext _uiContext;
        private IReadOnlyList<string> _lastQueuedMainLines;
        private bool _forceMainSave;

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
            AcceptCurrentAsMainBaseline();
        }

        internal event EventHandler<PersistenceFailedEventArgs> SaveFailed;

        event EventHandler<PersistenceFailedEventArgs> IPersistenceRetryTarget.SaveFailed
        {
            add { SaveFailed += value; }
            remove { SaveFailed -= value; }
        }
        bool IPersistenceRetryTarget.HasUnsavedChanges
        { get { return HasUnsavedChanges; } }
        bool IPersistenceRetryTarget.HasPendingSaves
        { get { return HasPendingSaves; } }
        void IPersistenceRetryTarget.RequestAutosave() { SaveAsync(); }

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
            if (String.IsNullOrWhiteSpace(filePath))
                return new PetSettings();
            PetSettings settings;
            Exception primaryError;
            bool primaryExists = File.Exists(filePath);
            if (primaryExists)
            {
                if (TryLoadSingleFile(filePath, out settings, out primaryError))
                    return settings;
                ApplicationDiagnostics.ReportNonFatal("settings-load-primary", primaryError);
            }

            string backupPath = filePath + ".bak";
            Exception backupError = null;
            if (File.Exists(backupPath) && TryLoadSingleFile(backupPath,
                out settings, out backupError))
            {
                // The recovered values are safe to use. Preserve the unreadable
                // primary before the next atomic save replaces it. Recovery is
                // itself a dirty startup state: repair the primary even when the
                // recovered values require no other normalization.
                if (primaryExists) settings._unreadablePrimaryPath = Path.GetFullPath(filePath);
                settings._forceMainSave = true;
                return settings;
            }
            if (File.Exists(backupPath))
                ApplicationDiagnostics.ReportNonFatal("settings-load-backup",
                    backupError);

            settings = new PetSettings();
            if (primaryExists) settings._unreadablePrimaryPath = Path.GetFullPath(filePath);
            if (File.Exists(backupPath))
                settings._unreadableBackupPath = Path.GetFullPath(backupPath);
            if (primaryExists || File.Exists(backupPath))
                settings._forceMainSave = true;
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
                settings.AcceptCurrentAsMainBaseline();
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
            SettingsWriteRequest request = CaptureWrite(filePath);
            Task<PersistenceResult> receipt = _writer.Enqueue(request);
            RememberMainRequest(request);
            return receipt.GetAwaiter().GetResult();
        }

        internal Task<PersistenceResult> SaveBarrierAsync()
        {
            SettingsWriteRequest request = CaptureWrite(FilePath);
            Task<PersistenceResult> receipt = _writer.Enqueue(request);
            RememberMainRequest(request);
            return receipt;
        }

        internal void SaveAsync()
        {
            SettingsWriteRequest request = CaptureWrite(FilePath);
            _writer.Enqueue(request, coalesce: true);
            RememberMainRequest(request);
        }

        internal bool SaveIfChangedAsync()
        {
            SettingsWriteRequest request = CaptureWrite(FilePath);
            if (!_forceMainSave && !_writer.IsDirty &&
                LinesEqual(_lastQueuedMainLines, request.Lines))
                return false;
            _writer.Enqueue(request, coalesce: true);
            RememberMainRequest(request);
            return true;
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

        private void AcceptCurrentAsMainBaseline()
        {
            _lastQueuedMainLines = PetSettingsCodec.Serialize(this).AsReadOnly();
            _forceMainSave = false;
        }

        private void RememberMainRequest(SettingsWriteRequest request)
        {
            if (request == null || !IsMainFilePath(request.FilePath)) return;
            _lastQueuedMainLines = new List<string>(request.Lines).AsReadOnly();
            _forceMainSave = false;
        }

        private static bool IsMainFilePath(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath)) return false;
            try
            {
                return String.Equals(Path.GetFullPath(filePath),
                    Path.GetFullPath(FilePath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool LinesEqual(IReadOnlyList<string> left,
            IReadOnlyList<string> right)
        {
            if (Object.ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count)
                return false;
            for (int index = 0; index < left.Count; index++)
                if (!String.Equals(left[index], right[index],
                    StringComparison.Ordinal)) return false;
            return true;
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
