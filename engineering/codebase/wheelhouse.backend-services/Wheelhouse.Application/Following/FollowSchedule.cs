namespace Wheelhouse.Application.Following;

/// <summary>Decides when a follower's pass reads the runner, so an idle console starts no runner process: at once after
/// the console submits work (<see cref="Wake"/>), then when the runner says its next look is due while work is
/// unfinished, else once per idle interval — a safety pass that catches work started outside the console. Held for the
/// process; Wheelhouse runs as one instance.</summary>
/// <remarks>A pass runs on its job's fixed delay and asks <see cref="TryBegin"/> first; a pass that is not due reads
/// nothing. The first pass after start is due, so a restarted console learns what is still in flight.</remarks>
public abstract class FollowSchedule(TimeProvider time)
{
    /// <summary>How much earlier than the named time a pass may read: passes are timed from the end of the one before,
    /// which the runner's own clock cannot see.</summary>
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(1);

    private readonly Lock _lock = new();
    private DateTimeOffset _dueAt = DateTimeOffset.MinValue;
    private bool _open = true;
    private long _wakes;

    /// <summary>Work was just submitted: the next pass reads the runner, even one already reading.</summary>
    public void Wake()
    {
        lock (_lock)
        {
            _wakes++;
            _dueAt = DateTimeOffset.MinValue;
        }
    }

    /// <summary>Starts a pass when one is due.</summary>
    /// <param name="ticket">What <see cref="Settle"/> or <see cref="Missed"/> takes once the read returns.</param>
    /// <returns>Whether this pass reads the runner.</returns>
    public bool TryBegin(out long ticket)
    {
        lock (_lock)
        {
            ticket = _wakes;
            return time.GetUtcNow() + Slack >= _dueAt;
        }
    }

    /// <summary>Records a read. While work is unfinished the next pass is due when the runner named, at most
    /// <paramref name="idle"/> away and at once when it named no time; otherwise after <paramref name="idle"/>. A wake
    /// since <paramref name="ticket"/> keeps the next pass due, since the read may have missed that work.</summary>
    /// <param name="ticket">The ticket <see cref="TryBegin"/> gave this pass.</param>
    /// <param name="open">Whether the runner holds unfinished work.</param>
    /// <param name="next">When the runner's next look is due, if it said.</param>
    /// <param name="idle">The safety pass's interval.</param>
    public void Settle(long ticket, bool open, TimeSpan? next, TimeSpan idle)
    {
        lock (_lock)
        {
            _open = open;
            if (_wakes != ticket)
                return;
            var wait = open ? (next is { } named ? Clamp(named, idle) : TimeSpan.Zero) : idle;
            _dueAt = time.GetUtcNow() + wait;
        }
    }

    /// <summary>Records a read that failed: the next pass reads again while work was unfinished, as the follower always
    /// did, else after <paramref name="idle"/>.</summary>
    /// <param name="ticket">The ticket <see cref="TryBegin"/> gave this pass.</param>
    /// <param name="idle">The safety pass's interval.</param>
    public void Missed(long ticket, TimeSpan idle)
    {
        lock (_lock)
        {
            if (_wakes != ticket)
                return;
            _dueAt = _open ? DateTimeOffset.MinValue : time.GetUtcNow() + idle;
        }
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan max) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > max ? max : value;
}
