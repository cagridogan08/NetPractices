using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RandomDataGenerator.FieldOptions;
using RandomDataGenerator.Randomizers;
using TestApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddScoped<IRandomizerString>(_ => RandomizerFactory.GetRandomizer(new FieldOptionsTextWords()));
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials();
    });
});

builder.Services.AddSwaggerGen();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapGet("/notifications", ([FromServices] IRandomizerString randomizer, CancellationToken token) =>
{
    return Results.Stream(async stream =>
    {
        await foreach (var notification in GetNotificationsAsync(randomizer, token))
        {
            var eventData = $"event: notification\ndata: {System.Text.Json.JsonSerializer.Serialize(notification)}\n\n";
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(eventData));
            await stream.FlushAsync();
        }
    }, "text/event-stream");

    async IAsyncEnumerable<NotificationEvent> GetNotificationsAsync(IRandomizerString randomizerString, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(4000, cancellationToken);
            yield return new NotificationEvent(Guid.NewGuid(), randomizerString.Generate()!);
        }
    }
});
//app.UseHttpsRedirection();
app.UseCors("CorsPolicy");


app.MapControllers();

await app.RunAsync("http://localhost:5000");
