using FOG.Agent;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "FOG Prime Agent");
builder.Services.AddSingleton<IOptions<AgentOptions>>(Options.Create(new AgentOptions()));
builder.Services.AddSingleton<AgentStateStore>();
builder.Services.AddSingleton<RuntimeIntegrityVerifier>();
builder.Services.AddSingleton<ProfileCatalog>();
builder.Services.AddSingleton<ConnectionHealthChecker>();
builder.Services.AddSingleton<EngineSupervisor>();
builder.Services.AddSingleton<AgentPipeServer>();
builder.Services.AddHostedService<Worker>();

try
{
    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FOG Prime Agent could not start: {ex.GetType().Name}");
    Environment.ExitCode = 1;
}
