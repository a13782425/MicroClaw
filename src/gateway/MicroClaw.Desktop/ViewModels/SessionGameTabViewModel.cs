using MicroClaw.Desktop.Games;
using Microsoft.Xna.Framework;

namespace MicroClaw.Desktop;

public class SessionGameTabViewModel : ViewModelBase
{
    public Game PongGame { get; } = new PongGame();
}
