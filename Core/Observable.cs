using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ConsoleUpdateCenter.Core
{
    /// <summary>
    /// Base propia para notificar cambios (no depende de la versión del SDK).
    /// </summary>
    public abstract class ObservableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Comando simple para el tema.
    /// </summary>
    public sealed class SimpleCommand : ICommand
    {
        private readonly Action execute;

        public SimpleCommand(Action execute)
        {
            this.execute = execute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object parameter) => execute != null;

        public void Execute(object parameter) => execute?.Invoke();
    }
}
