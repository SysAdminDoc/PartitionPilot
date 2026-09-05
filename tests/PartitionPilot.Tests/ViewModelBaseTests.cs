using System.ComponentModel;

namespace PartitionPilot.Tests;

public class ViewModelBaseTests
{
    [Fact]
    public void SetProperty_RaisesChangeNotificationWithTheCallersPropertyName()
    {
        var vm = new Probe();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Name = "first";

        Assert.Equal(["Name"], raised);
    }

    [Fact]
    public void SetProperty_ReportsWhetherAnythingChanged()
    {
        // View models branch on this return value to decide whether to do follow-up work such as
        // re-evaluating a command, so an assignment of the same value must not report a change.
        var vm = new Probe { Name = "same" };

        Assert.False(vm.AssignName("same"));
        Assert.True(vm.AssignName("different"));
    }

    [Fact]
    public void SetProperty_DoesNotRaiseWhenTheValueIsUnchanged()
    {
        var vm = new Probe { Name = "same" };
        var raised = 0;
        vm.PropertyChanged += (_, _) => raised++;

        vm.Name = "same";

        Assert.Equal(0, raised);
    }

    [Fact]
    public void SetProperty_TreatsNullTransitionsAsChanges()
    {
        var vm = new Probe();

        Assert.False(vm.AssignName(null));   // already null
        Assert.True(vm.AssignName("value"));
        Assert.True(vm.AssignName(null));
    }

    [Fact]
    public void SetProperty_ComparesByValueForValueTypes()
    {
        var vm = new Probe();

        Assert.True(vm.AssignCount(5));
        Assert.False(vm.AssignCount(5));
        Assert.True(vm.AssignCount(6));
    }

    [Fact]
    public void SetProperty_UsesEqualityComparerRatherThanReferenceEquality()
    {
        // Two distinct instances that compare equal must not be reported as a change.
        var vm = new Probe();

        Assert.True(vm.AssignName(new string(['a', 'b'])));
        Assert.False(vm.AssignName(new string(['a', 'b'])));
    }

    [Fact]
    public void OnPropertyChanged_IsSafeWithNoSubscribers()
    {
        new Probe().Raise();
    }

    private sealed class Probe : ViewModelBase
    {
        private string? _name;
        private int _count;

        public string? Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public bool AssignName(string? value) => SetProperty(ref _name, value, nameof(Name));
        public bool AssignCount(int value) => SetProperty(ref _count, value, nameof(Count));
        public int Count => _count;
        public void Raise() => OnPropertyChanged(nameof(Name));
    }
}
