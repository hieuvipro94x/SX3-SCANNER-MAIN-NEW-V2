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
                StartupManager.SetStatus("Äang khá»Ÿi Ä‘á»™ng á»©ng dá»¥ng...");
                startupWindow = new StartupStatusWindow();
                startupWindow.Show();

                await Task.Run(() =>
                {
                    DatabaseInitialize initialize = new DatabaseInitialize();
                    initialize.EnsureCreate();
                });

                StartupManager.SetStatus("Äang táº£i cáº¥u hÃ¬nh...");
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
                StartupManager.SetStatus("Sáºµn sÃ ng");
            }
            catch (Exception ex)
            {
                StartupManager.LogStartupError(
                    ex,
                    Model.Respository.DatabaseRepository.DatabasePath +
                    " | " +
                    Model.Respository.DatabaseRepository.ProductDatabasePath);
                StartupManager.Log("Application startup failed: " + ex);
                StartupManager.SetStatus("KhÃ´ng thá»ƒ khá»Ÿi Ä‘á»™ng á»©ng dá»¥ng.");
                string diagnosis = StartupManager.GetDatabaseDiagnosis(ex);
                MessageBox.Show(
                    "KhÃ´ng thá»ƒ khá»Ÿi Ä‘á»™ng SX3 SCANER." +
                    Environment.NewLine +
                    "NguyÃªn nhÃ¢n: " + diagnosis +
                    Environment.NewLine +
                    "Chi tiáº¿t: " + ex.Message +
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
