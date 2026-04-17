using FluentAssertions;
using MicroClaw.Core;
using MicroClaw.Core.Logging;

namespace MicroClaw.Tests.Core;

public sealed class MicroLoggerTests
{
    [Fact]
    public void NullMicroLoggerFactory_ReturnsNullLogger_WithAllLevelsDisabled()
    {
        IMicroLogger logger = NullMicroLoggerFactory.Instance.CreateLogger("Any");

        logger.Should().BeSameAs(NullMicroLogger.Instance);
        foreach (MicroLogLevel level in Enum.GetValues<MicroLogLevel>())
            logger.IsEnabled(level).Should().BeFalse();

        logger.BeginScope("state").Should().BeNull();
    }

    [Fact]
    public void Factory_SetToNull_ResetsToNullFactory()
    {
        IMicroLoggerFactory previous = MicroLogger.Factory;
        try
        {
            MicroLogger.Factory = new RecordingMicroLoggerFactory();
            MicroLogger.Factory.Should().BeOfType<RecordingMicroLoggerFactory>();

            MicroLogger.Factory = null!;
            MicroLogger.Factory.Should().BeSameAs(NullMicroLoggerFactory.Instance);
        }
        finally
        {
            MicroLogger.Factory = previous;
        }
    }

    [Fact]
    public void MicroLifeCycleLogger_UsesMostDerivedRuntimeTypeAsCategory()
    {
        IMicroLoggerFactory previous = MicroLogger.Factory;
        var factory = new RecordingMicroLoggerFactory();

        try
        {
            MicroLogger.Factory = factory;

            var sut = new DerivedLifeCycleProbe();
            sut.InvokeLogger();

            factory.RequestedCategoryNames.Should().ContainSingle()
                .Which.Should().Be(typeof(DerivedLifeCycleProbe).FullName);
        }
        finally
        {
            MicroLogger.Factory = previous;
        }
    }

    [Fact]
    public void LogInformation_ForwardsToUnderlyingLogWithInformationLevel()
    {
        var logger = new RecordingMicroLogger();

        logger.LogInformation("hello {Name}", "world");

        logger.Entries.Should().ContainSingle();
        LogEntry entry = logger.Entries[0];
        entry.Level.Should().Be(MicroLogLevel.Information);
        entry.Exception.Should().BeNull();
        entry.MessageTemplate.Should().Be("hello {Name}");
        entry.Args.Should().Equal("world");
    }

    [Fact]
    public void LogError_WithException_ForwardsExceptionAndErrorLevel()
    {
        var logger = new RecordingMicroLogger();
        var error = new InvalidOperationException("boom");

        logger.LogError(error, "failed for {Id}", 42);

        LogEntry entry = logger.Entries.Single();
        entry.Level.Should().Be(MicroLogLevel.Error);
        entry.Exception.Should().BeSameAs(error);
        entry.MessageTemplate.Should().Be("failed for {Id}");
        entry.Args.Should().Equal(42);
    }

    [Fact]
    public void CreateLoggerGeneric_DelegatesToTypeOverload()
    {
        var factory = new RecordingMicroLoggerFactory();

        _ = factory.CreateLogger<DerivedLifeCycleProbe>();

        factory.RequestedCategoryNames.Should().ContainSingle()
            .Which.Should().Be(typeof(DerivedLifeCycleProbe).FullName);
    }

    private sealed class DerivedLifeCycleProbe : MicroLifeCycle<MicroObject>
    {
        public IMicroLogger InvokeLogger() => (IMicroLogger)typeof(MicroLifeCycle<MicroObject>)
            .GetProperty("Logger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(this)!;
    }

    private sealed record LogEntry(
        MicroLogLevel Level,
        Exception? Exception,
        string MessageTemplate,
        object?[] Args);

    private sealed class RecordingMicroLoggerFactory : IMicroLoggerFactory
    {
        public List<string> RequestedCategoryNames { get; } = new();

        public IMicroLogger CreateLogger(string categoryName)
        {
            RequestedCategoryNames.Add(categoryName);
            return new RecordingMicroLogger();
        }

        public IMicroLogger CreateLogger(Type categoryType) => CreateLogger(categoryType.FullName ?? categoryType.Name);
    }

    private sealed class RecordingMicroLogger : IMicroLogger
    {
        public List<LogEntry> Entries { get; } = new();

        public bool IsEnabled(MicroLogLevel level) => true;

        public void Log(MicroLogLevel level, Exception? exception, string messageTemplate, params object?[] args)
            => Entries.Add(new LogEntry(level, exception, messageTemplate, args));

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
