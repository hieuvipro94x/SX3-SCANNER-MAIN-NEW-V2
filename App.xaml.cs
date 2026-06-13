using SX3_SCANER.Helper;
using SX3_SCANER.Model;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SX3_SCANER
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Local\SX3_SCANER_SingleInstance";
        private Mutex _singleInstanceMutex;

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            bool createdNew;
            StartupStatusWindow startupWindow = null;
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);

            if (!createdNew)
            {
                StartupManager.FocusExistingInstance();
                Shutdown();
                return;
            }

            try
            {
                StartupManager.SetStatus("\u0110ang kh\u1EDFi \u0111\u1ED9ng \u1EE9ng d\u1EE5ng...");
                startupWindow = new StartupStatusWindow();
                startupWindow.Show();

                await Task.Run(() =>
                {
                    DatabaseInitialize initialize = new DatabaseInitialize();
                    initialize.EnsureCreate();
                });

                StartupManager.SetStatus("\u0110ang t\u1EA3i c\u1EA5u h\u00ECnh...");

                MainWindow mainWindow = new MainWindow
                {
                    DataContext = new ViewModel.MainViewModel()
                };

                if (StartupManager.HasArgument(e.Args, "--minimized"))
                {
                    mainWindow.WindowState = WindowState.Minimized;
                }

                mainWindow.Show();
                startupWindow.Close();

                StartupManager.SetStatus("S\u1EB5n s\u00E0ng");
            }
            catch (Exception ex)
            {
                StartupManager.LogStartupError(
                    ex,
                    Model.Respository.DatabaseRepository.DatabasePath +
                    " | " +
                    Model.Respository.DatabaseRepository.ProductDatabasePath);

                StartupManager.Log("Application startup failed: " + ex);

                StartupManager.SetStatus("Kh\u00F4ng th\u1EC3 kh\u1EDFi \u0111\u1ED9ng \u1EE9ng d\u1EE5ng.");

                string diagnosis = StartupManager.GetDatabaseDiagnosis(ex);

                MessageBox.Show(
                    "Kh\u00F4ng th\u1EC3 kh\u1EDFi \u0111\u1ED9ng SX3 SCANER." +
                    Environment.NewLine +
                    "Nguy\u00EAn nh\u00E2n: " + diagnosis +
                    Environment.NewLine +
                    "Chi ti\u1EBFt: " + ex.Message +
                    Environment.NewLine +
                    "Log: " + StartupManager.ErrorLogPath,
                    "SX3 SCANER",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                startupWindow?.Close();
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }
    }
}
