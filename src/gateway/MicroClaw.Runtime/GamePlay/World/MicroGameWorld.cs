using MicroClaw.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace MicroClaw.Runtime.GamePlay;

public sealed partial class MicroGameWorld : MicroGameObject, IMicroTickable
{
    public MicroGameWorld(string id, string name, MicroGameDatabase database) : base(id, id)
    {
        Name = name;
        Status = MicroGameWorldStatus.Active;
        Database = database;
    }

    public MicroGameWorldStatus Status { get; private set; }

    public MicroGameDatabase Database { get; private set; }

    public void Pause() => Status = MicroGameWorldStatus.Paused;
    public void Resume() => Status = MicroGameWorldStatus.Active;
    public void Archive() => Status = MicroGameWorldStatus.Archived;

    protected override async ValueTask OnDestroyAsync(CancellationToken cancellationToken = default)
    {
        await Database.DisposeAsync();
    }
}
//实现
partial class MicroGameWorld
{

    public override string Name
    {
        get => base.Name;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("江湖名不能为空。", nameof(value));
            base.Name = value;
        }
    }

    /// <summary>
    /// 晨钟暮鼓节律频率（P4 才真正用到，先用最低帧避免空转开销）。
    /// </summary>
    uint IMicroTickable.Frame => 1;

    /// <summary>
    /// 顺序执行晨钟暮鼓步骤。
    /// </summary>
    public ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
