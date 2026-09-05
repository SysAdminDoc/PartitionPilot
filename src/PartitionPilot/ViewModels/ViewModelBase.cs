using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PartitionPilot;

/// <summary>
/// Change-notification base for the view models.
/// <para>
/// This was <c>CommunityToolkit.Mvvm.ObservableObject</c>. The package supplied nothing else, its
/// upstream has had no commits since March 2026 with maintainers saying nobody is available to keep it
/// going, and it has an open report of <c>AsyncRelayCommand</c> crashing on .NET 10 — a poor trade for
/// one base class in a tool that runs elevated.
/// </para>
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Assigns <paramref name="value"/> and raises a change notification, returning whether anything
    /// changed. Callers rely on the return value to decide whether to do follow-up work, so the
    /// equality check has to match what the toolkit did: <see cref="EqualityComparer{T}.Default"/>.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
