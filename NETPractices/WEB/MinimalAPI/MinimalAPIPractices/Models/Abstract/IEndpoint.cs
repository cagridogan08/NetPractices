namespace MinimalAPIPractices.Models.Abstract;

public interface IEndpoint
{
    void RegisterEndpoints(IEndpointRouteBuilder endpointRouteBuilder);
}