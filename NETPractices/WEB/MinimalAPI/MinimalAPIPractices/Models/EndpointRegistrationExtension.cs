using MinimalAPIPractices.Models.Abstract;

namespace MinimalAPIPractices.Models;

public static class EndpointRegistrationExtension
{
    public static void RegisterAllEndpoints(this WebApplication app)
    {
        var endpointDefinitions = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(p => p.GetTypes())
            .Where(type =>
                typeof(IEndpoint).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
            .Select(Activator.CreateInstance)
            .Cast<IEndpoint>();

        foreach (var endpointDefinition in endpointDefinitions)
        {
            // WebApplication implements IEndpointRouteBuilder
            endpointDefinition.RegisterEndpoints(app);
        }
    }


}