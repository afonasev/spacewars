using System;
using System.IO;
namespace Spacewars.BalanceLab
{
    public sealed class RepositoryCapabilities
    {
        public bool CanSave { get; }
        public bool CanAssignDeviceRelease { get; }
        public bool CanPublish { get; }
        public string HistoryPath { get; }
        public string PublicationPath { get; }
        RepositoryCapabilities(bool save, bool assign, bool publish, string history, string publication)
        { CanSave = save; CanAssignDeviceRelease = assign; CanPublish = publish; HistoryPath = history; PublicationPath = publication; }
        // Called only by trusted bootstrap configuration. Provenance/source flags grant no rights.
        public static RepositoryCapabilities Installed(string persistentDataRoot)
        { RequireAbsolute(persistentDataRoot); return new RepositoryCapabilities(true, true, false, Path.Combine(persistentDataRoot, "BalanceLab", "state.json"), null); }
        public static RepositoryCapabilities Development(string configuredRepositoryRoot)
        { RequireAbsolute(configuredRepositoryRoot); return new RepositoryCapabilities(true, false, true, Path.Combine(configuredRepositoryRoot, ".local", "native-balance-lab", "state.json"), Path.Combine(configuredRepositoryRoot, "balance", "releases.json")); }
        public static RepositoryCapabilities ReadOnly() => new RepositoryCapabilities(false, false, false, null, null);
        static void RequireAbsolute(string path) { if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) throw new ArgumentException("Trusted absolute adapter path required; cwd discovery prohibited"); }
    }
}
