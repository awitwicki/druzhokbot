# CLAUDE.md

Guidance for Claude Code when working in this repository. Keep this file accurate as the codebase evolves.

## What this project is

DruzhokBot is a self-hosted antispam bot for Telegram groups, written in C# / .NET 8. It runs as a single Docker process and protects rooms by:

1. Forcing every new joiner to solve an emoji captcha; failures are banned.
2. Detecting join-raid attacks ("angry mode") and escalating captcha-failure bans from 45 seconds to permanent for the duration of the attack.
3. Removing channel-reply spam messages (opensea.io / referral-bot patterns).

The bot is Ukrainian-language (user-facing strings in `TextResources.resx`).

## Solution layout

Four projects in `DruzhokBot.sln`. All target `net8.0`. Domain and Common have `<ImplicitUsings>enable</ImplicitUsings>` and `<Nullable>enable</Nullable>`; App and Tests do not.

| Project | Role | Notes |
| --- | --- | --- |
| [DruzhokBot.App](DruzhokBot.App/) | Composition root. Contains [Program.cs](DruzhokBot.App/Program.cs) and [CoreBot.cs](DruzhokBot.App/CoreBot.cs), the main update-dispatcher and business orchestrator. Builds an executable with `OutputType=Exe`. `<FileVersion>` here is the version shown in `/start`. | Depends only on `DruzhokBot.Common`. |
| [DruzhokBot.Common](DruzhokBot.Common/) | Service implementations and pure helpers. `Services/` for stateful services (`TelegramBotClientWrapper`, `BotLogger`, `AttackDetector`); `Helpers/` for stateless helpers (`SpamChecker`, `CaptchaChallengeBuilder`, `CaptchaKeyboardBuilder`, `EmojiPool`, `InfluxDbLiteClient`, `UserExtensions`); `Extensions/` for extension methods on Telegram SDK types. | Depends on `DruzhokBot.Domain`. |
| [DruzhokBot.Domain](DruzhokBot.Domain/) | Pure model layer. `DTO/` for records (`AngryModeState`, `CaptchaChallenge`, `CaptchaOption`, `UserBanQueueDto`); `Interfaces/` for the three injectable abstractions (`ITelegramBotClientWrapper`, `IBotLogger`, `IAttackDetector`); root holds `Consts.cs`, `TextResources.resx` + Designer, `LogTemplates.resx` + Designer. | No project dependencies. [AssemblyVisibility.cs](DruzhokBot.Domain/AssemblyVisibility.cs) exposes internals to `DruzhokBot.App` and `DruzhokBot.Tests` (the resx-generated `TextResources` class is `internal`). |
| [DruzhokBot.Tests](DruzhokBot.Tests/) | xUnit + Moq tests. Each top-level production class has a sibling test class. `TestData/` holds shared `Update`/`UserBanQueueDto`/`Message` factories. | References `DruzhokBot.App` (transitively pulls in Common and Domain). |

Tests project does NOT have implicit usings; explicit `using` directives are required.

## Tech stack

- **Telegram.Bot 22.9.6.1** — the SDK; wrapped behind `ITelegramBotClientWrapper` so business logic never touches the SDK directly. The wrapper translates SDK signatures into our own.
- **NLog 6.0.2** — local console logging.
- **InfluxData.Net 8.0.1** — time-series logging to a local InfluxDB at `http://localhost:8086`, database `bots`, measurement `druzhokbot_logs`. See [InfluxDBLiteClient.cs](DruzhokBot.Common/Helpers/InfluxDBLiteClient.cs).
- **xUnit 2.9.3 + Moq 4.20.72** — tests.

## Configuration / runtime

Two environment variables, read in [Program.cs](DruzhokBot.App/Program.cs):

| Var | Required | Purpose |
| --- | --- | --- |
| `DRUZHOKBOT_TELEGRAM_TOKEN` | yes | Telegram bot token; missing token logs an error and the process idles forever. |
| `DRUZHOKBOT_INFLUX_QUERY` | no | Documented in README as the InfluxDB URL toggle, but the current `InfluxDbLiteClient` hardcodes `http://localhost:8086`. Treat the env var as forward-looking. |

The bot runs as a single process (`Program.cs` ends with `await Task.Delay(-1)`). Deployed via [docker-compose.yml](docker-compose.yml) on the user's Synology NAS. There is no persistent storage — all state lives in memory and is dropped on restart.

## Bootstrap flow

[Program.cs](DruzhokBot.App/Program.cs):

1. Configure NLog console logging at `Info`.
2. Read assembly version via `FileVersionInfo.GetVersionInfo`.
3. Emit a `bot_started` event to InfluxDB.
4. Read `DRUZHOKBOT_TELEGRAM_TOKEN`.
5. Construct `TelegramBotClientWrapper(token)`, then `new CoreBot(bot)`.
6. `CoreBot` constructor synchronously: drops pending updates, subscribes update/error handlers, fetches bot identity, logs ready.
7. Process idles on `Task.Delay(-1)`; the bot client raises events on its own thread.

`CoreBot`'s constructor takes three parameters; only the first is required. Optional parameters use a null-fallback pattern (`paramName ?? new Concrete()`) so the prod entry point doesn't need DI:

```csharp
public CoreBot(
    ITelegramBotClientWrapper botClientWrapper,
    IBotLogger? botLogger = null,
    IAttackDetector? attackDetector = null)
```

## Update pipeline

The single entry point for incoming Telegram updates is [`CoreBot.HandleUpdateAsync`](DruzhokBot.App/CoreBot.cs). It dispatches based on `Update` shape, in this order. Branches are independent and most are `if`-not-`else`, so a single update can trigger multiple paths.

```
Update arrives
│
├── update.Type == Message
│   ├── if user is in UsersBanQueue (mid-captcha) → delete the message
│   └── else if Message replies to a channel post AND text matches SpamChecker
│       → delete the message + LogRemoveSpam
│
├── update.Type == Message AND text == "/start"
│   → reply with TextResources.StartMessage (formatted with version)
│
├── update.ChatMember.NewChatMember.Status == Member
│   → OnNewUser (captcha + angry-mode hook)
│
├── update.Message.Type == NewChatMembers   → delete the join system message
├── update.Message.Type == LeftChatMember  → delete the leave system message
│
└── update.Type == CallbackQuery → BotOnCallbackQueryReceived
```

Top-level `try/catch` in `HandleUpdateAsync` routes any escaping exception to `HandleErrorAsync`, which logs but never rethrows. Inner branches each have their own `try/catch` so one failure doesn't poison the rest.

## Business logic pipelines

### 1. New-user captcha verification

[`CoreBot.OnNewUser`](DruzhokBot.App/CoreBot.cs) — fired when `ChatMemberStatus.Member` arrives.

```
LogUserJoined (NLog + InfluxDB user_joined event)
│
├── if user.IsBot → return
│
├── _attackDetector.RegisterJoin(chatId)
│      ├── adds timestamp to per-chat sliding window (100s)
│      ├── prunes entries older than 100s
│      └── returns true iff window >= 3 AND angry mode not already active
│
├── if RegisterJoin returned true:
│      _ = RunAngryModeLifetime(...)        // fire-and-forget; see angry mode below
│
├── Build CaptchaChallenge:
│      6 random emojis (RandomNumberGenerator), one marked correct,
│      base64url-safe tokens (9 bytes), TTL = 90s
│      → see CaptchaChallengeBuilder + EmojiPool
│
├── UsersBanQueue.TryAdd((userId, chatId), UserBanQueueDto{Chat,User,Challenge})
│
├── Build inline keyboard (CaptchaKeyboardBuilder), payload "captcha|<token>"
│
├── Thread.Sleep(2_000)                     // give Telegram time to settle
├── SendTextMessageAsync(NewUserVerificationMessage, keyboard)
│
├── Thread.Sleep(90_000)                    // captcha timeout
└── if entry still in UsersBanQueue → KickUser (timeout path)
```

Each new user runs `OnNewUser` on its own thread (Telegram SDK invokes handlers concurrently). The `Thread.Sleep` calls are intentional — the bot is single-process and these waits don't block other updates. The `UsersBanQueue` is a `ConcurrentDictionary<(long, long), UserBanQueueDto>`.

[`CoreBot.BotOnCallbackQueryReceived`](DruzhokBot.App/CoreBot.cs) handles button clicks:

- Payload format: `captcha|<token>`. Anything else (legacy or forged) → "random user clicked verify" toast.
- If the clicker has no entry in `UsersBanQueue` at `(userId, chatId)` → same toast (different user, expired, already resolved).
- Token must match one in this user's `Challenge.Options`. Mismatch → toast (forged token).
- Correct option → success toast, `TryRemove` from queue, `LogUserVerified`.
- Wrong option → fail toast, `TryRemove` from queue, `KickUser`.
- Always delete the captcha message at the end.

[`CoreBot.KickUser`](DruzhokBot.App/CoreBot.cs) — the only ban path:

- If `_attackDetector.IsAngryModeActive(chatId)` → permanent ban (no `untilDate`); `RegisterBanInAngryMode` increments count and possibly extends the timer; on `BannedCount == 1` post `TextResources.ChatUnderAttackMessage`.
- Otherwise → 45-second ban (`DateTime.Now.AddSeconds(45)`).
- Always `LogUserBanned`.

### 2. Angry mode (attack response)

Spec: [docs/superpowers/specs/2026-04-24-angry-mode-design.md](docs/superpowers/specs/2026-04-24-angry-mode-design.md). All state in-memory in [`AttackDetector`](DruzhokBot.Common/Services/AttackDetector.cs).

Constants (also in `AttackDetector`):

| Constant | Value | Meaning |
| --- | --- | --- |
| `WindowSize` | 100 s | Sliding window for join-rate detection |
| `TriggerCount` | 3 | Joins-in-window threshold |
| `InitialDuration` | 3 min | Angry-mode default lifetime |
| `ExtensionThreshold` | 60 s | Extend only if remaining time < this |
| `ExtensionAmount` | 45 s | How much each late ban extends |

State (single coarse `_lock`, plus `ConcurrentDictionary` containers):

- `_windows: Dict<chatId, LinkedList<DateTime>>` — per-chat join-time window.
- `_active: Dict<chatId, AngryModeState>` — currently active sessions.
- `_completions: Dict<chatId, TaskCompletionSource<AngryModeState>>` — awaitable handles for `StartAngryMode` callers.

Lifecycle:

```
join arrives                            (CoreBot.OnNewUser)
   │
   ▼
RegisterJoin(chatId)
   │ adds timestamp; prunes old entries; returns true iff
   │   window.Count >= 3 AND not already active
   │
   ▼ (true)
RunAngryModeLifetime(chatId)            (fire-and-forget in CoreBot)
   │ awaits _attackDetector.StartAngryMode(chatId)
   │
   ▼
StartAngryMode(chatId)                  (AttackDetector)
   │ creates AngryModeState{ now, now+3min, BannedCount:0 }
   │ stores in _active and _completions
   │ spawns RunLifetime(chatId)
   │ returns Task<AngryModeState>
   │
   ▼
RunLifetime(chatId)                     (poll loop)
   │ each iteration under _lock:
   │   read state, read clock; break when now >= EndTime
   │   delay = min(remaining, _pollInterval)   (prod _pollInterval=1s)
   │ on break: TryRemove from _active and _completions; TrySetResult(finalState)

while alive:
   captcha-fail or timeout → CoreBot.KickUser
       │ IsAngryModeActive → true
       │ permanent BanChatMemberAsync (no untilDate)
       │ RegisterBanInAngryMode(chatId):
       │   under lock, atomically:
       │     if expired/missing → return null   (TOCTOU; ban already happened)
       │     else:
       │       BannedCount++
       │       if remaining < 60s: EndTime += 45s
       │       _active[chatId] = new snapshot (record `with`)
       │ if updated.BannedCount == 1 → SendText ChatUnderAttackMessage

eventually:
   RunLifetime observes now >= EndTime
   completes Task<AngryModeState> with final state
   ▼
RunAngryModeLifetime continuation
   if finalState.BannedCount > 0:
       SendText AttackOverMessage("Атаку відбито. Забанено {count} ботів за {minutes} хв.")
   else: silent (legitimate burst)
```

Key invariants:

- `RegisterJoin` returns `true` at most once per session — once `_active` contains the chat, the `&& !angryModeActive` guard prevents re-trigger.
- `AngryModeState` is a record; mutations replace the whole reference under lock, so readers never see a torn state.
- The lifetime task re-reads `_active[chatId]` every iteration, so extensions applied by `RegisterBanInAngryMode` are picked up automatically without separate signaling.
- Captcha flow runs unchanged for every new user during angry mode. The only behavioral change is `KickUser`'s ban-duration choice.
- All `RunAngryModeLifetime` work runs under a top-level `try/catch` so a fire-and-forget failure cannot crash the process.

### 3. Channel-reply spam

When a message replies to a `SenderChat.Type == Channel` post, [`SpamChecker.IsSpam`](DruzhokBot.Common/Helpers/SpamChecker.cs) tests the text against a single compiled regex:

```
opensea\.io | @\w+bot\b | t\.me/[^\s]+bot\b
```

Matches → `DeleteMessageAsync` + `_botLogger.LogRemoveSpam`. This path is independent of captcha and angry mode.

## Cross-cutting concerns

### Logging

Two channels, both fire from [`BotLogger`](DruzhokBot.Common/Services/BotLogger.cs):

- **NLog** — human-readable lines to console.
- **InfluxDB** — `Consts.AppLogsTableName` = `druzhokbot_logs`. Tags include `chat_id`, `chat_name`, `chat_username`, `user_id`, `user_name`, `user_fullname`, `event_type`. The `event_type` values live in [Consts.cs](DruzhokBot.Domain/Consts.cs): `bot_started`, `user_joined`, `user_verified`, `ban_user`, `remove_spam_message`. InfluxDB writes are async fire-and-forget; failures are logged via NLog and never propagate.

`IBotLogger` exposes four methods: `LogUserJoined`, `LogUserVerified`, `LogUserBanned`, `LogRemoveSpam`. Add new event types by extending the interface, the implementation, and `Consts`.

### Internationalization / strings

User-facing strings live in [TextResources.resx](DruzhokBot.Domain/TextResources.resx) (Ukrainian). The companion [TextResources.Designer.cs](DruzhokBot.Domain/TextResources.Designer.cs) is committed (not regenerated by the SDK build). When adding a new string you must edit BOTH files: the resx entry and the matching `internal static string` property. Tests reference the strings directly via `InternalsVisibleTo`.

`LogTemplates.resx`/`Designer.cs` mirrors the same pattern for log-line templates.

### Tests

- xUnit + Moq. Filename and class name = `<ClassUnderTest>Tests`.
- Concurrency-sensitive code (`AttackDetector`, the lifetime task) injects `Func<DateTime>` for clock and a short `_pollInterval` (10 ms in tests). See `FakeClock` helper in [AttackDetectorTests.cs](DruzhokBot.Tests/AttackDetectorTests.cs).
- For `CoreBot` tests that exercise `OnNewUser` (which contains a 2-second `Thread.Sleep` followed by send), the pattern is:
  ```csharp
  _ = Task.Run(() => coreBot.HandleUpdateAsync(...));
  await Task.Delay(<200-300ms>);
  // assert on what should have happened before the sleep finishes
  ```
- Mocks for `IBotLogger` and `IAttackDetector` are constructed in the test fixture and passed via `CreateBot()`. `IAttackDetector` mock returns default `false`/`null`, which preserves pre-angry-mode behavior for tests that don't exercise it.
- All async work runs under deterministic clock advancement; the suite has no real `Thread.Sleep` outside the production code under test.
- Total: 83 tests as of 2026-04-26, all green.

### Telegram SDK abstraction

The bot never imports `TelegramBotClient` directly outside of [TelegramBotClientWrapper.cs](DruzhokBot.Common/Services/TelegramBotClientWrapper.cs). All other code talks through `ITelegramBotClientWrapper`, which simplifies mocking. When the SDK upgrade changes a method shape, the only file that needs edits is the wrapper.

Note: `_cancellationToken` field on the wrapper is declared but never assigned, so `OnUpdate` always passes `default(CancellationToken)` to handlers. This means the `CancellationToken` parameter threaded through `CoreBot` is always `CancellationToken.None` in production. If you ever wire a real shutdown token, audit `CoreBot.RunAngryModeLifetime` and the captcha `Thread.Sleep` paths for cooperative cancellation.

## Build and test commands

```
dotnet build DruzhokBot.sln          # whole solution build
dotnet test DruzhokBot.sln           # whole-solution test run
dotnet test DruzhokBot.Tests --filter "FullyQualifiedName~<class-or-test>"
docker-compose up -d                 # production deploy
```

## Working preferences

- **Do not commit unless the user explicitly asks.** Workflows like `/superpowers:executing-plans` MUST end at a "stopped for review" gate, not at `git commit`. (Stored in user auto-memory.)
- Default to writing **no comments**; only add when the why is non-obvious.
- New features go through `superpowers:brainstorming` → spec in `docs/superpowers/specs/` → plan in `docs/superpowers/plans/` → execution.
- Keep this CLAUDE.md updated when architectural facts change (new project, new abstraction, new top-level pipeline branch). It is the cheap source of truth that future sessions will load first.
