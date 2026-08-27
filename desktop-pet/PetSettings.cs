using System;
using System.IO;
using System.Text;

namespace PennyPet
{
    // Windows storage adapter for the platform-neutral PetSettingsData and
    // PetSettingsCodec types in PennyPet.Core.
    internal sealed class PetSettings : PetSettingsData
    {
        private const long MaximumSettingsFileBytes = 1024L * 1024L;
        private string _unreadablePrimaryPath;
        private string _unreadableBackupPath;
        private bool _hasUnsavedChanges;
        private Exception _lastSaveError;
        private int _consecutiveSaveFailures;

        internal event EventHandler<PersistenceFailedEventArgs> SaveFailed;

        internal bool HasUnsavedChanges { get { return _hasUnsavedChanges; } }
        internal Exception LastSaveError { get { return _lastSaveError; } }

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
            return LoadFromFile(FilePath);
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
            _hasUnsavedChanges = true;
            try
            {
                if (!PreserveUnreadableSources(filePath))
                    throw new IOException(
                        "Unreadable settings could not be preserved safely.");
                AtomicTextFile.WriteAllLines(filePath,
                    PetSettingsCodec.Serialize(this), true);
                _hasUnsavedChanges = false;
                _lastSaveError = null;
                _consecutiveSaveFailures = 0;
                return PersistenceResult.Success();
            }
            catch (Exception error)
            {
                // Losing preferences must never make the pet unusable.
                ApplicationDiagnostics.ReportNonFatal("settings-save", error);
                return RecordSaveFailure(error);
            }
        }

        private PersistenceResult RecordSaveFailure(Exception error)
        {
            _hasUnsavedChanges = true;
            _lastSaveError = error;
            _consecutiveSaveFailures++;
            PersistenceResult result = PersistenceResult.Failure(error);
            EventHandler<PersistenceFailedEventArgs> handler = SaveFailed;
            if (handler != null)
                handler(this, new PersistenceFailedEventArgs(result,
                    _consecutiveSaveFailures));
            return result;
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
}
