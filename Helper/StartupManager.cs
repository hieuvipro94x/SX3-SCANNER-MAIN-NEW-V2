using SX3_SCANER.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
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
            get
            {
                return "Đã tắt ghi log file.";
            }
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

        internal static void EnsureStartWithWindows()
        {
            try
            {
                string executablePath = Process.GetCurrentProcess().MainModule.FileName;

                if (string.IsNullOrWhiteSpace(executablePath) ||
                    !File.Exists(executablePath))
                {
                    Log("Không xác định được file EXE để đăng ký khởi động cùng Windows.");
                    return;
                }

                string runCommand = "\"" + executablePath + "\" --autostart";

                using (RegistryKey runKey = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (runKey == null)
                    {
                        Log("Không mở được registry Run của CurrentUser.");
                        return;
                    }

                    object currentValue = runKey.GetValue("SX3 Scanner");
                    string currentCommand = currentValue == null
                        ? string.Empty
                        : currentValue.ToString();

                    if (!string.Equals(
                        currentCommand,
                        runCommand,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        runKey.SetValue(
                            "SX3 Scanner",
                            runCommand,
                            RegistryValueKind.String);
                    }
                }

                Log("Đã kiểm tra/đăng ký app khởi động cùng Windows.");
            }
            catch (Exception ex)
            {
                Log("Không đăng ký được app khởi động cùng Windows: " + ex);
            }
        }

        internal static bool IsStartWithWindowsEnabled()
        {
            try
            {
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run",
                    false))
                {
                    object currentValue = runKey == null
                        ? null
                        : runKey.GetValue("SX3 Scanner");

                    return currentValue != null &&
                        !string.IsNullOrWhiteSpace(currentValue.ToString());
                }
            }
            catch (Exception ex)
            {
                Log("Không kiểm tra được trạng thái khởi động cùng Windows: " + ex);
                return false;
            }
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
            TryAppendLog(message);
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

        private static void TryAppendLog(string message)
        {
            // Tắt ghi log ra AppData\Local\SX3_SCANER để không phát sinh file log khi chạy máy sản xuất.
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
                        SX3_SCANER.Helper.ProfessionalMessageBox.Show(
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
                    SX3_SCANER.Helper.ProfessionalMessageBox.Show(
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
