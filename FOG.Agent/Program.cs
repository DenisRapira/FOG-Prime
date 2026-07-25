using FOG.Agent;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.UseUtcTimestamp = true;
    options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled;
});
builder.Services.AddWindowsService(options => options.ServiceName = "FOG Prime Agent");
builder.Services.AddOptions<AgentOptions>();
builder.Services.AddSingleton<AgentStateStore>();
builder.Services.AddSingleton<RuntimeIntegrityVerifier>();
builder.Services.AddSingleton<ProfileCatalog>();
builder.Services.AddSingleton<ConnectionHealthChecker>();
builder.Services.AddSingleton<ICompatibilitySignalSource, SystemCompatibilitySignalSource>();
builder.Services.AddSingleton<CompatibilityGuard>();
builder.Services.AddSingleton<INetworkAdapterSource, SystemNetworkAdapterSource>();
builder.Services.AddSingleton<ExternalTunnelDetector>();
builder.Services.AddSingleton<DriverConflictBackoff>();
builder.Services.AddSingleton<ProfileRotation>();
builder.Services.AddSingleton<WinDivertServiceManager>();
builder.Services.AddSingleton<EngineSupervisor>();
builder.Services.AddSingleton<AgentPipeServer>();
builder.Services.AddHostedService<Worker>();

try
{
    using var host = builder.Build();
    _ = host.Services.GetRequiredService<ProfileCatalog>();
    await host.Services.GetRequiredService<RuntimeIntegrityVerifier>()
        .VerifyAsync(CancellationToken.None);
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FOG Prime Agent could not start: {ex.GetType().Name}");
    Environment.ExitCode = 1;
}
