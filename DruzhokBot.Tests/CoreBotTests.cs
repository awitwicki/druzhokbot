using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DruzhokBot.App;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Domain;
using DruzhokBot.Domain.Interfaces;
using Moq;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using DruzhokBot.Tests.TestData;
using DruzhokBot.Domain.DTO;
using DruzhokBot.Domain.Entities;

namespace DruzhokBot.Tests;

public class CoreBotTests
{
    private readonly Mock<ITelegramBotClientWrapper> _telegramBotClientWrapperMock;
    private readonly Mock<IBotLogger> _botLoggerMock;
    private readonly Mock<IAttackDetector> _attackDetectorMock;
    private readonly Mock<IChatSettingsRepository> _chatSettingsRepositoryMock;

    public CoreBotTests()
    {
        _telegramBotClientWrapperMock = new Mock<ITelegramBotClientWrapper>();
        _botLoggerMock = new Mock<IBotLogger>();
        _attackDetectorMock = new Mock<IAttackDetector>();
        _chatSettingsRepositoryMock = new Mock<IChatSettingsRepository>();

        _telegramBotClientWrapperMock
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Username = "abc" });

        _telegramBotClientWrapperMock
            .Setup(c => c.SendTextMessageAsync(
                It.IsAny<ChatId>(),
                It.IsAny<string>(),
                It.IsAny<ParseMode>(),
                It.IsAny<int>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message
            {
                Id = 1,
                Chat = new Chat { Id = 1 }
            });
    }

    private CoreBot CreateBot() => new(
        _telegramBotClientWrapperMock.Object,
        _botLoggerMock.Object,
        _attackDetectorMock.Object,
        _chatSettingsRepositoryMock.Object);

    [Fact]
    public async Task OnStartMessage_ShouldResponseWithHelloMessage()
    {
        var coreBot = CreateBot();
        var update = UpdateTestData.StartMessage();

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken());

        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                update.Message.Chat.Id,
                It.Is<string>(s => s.Contains("Дружок")),
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task OnNewUser_LowRisk_SendsCaptchaContainingTargetUkrainianName()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 1;
        const int chatId = 770;
        var update = UpdateTestData.UserJoined(userJoinedId, chatId);

        // OnNewUser blocks synchronously on Thread.Sleep; run it on a worker so we can observe state
        // during the captcha wait rather than after it. The task is abandoned when the test ends.
        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        // Wait enough time for the 2s pre-send sleep.
        await Task.Delay(3000);

        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                chatId,
                It.Is<string>(s => EmojiPool.All
                    .Select(EmojiPool.GetUkrainianName)
                    .Any(name => s.Contains(name))),
                ParseMode.Markdown,
                It.IsAny<int?>(),
                It.IsNotNull<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Once());

        Assert.True(coreBot.UsersBanQueue.ContainsKey((userJoinedId, chatId)));
    }

    [Fact]
    public async Task OnNewUser_BotsAreIgnored()
    {
        var coreBot = CreateBot();

        var update = new Update
        {
            ChatMember = new ChatMemberUpdated
            {
                Chat = new Chat { Id = 1, Title = "t" },
                NewChatMember = new ChatMemberMember
                {
                    User = new User { Id = 2, IsBot = true }
                }
            }
        };

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken());

        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                It.IsAny<ChatId>(),
                It.IsAny<string>(),
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _telegramBotClientWrapperMock.Verify(mock => mock.BanChatMemberAsync(
                It.IsAny<ChatId>(),
                It.IsAny<long>(),
                It.IsAny<DateTime?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Callback_CorrectToken_Verifies_AndRemovesFromQueue()
    {
        var coreBot = CreateBot();
        const long userId = 1;
        const int chatId = 1;

        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;
        var correctToken = challenge.Options.Single(o => o.IsCorrect).Token;

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{correctToken}");

        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        _telegramBotClientWrapperMock.Verify(mock => mock.AnswerCallbackQueryAsync(
                callback.Id,
                TextResources.VerificationSuccessfull,
                true,
                null,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.False(coreBot.UsersBanQueue.ContainsKey((userId, chatId)));
    }

    [Fact]
    public async Task Callback_WrongToken_BansUser()
    {
        var coreBot = CreateBot();
        const long userId = 1;
        const int chatId = 1;

        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;
        var wrongToken = challenge.Options.First(o => !o.IsCorrect).Token;

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{wrongToken}");

        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        _telegramBotClientWrapperMock.Verify(mock => mock.AnswerCallbackQueryAsync(
                callback.Id,
                TextResources.VerificationFailed,
                true,
                null,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _telegramBotClientWrapperMock.Verify(mock => mock.BanChatMemberAsync(
                chatId,
                userId,
                It.IsAny<DateTime?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.False(coreBot.UsersBanQueue.ContainsKey((userId, chatId)));
    }

    [Fact]
    public async Task Callback_StaleOrForgedToken_TreatedAsRandomUser()
    {
        var coreBot = CreateBot();
        const long userId = 1;
        const int chatId = 1;

        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;

        var forgedCallback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|never-issued-token");

        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, forgedCallback);

        _telegramBotClientWrapperMock.Verify(mock => mock.AnswerCallbackQueryAsync(
                forgedCallback.Id,
                TextResources.RandomUserClickedVerifyButtonResponse,
                true,
                null,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Entry still active — no resolution happened.
        Assert.True(coreBot.UsersBanQueue.ContainsKey((userId, chatId)));
    }

    [Fact]
    public async Task Callback_FromDifferentUser_TreatedAsRandomUser()
    {
        var coreBot = CreateBot();
        const long ownerId = 1;
        const long otherId = 99;
        const int chatId = 1;

        var challenge = CaptchaChallengeBuilder.Build(ownerId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, ownerId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(ownerId, chatId)] = dto;
        var correctToken = challenge.Options.Single(o => o.IsCorrect).Token;

        var callback = UpdateTestData.UserCallbackQuery(otherId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{correctToken}");

        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        _telegramBotClientWrapperMock.Verify(mock => mock.AnswerCallbackQueryAsync(
                callback.Id,
                TextResources.RandomUserClickedVerifyButtonResponse,
                true,
                null,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Owner's entry still active; no ban.
        Assert.True(coreBot.UsersBanQueue.ContainsKey((ownerId, chatId)));
    }

    [Fact]
    public async Task Callback_LegacyPayload_TreatedAsRandomUser()
    {
        var coreBot = CreateBot();
        const long userId = 1;
        const int chatId = 1;

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId, $"new_user|{userId}");

        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        _telegramBotClientWrapperMock.Verify(mock => mock.AnswerCallbackQueryAsync(
                callback.Id,
                TextResources.RandomUserClickedVerifyButtonResponse,
                true,
                null,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnUnverifiedUser_Messages_Are_Removed_From_Chat()
    {
        var coreBot = CreateBot();
        const long newUserId = 1;
        const long oldUserId = 2;
        const int chatId = 1;

        coreBot.UsersBanQueue[(newUserId, chatId)] = UserBanQueueDtoTestData.UserBanQueueDto(chatId, newUserId);

        var messages = UpdateTestData.RandomMessagesFromTwoUsersInSingleChat(newUserId, oldUserId, chatId);
        var oldUserMessagesIds = messages.Where(x => x.Message!.From!.Id == oldUserId)
            .Select(x => x.Message.MessageId).ToArray();
        var expectedRemovals = messages.Count(x => x.Message!.From!.Id == newUserId);

        foreach (var msg in messages)
        {
            await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, msg, new CancellationToken());
        }

        _telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                chatId,
                It.IsNotIn(oldUserMessagesIds),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedRemovals));
    }

    [Theory]
    [InlineData("https://opensea.io/collection")]
    [InlineData("opensea.io")]
    [InlineData("opensea.io/collection")]
    [InlineData("http/opensea.io fs /lection")]
    public async Task OnNotMemberUser_SendsOpenSeaSpamMessage_ShouldRemoveSpam(string messageText)
    {
        var coreBot = CreateBot();
        const int chatId = 7;
        const int messageId = 11;

        var message = new Update
        {
            Message = new Message
            {
                Id = messageId,
                Date = DateTime.Now,
                Chat = new Chat { Id = chatId },
                From = new User { Id = 1 },
                Text = messageText,
                ReplyToMessage = new Message
                {
                    SenderChat = new Chat { Type = ChatType.Channel }
                }
            }
        };

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, message, CancellationToken.None);

        _telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                chatId,
                messageId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnNotMemberUser_SendsNotSpamMessage_ShouldDoNothing()
    {
        var coreBot = CreateBot();
        const int chatId = 7;
        const int messageId = 11;

        var message = new Update
        {
            Message = new Message
            {
                Id = messageId,
                Date = DateTime.Now,
                Chat = new Chat { Id = chatId },
                From = new User { Id = 1 },
                Text = "https://google.io/collection",
                ReplyToMessage = new Message
                {
                    SenderChat = new Chat { Type = ChatType.Channel }
                }
            }
        };

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, message, CancellationToken.None);

        _telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                chatId,
                messageId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    public static TheoryData<ChatMember> AlreadyInChatOldMembers => new()
    {
        new ChatMemberMember(),
        new ChatMemberAdministrator(),
        new ChatMemberRestricted { IsMember = true },
    };

    [Theory]
    [MemberData(nameof(AlreadyInChatOldMembers))]
    public async Task MemberStatusChange_ForUserAlreadyInChat_DoesNotStartCaptcha(ChatMember oldChatMember)
    {
        var coreBot = CreateBot();
        const long userId = 510;
        const int chatId = 610;
        var update = UpdateTestData.MemberStatusChanged(oldChatMember, userId, chatId);

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken());

        _botLoggerMock.Verify(l => l.LogUserJoined(It.IsAny<User>(), It.IsAny<Chat>()), Times.Never);
        _attackDetectorMock.Verify(d => d.RegisterJoin(It.IsAny<long>()), Times.Never);
        Assert.False(coreBot.UsersBanQueue.ContainsKey((userId, chatId)));
    }

    [Fact]
    public async Task MemberStatusChange_FromRestrictedNotInChat_StartsCaptcha()
    {
        var coreBot = CreateBot();
        const long userId = 511;
        const int chatId = 611;
        var update = UpdateTestData.MemberStatusChanged(new ChatMemberRestricted { IsMember = false }, userId, chatId);

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));
        await Task.Delay(200);

        _attackDetectorMock.Verify(d => d.RegisterJoin(chatId), Times.Once);
    }

    [Fact]
    public async Task OnNewUser_RegistersJoinWithDetector()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 500;
        const int chatId = 600;
        var update = UpdateTestData.UserJoined(userJoinedId, chatId);

        // Fire-and-forget; we only care about the synchronous RegisterJoin call.
        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        // Give OnNewUser a moment to reach RegisterJoin (it's before the 2s Thread.Sleep).
        await Task.Delay(200);

        _attackDetectorMock.Verify(d => d.RegisterJoin(chatId), Times.Once);
    }

    [Fact]
    public async Task OnNewUser_WhenRegisterJoinReturnsTrue_StartsAngryModeAndSendsAttackOverOnCompletion()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 501;
        const int chatId = 601;
        var update = UpdateTestData.UserJoined(userJoinedId, chatId);

        _attackDetectorMock.Setup(d => d.RegisterJoin(chatId)).Returns(true);
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var finalState = new AngryModeState(
            ChatId: chatId,
            AttackStartTime: start,
            EndTime: start + TimeSpan.FromMinutes(3),
            BannedCount: 4);
        _attackDetectorMock.Setup(d => d.StartAngryMode(chatId)).ReturnsAsync(finalState);

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        // Let the fire-and-forget lifetime task observe the mocked completion.
        await Task.Delay(300);

        _attackDetectorMock.Verify(d => d.StartAngryMode(chatId), Times.Once);
        _telegramBotClientWrapperMock.Verify(c => c.SendTextMessageAsync(
                chatId,
                It.Is<string>(s => s.Contains("4") && s.Contains("3")),
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnNewUser_WhenAngryModeEndsWithZeroBans_DoesNotSendAttackOverMessage()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 502;
        const int chatId = 602;
        var update = UpdateTestData.UserJoined(userJoinedId, chatId);

        _attackDetectorMock.Setup(d => d.RegisterJoin(chatId)).Returns(true);
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _attackDetectorMock.Setup(d => d.StartAngryMode(chatId)).ReturnsAsync(
            new AngryModeState(chatId, start, start + TimeSpan.FromMinutes(3), BannedCount: 0));

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        await Task.Delay(300);

        // Verify no AttackOverMessage was sent to this chat. The captcha message
        // sent by the normal flow targets this chat id only after Thread.Sleep(2s);
        // at 300ms we're still inside the 2s pre-send delay.
        _telegramBotClientWrapperMock.Verify(c => c.SendTextMessageAsync(
                chatId,
                It.IsAny<string>(),
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task KickUser_ViaCallback_WhenAngryModeActive_BansPermanentlyAndRegistersBan()
    {
        var coreBot = CreateBot();
        const long userId = 700;
        const int chatId = 701;

        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;
        var wrongToken = challenge.Options.First(o => !o.IsCorrect).Token;

        _attackDetectorMock.Setup(d => d.IsAngryModeActive(chatId)).Returns(true);
        _attackDetectorMock.Setup(d => d.RegisterBanInAngryMode(chatId))
            .Returns(new AngryModeState(chatId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(3), BannedCount: 5));

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{wrongToken}");
        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        // Permanent ban: untilDate must be null.
        _telegramBotClientWrapperMock.Verify(mock => mock.BanChatMemberAsync(
                chatId,
                userId,
                null,
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _attackDetectorMock.Verify(d => d.RegisterBanInAngryMode(chatId), Times.Once);
    }

    [Fact]
    public async Task KickUser_ViaCallback_WhenAngryModeActive_AndFirstBan_SendsUnderAttackMessage()
    {
        var coreBot = CreateBot();
        const long userId = 710;
        const int chatId = 711;

        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;
        var wrongToken = challenge.Options.First(o => !o.IsCorrect).Token;

        _attackDetectorMock.Setup(d => d.IsAngryModeActive(chatId)).Returns(true);
        _attackDetectorMock.Setup(d => d.RegisterBanInAngryMode(chatId))
            .Returns(new AngryModeState(chatId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(3), BannedCount: 1));

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{wrongToken}");
        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        _telegramBotClientWrapperMock.Verify(c => c.SendTextMessageAsync(
                chatId,
                TextResources.ChatUnderAttackMessage,
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task KickUser_ViaCallback_WhenFirstBanInAngryMode_StoresStartMessageId()
    {
        var coreBot = CreateBot();
        const long userId = 740;
        const int chatId = 741;
        const int startMessageId = 4242;

        _telegramBotClientWrapperMock
            .Setup(c => c.SendTextMessageAsync(
                chatId,
                TextResources.ChatUnderAttackMessage,
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Message { Id = startMessageId, Chat = new Chat { Id = chatId } });

        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;
        var wrongToken = challenge.Options.First(o => !o.IsCorrect).Token;

        _attackDetectorMock.Setup(d => d.IsAngryModeActive(chatId)).Returns(true);
        _attackDetectorMock.Setup(d => d.RegisterBanInAngryMode(chatId))
            .Returns(new AngryModeState(chatId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(3), BannedCount: 1));

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{wrongToken}");
        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        Assert.True(coreBot.AttackStartMessageIds.TryGetValue(chatId, out var stored));
        Assert.Equal(startMessageId, stored);
    }

    [Fact]
    public async Task RunAngryModeLifetime_WithStoredStartMessage_DeletesItWhenAttackEnds()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 800;
        const int chatId = 801;
        const int startMessageId = 5151;
        var update = UpdateTestData.UserJoined(userJoinedId, chatId);

        coreBot.AttackStartMessageIds[chatId] = startMessageId;

        _attackDetectorMock.Setup(d => d.RegisterJoin(chatId)).Returns(true);
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _attackDetectorMock.Setup(d => d.StartAngryMode(chatId)).ReturnsAsync(
            new AngryModeState(chatId, start, start + TimeSpan.FromMinutes(3), BannedCount: 4));

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        await Task.Delay(300);

        _telegramBotClientWrapperMock.Verify(c => c.DeleteMessageAsync(
                chatId,
                startMessageId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.False(coreBot.AttackStartMessageIds.ContainsKey(chatId));
    }

    [Fact]
    public async Task RunAngryModeLifetime_WithNoStoredStartMessage_DoesNotCallDelete()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 810;
        const int chatId = 811;
        var update = UpdateTestData.UserJoined(userJoinedId, chatId);

        _attackDetectorMock.Setup(d => d.RegisterJoin(chatId)).Returns(true);
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _attackDetectorMock.Setup(d => d.StartAngryMode(chatId)).ReturnsAsync(
            new AngryModeState(chatId, start, start + TimeSpan.FromMinutes(3), BannedCount: 0));

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        await Task.Delay(300);

        _telegramBotClientWrapperMock.Verify(c => c.DeleteMessageAsync(
                chatId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        Assert.False(coreBot.AttackStartMessageIds.ContainsKey(chatId));
    }

    [Fact]
    public async Task RunAngryModeLifetime_WhenDeleteThrows_StillCompletesAndSendsRecap()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 820;
        const int chatId = 821;
        const int startMessageId = 9999;
        var update = UpdateTestData.UserJoined(userJoinedId, chatId);

        coreBot.AttackStartMessageIds[chatId] = startMessageId;

        _telegramBotClientWrapperMock
            .Setup(c => c.DeleteMessageAsync(chatId, startMessageId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiRequestException("message to delete not found", 400));

        _attackDetectorMock.Setup(d => d.RegisterJoin(chatId)).Returns(true);
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _attackDetectorMock.Setup(d => d.StartAngryMode(chatId)).ReturnsAsync(
            new AngryModeState(chatId, start, start + TimeSpan.FromMinutes(3), BannedCount: 2));

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        await Task.Delay(300);

        _telegramBotClientWrapperMock.Verify(c => c.SendTextMessageAsync(
                chatId,
                It.Is<string>(s => s.Contains("2") && s.Contains("3")),
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _telegramBotClientWrapperMock.Verify(c => c.DeleteMessageAsync(
                chatId,
                startMessageId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task KickUser_ViaCallback_WhenAngryModeActive_AndSubsequentBan_DoesNotSendUnderAttackMessage()
    {
        var coreBot = CreateBot();
        const long userId = 720;
        const int chatId = 721;

        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;
        var wrongToken = challenge.Options.First(o => !o.IsCorrect).Token;

        _attackDetectorMock.Setup(d => d.IsAngryModeActive(chatId)).Returns(true);
        _attackDetectorMock.Setup(d => d.RegisterBanInAngryMode(chatId))
            .Returns(new AngryModeState(chatId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(3), BannedCount: 2));

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{wrongToken}");
        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        _telegramBotClientWrapperMock.Verify(c => c.SendTextMessageAsync(
                chatId,
                TextResources.ChatUnderAttackMessage,
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task KickUser_ViaCallback_WhenAngryModeInactive_Uses45sBan()
    {
        var coreBot = CreateBot();
        const long userId = 730;
        const int chatId = 731;

        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;
        var wrongToken = challenge.Options.First(o => !o.IsCorrect).Token;

        _attackDetectorMock.Setup(d => d.IsAngryModeActive(chatId)).Returns(false);

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{wrongToken}");
        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        // 45s ban: untilDate must be non-null.
        _telegramBotClientWrapperMock.Verify(mock => mock.BanChatMemberAsync(
                chatId,
                userId,
                It.Is<DateTime?>(d => d.HasValue),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _attackDetectorMock.Verify(d => d.RegisterBanInAngryMode(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task OnNewUser_ViaJoinRequest_DoesNotRegisterJoin()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 900;
        const int chatId = 901;
        var update = UpdateTestData.UserJoinedViaRequest(userJoinedId, chatId);

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        await Task.Delay(300);

        _attackDetectorMock.Verify(d => d.RegisterJoin(It.IsAny<long>()), Times.Never);
        _attackDetectorMock.Verify(d => d.IsAngryModeActive(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task OnNewUser_ViaJoinRequest_Uses24HourChallengeTtl()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 910;
        const int chatId = 911;
        var update = UpdateTestData.UserJoinedViaRequest(userJoinedId, chatId);

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        await Task.Delay(300);

        Assert.True(coreBot.UsersBanQueue.TryGetValue((userJoinedId, chatId), out var dto));
        Assert.True(dto.ViaJoinRequest);
        Assert.True(dto.Challenge.ExpiresAt > DateTime.UtcNow.AddHours(23));
        Assert.True(dto.Challenge.ExpiresAt < DateTime.UtcNow.AddHours(25));
    }

    [Fact]
    public async Task OnNewUser_ViaJoinRequest_SendsHoursWordedMessage()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 920;
        const int chatId = 921;
        var update = UpdateTestData.UserJoinedViaRequest(userJoinedId, chatId);

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        await Task.Delay(3000);

        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                chatId,
                It.Is<string>(s => s.Contains("24 години")),
                ParseMode.Markdown,
                It.IsAny<int?>(),
                It.IsNotNull<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task OnNewUser_ViaJoinRequest_WhenAngryModeActive_StillUses24Hours()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 930;
        const int chatId = 931;
        var update = UpdateTestData.UserJoinedViaRequest(userJoinedId, chatId);

        _attackDetectorMock.Setup(d => d.IsAngryModeActive(chatId)).Returns(true);

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        await Task.Delay(300);

        Assert.True(coreBot.UsersBanQueue.TryGetValue((userJoinedId, chatId), out var dto));
        Assert.True(dto.Challenge.ExpiresAt > DateTime.UtcNow.AddHours(23));
        Assert.True(dto.Challenge.ExpiresAt < DateTime.UtcNow.AddHours(25));
    }

    [Fact]
    public async Task OnNewUser_NormalJoin_StillRegistersJoinAndUsesShortTtl()
    {
        var coreBot = CreateBot();
        const long userJoinedId = 940;
        const int chatId = 941;
        var update = UpdateTestData.UserJoined(userJoinedId, chatId);

        _ = Task.Run(() => coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken()));

        await Task.Delay(300);

        _attackDetectorMock.Verify(d => d.RegisterJoin(chatId), Times.Once);
        Assert.True(coreBot.UsersBanQueue.TryGetValue((userJoinedId, chatId), out var dto));
        Assert.False(dto.ViaJoinRequest);
        Assert.True(dto.Challenge.ExpiresAt < DateTime.UtcNow.AddMinutes(2));
    }

    [Fact]
    public async Task KickUser_ViaCallback_JoinRequestUser_WhenAngryModeActive_Uses45sBan()
    {
        var coreBot = CreateBot();
        const long userId = 950;
        const int chatId = 951;

        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromHours(24));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        dto.ViaJoinRequest = true;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;
        var wrongToken = challenge.Options.First(o => !o.IsCorrect).Token;

        _attackDetectorMock.Setup(d => d.IsAngryModeActive(chatId)).Returns(true);

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{wrongToken}");
        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        _telegramBotClientWrapperMock.Verify(mock => mock.BanChatMemberAsync(
                chatId,
                userId,
                It.Is<DateTime?>(d => d.HasValue),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _attackDetectorMock.Verify(d => d.RegisterBanInAngryMode(It.IsAny<long>()), Times.Never);
    }

    private void SetupChatMemberStatus(long chatId, long userId, ChatMember member)
    {
        member.User = new User { Id = userId };
        _telegramBotClientWrapperMock
            .Setup(c => c.GetChatMemberAsync(It.Is<ChatId>(id => id.Identifier == chatId), userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);
    }

    private void VerifyReply(long chatId, string text, Times times)
    {
        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                It.Is<ChatId>(id => id.Identifier == chatId),
                text,
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            times);
    }

    private async Task VerifyCorrectCaptcha(CoreBot coreBot, long userId, int chatId)
    {
        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;
        var correctToken = challenge.Options.Single(o => o.IsCorrect).Token;

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{correctToken}");
        callback.From.FirstName = "Jack";
        callback.From.LastName = "<Doe>";

        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);
    }

    [Fact]
    public async Task Callback_CorrectToken_WhenGreetingEnabled_SendsCustomGreetingWithMention()
    {
        var coreBot = CreateBot();
        const long userId = 7;
        const int chatId = 10;
        _chatSettingsRepositoryMock.Setup(r => r.GetAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSettings { ChatId = chatId, GreetingEnabled = true, GreetingText = "Hi {user} & welcome" });

        await VerifyCorrectCaptcha(coreBot, userId, chatId);

        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                It.Is<ChatId>(id => id.Identifier == chatId),
                "Hi <a href=\"tg://user?id=7\">Jack &lt;Doe&gt;</a> &amp; welcome",
                ParseMode.Html,
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Callback_CorrectToken_WhenGreetingEnabledWithoutText_SendsDefaultGreeting()
    {
        var coreBot = CreateBot();
        const long userId = 7;
        const int chatId = 10;
        _chatSettingsRepositoryMock.Setup(r => r.GetAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSettings { ChatId = chatId, GreetingEnabled = true });

        await VerifyCorrectCaptcha(coreBot, userId, chatId);

        var expected = CoreBot.BuildGreetingHtml(TextResources.DefaultGreetingMessage, new User
            { Id = userId, FirstName = "Jack", LastName = "<Doe>" });
        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                It.Is<ChatId>(id => id.Identifier == chatId),
                expected,
                ParseMode.Html,
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Callback_CorrectToken_WhenGreetingDisabled_DoesNotSendGreeting()
    {
        var coreBot = CreateBot();
        const int chatId = 10;
        _chatSettingsRepositoryMock.Setup(r => r.GetAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSettings { ChatId = chatId, GreetingEnabled = false, GreetingText = "Hi" });

        await VerifyCorrectCaptcha(coreBot, 7, chatId);

        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                It.IsAny<ChatId>(),
                It.IsAny<string>(),
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Callback_CorrectToken_WhenSettingsLookupThrows_StillDeletesCaptchaMessage()
    {
        var coreBot = CreateBot();
        const int chatId = 10;
        _chatSettingsRepositoryMock.Setup(r => r.GetAsync(chatId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await VerifyCorrectCaptcha(coreBot, 7, chatId);

        _telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                It.Is<ChatId>(id => id.Identifier == chatId), 56, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Callback_WrongToken_DoesNotSendGreeting()
    {
        var coreBot = CreateBot();
        const long userId = 7;
        const int chatId = 10;
        _chatSettingsRepositoryMock.Setup(r => r.GetAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSettings { ChatId = chatId, GreetingEnabled = true });

        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        var dto = UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId);
        dto.Challenge = challenge;
        coreBot.UsersBanQueue[(userId, chatId)] = dto;
        var wrongToken = challenge.Options.First(o => !o.IsCorrect).Token;

        var callback = UpdateTestData.UserCallbackQuery(userId, chatId,
            $"{Consts.CaptchaCallbackPrefix}|{wrongToken}");
        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, callback);

        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                It.IsAny<ChatId>(),
                It.IsAny<string>(),
                ParseMode.Html,
                It.IsAny<int?>(),
                It.IsAny<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("/greeting_on", true)]
    [InlineData("/greeting_off", false)]
    [InlineData("/greeting_on@abc", true)]
    public async Task GreetingToggleCommand_FromAdmin_UpdatesSetting(string text, bool expected)
    {
        var coreBot = CreateBot();
        const long adminId = 5;
        const int chatId = -100;
        SetupChatMemberStatus(chatId, adminId, new ChatMemberAdministrator());

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object,
            UpdateTestData.GroupCommand(text, adminId, chatId), new CancellationToken());

        _chatSettingsRepositoryMock.Verify(r => r.SetGreetingEnabledAsync(chatId, expected,
            It.IsAny<CancellationToken>()), Times.Once);
        VerifyReply(chatId,
            expected ? TextResources.GreetingEnabledMessage : TextResources.GreetingDisabledMessage, Times.Once());
    }

    [Fact]
    public async Task GreetingSetCommand_FromCreator_SavesMultilineText()
    {
        var coreBot = CreateBot();
        const long ownerId = 5;
        const int chatId = -100;
        SetupChatMemberStatus(chatId, ownerId, new ChatMemberOwner());

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object,
            UpdateTestData.GroupCommand("/greeting_set Привіт, {user}!\nПрочитай правила.", ownerId, chatId),
            new CancellationToken());

        _chatSettingsRepositoryMock.Verify(r => r.SetGreetingTextAsync(chatId, "Привіт, {user}!\nПрочитай правила.",
            It.IsAny<CancellationToken>()), Times.Once);
        VerifyReply(chatId, TextResources.GreetingTextSavedMessage, Times.Once());
    }

    [Fact]
    public async Task GreetingSetCommand_WithoutText_RepliesWithUsage()
    {
        var coreBot = CreateBot();
        const long adminId = 5;
        const int chatId = -100;
        SetupChatMemberStatus(chatId, adminId, new ChatMemberAdministrator());

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object,
            UpdateTestData.GroupCommand("/greeting_set   ", adminId, chatId), new CancellationToken());

        _chatSettingsRepositoryMock.Verify(r => r.SetGreetingTextAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        VerifyReply(chatId, TextResources.GreetingSetUsageMessage, Times.Once());
    }

    [Fact]
    public async Task GreetingSetCommand_TooLongText_IsRejected()
    {
        var coreBot = CreateBot();
        const long adminId = 5;
        const int chatId = -100;
        SetupChatMemberStatus(chatId, adminId, new ChatMemberAdministrator());

        var text = "/greeting_set " + new string('a', Consts.GreetingTextMaxLength + 1);
        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object,
            UpdateTestData.GroupCommand(text, adminId, chatId), new CancellationToken());

        _chatSettingsRepositoryMock.Verify(r => r.SetGreetingTextAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        VerifyReply(chatId, string.Format(TextResources.GreetingTextTooLongMessage, Consts.GreetingTextMaxLength),
            Times.Once());
    }

    [Fact]
    public async Task GreetingResetCommand_FromAdmin_ClearsText()
    {
        var coreBot = CreateBot();
        const long adminId = 5;
        const int chatId = -100;
        SetupChatMemberStatus(chatId, adminId, new ChatMemberAdministrator());

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object,
            UpdateTestData.GroupCommand("/greeting_reset", adminId, chatId), new CancellationToken());

        _chatSettingsRepositoryMock.Verify(r => r.SetGreetingTextAsync(chatId, null,
            It.IsAny<CancellationToken>()), Times.Once);
        VerifyReply(chatId, TextResources.GreetingTextResetMessage, Times.Once());
    }

    [Fact]
    public async Task GreetingStatusCommand_FromAdmin_RepliesWithCurrentSettings()
    {
        var coreBot = CreateBot();
        const long adminId = 5;
        const int chatId = -100;
        SetupChatMemberStatus(chatId, adminId, new ChatMemberAdministrator());
        _chatSettingsRepositoryMock.Setup(r => r.GetAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSettings { ChatId = chatId, GreetingEnabled = true, GreetingText = "Custom" });

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object,
            UpdateTestData.GroupCommand("/greeting", adminId, chatId), new CancellationToken());

        VerifyReply(chatId,
            string.Format(TextResources.GreetingStatusMessage, TextResources.GreetingStatusEnabled, "Custom"),
            Times.Once());
    }

    [Theory]
    [InlineData("/greeting_on")]
    [InlineData("/greeting_set spam")]
    public async Task GreetingCommand_FromRegularMember_IsRejected(string text)
    {
        var coreBot = CreateBot();
        const long userId = 5;
        const int chatId = -100;
        SetupChatMemberStatus(chatId, userId, new ChatMemberMember());

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object,
            UpdateTestData.GroupCommand(text, userId, chatId), new CancellationToken());

        _chatSettingsRepositoryMock.Verify(r => r.SetGreetingEnabledAsync(It.IsAny<long>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _chatSettingsRepositoryMock.Verify(r => r.SetGreetingTextAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        VerifyReply(chatId, TextResources.OnlyAdminsCanChangeSettingsMessage, Times.Once());
    }

    [Fact]
    public async Task GreetingCommand_FromAnonymousAdmin_IsAccepted()
    {
        var coreBot = CreateBot();
        const int chatId = -100;
        var update = UpdateTestData.GroupCommand("/greeting_on", 1087968824, chatId);
        update.Message!.SenderChat = new Chat { Id = chatId, Type = ChatType.Supergroup };

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken());

        _chatSettingsRepositoryMock.Verify(r => r.SetGreetingEnabledAsync(chatId, true,
            It.IsAny<CancellationToken>()), Times.Once);
        _telegramBotClientWrapperMock.Verify(c => c.GetChatMemberAsync(It.IsAny<ChatId>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GreetingCommand_AddressedToAnotherBot_IsIgnored()
    {
        var coreBot = CreateBot();
        const long adminId = 5;
        const int chatId = -100;
        SetupChatMemberStatus(chatId, adminId, new ChatMemberAdministrator());

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object,
            UpdateTestData.GroupCommand("/greeting_on@otherbot", adminId, chatId), new CancellationToken());

        _chatSettingsRepositoryMock.Verify(r => r.SetGreetingEnabledAsync(It.IsAny<long>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _telegramBotClientWrapperMock.Verify(c => c.GetChatMemberAsync(It.IsAny<ChatId>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GreetingCommand_InPrivateChat_IsIgnored()
    {
        var coreBot = CreateBot();
        const long userId = 5;
        var update = UpdateTestData.GroupCommand("/greeting_on", userId, (int)userId);
        update.Message!.Chat.Type = ChatType.Private;

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken());

        _chatSettingsRepositoryMock.Verify(r => r.SetGreetingEnabledAsync(It.IsAny<long>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void BuildGreetingHtml_EscapesTextAndReplacesEveryPlaceholder()
    {
        var html = CoreBot.BuildGreetingHtml("<b>{user}</b> {user}", new User { Id = 3, FirstName = "A&B" });

        Assert.Equal(
            "&lt;b&gt;<a href=\"tg://user?id=3\">A&amp;B</a>&lt;/b&gt; <a href=\"tg://user?id=3\">A&amp;B</a>",
            html);
    }
}
