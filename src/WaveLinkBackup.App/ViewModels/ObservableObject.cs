using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// The one INotifyPropertyChanged base. Hand-written rather than taken from a toolkit: the
/// shell has three view models, this is fifteen lines, and a source generator would be the
/// project's second production dependency for it.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        Raise(name);

        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
