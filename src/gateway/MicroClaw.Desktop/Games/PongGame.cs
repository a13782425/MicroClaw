using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MicroClaw.Desktop.Games;

/// <summary>
/// 自动对战 Pong 演示，供 SessionView 中的 MonoGameControl 使用。
/// 所有纹理均程序生成，无需外部资源文件。
/// </summary>
public sealed class PongGame : Game
{
    // ---------- 常量 ----------
    private const int VirtualWidth = 668;
    private const int VirtualHeight = 200;
    private const int PaddleWidth = 10;
    private const int PaddleHeight = 50;
    private const int BallSize = 10;
    private const float PaddleSpeed = 200f;
    private const float BallBaseSpeed = 220f;

    // ---------- 图形 ----------
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _whiteTexture = null!;   // 1×1 白色像素，所有形状复用

    // ---------- 游戏状态 ----------
    private Vector2 _ballPos;
    private Vector2 _ballVel;
    private float _leftPaddleY;
    private float _rightPaddleY;

    public PongGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = VirtualWidth,
            PreferredBackBufferHeight = VirtualHeight,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = false;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // 生成 1×1 白色纹理，用于绘制所有矩形
        _whiteTexture = new Texture2D(GraphicsDevice, 1, 1);
        _whiteTexture.SetData([Color.White]);

        Reset();
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // 移动球
        _ballPos += _ballVel * dt;

        // 上下墙壁反弹
        if (_ballPos.Y <= 0)
        {
            _ballPos.Y = 0;
            _ballVel.Y = Math.Abs(_ballVel.Y);
        }
        else if (_ballPos.Y + BallSize >= VirtualHeight)
        {
            _ballPos.Y = VirtualHeight - BallSize;
            _ballVel.Y = -Math.Abs(_ballVel.Y);
        }

        // AI 追球 —— 挡板中心跟踪球的 Y 轴
        float ballCenterY = _ballPos.Y + BallSize / 2f;

        float leftCenter = _leftPaddleY + PaddleHeight / 2f;
        float leftDiff = ballCenterY - leftCenter;
        float leftMove = Math.Sign(leftDiff) * Math.Min(Math.Abs(leftDiff), PaddleSpeed * dt);
        _leftPaddleY = Math.Clamp(_leftPaddleY + leftMove, 0, VirtualHeight - PaddleHeight);

        float rightCenter = _rightPaddleY + PaddleHeight / 2f;
        float rightDiff = ballCenterY - rightCenter;
        float rightMove = Math.Sign(rightDiff) * Math.Min(Math.Abs(rightDiff), PaddleSpeed * dt);
        _rightPaddleY = Math.Clamp(_rightPaddleY + rightMove, 0, VirtualHeight - PaddleHeight);

        // 与左挡板碰撞
        var leftPaddleRect = new Rectangle(20, (int)_leftPaddleY, PaddleWidth, PaddleHeight);
        var ballRect = new Rectangle((int)_ballPos.X, (int)_ballPos.Y, BallSize, BallSize);
        if (ballRect.Intersects(leftPaddleRect) && _ballVel.X < 0)
        {
            _ballVel.X = Math.Abs(_ballVel.X) * 1.03f;  // 每次碰撞微加速
            _ballVel.Y += (ballCenterY - (_leftPaddleY + PaddleHeight / 2f)) * 1.5f;
            _ballVel = ClampSpeed(_ballVel, 600f);
        }

        // 与右挡板碰撞
        int rightPaddleX = VirtualWidth - 20 - PaddleWidth;
        var rightPaddleRect = new Rectangle(rightPaddleX, (int)_rightPaddleY, PaddleWidth, PaddleHeight);
        if (ballRect.Intersects(rightPaddleRect) && _ballVel.X > 0)
        {
            _ballVel.X = -Math.Abs(_ballVel.X) * 1.03f;
            _ballVel.Y += (ballCenterY - (_rightPaddleY + PaddleHeight / 2f)) * 1.5f;
            _ballVel = ClampSpeed(_ballVel, 600f);
        }

        // 出界（左/右）—— 重置
        if (_ballPos.X < -BallSize || _ballPos.X > VirtualWidth + BallSize)
        {
            Reset();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(18, 18, 18));

        _spriteBatch.Begin();

        // 中线虚线
        int dashHeight = 8;
        int dashGap = 8;
        int centerX = VirtualWidth / 2 - 1;
        for (int y = 0; y < VirtualHeight; y += dashHeight + dashGap)
            _spriteBatch.Draw(_whiteTexture,
                new Rectangle(centerX, y, 2, dashHeight),
                new Color(80, 80, 80));

        // 左挡板
        _spriteBatch.Draw(_whiteTexture,
            new Rectangle(20, (int)_leftPaddleY, PaddleWidth, PaddleHeight),
            Color.White);

        // 右挡板
        _spriteBatch.Draw(_whiteTexture,
            new Rectangle(VirtualWidth - 20 - PaddleWidth, (int)_rightPaddleY, PaddleWidth, PaddleHeight),
            Color.White);

        // 球
        _spriteBatch.Draw(_whiteTexture,
            new Rectangle((int)_ballPos.X, (int)_ballPos.Y, BallSize, BallSize),
            Color.White);

        _spriteBatch.End();

        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _whiteTexture?.Dispose();
            _spriteBatch?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------- 辅助方法 ----------

    private void Reset()
    {
        _ballPos = new Vector2(VirtualWidth / 2f - BallSize / 2f, VirtualHeight / 2f - BallSize / 2f);

        // 随机初始方向（左右各半，垂直角度 ±30°）
        var rng = Random.Shared;
        float angle = (float)(rng.NextDouble() * Math.PI / 3 - Math.PI / 6); // -30° ~ +30°
        float dirX = rng.Next(2) == 0 ? 1 : -1;
        _ballVel = new Vector2(
            dirX * BallBaseSpeed * (float)Math.Cos(angle),
            BallBaseSpeed * (float)Math.Sin(angle));

        _leftPaddleY = VirtualHeight / 2f - PaddleHeight / 2f;
        _rightPaddleY = VirtualHeight / 2f - PaddleHeight / 2f;
    }

    private static Vector2 ClampSpeed(Vector2 vel, float maxSpeed)
    {
        float speed = vel.Length();
        return speed > maxSpeed ? vel * (maxSpeed / speed) : vel;
    }
}
