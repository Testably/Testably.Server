using System.Net;
using System.Reflection;
using System.Text.Json;
using Serilog;

namespace Testably.Server;

public class Program
{
	public static void Main(string[] args)
	{
		Directory.SetCurrentDirectory(
			Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!);

		var builder = WebApplication.CreateBuilder(args);

		// Add services to the container.
#if DEBUG
		builder.Services.AddHttpClient("Proxied");
#else
		// https://www.ionos.de/hilfe/index.php?id=4426
		var webProxy = new WebProxy("http://winproxy.server.lan:3128");
		builder.Services.AddHttpClient("Proxied")
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
		{
			Proxy = webProxy
		});
#endif
		builder.Services.AddRazorPages();
		builder.Services.AddControllers()
			.AddJsonOptions(o
				=> o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

		builder.Configuration.AddJsonFile("appsettings.Secrets.json", true);
		builder.Host.UseSerilog((context, configuration)
			=> configuration.ReadFrom.Configuration(context.Configuration));

		var app = builder.Build();

		// Configure the HTTP request pipeline.
		if (!app.Environment.IsDevelopment())
		{
			app.UseExceptionHandler("/Error");
			// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
			app.UseHsts();
		}

		app.UseHttpsRedirection();
		app.UseStaticFiles();

		app.UseRouting();

		app.UseAuthorization();

		app.MapRazorPages();
		app.MapControllers();

		app.Run();
	}
}