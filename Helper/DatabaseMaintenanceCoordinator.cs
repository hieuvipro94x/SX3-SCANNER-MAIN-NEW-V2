using System;
using System.Diagnostics;
using System.Threading;

namespace SX3_SCANER.Helper
{
    internal static class DatabaseMaintenanceCoordinator
    {
        private static readonly object SyncRoot = new object();
        private static int _activeOperations;
        private static bool _maintenancePending;
        private static bool _maintenanceActive;

        [ThreadStatic]
        private static int _maintenanceOwnerDepth;

        internal static event Action MaintenanceRequested;

        internal static bool IsMaintenancePendingOrActive
        {
            get
            {
                lock (SyncRoot)
                {
                    return _maintenancePending || _maintenanceActive;
                }
            }
        }

        internal static IDisposable EnterOperation(string operationName)
        {
            if (_maintenanceOwnerDepth > 0)
                return EmptyScope.Instance;

            lock (SyncRoot)
            {
                while (_maintenancePending || _maintenanceActive)
                {
                    Monitor.Wait(SyncRoot);
                }

                _activeOperations++;
                return new OperationScope(operationName);
            }
        }

        internal static IDisposable EnterMaintenance(
            string operationName,
            TimeSpan timeout)
        {
            if (_maintenanceOwnerDepth > 0)
            {
                _maintenanceOwnerDepth++;
                return new NestedMaintenanceScope();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            lock (SyncRoot)
            {
                while (_maintenancePending || _maintenanceActive)
                {
                    WaitWithTimeout(operationName, timeout, stopwatch);
                }

                _maintenancePending = true;
            }

            try
            {
                Action handler = MaintenanceRequested;
                if (handler != null)
                {
                    foreach (Action subscriber in handler.GetInvocationList())
                    {
                        try
                        {
                            subscriber();
                        }
                        catch (Exception ex)
                        {
                            StartupManager.Log(
                                "Khong huy duoc mot tac vu truoc database maintenance: " + ex);
                        }
                    }
                }

                lock (SyncRoot)
                {
                    while (_activeOperations > 0)
                    {
                        WaitWithTimeout(operationName, timeout, stopwatch);
                    }

                    _maintenancePending = false;
                    _maintenanceActive = true;
                    _maintenanceOwnerDepth = 1;
                    return new MaintenanceScope(operationName);
                }
            }
            catch
            {
                lock (SyncRoot)
                {
                    _maintenancePending = false;
                    Monitor.PulseAll(SyncRoot);
                }
                throw;
            }
        }

        private static void WaitWithTimeout(
            string operationName,
            TimeSpan timeout,
            Stopwatch stopwatch)
        {
            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero || !Monitor.Wait(SyncRoot, remaining))
            {
                throw new TimeoutException(
                    "Không thể khóa database an toàn cho tác vụ '" +
                    operationName + "' trong " + timeout.TotalSeconds.ToString("0") +
                    " giây. Database chưa bị thay đổi.");
            }
        }

        private static void ExitOperation(string operationName)
        {
            lock (SyncRoot)
            {
                if (_activeOperations <= 0)
                {
                    StartupManager.Log(
                        "Database activity counter invalid khi ket thuc: " + operationName);
                    return;
                }

                _activeOperations--;
                if (_activeOperations == 0)
                    Monitor.PulseAll(SyncRoot);
            }
        }

        private static void ExitMaintenance(string operationName)
        {
            lock (SyncRoot)
            {
                _maintenanceOwnerDepth = 0;
                _maintenanceActive = false;
                Monitor.PulseAll(SyncRoot);
            }
            StartupManager.Log("Da ket thuc database maintenance: " + operationName);
        }

        private sealed class OperationScope : IDisposable
        {
            private string _operationName;

            internal OperationScope(string operationName)
            {
                _operationName = operationName ?? "database operation";
            }

            public void Dispose()
            {
                string operationName = Interlocked.Exchange(ref _operationName, null);
                if (operationName != null)
                    ExitOperation(operationName);
            }
        }

        private sealed class MaintenanceScope : IDisposable
        {
            private string _operationName;

            internal MaintenanceScope(string operationName)
            {
                _operationName = operationName ?? "database maintenance";
            }

            public void Dispose()
            {
                string operationName = Interlocked.Exchange(ref _operationName, null);
                if (operationName != null)
                    ExitMaintenance(operationName);
            }
        }

        private sealed class NestedMaintenanceScope : IDisposable
        {
            public void Dispose()
            {
                if (_maintenanceOwnerDepth > 1)
                    _maintenanceOwnerDepth--;
            }
        }

        private sealed class EmptyScope : IDisposable
        {
            internal static readonly EmptyScope Instance = new EmptyScope();
            public void Dispose() { }
        }
    }
}
