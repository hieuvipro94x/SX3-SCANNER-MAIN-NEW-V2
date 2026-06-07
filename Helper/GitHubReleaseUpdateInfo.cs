namespace SX3_SCANER.Helper
{
    public sealed class GitHubReleaseUpdateInfo
    {
        public string Version { get; set; }

        public string TagName { get; set; }

        public string ReleaseNotes { get; set; }

        public string FileName { get; set; }

        public long FileSize { get; set; }

        public string DownloadUrl { get; set; }

        public bool IsUpdateAvailable { get; set; }
    }
}
