using SX3_SCANER.Helper;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace SX3_SCANER
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Local\SX3_SCANER_SingleInstance";
        private Mutex _singleInstanceMutex;

        public App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveLocalAssembly;
        }

        private static Assembly ResolveLocalAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                AssemblyName requested = new AssemblyName(args.Name);
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string candidate = Path.Combine(baseDirectory, requested.Name + ".dll");

                return File.Exists(candidate)
                    ? Assembly.LoadFrom(candidate)
                    : null;
            }
            catch
            {
                return null;
            }
        }

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
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                StartupManager.SetStatus("Đang khởi động ứng dụng...");
                startupWindow = new StartupStatusWindow();
                startupWindow.Show();

                ViewModel.MainViewModel mainViewModel = new ViewModel.MainViewModel();

                await mainViewModel.InitializeApplicationAsync();

                if (!mainViewModel.IsApplicationReady)
                {
                    startupWindow.Close();
                    Shutdown();
                    return;
                }

                StartupManager.SetStatus("Đã kiểm tra xong. Đang mở màn quét...");

                MainWindow mainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };

                if (StartupManager.HasArgument(e.Args, "--minimized"))
                {
                    mainWindow.WindowState = WindowState.Minimized;
                }

                Application.Current.MainWindow = mainWindow;
                startupWindow.Close();
                startupWindow = null;
                mainWindow.Show();
                ShutdownMode = ShutdownMode.OnMainWindowClose;
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

                SX3_SCANER.Helper.ProfessionalMessageBox.Show(
                    "Kh\u00F4ng th\u1EC3 kh\u1EDFi \u0111\u1ED9ng SX3 SCANER." +
                    Environment.NewLine +
                    "Nguy\u00EAn nh\u00E2n: " + diagnosis +
                    Environment.NewLine +
                    "Chi ti\u1EBFt: " + ex.Message,
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
