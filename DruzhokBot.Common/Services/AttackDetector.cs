using System.Collections.Concurrent;
using DruzhokBot.Domain.DTO;
using DruzhokBot.Domain.Interfaces;

namespace DruzhokBot.Common.Services;

public class AttackDetector : IAttackDetector
{
    public static readonly TimeSpan WindowSize = TimeSpan.FromSeconds(100);
    public const int TriggerCount = 3;
    public static readonly TimeSpan InitialDuration = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan ExtensionThreshold = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ExtensionAmount = TimeSpan.FromMinutes(5);

    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _pollInterval;

    private readonly object _lock = new();
    private readonly ConcurrentDictionary<long, AngryModeState> _active = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<AngryModeState>> _completions = new();
    private readonly ConcurrentDictionary<long, LinkedList<DateTime>> _windows = new();

    public AttackDetector(Func<DateTime>? clock = null, TimeSpan? pollInterval = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    public bool RegisterJoin(long chatId)
    {
        lock (_lock)
        {
            var window = _windows.GetOrAdd(chatId, _ => new LinkedList<DateTime>());
            var now = _clock();
            window.AddLast(now);

            var cutoff = now - WindowSize;
            while (window.First is not null && window.First.Value < cutoff)
                window.RemoveFirst();

            var angryModeActive = _active.TryGetValue(chatId, out var state)
                && now < state.EndTime;

            return window.Count >= TriggerCount && !angryModeActive;
        }
    }

    public bool IsAngryModeActive(long chatId)
    {
        lock (_lock)
        {
            return _active.TryGetValue(chatId, out var state) && _clock() < state.EndTime;
        }
    }

    public Task<AngryModeState> StartAngryMode(long chatId)
    {
        var tcs = new TaskCompletionSource<AngryModeState>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            var now = _clock();
            var state = new AngryModeState(chatId, now, now + InitialDuration, BannedCount: 0);
            if (!_active.TryAdd(chatId, state))
                throw new InvalidOperationException($"Angry mode already active for chat {chatId}.");
            _completions[chatId] = tcs;
        }

        _ = RunLifetime(chatId);
        return tcs.Task;
    }

    public AngryModeState? RegisterBanInAngryMode(long chatId)
    {
        lock (_lock)
        {
            if (!_active.TryGetValue(chatId, out var current))
                return null;

            var now = _clock();
            if (now >= current.EndTime)
                return null; // expired — lifetime task has not cleaned up yet

            var newEndTime = current.EndTime;
            if (current.EndTime - now < ExtensionThreshold)
                newEndTime = current.EndTime + ExtensionAmount;

            var updated = current with
            {
                BannedCount = current.BannedCount + 1,
                EndTime = newEndTime
            };
            _active[chatId] = updated;
            return updated;
        }
    }

    /// <summary>
    /// Polls until <see cref="AngryModeState.EndTime"/> for the chat is reached, then removes
    /// the state and resolves the completion source with the final snapshot. Re-reads the state
    /// each iteration so extensions applied by <see cref="RegisterBanInAngryMode"/> are picked up.
    /// </summary>
    private async Task RunLifetime(long chatId)
    {
        try
        {
            while (true)
            {
                AngryModeState? state;
                DateTime now;
                lock (_lock)
                {
                    now = _clock();
                    _active.TryGetValue(chatId, out state);
                }

                if (state is null || now >= state.EndTime)
                    break;

                var remaining = state.EndTime - now;
                var delay = remaining < _pollInterval ? remaining : _pollInterval;
                await Task.Delay(delay);
            }

            AngryModeState? finalState;
            lock (_lock)
            {
                _active.TryRemove(chatId, out finalState);
                _completions.TryRemove(chatId, out var tcs);
                if (tcs is not null && finalState is not null)
                    tcs.TrySetResult(finalState);
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _active.TryRemove(chatId, out _);
                _completions.TryRemove(chatId, out var tcs);
                tcs?.TrySetException(ex);
            }
        }
    }
}
