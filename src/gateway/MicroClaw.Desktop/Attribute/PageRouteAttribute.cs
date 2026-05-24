namespace MicroClaw.Desktop;

[AttributeUsage(AttributeTargets.Class)]
public class PageRouteAttribute(string route) : Attribute
{
    public string Route { get; } = route;
}