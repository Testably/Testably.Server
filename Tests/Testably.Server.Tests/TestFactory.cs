using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Testably.Server.Tests;

public class TestFactory : WebApplicationFactory<Program>
{
	private readonly IHttpClientFactory? _httpClientFactory;

	public TestFactory(IHttpClientFactory? httpClientFactory = null)
	{
		_httpClientFactory = httpClientFactory;
	}

	/// <inheritdoc />
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		base.ConfigureWebHost(builder);
		builder.ConfigureTestServices(services =>
		{
			if (_httpClientFactory != null)
			{
				var descriptor =
					services.SingleOrDefault(d => d.ServiceType == _httpClientFactory.GetType());
				if (descriptor != null)
				{
					services.Remove(descriptor);
				}

				services.AddSingleton(_httpClientFactory);
			}
		});
	}
}