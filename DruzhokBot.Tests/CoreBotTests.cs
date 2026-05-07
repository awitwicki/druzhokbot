using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DruzhokBot.App;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Domain;
using DruzhokBot.Domain.Interfaces;
using Moq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using DruzhokBot.Tests.TestData;
using DruzhokBot.Domain.DTO;

namespace DruzhokBot.Tests;

public class CoreBotTests
{
    private readonly Mock<ITelegramBotClientWrapper> _telegramBotClientWrapperMock;
    private readonly Mock<IBotLogger> _botLoggerMock;
    private readonly Mock<IAttackDetector> _attackDetectorMock;

    public CoreBotTests()
    {
        _telegramBotClientWrapperMock = new Mock<ITelegramBotClientWrapper>();
        _botLoggerMock = new Mock<IBotLogger>();
        _attackDetectorMock = new Mock<IAttackDetector>();

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
        _attackDetectorMock.Object);

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
}
