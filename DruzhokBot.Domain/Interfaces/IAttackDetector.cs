using DruzhokBot.Domain.DTO;

namespace DruzhokBot.Domain.Interfaces;

public interface IAttackDetector
{
    /// <summary>
    /// Records a join in the chat's sliding window. Returns true iff the window
    /// has reached the trigger threshold AND angry mode is not already active.
    /// </summary>
    bool RegisterJoin(long chatId);

    /// <summary>
    /// Returns true iff an active angry-mode session currently exists for this chat
    /// and has not yet expired.
    /// </summary>
    bool IsAngryModeActive(long chatId);

    /// <summary>
    /// Begins a new angry-mode session for the chat and returns a task that
    /// completes with the final <see cref="AngryModeState"/> when the session
    /// expires (after all extensions). Callers must not invoke this when
    /// angry mode is already active for the same chat.
    /// </summary>
    Task<AngryModeState> StartAngryMode(long chatId);

    /// <summary>
    /// Records a permaban that just happened inside angry mode. Increments the
    /// ban count, extends the end time by 45 seconds if less than 60 seconds
    /// remain, and returns a snapshot of the updated state. Returns null if
    /// angry mode expired between the caller's <see cref="IsAngryModeActive"/>
    /// check and this call (benign TOCTOU race).
    /// </summary>
    AngryModeState? RegisterBanInAngryMode(long chatId);
}
