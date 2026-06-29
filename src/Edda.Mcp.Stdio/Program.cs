using Edda.Hosting.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Stdio MCP host: exposes the same allow-listed AKG/TDK tools as the web host, but over the
// stdio transport so local MCP clients (e.g. Claude Desktop) can spawn it as a child process.
var builder = Host.CreateApplicationBuilder(args);

// stdout carries the JSON-RPC protocol stream — all logging MUST go to stderr instead.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddEddaCore(builder.Configuration);

builder.Services.AddMcpServer().WithStdioServerTransport();
builder.Services.AddEddaMcpHandlers();

var host = builder.Build();
await host.RunAsync();
