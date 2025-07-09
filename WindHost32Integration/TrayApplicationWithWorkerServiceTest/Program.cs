using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

var builder = Host.CreateDefaultBuilder()
    .ConfigureHostConfiguration(builder =>
    {
        builder.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
        builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
        builder.AddEnvironmentVariables();
    })
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseKestrel(p =>
        {
            p.Limits.MaxRequestBodySize = 10 * 1024 * 1024 * 30; // Set max request body size to 300 MB
        });
        webBuilder.ConfigureAppConfiguration((_, config) =>
        {
            config.Build();
            var url = $"http://0.0.0.0:50000";
            if (!string.IsNullOrEmpty(url))
            {
                webBuilder.UseUrls(url); // Set the URL
            }
        });

        webBuilder.ConfigureServices(services =>
        {
            services.AddHttpContextAccessor();
            services.AddSingleton<TrayApplicationWithWorkerServiceTest.Services.TrayService>();
            //services.AddSingleton<ProcessLauncherService>();
            //services.AddSingleton<StatusService>();
            //services.AddSingleton<SystemTrayManager>();
            //services.AddSingleton<Services.ProjectService>();
            services.AddControllers();
            services.AddSignalR();
        });

        webBuilder.Configure(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        });
    })
    .ConfigureLogging((_, loggingBuilder) =>
    {
        loggingBuilder.ClearProviders();
        loggingBuilder.AddConsole();
        loggingBuilder.SetMinimumLevel(LogLevel.Information);
    });

var app = builder.Build();
app.Services.GetRequiredService<TrayApplicationWithWorkerServiceTest.Services.TrayService>().Start();
await app.RunAsync();