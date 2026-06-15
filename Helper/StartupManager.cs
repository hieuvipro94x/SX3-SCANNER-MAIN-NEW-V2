using SX3_SCANER.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace SX3_SCANER.Helper
{
    internal static class StartupManager
    {
        private static readonly object StatusSync = new object();

        private static readonly HashSet<string> LoggedKeys =
            new HashSet<string>(StringComparer.Ordinal);

        private static string _currentStatus = "Sẵn sàng";

        internal static event Action<string> StatusChanged;

        public static event Action<AnnouncementServerStatusInfo>
            AnnouncementServerStatusChanged;

        internal static string ErrorLogPath
        {
            get { return string.Empty; }
        }

        internal static string CurrentStatus
        {
            get
            {
                lock (StatusSync)
                {
                    return _currentStatus;
                }
            }
        }

        public static AnnouncementServerStatusInfo CurrentAnnouncementServerStatus
        {
            get;
            private set;
        } = AnnouncementServerStatusInfo.Unknown();

        internal static void SetStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Action<string> handler;

            lock (StatusSync)
            {
                _currentStatus = message.Trim();
                handler = StatusChanged;
            }

            Debug.WriteLine("[StartupStatus] " + _currentStatus);
            handler?.Invoke(_currentStatus);
        }

        public static void SetAnnouncementServerStatus(
            AnnouncementServerStatusInfo status)
        {
            if (status == null)
                return;

            CurrentAnnouncementServerStatus = status;
            AnnouncementServerStatusChanged?.Invoke(status);
        }

        internal static bool HasArgument(string[] args, string argument)
        {
            if (args == null)
                return false;

            foreach (string value in args)
            {
                if (string.Equals(
                    value,
                    argument,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void FocusExistingInstance()
        {
            try
            {
                Process current = Process.GetCurrentProcess();

                foreach (Process process in
                         Process.GetProcessesByName(current.ProcessName))
                {
                    if (process.Id == current.Id)
                        continue;

                    IntPtr windowHandle = WaitForMainWindowHandle(process);

                    if (windowHandle == IntPtr.Zero)
                        continue;

                    if (IsIconic(windowHandle))
                        ShowWindowAsync(windowHandle, 9);

                    SetForegroundWindow(windowHandle);
                    return;
                }

                ShowWarning(
                    "Ứng dụng đã chạy",
                    "Đã tồn tại tiến trình khác nhưng không tìm thấy cửa sổ chính.");
            }
            catch (Exception ex)
            {
                ShowError(
                    "Không thể chuyển focus đến ứng dụng đang chạy.",
                    ex);
            }
        }

        internal static void Log(string message)
        {
            Debug.WriteLine("[StartupManager] " + message);
        }

        internal static void LogOnce(string key, string message)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                Log(message);
                return;
            }

            lock (StatusSync)
            {
                if (!LoggedKeys.Add(key))
                    return;
            }

            Log(message);
        }

        internal static void LogStartupError(
            Exception exception,
            string databasePath)
        {
            string detail =
                "Database path: " + (databasePath ?? "(unknown)") +
                Environment.NewLine +
                Environment.NewLine +
                "Loại lỗi: " +
                (exception == null ? "(none)" : exception.GetType().FullName) +
                Environment.NewLine +
                Environment.NewLine +
                "Nội dung lỗi: " +
                (exception == null ? "(none)" : exception.Message) +
                Environment.NewLine +
                Environment.NewLine +
                "Chẩn đoán: " + GetDatabaseDiagnosis(exception);

            ShowError("Lỗi khởi động ứng dụng", detail);
        }

        internal static string GetDatabaseDiagnosis(Exception exception)
        {
            string details = exception == null
                ? string.Empty
                : exception.ToString();

            if (details.IndexOf(
                    "database is locked",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Database đang bị khóa.";
            }

            if (details.IndexOf(
                    "file is not a database",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "File database không hợp lệ.";
            }

            if (details.IndexOf(
                    "unable to open database file",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Không mở được file database.";
            }

            return "Lỗi khởi động hoặc database khác.";
        }

        internal static void ShowError(string title, Exception exception)
        {
            ShowError(
                title,
                exception == null
                    ? "Không có thông tin lỗi."
                    : exception.Message);
        }

        internal static void ShowError(string title, string message)
        {
            ShowMessageBox(
                title,
                message,
                MessageBoxImage.Error);
        }

        internal static void ShowWarning(string title, string message)
        {
            ShowMessageBox(
                title,
                message,
                MessageBoxImage.Warning);
        }

        internal static void ShowInfo(string title, string message)
        {
            ShowMessageBox(
                title,
                message,
                MessageBoxImage.Information);
        }

        private static void ShowMessageBox(
            string title,
            string message,
            MessageBoxImage icon)
        {
            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(
                    new Action(() =>
                    {
                        MessageBox.Show(
                            message ?? string.Empty,
                            string.IsNullOrWhiteSpace(title)
                                ? "SX3 Scanner"
                                : title,
                            MessageBoxButton.OK,
                            icon);
                    }));
            }
            catch
            {
                try
                {
                    MessageBox.Show(
                        message ?? string.Empty,
                        string.IsNullOrWhiteSpace(title)
                            ? "SX3 Scanner"
                            : title,
                        MessageBoxButton.OK,
                        icon);
                }
                catch
                {
                    // Không để popup lỗi làm crash app.
                }
            }
        }

        private static IntPtr WaitForMainWindowHandle(Process process)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                process.Refresh();

                if (process.MainWindowHandle != IntPtr.Zero)
                    return process.MainWindowHandle;

                System.Threading.Thread.Sleep(100);
            }

            return IntPtr.Zero;
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
    }
}