using FOG.Agent;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "FOG Prime Agent");
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddSingleton<AgentStateStore>();
builder.Services.AddSingleton<RuntimeIntegrityVerifier>();
builder.Services.AddSingleton<ProfileCatalog>();
builder.Services.AddSingleton<ConnectionHealthChecker>();
builder.Services.AddSingleton<EngineSupervisor>();
builder.Services.AddSingleton<AgentPipeServer>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
