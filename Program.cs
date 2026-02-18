using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AnalyticsAPI.Sync.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddSingleton<TokenService>();
        services.AddSingleton<BCApiService>();
        services.AddSingleton<SqlService>();
        services.AddSingleton<TenantService>();  
    })
    .Build();

host.Run();