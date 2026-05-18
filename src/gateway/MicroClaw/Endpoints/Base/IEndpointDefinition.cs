namespace MicroClaw.Endpoints;
public interface IEndpointDefinition
{
    string Route { get; }
    string[] Methods { get; }
    string[] Tags { get; }
    Delegate Handler { get; }
}
internal static class EndpointDefinitionExtensions
{
    public static IEndpointRouteBuilder RegisterEndpoint(this IEndpointRouteBuilder route, IEndpointDefinition definition)
    {
        var routeHandler = route.MapMethods(definition.Route, definition.Methods, definition.Handler);
        if (definition.Tags.Length > 0)
        {
            routeHandler.WithTags(definition.Tags);
        }
        return route;
    }
}