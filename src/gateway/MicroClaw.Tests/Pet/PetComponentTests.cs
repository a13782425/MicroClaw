using System.Runtime.CompilerServices;
using FluentAssertions;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Agent;
using MicroClaw.Agent.Memory;
using MicroClaw.Agent.Restorers;
using MicroClaw.Core;
using MicroClaw.Pet;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Observer;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using MicroClaw.Skills;
using MicroClaw.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

public sealed class PetComponentTests
{
    [Fact]
    public async Task Helpers_WhenAttachedToMicroPet_ReturnPetSessionAndPetComponents()
    {
        MicroPet pet = CreatePet("pet-session");
        ProbePetComponent component = await pet.AddComponentAsync<ProbePetComponent>();
        RequiredPetComponent required = await pet.AddComponentAsync<RequiredPetComponent>();

        component.Pet.Should().BeSameAs(pet);
        component.GetRequiredPet().Should().BeSameAs(pet);
        component.MicroSession.Should().BeSameAs(pet.MicroSession);
        component.SessionId.Should().Be("pet-session");
        component.GetPetComponent<RequiredPetComponent>().Should().BeSameAs(required);
        component.GetRequiredPetComponent<RequiredPetComponent>().Should().BeSameAs(required);
    }

    [Fact]
    public async Task RequiredPetHelpers_WhenAttachedToPlainMicroObject_ThrowClearError()
    {
        var host = new MicroObject();
        ProbePetComponent component = await host.AddComponentAsync<ProbePetComponent>();
        await host.AddComponentAsync<RequiredPetComponent>();

        component.Pet.Should().BeNull();
        component.Invoking(static current => current.GetRequiredPet())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*requires a MicroPet host*");
        component.Invoking(static current => _ = current.MicroSession)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*requires a MicroPet host*");
        component.Invoking(static current => _ = current.SessionId)
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*requires a MicroPet host*");
        component.Invoking(static current => current.GetPetComponent<RequiredPetComponent>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*requires a MicroPet host*");
        component.Invoking(static current => current.GetRequiredPetComponent<RequiredPetComponent>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*requires a MicroPet host*");
    }

    [Fact]
    public async Task GetRequiredPetComponent_WhenMissing_ThrowsWithComponentNames()
    {
        MicroPet pet = CreatePet("pet-session");
        ProbePetComponent component = await pet.AddComponentAsync<ProbePetComponent>();

        component.Invoking(static current => current.GetRequiredPetComponent<RequiredPetComponent>())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*RequiredPetComponent*ProbePetComponent*");
    }

    private static MicroPet CreatePet(string sessionId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<PetStateStore>();
        services.AddSingleton<PetRateLimiter>();
        services.AddSingleton<IEmotionStore>(_ => Substitute.For<IEmotionStore>());
        services.AddSingleton<IEmotionRuleEngine>(_ => Substitute.For<IEmotionRuleEngine>());
        services.AddSingleton<IEmotionBehaviorMapper>(_ => Substitute.For<IEmotionBehaviorMapper>());
        services.AddSingleton<IProviderRouter>(_ => Substitute.For<IProviderRouter>());
        services.AddSingleton<ProviderService>();
        services.AddSingleton<PetModelSelector>();
        services.AddSingleton<PetDecisionEngine>();
        services.AddSingleton<PetSessionObserver>(sp => new PetSessionObserver(Path.GetTempPath(), sp.GetRequiredService<ILogger<PetSessionObserver>>()));
        services.AddSingleton<PetSelfAwarenessReportBuilder>();
        services.AddSingleton<ISessionService>(_ => Substitute.For<ISessionService>());
        services.AddSingleton<MicroAgentService>();
        services.AddSingleton<IMicroAgentService>(sp => sp.GetRequiredService<MicroAgentService>());
        services.AddSingleton<ToolCollector>(sp => new ToolCollector([], new McpServerConfigStore(), sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<SkillToolFactory>(_ => Uninitialized<SkillToolFactory>());
        services.AddSingleton<ChatContentRestorerService>(_ => Uninitialized<ChatContentRestorerService>());
        services.AddSingleton<IContextOverflowSummarizer>(_ => Substitute.For<IContextOverflowSummarizer>());

        ServiceProvider provider = services.BuildServiceProvider();
        IMicroSession session = Substitute.For<IMicroSession>();
        session.Id.Returns(sessionId);

        return new MicroPet(
            provider,
            session,
            new PetState { SessionId = sessionId },
            new PetConfig(),
            EmotionState.Default,
            PetContextState.Active);
    }

    private static T Uninitialized<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class ProbePetComponent : PetComponent;

    private sealed class RequiredPetComponent : PetComponent;
}