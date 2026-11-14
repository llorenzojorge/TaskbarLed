using TaskbarLed.Core.Sessions;
using static TaskbarLed.Core.Tests.Build;

namespace TaskbarLed.Core.Tests;

public class SessionStoreTests
{
    [Fact]
    public void Snapshot_keeps_first_seen_order_even_after_removals()
    {
        var store = new SessionStore();
        store.Apply(new SessionStarted("a", Source, At(0), null));
        store.Apply(new SessionStarted("b", Source, At(1), null));
        store.Apply(new SessionEnded("a", Source, At(2)));
        store.Apply(new SessionStarted("c", Source, At(3), null));
        store.Apply(new ActivityStarted("b", Source, At(4), "x"));

        Assert.Equal(["b", "c"], store.Snapshot().Select(s => s.Id));
    }

    [Fact]
    public void AcknowledgeCompleted_reports_whether_anything_changed()
    {
        var store = new SessionStore();
        store.Apply(new ResponseCompleted("a", Source, At(0), null));

        Assert.True(store.AcknowledgeCompleted());
        Assert.False(store.AcknowledgeCompleted());
        Assert.Equal(SessionStatus.Idle, store.Snapshot().Single().Status);
    }

    [Fact]
    public void AcknowledgeCompleted_can_be_limited_to_the_sessions_the_user_sees()
    {
        var store = new SessionStore();
        store.Apply(new ResponseCompleted("seen", Source, At(0), null));
        store.Apply(new ResponseCompleted("hidden", Source, At(0), null));

        Assert.True(store.AcknowledgeCompleted(s => s.Id == "seen"));

        Assert.Equal(SessionStatus.Idle, store.Snapshot().Single(s => s.Id == "seen").Status);
        Assert.Equal(SessionStatus.Completed, store.Snapshot().Single(s => s.Id == "hidden").Status);
    }

    [Fact]
    public void PruneStale_drops_old_sessions_but_keeps_ones_waiting_for_the_user()
    {
        var time = new FakeTime(At(0));
        var store = new SessionStore(time);
        store.Apply(new ResponseCompleted("old", Source, At(0), null));
        store.Apply(new AttentionRequested("waiting", Source, At(0), null));
        store.Apply(new PromptSubmitted("fresh", Source, At(3500), null, null));

        time.Now = At(3600);
        Assert.True(store.PruneStale(TimeSpan.FromMinutes(30)));

        Assert.Equal(["waiting", "fresh"], store.Snapshot().Select(s => s.Id));
    }

    [Fact]
    public void Changed_fires_after_each_applied_event()
    {
        var store = new SessionStore();
        var count = 0;
        store.Changed += (_, _) => count++;

        store.ApplyRange([new SessionStarted("a", Source, At(0), null), new SessionEnded("a", Source, At(1))]);

        Assert.Equal(2, count);
    }
}
