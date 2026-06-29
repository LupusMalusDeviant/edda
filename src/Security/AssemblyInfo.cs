using System.Runtime.CompilerServices;

// Allow Agent to create TaintTracker instances (per-turn construction in AgentRuntime).
[assembly: InternalsVisibleTo("Edda.Agent")]

// Allow Security.Tests to access TaintTracker internals for white-box testing.
[assembly: InternalsVisibleTo("Edda.Security.Tests")]
