using SX3_SCANER.Helper;
using System.Windows;

namespace SX3_SCANER
{
    public partial class UpdateReleaseNotesWindow : Window
    {
        public bool Accepted { get; private set; }

        public UpdateReleaseNotesWindow(string currentVersion, GitHubReleaseUpdateInfo update)
        {
            InitializeComponent();

            txtCurrentVersion.Text = "V" + currentVersion;
            txtNewVersion.Text = "V" + update.Version;
            txtFileName.Text = update.FileName;
            txtFileSize.Text = FormatFileSize(update.FileSize);
            txtReleaseSource.Text = GitHubReleaseUpdateService.ReleasesPageUrl;

            txtReleaseNotes.Text = string.IsNullOrWhiteSpace(update.ReleaseNotes)
                ? "Không có ghi chú phát hành."
                : update.ReleaseNotes.Trim();

            RequiredBadge.Visibility = Visibility.Collapsed;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "Không xác định";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return size.ToString(unitIndex == 0 ? "0" : "0.##") + " " + units[unitIndex];
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            Accepted = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Accepted = false;
            DialogResult = false;
            Close();
        }
    }
}
