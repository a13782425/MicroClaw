using MicroClaw.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 游戏世界的服务
/// </summary>
public sealed class GameWorldService : MicroService
{
    private readonly ConcurrentDictionary<string, MicroGameWorld> _worlds = new();


    /// <summary>
    /// 获取所有游戏世界
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<MicroGameWorld> GetWorlds() => _worlds.Values.ToList();

    /// <summary>
    /// 获取一个游戏世界
    /// </summary>
    /// <param name="worldId"></param>
    /// <returns></returns>
    public MicroGameWorld? GetWorld(string worldId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);
        if (_worlds.TryGetValue(worldId, out var world))
            return world;
        return null;
    }

    /// <summary>
    /// 创建一个游戏世界,如果worldId已存在则返回现在的世界
    /// </summary>
    /// <param name="worldId"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    public MicroGameWorld? CreateWorld(string worldId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_worlds.TryGetValue(worldId, out var world))
            return world;
        world = new MicroGameWorld(worldId, name, null);
        if (_worlds.TryAdd(worldId, world))
            return world;
        return null;
    }
}
