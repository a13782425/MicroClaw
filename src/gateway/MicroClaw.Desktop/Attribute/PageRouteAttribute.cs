namespace MicroClaw.Desktop;
[AttributeUsage(AttributeTargets.Class)]
public class PageRouteAttribute(string route) : Attribute
{
    public string Route { get; } = route;
}
internal static class PageRouteDefine
{
    public const string RouteMicroSession = "/sessions/";
    public const string RouteMicroAgents = "/micro/agents";
    public const string RouteMicroSkills = "/micro/skills";
    public const string RouteMicroMcp = "/micro/mcp";
    public const string RouteMicroTools = "/micro/tools";
    public const string RouteMicroPlugins = "/micro/plugins";
    public const string RouteSettingsProviders = "/settings/providers";
    public const string RouteSettingsUsage = "/settings/usage";
    public const string RouteSettingsAbout = "/settings/about";
    
    public const string RouteSessionChat = "session/chat";
    public const string RouteSessionGame = "session/game";
}