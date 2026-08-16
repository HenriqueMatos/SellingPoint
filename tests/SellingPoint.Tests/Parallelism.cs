// The tests run one at a time.
//
// TempDb.Dispose calls SqliteConnection.ClearAllPools(), which is global: it
// empties the pool for every database in the process, not only its own. With
// xunit running test classes in parallel, one class finishing could pull a
// connection out from under another that was still using it.
//
// This was chased rather than assumed. A full-suite run failed twice on
// EndToEndTests.A_full_night_at_the_till and could never be reproduced - not in
// eight consecutive full runs, not in six runs of that test alone, not under a
// concurrent rebuild. So the mechanism above is a plausible explanation and not
// a proven one.
//
// Turning parallelism off costs a second of wall clock on a suite that runs in
// under one. That is a cheap way to remove a whole class of doubt, and a flaky
// suite costs far more than a second: it teaches people to re-run red builds
// instead of reading them.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
