using Xunit;

// The font tests are integration tests over shared global state: AppPaths
// points them all at one data/font_cache folder, and FontPreparer writes cache
// files and prunes unreferenced ones there. Run two of those at once and a test
// can see files appear and vanish underneath it - which showed up as a single
// failure that only ever happened in a full solution run and never when the
// project was run on its own.
//
// The whole suite takes about a second, so there is nothing to win by
// parallelising it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
