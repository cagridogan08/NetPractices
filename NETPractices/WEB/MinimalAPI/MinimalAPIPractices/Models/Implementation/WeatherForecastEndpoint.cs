using Microsoft.AspNetCore.Mvc;
using MinimalAPIPractices.Infastructure.Context;
using MinimalAPIPractices.Models.Abstract;

namespace MinimalAPIPractices.Models.Implementation;

public class WeatherForecastEndpoint : IEndpoint
{
    private static readonly string[] Summaries = new[]
    {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };
    public void RegisterEndpoints(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapGet("/weather", (ILogger<WeatherForecastEndpoint> logger) =>
        {
            logger.LogInformation("Getting weather forecast");
            return Summaries;
        });
    }
}