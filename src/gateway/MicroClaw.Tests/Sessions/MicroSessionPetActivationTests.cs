using System.Reflection;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Pet;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Storage;
using MicroClaw.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace MicroClaw.Tests.Sessions;

public sealed class MicroSessionPetActivationTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HandleMessageAsync_WhenSessionIsApproved_ActivatesPetBeforeDispatch()
    {
        InitializeConfig();
        (IServiceProvider serviceProvider, PetService petService) = CreateServiceProvider();
        var entityConfig = new SessionEntityConfig
        {
            Id = "session-pet-activation",
            Title = "T",
            ProviderId = "p",
            ChannelType = "web",
            ChannelId = "web",
            CreatedAtMs = 1,
        };
        MicroSession session = await MicroSession.CreateAsync(entityConfig, serviceProvider, CancellationToken.None);
        session.Approve("approved");

        IPet pet = Substitute.For<IPet>();
        pet.HandleMessageAsync(Arg.Any<IReadOnlyList<SessionMessage>>(), Arg.Any<CancellationToken>(), Arg.Any<string>())
            .Returns(_ => EmptyStream());
        petService.ActivateAsync(Arg.Is<IMicroSession>(candidate => ReferenceEquals(candidate, session)), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IPet?>(pet));

        await foreach (StreamItem _ in session.HandleMessageAsync("hello", null, "web", CancellationToken.None))
        {
        }

        await petService.Received(1).ActivateAsync(session, Arg.Any<CancellationToken>());
        pet.Received(1).HandleMessageAsync(Arg.Any<IReadOnlyList<SessionMessage>>(), Arg.Any<CancellationToken>(), "web");
    }

    public void Dispose()
    {
        ResetMicroClawConfig();
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private (IServiceProvider ServiceProvider, PetService PetService) CreateServiceProvider()
    {
        PetStateStore stateStore = new();
        IEmotionStore emotionStore = Substitute.For<IEmotionStore>();
        ILogger<PetService> logger = Substitute.For<ILogger<PetService>>();

        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(PetStateStore)).Returns(stateStore);
        serviceProvider.GetService(typeof(IEmotionStore)).Returns(emotionStore);
        serviceProvider.GetService(typeof(ILogger<PetService>)).Returns(logger);

        PetService petService = Substitute.For<PetService>(serviceProvider);
        petService.CreateOrLoadAsync(Arg.Any<IMicroSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IPet?>(null));
        serviceProvider.GetService(typeof(PetService)).Returns(petService);

        return (serviceProvider, petService);
    }

    private void InitializeConfig()
    {
        ResetMicroClawConfig();
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(Path.Combine(_tempRoot, "config"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "workspace", "sessions"));
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", _tempRoot);

        IConfiguration configuration = new ConfigurationBuilder().Build();
        MicroClawConfig.Initialize(configuration, Path.Combine(_tempRoot, "config"));
    }

    private static void ResetMicroClawConfig()
    {
        MethodInfo? resetMethod = typeof(MicroClawConfig).GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic);
        resetMethod?.Invoke(null, null);
    }

    private static async IAsyncEnumerable<StreamItem> EmptyStream()
    {
        await Task.Yield();
        yield break;
    }
}