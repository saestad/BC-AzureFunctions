using AnalyticsAPI.Sync.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddHttpClient();
        services.AddSingleton<TokenService>();
        services.AddSingleton<BCApiService>();
        services.AddSingleton<SqlService>();
    })
    .Build();

host.Run();