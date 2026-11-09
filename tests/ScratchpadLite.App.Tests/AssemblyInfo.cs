using Xunit;

// These tests drive WPF: one process-wide Application, windows with real
// handles, and dispatchers. Running two of those at once makes failures depend
// on timing rather than on the code, so this assembly runs its tests one at a
// time. The suite takes about a second, so there is nothing to win by
// parallelising it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
