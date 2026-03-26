using FluentAssertions;
using MicroClaw.Agent.Middleware;

namespace MicroClaw.Tests.Agents;

public sealed class MaxDepthMiddlewareTests
{
    // ── CurrentDepth / ExecuteAsync ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_IncreasesDepthToOneInsideLambda()
    {
        int capturedDepth = 0;

        await MaxDepthMiddleware.ExecuteAsync<object?>(async () =>
        {
            capturedDepth = MaxDepthMiddleware.CurrentDepth;
            await Task.CompletedTask;
            return null;
        });

        capturedDepth.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_RestoresDepthToZeroAfterCompletion()
    {
        await MaxDepthMiddleware.ExecuteAsync<object?>(
            () => Task.FromResult<object?>(null));

        MaxDepthMiddleware.CurrentDepth.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_RestoresDepthToZeroAfterException()
    {
        Func<Task> act = () => MaxDepthMiddleware.ExecuteAsync<object?>(async () =>
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("simulated error");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        MaxDepthMiddleware.CurrentDepth.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_NestedCalls_AccumulatesDepthCorrectly()
    {
        var depths = new List<int>();

        await MaxDepthMiddleware.ExecuteAsync(async () =>
        {
            depths.Add(MaxDepthMiddleware.CurrentDepth); // 1

            await MaxDepthMiddleware.ExecuteAsync(async () =>
            {
                depths.Add(MaxDepthMiddleware.CurrentDepth); // 2
                await Task.CompletedTask;
                return 0;
            });

            return 0;
        });

        depths.Should().Equal(1, 2);
        MaxDepthMiddleware.CurrentDepth.Should().Be(0);
    }

    [Fact]
    public void ExecuteAsync_ThrowsWhenMaxDepthExceeded()
    {
        Func<Task> act = () =>
            MaxDepthMiddleware.ExecuteAsync(async () =>
            {
                // depth = 1, maxDepth = 1, 内层调用 depth = 2 > maxDepth → 抛出
                await MaxDepthMiddleware.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    return 0;
                }, maxDepth: 1);

                return 0;
            }, maxDepth: 1);

        act.Should().ThrowAsync<MaxDepthExceededException>()
            .Where(ex => ex.MaxDepth == 1 && ex.CurrentDepth == 2);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsValueFromOperation()
    {
        var result = await MaxDepthMiddleware.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            return 42;
        });

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_WithDefaultMaxDepth_AllowsUpToFiveLevels()
    {
        // 验证默认上限（DefaultMaxDepth = 5）允许 5 层嵌套
        int finalDepth = await Recurse(0, MaxDepthMiddleware.DefaultMaxDepth);

        finalDepth.Should().Be(MaxDepthMiddleware.DefaultMaxDepth);

        static async Task<int> Recurse(int current, int target)
        {
            if (current >= target) return current;
            return await MaxDepthMiddleware.ExecuteAsync(
                () => Recurse(current + 1, target));
        }
    }

    // ── CheckDepth ─────────────────────────────────────────────────────────

    [Fact]
    public void CheckDepth_AtZeroDepth_DoesNotThrow()
    {
        var act = () => MaxDepthMiddleware.CheckDepth(maxDepth: 5);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task CheckDepth_WhenDepthEqualsMax_ThrowsException()
    {
        MaxDepthExceededException? caught = null;

        try
        {
            await MaxDepthMiddleware.ExecuteAsync(async () =>
            {
                // depth = 1, maxDepth = 1 → （1 >= 1）→ 抛出 MaxDepthExceededException(2, 1)
                MaxDepthMiddleware.CheckDepth(maxDepth: 1);
                await Task.CompletedTask;
                return 0;
            }, maxDepth: 1);
        }
        catch (MaxDepthExceededException ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        caught!.CurrentDepth.Should().Be(2);
        caught.MaxDepth.Should().Be(1);
    }

    [Fact]
    public void CheckDepth_BelowLimit_DoesNotThrow()
    {
        var act = () => MaxDepthMiddleware.CheckDepth(maxDepth: MaxDepthMiddleware.DefaultMaxDepth);

        act.Should().NotThrow();
    }

    // ── MaxDepthExceededException ──────────────────────────────────────────

    [Fact]
    public void MaxDepthExceededException_ExposesCorrectProperties()
    {
        var ex = new MaxDepthExceededException(6, 5);

        ex.CurrentDepth.Should().Be(6);
        ex.MaxDepth.Should().Be(5);
    }

    [Fact]
    public void MaxDepthExceededException_MessageContainsBothDepthValues()
    {
        var ex = new MaxDepthExceededException(6, 5);

        ex.Message.Should().Contain("6").And.Contain("5");
    }

    [Fact]
    public void MaxDepthExceededException_IsInvalidOperationException()
    {
        var ex = new MaxDepthExceededException(3, 2);

        ex.Should().BeAssignableTo<InvalidOperationException>();
    }
}
