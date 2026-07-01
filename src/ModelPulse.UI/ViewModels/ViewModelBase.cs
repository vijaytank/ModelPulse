using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModelPulse.UI.ViewModels
{
    /// <summary>
    /// Base class for MVVM ViewModels, providing diff-based PropertyChanged notification (see NFR-02).
    /// </summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Compares the current field value with the new value.
        /// Raises PropertyChanged ONLY when the value has changed, preventing redundant WPF layout cycles.
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
