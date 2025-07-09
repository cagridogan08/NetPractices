// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MockDatabaseTester;

Console.WriteLine("Hello, World!");
var builder = Host.CreateApplicationBuilder();
builder.Services.AddLogging();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<LoginHandler>();
builder.Services.AddHostedService<DatabaseService>();
var app = builder.Build();
app.Run();