using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SX3_SCANER.Helper;

namespace SX3_SCANER.ViewModel
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal class RelayCommand<T> : ICommand
    {
        private readonly Predicate<T> _canExecute;
        private readonly Action<T> _execute;

        public RelayCommand(Predicate<T> canExecute, Action<T> execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null) return true;
            if (parameter == null && typeof(T).IsValueType) return _canExecute(default(T));
            if (parameter == null || parameter is T) return _canExecute((T)parameter);
            return false;
        }

        public void Execute(object parameter)
        {
            if (parameter == null && typeof(T).IsValueType)
            {
                _execute(default(T));
                return;
            }
            _execute((T)parameter);
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    internal sealed class AsyncRelayCommand<T> : ICommand
    {
        private readonly Predicate<T> _canExecute;
        private readonly Func<T, Task> _executeAsync;
        private bool _isExecuting;

        public AsyncRelayCommand(Predicate<T> canExecute, Func<T, Task> executeAsync)
        {
            _canExecute = canExecute;
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        }

        public bool CanExecute(object parameter)
        {
            if (_isExecuting) return false;
            if (_canExecute == null) return true;
            if (parameter == null && typeof(T).IsValueType)
                return _canExecute(default(T));
            return (parameter == null || parameter is T) && _canExecute((T)parameter);
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;

            T value = parameter == null && typeof(T).IsValueType
                ? default(T)
                : (T)parameter;

            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await _executeAsync(value);
            }
            catch (Exception ex)
            {
                StartupManager.Log("Async command failed: " + ex);
                ProfessionalMessageBox.Show(
                    "Tác vụ không thể hoàn tất. Vui lòng thử lại hoặc liên hệ kỹ thuật.",
                    "LỖI THỰC THI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
