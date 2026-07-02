using Xunit;

// The host reads GRAPH_PROVIDER / EDDA_AUTH_TOKEN / MCP_SERVER_ENABLED eagerly from environment variables
// (before Build), so the factory sets process-global env vars while a host is constructed. Running the
// hosting tests serially keeps that env mutation from leaking between concurrently-built hosts.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
