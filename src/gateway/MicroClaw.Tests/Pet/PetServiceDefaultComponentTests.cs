using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Agent;
using MicroClaw.Agent.Memory;
using MicroClaw.Agent.Restorers;
using MicroClaw.Configuration;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

public sealed class PetServiceDefaultComponentTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateOrLoadAsync_WhenCreatingPet_AttachesDefaultComponentSkeletons()
    {
        InitializeConfig();
        PetService service = CreatePetService();
        IMicroSession session = CreateSession("pet-create");

        MicroPet pet = (MicroPet)(await service.CreateOrLoadAsync(session, CancellationToken.None))!;

        AssertDefaultComponents(pet);
    }

    [Fact]
    public async Task CreateOrLoadAsync_WhenLoadingExistingPet_AttachesDefaultComponentSkeletons()
    {
        InitializeConfig();
        PetService service = CreatePetService();
        IMicroSession session = CreateSession("pet-load");
        await service.CreateOrLoadAsync(session, CancellationToken.None);

        MicroPet loaded = (MicroPet)(await service.CreateOrLoadAsync(session, CancellationToken.None))!;

        AssertDefaultComponents(loaded);
    }

    [Fact]
    public async Task CreateOrLoadAsync_WhenApprovedAndEngineRunning_RegistersPetAndActivatesComponents()
    {
        InitializeConfig();
        await using HostedPetService hosted = await CreateStartedPetServiceAsync();
        IMicroSession session = CreateSession("pet-engine-active");

        MicroPet pet = (MicroPet)(await hosted.Service.CreateOrLoadAsync(session, CancellationToken.None))!;

        pet.Engine.Should().BeSameAs(hosted.Engine);
        pet.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
        hosted.Engine.Objects.Should().Contain(pet);
        pet.Components.Should().OnlyContain(static component => component.LifeCycleState == MicroLifeCycleState.Active);
        pet.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task CreateOrLoadAsync_WhenNotApproved_DoesNotRegisterPetWithEngine()
    {
        InitializeConfig();
        await using HostedPetService hosted = await CreateStartedPetServiceAsync();
        IMicroSession session = CreateSession("pet-engine-disabled", approved: false);

        MicroPet pet = (MicroPet)(await hosted.Service.CreateOrLoadAsync(session, CancellationToken.None))!;

        pet.Engine.Should().BeNull();
        pet.LifeCycleState.Should().Be(MicroLifeCycleState.Detached);
        hosted.Engine.Objects.Should().NotContain(pet);
        pet.Components.Should().OnlyContain(static component => component.LifeCycleState == MicroLifeCycleState.Attached);
        pet.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ActivateAsync_WhenExistingPetBecomesApproved_RegistersSameRuntimePet()
    {
        InitializeConfig();
        await using HostedPetService hosted = await CreateStartedPetServiceAsync();
        bool approved = false;
        IPet? attachedPet = null;
        IMicroSession session = CreateSession("pet-engine-approve", () => approved, () => attachedPet);

        attachedPet = await hosted.Service.CreateOrLoadAsync(session, CancellationToken.None);
        approved = true;

        IPet? activatedPet = await hosted.Service.ActivateAsync(session, CancellationToken.None);

        activatedPet.Should().BeSameAs(attachedPet);
        MicroPet pet = (MicroPet)activatedPet!;
        pet.Engine.Should().BeSameAs(hosted.Engine);
        pet.LifeCycleState.Should().Be(MicroLifeCycleState.Active);
        pet.Components.Should().OnlyContain(static component => component.LifeCycleState == MicroLifeCycleState.Active);
        pet.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task DeactivateAsync_WhenPetIsRegistered_UnregistersPetAndDisablesRuntimePet()
    {
        InitializeConfig();
        await using HostedPetService hosted = await CreateStartedPetServiceAsync();
        IPet? attachedPet = null;
        IMicroSession session = CreateSession("pet-engine-disable", () => true, () => attachedPet);
        attachedPet = await hosted.Service.CreateOrLoadAsync(session, CancellationToken.None);
        MicroPet pet = (MicroPet)attachedPet!;

        IPet? deactivatedPet = await hosted.Service.DeactivateAsync(session, CancellationToken.None);

        deactivatedPet.Should().BeSameAs(attachedPet);
        pet.Engine.Should().BeNull();
        pet.LifeCycleState.Should().Be(MicroLifeCycleState.Detached);
        hosted.Engine.Objects.Should().NotContain(pet);
        pet.Components.Should().OnlyContain(static component => component.LifeCycleState == MicroLifeCycleState.Attached);
        pet.IsEnabled.Should().BeFalse();
    }

    public void Dispose()
    {
        ResetMicroClawConfig();
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static void AssertDefaultComponents(MicroPet pet)
    {
        Type[] expectedOrder =
        [
            typeof(PetStateComponent),
            typeof(PetEmotionComponent),
            typeof(PetPromptComponent),
            typeof(PetRateLimitComponent),
            typeof(PetKnowledgeComponent),
            typeof(PetNotificationComponent),
            typeof(PetDecisionComponent),
            typeof(PetDispatchLifecycleComponent),
            typeof(PetObservationComponent),
            typeof(PetActionComponent),
            typeof(PetHeartbeatComponent),
        ];

        pet.Components.Should().HaveCount(11);
        pet.Components.Select(static component => component.GetType()).Should().Equal(expectedOrder);
        pet.GetComponent<PetStateComponent>().Should().NotBeNull();
        pet.GetComponent<PetEmotionComponent>().Should().NotBeNull();
        pet.GetComponent<PetPromptComponent>().Should().NotBeNull();
        pet.GetComponent<PetRateLimitComponent>().Should().NotBeNull();
        pet.GetComponent<PetKnowledgeComponent>().Should().NotBeNull();
        pet.GetComponent<PetNotificationComponent>().Should().NotBeNull();
        pet.GetComponent<PetDecisionComponent>().Should().NotBeNull();
        pet.GetComponent<PetDispatchLifecycleComponent>().Should().NotBeNull();
        pet.GetComponent<PetObservationComponent>().Should().NotBeNull();
        pet.GetComponent<PetActionComponent>().Should().NotBeNull();
        pet.GetComponent<PetHeartbeatComponent>().Should().NotBeNull();
    }

    private async Task<HostedPetService> CreateStartedPetServiceAsync()
    {
        ServiceProvider serviceProvider = CreateServiceProvider();
        PetService petService = serviceProvider.GetRequiredService<PetService>();
        MicroEngine engine = new(serviceProvider, [petService]);
        await engine.StartAsync(CancellationToken.None);
        return new HostedPetService(serviceProvider, engine, petService);
    }

    private PetService CreatePetService()
        => CreateServiceProvider().GetRequiredService<PetService>();

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<PetStateStore>();
        services.AddSingleton<PetRateLimiter>();
        services.AddSingleton<IEmotionStore>(_ => CreateEmotionStore());
        services.AddSingleton<IEmotionRuleEngine>(_ => Substitute.For<IEmotionRuleEngine>());
        services.AddSingleton<IEmotionBehaviorMapper>(_ => Substitute.For<IEmotionBehaviorMapper>());
        services.AddSingleton<IProviderRouter>(_ => Substitute.For<IProviderRouter>());
        services.AddSingleton<ProviderService>();
        services.AddSingleton<PetModelSelector>();
        services.AddSingleton<PetDecisionEngine>();
        services.AddSingleton<PetSessionObserver>(sp => new PetSessionObserver(_tempRoot, sp.GetRequiredService<ILogger<PetSessionObserver>>()));
        services.AddSingleton<PetSelfAwarenessReportBuilder>();
        services.AddSingleton<ISessionService>(_ => Substitute.For<ISessionService>());
        services.AddSingleton<MicroAgentService>();
        services.AddSingleton<IMicroAgentService>(sp => sp.GetRequiredService<MicroAgentService>());
        services.AddSingleton<ToolCollector>(sp => new ToolCollector([], new McpServerConfigStore(), sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<SkillToolFactory>(_ => Uninitialized<SkillToolFactory>());
        services.AddSingleton<ChatContentRestorerService>(_ => Uninitialized<ChatContentRestorerService>());
        services.AddSingleton<IContextOverflowSummarizer>(_ => Substitute.For<IContextOverflowSummarizer>());
        services.AddSingleton<PetService>();

        return services.BuildServiceProvider();
    }

    private static IEmotionStore CreateEmotionStore()
    {
        IEmotionStore emotionStore = Substitute.For<IEmotionStore>();
        emotionStore.GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmotionState.Default));
        return emotionStore;
    }

    private static IMicroSession CreateSession(string sessionId, bool approved = true)
        => CreateSession(sessionId, () => approved, () => null);

    private static IMicroSession CreateSession(string sessionId, Func<bool> isApproved, Func<IPet?> getPet)
    {
        IMicroSession session = Substitute.For<IMicroSession>();
        session.Id.Returns(sessionId);
        session.IsApproved.Returns(_ => isApproved());
        session.Pet.Returns(_ => getPet());
        return session;
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

    private static T Uninitialized<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class HostedPetService(ServiceProvider serviceProvider, MicroEngine engine, PetService service) : IAsyncDisposable
    {
        public MicroEngine Engine { get; } = engine;

        public PetService Service { get; } = service;

        public async ValueTask DisposeAsync()
        {
            await Engine.DisposeAsync();
            await serviceProvider.DisposeAsync();
        }
    }
}