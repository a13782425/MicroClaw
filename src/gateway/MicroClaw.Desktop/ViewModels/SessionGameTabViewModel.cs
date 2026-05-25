using MicroClaw.Desktop.Games;
using Microsoft.Xna.Framework;

namespace MicroClaw.Desktop.ViewModels;

[PageRoute(PageRouteDefine.RouteSessionGame)]
public class SessionGameTabViewModel : RouteViewModelBase
{
    public Game PongGame { get; } = new PongGame();
}
