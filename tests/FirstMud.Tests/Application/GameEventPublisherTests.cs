using FirstMud.Application.Events;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FirstMud.Tests.Application;

public class GameEventPublisherTests
{
    private record TestEvent(string Tag) : IIntegrationEvent;

    private sealed class RecordingSubscriber : IGameEventSubscriber<TestEvent>
    {
        public List<(Guid PlayerId, TestEvent Evt)> Received { get; } = new();

        public Task HandleAsync(Guid playerId, TestEvent @event, CancellationToken ct)
        {
            Received.Add((playerId, @event));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSubscriber : IGameEventSubscriber<TestEvent>
    {
        public Task HandleAsync(Guid playerId, TestEvent @event, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task PublishAsync_FansOutToAllSubscribers()
    {
        var first = new RecordingSubscriber();
        var second = new RecordingSubscriber();
        var services = new ServiceCollection()
            .AddSingleton<IGameEventSubscriber<TestEvent>>(first)
            .AddSingleton<IGameEventSubscriber<TestEvent>>(second)
            .BuildServiceProvider();

        var publisher = new GameEventPublisher(services, NullLogger<GameEventPublisher>.Instance);
        var playerId = Guid.NewGuid();

        await publisher.PublishAsync(playerId, new TestEvent("hello"));

        first.Received.Should().ContainSingle().Which.Should().Be((playerId, new TestEvent("hello")));
        second.Received.Should().ContainSingle();
    }

    [Fact]
    public async Task PublishAsync_IsolatesSubscriberFailures()
    {
        var recording = new RecordingSubscriber();
        var services = new ServiceCollection()
            .AddSingleton<IGameEventSubscriber<TestEvent>>(new ThrowingSubscriber())
            .AddSingleton<IGameEventSubscriber<TestEvent>>(recording)
            .BuildServiceProvider();

        var publisher = new GameEventPublisher(services, NullLogger<GameEventPublisher>.Instance);

        var act = async () => await publisher.PublishAsync(Guid.NewGuid(), new TestEvent("x"));

        await act.Should().NotThrowAsync("a faulty subscriber must not abort siblings");
        recording.Received.Should().HaveCount(1);
    }

    [Fact]
    public async Task PublishAsync_NoSubscribers_IsNoOp()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var publisher = new GameEventPublisher(services, NullLogger<GameEventPublisher>.Instance);

        var act = async () => await publisher.PublishAsync(Guid.NewGuid(), new TestEvent("x"));

        await act.Should().NotThrowAsync();
    }
}
