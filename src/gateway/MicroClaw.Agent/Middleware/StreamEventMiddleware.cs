using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Gateway.Contracts.Streaming;
using MicroClaw.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Threading.Channels;

namespace MicroClaw.Agent.Middleware;

/// <summary>
/// 工具调用事件中间件工厂（AF 函数调用层）。
/// 通过 <see cref="FunctionInvocationDelegatingAgentBuilderExtensions.Use"/> 注册，
/// 在每次工具调用前后向 <see cref="ChannelWriter{StreamItem}"/> 写入 <see cref="ToolCallItem"/> 和 <see cref="ToolResultItem"/>。
/// <para>
/// Phase 1 中该逻辑已直接内嵌在 <see cref="AgentFactory"/> 的 <c>FunctionInvoker</c> 中，
/// 本文件保留 AF Builder 扩展模式的参考实现，供 Phase 2 迁移时使用。
/// </para>
/// </summary>
public static class StreamEventMiddleware
{
    /// <summary>
    /// 创建 AF 函数调用中间件委托，用于 <c>agent.AsBuilder().Use(middleware).Build()</c>。
    /// </summary>
    public static Func<AIAgent, FunctionInvocationContext,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>,
        CancellationToken, ValueTask<object?>>
        Create(ChannelWriter<StreamItem> eventWriter)
    {
        return async (AIAgent agent, FunctionInvocationContext ctx,
            Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
            CancellationToken ct) =>
        {
            string callId = ctx.CallContent?.CallId ?? ctx.Function.Name;
            IDictionary<string, object?>? args = ctx.Arguments?.ToDictionary(k => k.Key, v => v.Value);
            string? visibility = SkillToolProvider.InternalToolNames.Contains(ctx.Function.Name)
                ? MessageVisibility.LlmOnly
                : null;

            // ① 工具调用前 — 写入 ToolCallItem
            await eventWriter.WriteAsync(new ToolCallItem(callId, ctx.Function.Name, args) { Visibility = visibility }, ct);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool success = true;
            object? result = null;
            try
            {
                result = await next(ctx, ct);
            }
            catch (Exception ex)
            {
                success = false;
                result = $"Error: {ex.Message}";
                throw;
            }
            finally
            {
                sw.Stop();
                string resultText = result switch
                {
                    string s => s,
                    null => string.Empty,
                    _ => System.Text.Json.JsonSerializer.Serialize(result)
                };
                // ② 工具调用后 — 写入 ToolResultItem
                await eventWriter.WriteAsync(
                    new ToolResultItem(callId, ctx.Function.Name, resultText, success, sw.ElapsedMilliseconds) { Visibility = visibility },
                    CancellationToken.None);
            }

            return result;
        };
    }
}
