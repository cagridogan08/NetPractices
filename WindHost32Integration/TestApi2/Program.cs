using System.Runtime.CompilerServices;
using TestApi;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapGet("/notification", () =>
{
    async IAsyncEnumerable<NotificationEvent> GetNotificationsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(4000, cancellationToken);

            yield return new NotificationEvent(Guid.CreateVersion7(), Guid.NewGuid().ToString());
        }
    }
});
app.UseAuthorization();

app.MapControllers();

app.Run();
