using Xunit.Sdk;
using Xunit.v3;

// Every test that starts the application builds a host through WebApplicationFactory. Hosts that are built at the
// same time in one process get in each other's way (the factory finds the host that the application built by
// listening to a process-wide diagnostic source), which makes starting them fail now and then.
[assembly: Parallelization(Mode = ParallelMode.None)]
