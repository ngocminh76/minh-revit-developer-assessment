using System;
using System.Windows.Input;

namespace Shared_Core.Base
{
    /// <summary>
    /// Custom ICommand implementation with a built-in 0.5s throttle to prevent double execution.
    /// </summary>
    public class MyCmd : ICommand
    {
        private readonly Action _execute;
        private readonly Func<object, bool> _canExecute;
        private readonly TimeSpan _throttleTime = TimeSpan.FromSeconds(0.5);
        private DateTime _lastExecutionTime = DateTime.MinValue;

        public MyCmd(Action execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            if (DateTime.Now - _lastExecutionTime > _throttleTime)
            {
                _lastExecutionTime = DateTime.Now;
                _execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// Custom generic ICommand implementation with a built-in 0.5s throttle to prevent double execution.
    /// </summary>
    /// <typeparam name="T">The type of the command parameter.</typeparam>
    public class MyCmd<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;
        private readonly TimeSpan _throttleTime = TimeSpan.FromSeconds(0.5);
        private DateTime _lastExecutionTime = DateTime.MinValue;

        public MyCmd(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute((T)parameter);
        }

        public void Execute(object parameter)
        {
            if (DateTime.Now - _lastExecutionTime > _throttleTime)
            {
                _lastExecutionTime = DateTime.Now;
                _execute((T)parameter);
            }
        }

        public void RaiseCanExecuteChanged()
        {
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
}
