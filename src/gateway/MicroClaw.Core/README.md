# MicroClaw.Core 使用指南

MicroClaw.Core 是 gateway 的运行时内核，提供统一的生命周期、服务/对象/组件三层抽象，以及可选的按帧调度（Tick）。

本文面向在 MicroClaw 内写业务服务和领域对象的开发者。

## 1. 概念速查

| 抽象 | 基类/接口 | 定位 | 宿主是谁 |
| --- | --- | --- | --- |
| `MicroEngine` | `sealed class` | 运行时根，管理所有服务和对象的启停、Tick | — |
| `MicroService` | `abstract class : MicroLifeCycle<MicroEngine>` | 单例后台服务（DB、缓存、渠道等），按 `Order` 启停 | `MicroEngine` |
| `MicroObject` | `class : MicroLifeCycle<MicroEngine>` | 领域实体（如 Session、Pet），支持挂组件 | `MicroEngine` |
| `MicroComponent` | `abstract class : MicroLifeCycle<MicroObject>` | 可装配到 `MicroObject` 上的功能单元 | `MicroObject` |
| `IMicroTickable` | `interface` | 可选：需要被引擎按帧调度的节点 | — |
| `MicroLifeCycle<THost>` | 所有上述抽象的基类 | 统一状态机 + 钩子 | — |
| `IMicroLogger` / `MicroLogger.Factory` | `Logging/` | 内部日志抽象 + 工厂环境入口 | — |

## 2. 生命周期

所有节点共用一条状态机（见 `MicroLifeCycle<THost>`）：

```
Detached ──Attach──▶ Attached ──Initialize──▶ Initialized ──Activate──▶ Active
                                                              ◀──Deactivate──┘
               ◀──Detach (自动回滚)──────────────────────────────────────┘

任意状态 ──DisposeAsync──▶ Disposed
```

覆写钩子（全部 `protected virtual ValueTask OnXxxAsync(CancellationToken)`）：

| 钩子 | 触发时机 | 典型用途 |
| --- | --- | --- |
| `OnAttachedAsync` | 刚挂到 Host 上 | 缓存 Host 引用、浅准备 |
| `OnInitializedAsync` | 进入 Initialized | 目录准备、资源申请、读取配置 |
| `OnActivatedAsync` | 进入 Active | 订阅事件、开始接受请求 |
| `OnDeactivatedAsync` | 离开 Active | 停止订阅、flush 缓冲 |
| `OnUninitializedAsync` | 离开 Initialized | 释放"可重开"的资源 |
| `OnDetachedAsync` | 从 Host 上分离 | 清缓存引用 |
| `OnDisposedAsync` | 最终释放 | 释放 SemaphoreSlim、FileHandle 等 |

**重要约定：**

- 钩子里**不要**再发起会反过来推进自己状态的调用（如在 `OnInitializedAsync` 里 `await this.ActivateAsync()`）。引擎会统一驱动。
- 钩子抛异常会触发回滚：已前进的节点会被反向调回前一状态，异常会原样或以 `AggregateException` 重新抛出。
- 使用 `Logger`（基类属性）写日志；基类还会在状态切换时自动写 Debug trace（分类名 = 运行时类型）。

## 3. MicroEngine：起停与注册

### 起停

```csharp
MicroEngine engine = new(serviceProvider, [serviceA, serviceB]);
await engine.StartAsync(ct);
// ... 运行 ...
await engine.StopAsync(ct);
```

或异步工厂（推荐在异步代码路径上用）：

```csharp
MicroEngine engine = await MicroEngine.CreateAsync(serviceProvider, [serviceA, serviceB], ct);
```

状态机：`Stopped → Starting → Running → Stopping → Stopped`；起停失败会变 `Faulted`，需要再调一次 `StopAsync` 清理。

### 注册 / 注销

运行时动态挂载（可在任何时候调用，但**不能在正在执行的 Start/Stop/Tick/其他 mutation 的同一调用链里**递归调用——引擎有重入检测）：

```csharp
await engine.RegisterObjectAsync(microObject, ct);   // 引擎 Running 时会立即激活
await engine.UnregisterObjectAsync(microObject, ct); // 会先 drain Tick，再停用、分离
await engine.RegisterServiceAsync(service, ct);
await engine.UnregisterServiceAsync(service, ct);
```

所有注册/注销失败时引擎会自动回滚。

### 手动 Tick（测试）

```csharp
await engine.StartAsync();
await engine.TickAsync(TimeSpan.FromMilliseconds(16));
await engine.StopAsync();
```

如果希望引擎自己转 tick 循环，启动后调 `engine.RunAsync(ct)`（通常由 `MicroEngineHostedService` 替你做，见第 8 节）。

### 访问 DI

在引擎或任何挂接到引擎的节点内部，可用：

```csharp
T? foo = engine.GetService<T>();
T required = engine.GetRequiredService<T>();
```

## 4. MicroService：写一个后台服务

```csharp
public sealed class MyService : MicroService
{
    public override int Order => 20; // 数值越小越先启动；停止时逆序

    protected override async ValueTask StartAsync(CancellationToken ct = default)
    {
        // 到这里，依赖的、Order 更小的服务已经在 Running。
    }

    protected override async ValueTask StopAsync(CancellationToken ct = default)
    {
        // 尽力清理；抛异常会被收集到 AggregateException，但不会阻止其他服务停机。
    }
}
```

**`Order` 约定**（当前项目惯例，可根据需要调整）：

- `0-19`：基础设施（Agent 仓库、插件加载器等）
- `20-49`：核心业务服务（Session、Channel 等）
- `50+`：外围集成（webhook 桥接、定时任务等）

**构造约定**：推荐构造注入 `IServiceProvider` 或具体依赖，但**不要在构造函数里做 IO 或解析强依赖**；惰性解析放 `StartAsync` 里（或直接通过 `Engine!.GetRequiredService<T>()`）。

**ActivationFailed**：如果 `StartAsync` 抛异常，基类会按补偿语义调一次 `StopAsync`（`OnDeactivatedAsync`），此时 `ActivationFailed == true`；子类可据此跳过"未启动成功就不该清理"的分支。

## 5. MicroObject + MicroComponent：组件模式

`MicroObject` 是领域实体的基类，它自己的生命周期由引擎或宿主代码驱动。组件挂在对象上，跟随对象的生命周期一起前进。

### 对象

```csharp
public class MySession : MicroObject
{
    // 构造函数中做纯数据初始化，不做 IO。
    // 组件通过 AddComponentAsync 显式挂接。
}
```

### 组件

```csharp
public sealed class MessagesComponent : MicroComponent
{
    protected override ValueTask OnInitializedAsync(CancellationToken ct = default)
    {
        // 需要 Host 时用 GetRequiredHost()；类型已是 MicroObject，做 pattern match 即可。
        MySession session = (MySession)GetRequiredHost();
        Directory.CreateDirectory(GetSessionDir(session.Id));
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposedAsync(CancellationToken ct = default)
    {
        // 释放持有的 SemaphoreSlim / FileStream 等。
        return ValueTask.CompletedTask;
    }

    public void AddMessage(Message m) { /* ... */ }
}
```

### 挂接与查询

```csharp
MessagesComponent messages = await session.AddComponentAsync<MessagesComponent>(ct);
// 或挂已有实例：
await session.AddComponentAsync(new MessagesComponent(), ct);

MessagesComponent? maybe = session.GetComponent<MessagesComponent>();
bool ok = session.TryGetComponent<MessagesComponent>(out var got);

await session.RemoveComponentAsync<MessagesComponent>(ct);
```

**规则**：

- 同一 `MicroObject` 上**每个 runtime 类型最多一个组件实例**。
- `AddComponentAsync` 会根据当前 Object 的状态把组件**同步推进到同一状态**：Object 已 `Active` → 组件挂上就立刻 `Initialized → Active`。
- Object 进入 `Active` 时会按挂载顺序激活组件；退出时逆序停用。
- Object `Dispose` 时会级联 `Dispose` 所有组件。
- 组件支持按基类/接口查询，但如果有多个组件都能赋值给该接口则 `GetComponent<I>` 会抛异常。

### 不走引擎的局部使用

`MicroObject` 不是必须注册到引擎。如果只是想借用"对象 + 组件"这个组合模式，可以直接 `new MyObject()`，然后 `AddComponentAsync` 手动挂组件——此时组件停在 `Attached` 态，`OnInitializedAsync` 等钩子**不会**自动触发。需要的话手动调 `component.InitializeAsync()`（`internal`，同 assembly / `InternalsVisibleTo` 下可见）。

这是 Session 目前的做法：`SessionService` 本地持有 `MicroSession`，不注册给引擎，只用组件模式做功能组装。

## 6. IMicroTickable：按帧调度

给需要定时推进的对象/服务实现 `IMicroTickable`：

```csharp
public sealed class MyTicker : MicroObject, IMicroTickable
{
    public ValueTask TickAsync(TimeSpan delta, CancellationToken ct = default)
    {
        // 逐帧工作；与其他 Tickable 并不保证线程隔离，但保证不与同一节点的生命周期转换并发。
        return ValueTask.CompletedTask;
    }
}
```

- 只有**实现接口且处于 `Active`** 的节点会被调度。
- `MicroService` 的 Tick 顺序由 `Order` 决定；`MicroObject` 的 Tick 顺序是 0（在服务之后）。
- 节点停用/注销时调度器会先 drain 当前帧再解除注册，不会出现"停掉还被调一下"的竞态。

## 7. 日志接入

`MicroClaw.Core` 自己不依赖 `Microsoft.Extensions.Logging`，走的是 `IMicroLogger` 抽象。宿主在启动时替换工厂即可把日志导出去：

```csharp
MicroClaw.Core.Logging.MicroLogger.Factory = myAdapterFactory; // 实现 IMicroLoggerFactory
```

默认是 `NullMicroLoggerFactory`（不输出）。`MicroLifeCycle` / `MicroEngine` 内部写的是 `Debug` 级别 trace，仅当工厂提供的 logger `IsEnabled(Debug)` 才会真正走一次字符串拼接——热路径安全。

## 8. DI 接线（宿主侧）

在 `MicroClaw` 项目里已经准备好两个扩展方法，正常 ASP.NET Host 接线如下：

```csharp
// 注册引擎 + HostedService（它会驱动引擎 Start/Run/Stop 跟随主机生命周期）
builder.Services.AddMicroEngine();

// 注册一个 MicroService：会同时以具体类型和 MicroService 基类注册到 DI，
// 便于 MicroEngine 构造时从 IEnumerable<MicroService> 收集。
builder.Services.AddMicroService<MySessionService>();
builder.Services.MapAs<ISessionService, MySessionService>(); // 需要接口暴露时再 MapAs
```

`MicroEngineHostedService`（见 `src/gateway/MicroClaw/Services/MicroEngineHostedService.cs`）会：

1. `StartAsync` 时先 `engine.StartAsync()`，再启动自身 `BackgroundService`。
2. `ExecuteAsync` 里运行 `engine.RunAsync()` 的 Tick 主循环。
3. `StopAsync` 时先取消主循环，再 `engine.StopAsync()`，5 秒硬超时。

`MicroObject` 目前的约定是**不**直接注册到 DI / 引擎——它们通常由某个 `MicroService` 拥有并在 `StartAsync` 里自己 new 出来、自己 `AddComponentAsync`、自己持有。如果你确需一个常驻实体由引擎管理，可以在 service 的 `StartAsync` **之外**的时机（比如 HostedService 接续钩子、或引擎 Running 之后的某个业务请求）调用 `engine.RegisterObjectAsync`。

## 9. 线程模型与重入

- 每个生命周期节点有一个实例级 `SemaphoreSlim` 转换门禁，保证同一节点不会并发推进状态。
- 同一异步调用链里允许重入：通过 `AsyncLocal` 的 scope 识别"同一流内的嵌套调用"不再重复 Wait。
- `MicroEngine` 有一个全局 `_executionGate`：`Start` / `Stop` / `Tick` / `Register*` / `Unregister*` 互斥。**不要**在 `MicroService.StartAsync` / `OnActivatedAsync` 里调 `Engine.RegisterObjectAsync`，会被 `ThrowIfReentered()` 拒绝。动态注册应在引擎 `Running` 之后、从引擎调用链外部发起（例如处理 HTTP 请求时）。
- 异常策略：所有停机/回滚路径都是"尽力而为 + 收集错误 + 统一抛出"，不会因为单个节点 fail 阻断其他节点清理。单错用 `ExceptionDispatchInfo` 原样抛；多错走 `AggregateException`。

## 10. 常见反模式 / 踩坑点

- **在构造函数里做 IO 或解析依赖**：构造顺序由 DI 决定，拿不到完整依赖图。放 `StartAsync` / `OnInitializedAsync`。
- **在钩子里阻塞 `Wait()` / `Result`**：会死锁转换门。一律 `await`。
- **在 `OnDeactivatedAsync` 里假设"一定启动成功过"**：启动失败时基类会补偿调这里，检查 `ActivationFailed` 属性。
- **跨 Host 复用 Lifecycle 节点**：基类会拒绝（`A lifecycle node can only belong to one host at a time.`）。Dispose 后再重新挂载也不行——Disposed 是终态。
- **在引擎 `Running` 的同步回调里再调 `Register*`**：重入保护会抛。从业务请求（控制器/Hub）里调没问题。
- **忘了 `IMicroTickable` 需要 `Active` 才被调度**：对象处于 `Initialized` 不会 tick；调度器按每帧的 `Active` 节点集合刷新。

## 11. 最小可运行示例

```csharp
public sealed class GreeterService : MicroService, IMicroTickable
{
    private int _ticks;

    public override int Order => 10;

    protected override ValueTask StartAsync(CancellationToken ct = default)
    {
        Logger.Log(MicroLogLevel.Info, null, "Greeter starting.");
        return ValueTask.CompletedTask;
    }

    public ValueTask TickAsync(TimeSpan delta, CancellationToken ct = default)
    {
        _ticks++;
        return ValueTask.CompletedTask;
    }

    protected override ValueTask StopAsync(CancellationToken ct = default)
    {
        Logger.Log(MicroLogLevel.Info, null, "Greeter stopped after {Ticks} ticks.", _ticks);
        return ValueTask.CompletedTask;
    }
}

// 宿主侧
var engine = new MicroEngine(serviceProvider, [new GreeterService()]);
await engine.StartAsync();
await engine.TickAsync(TimeSpan.FromMilliseconds(16));
await engine.StopAsync();
```

---

更多参考：

- `src/gateway/MicroClaw.Tests/Core/MicroEngineTests.cs`：引擎行为的系统级测试样例。
- `src/gateway/MicroClaw.Tests/Core/MicroObjectTests.cs`：对象/组件行为的样例。
- `src/gateway/MicroClaw/Sessions/`：一个不走引擎注册、只借用"对象 + 组件"模式的实战例子（`MicroSession` + `SessionMessagesComponent`）。
