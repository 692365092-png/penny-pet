using System;
using System.IO;

namespace PennyPet
{
    internal sealed class PersistenceResult
    {
        private PersistenceResult(bool succeeded, Exception error)
        {
            Succeeded = succeeded;
            Error = error;
        }

        internal bool Succeeded { get; private set; }
        internal Exception Error { get; private set; }
        internal string ErrorMessage
        {
            get { return Error == null ? String.Empty : Error.Message; }
        }

        internal static PersistenceResult Success()
        {
            return new PersistenceResult(true, null);
        }

        internal static PersistenceResult Failure(Exception error)
        {
            return new PersistenceResult(false, error ??
                new IOException("The data could not be saved."));
        }
    }

    internal sealed class PersistenceFailedEventArgs : EventArgs
    {
        internal PersistenceFailedEventArgs(PersistenceResult result,
            int consecutiveFailures)
        {
            Result = result;
            ConsecutiveFailures = consecutiveFailures;
        }

        internal PersistenceResult Result { get; private set; }
        internal int ConsecutiveFailures { get; private set; }
    }
}
