using Xunit;

// Six classes - AppResourceOrderTests, MainWindowClosingTests, MainWindowGeometryTests,
// MainWindowListStateTests, MainWindowSelectionTests, ThemeFollowingTests - all mutate the one
// shared Application.Current.Resources.MergedDictionaries and call ThemeManager.Apply on Wpf.cs's
// single STA dispatcher thread. Each individual Wpf.Run call is serialised on that dispatcher,
// but a class's own SEQUENCE of Wpf.Run calls is not atomic against another class's: xunit runs
// test classes in parallel collections by default (no [Collection] groups any of the six
// together), so class A's Apply(Dark) can be followed by class B's Apply(Light) slipping in
// before class A's next Wpf.Run call reads the resources it expects - an intermittent,
// several-classes-fail-together flake (~1 run in 6), not specific to any one class's own logic.
//
// DisableTestParallelization, not a shared [CollectionDefinition] naming just the six: the
// six are read from Application.Current - process-wide, ambient state - not from an object
// reference an explicit collection could scope test isolation around, so grouping only those six
// still leaves every OTHER class free to run in parallel with them and does not remove the race.
// The whole assembly runs in about a second either way, so serialising everything costs nothing
// and is the version least likely to bit-rot back into the same bug when new classes are added
// that happen to touch Application.Current or ThemeManager.Apply themselves.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
