using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Testably.Server.Tests;

public class TestFactory : WebApplicationFactory<Program>
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory? _httpClientFactory;

    public TestFactory(IHttpClientFactory? httpClientFactory = null,
        Action<Dictionary<string, string>>? configuration = null)
    {
        _httpClientFactory = httpClientFactory;
        var builder = new ConfigurationBuilder();
        var inMemoryConfiguration = new Dictionary<string, string>();
        configuration?.Invoke(inMemoryConfiguration);
        _configuration = builder.AddInMemoryCollection(inMemoryConfiguration!).Build();
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            if (_httpClientFactory != null)
            {
                var descriptor = services
                    .SingleOrDefault(d => d.ServiceType == _httpClientFactory.GetType());
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddSingleton(_httpClientFactory);
            }

            services.AddSingleton(_configuration);
        });
    }

    /// <summary>
    ///     https://stackoverflow.com/a/69825605
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseContentRoot(Directory.GetCurrentDirectory());
        return base.CreateHost(builder);
    }
}