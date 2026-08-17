using System.ComponentModel;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The INotifyPropertyChanged base that all view models inherit. Set has two branches and the
/// unchanged-value path — which skips the write and raise — is the kind of bug that breaks a
/// whole screen silently and never shows in a log.
/// </summary>
public sealed class ObservableObjectTests
{
    private sealed class TestViewModel : ObservableObject
    {
        private string _name = string.Empty;
        private int _count;

        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        public int Count
        {
            get => _count;
            set => Set(ref _count, value);
        }

        public bool TestSetWithChange(string newValue) =>
            Set(ref _name, newValue, nameof(Name));

        public bool TestSetWithoutChange(string newValue) =>
            Set(ref _name, newValue, nameof(Name));

        public void RaiseManually() => Raise(nameof(Name));
    }

    [Fact]
    public void Set_with_a_changed_value_writes_the_field_and_raises_the_event()
    {
        var vm = new TestViewModel();
        PropertyChangedEventArgs? received = null;

        vm.PropertyChanged += (_, e) => received = e;

        var result = vm.TestSetWithChange("NewValue");

        Assert.True(result);
        Assert.Equal("NewValue", vm.Name);
        Assert.NotNull(received);
        Assert.Equal(nameof(vm.Name), received.PropertyName);
    }

    [Fact]
    public void Set_with_an_unchanged_value_returns_false_and_does_not_raise()
    {
        var vm = new TestViewModel { Name = "InitialValue" };
        var eventRaised = false;

        vm.PropertyChanged += (_, _) => eventRaised = true;

        var result = vm.TestSetWithoutChange("InitialValue");

        Assert.False(result);
        Assert.False(eventRaised);
        Assert.Equal("InitialValue", vm.Name);
    }

    [Fact]
    public void Set_raises_with_the_correct_property_name_using_CallerMemberName()
    {
        var vm = new TestViewModel();
        var collectedNames = new List<string?>();

        vm.PropertyChanged += (_, e) => collectedNames.Add(e.PropertyName);

        vm.Name = "Test";
        vm.Count = 42;

        Assert.Equal(new[] { nameof(vm.Name), nameof(vm.Count) }, collectedNames);
    }

    [Fact]
    public void Raise_fires_with_the_inferred_property_name()
    {
        var vm = new TestViewModel();
        PropertyChangedEventArgs? received = null;

        vm.PropertyChanged += (_, e) => received = e;

        vm.RaiseManually();

        Assert.NotNull(received);
        Assert.Equal(nameof(vm.Name), received.PropertyName);
    }
}
