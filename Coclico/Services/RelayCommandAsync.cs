using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Coclico.Services
{
    public class RelayCommandAsync : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Predicate<object?>?  _canExecute;
        private bool _isExecuting;

        public RelayCommandAsync(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
        {
            _execute    = execute    ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanExecute(object? parameter) =>
            !_isExecuting && (_canExecute == null || _canExecute(parameter));

        public async void Execute(object? parameter)
        {
            if (_isExecuting) return;
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute(parameter).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex, $"RelayCommandAsync.Execute — {_execute.Method.Name}");
            }
            finally
            {
                _isExecuting = false;
                System.Windows.Application.Current?.Dispatcher?.Invoke(RaiseCanExecuteChanged);
            }
        }

        public void RaiseCanExecuteChanged() =>
            CommandManager.InvalidateRequerySuggested();

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
