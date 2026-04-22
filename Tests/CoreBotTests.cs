using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using druzhokbot;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Common.Services;
using DruzhokBot.Domain;
using DruzhokBot.Domain.DTO;
using DruzhokBot.Domain.Interfaces;
using Moq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using Tests.TestData;

namespace Tests;

public class CoreBotTests
{
    private readonly Mock<ITelegramBotClientWrapper> _telegramBotClientWrapperMock;
    private readonly Mock<IUserRiskScorer> _riskScorerMock;
    private readonly Mock<IBotLogger> _botLoggerMock;
    private readonly InMemoryCaptchaChallengeStore _captchaStore;

    public CoreBotTests()
    {
        _telegramBotClientWrapperMock = new Mock<ITelegramBotClientWrapper>();
        _riskScorerMock = new Mock<IUserRiskScorer>();
        _botLoggerMock = new Mock<IBotLogger>();
        _captchaStore = new InMemoryCaptchaChallengeStore();

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

        // Default: every user classified Low. Individual tests override when needed.
        _riskScorerMock
            .Setup(s => s.ScoreAsync(It.IsAny<User>(), It.IsAny<ITelegramBotClientWrapper>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserRiskAssessment(UserRiskLevel.Low, ""));
    }

    private CoreBot CreateBot() => new(
        _telegramBotClientWrapperMock.Object,
        _riskScorerMock.Object,
        _captchaStore,
        _botLoggerMock.Object);

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

        // Start OnNewUser in background — it sleeps 92s total; we only verify the SendTextMessageAsync call.
        var task = coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken());

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

        Assert.Contains(coreBot.UsersBanQueue, x => x.UserId == userJoinedId && x.ChatId == chatId);
    }

    [Fact]
    public async Task OnNewUser_HighRisk_AutoBans_AndDoesNotSendCaptcha()
    {
        _riskScorerMock
            .Setup(s => s.ScoreAsync(It.IsAny<User>(), It.IsAny<ITelegramBotClientWrapper>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserRiskAssessment(UserRiskLevel.High, "suspicious_username,no_photo"));

        var coreBot = CreateBot();
        const long userJoinedId = 1;
        const int chatId = 770;
        var update = UpdateTestData.UserJoined(userJoinedId, chatId);

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken());

        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                It.IsAny<ChatId>(),
                It.IsAny<string>(),
                It.IsAny<ParseMode>(),
                It.IsAny<int?>(),
                It.IsNotNull<ReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _telegramBotClientWrapperMock.Verify(mock => mock.BanChatMemberAsync(
                chatId,
                userJoinedId,
                It.IsAny<DateTime?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _botLoggerMock.Verify(l => l.LogUserAutoBanned(
                It.Is<User>(u => u.Id == userJoinedId),
                It.Is<Chat>(c => c.Id == chatId),
                "suspicious_username,no_photo"),
            Times.Once);

        _botLoggerMock.Verify(l => l.LogUserBanned(It.IsAny<UserBanQueueDto>()), Times.Never);
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

        _riskScorerMock.Verify(s => s.ScoreAsync(
                It.IsAny<User>(), It.IsAny<ITelegramBotClientWrapper>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Callback_CorrectToken_Verifies_AndRemovesFromQueues()
    {
        var coreBot = CreateBot();
        const long userId = 1;
        const int chatId = 1;

        coreBot.UsersBanQueue.Add(UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId));
        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        _captchaStore.Add(challenge);
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

        Assert.Null(_captchaStore.TryGet(userId, chatId));
        Assert.Empty(coreBot.UsersBanQueue);
    }

    [Fact]
    public async Task Callback_WrongToken_BansUser()
    {
        var coreBot = CreateBot();
        const long userId = 1;
        const int chatId = 1;

        coreBot.UsersBanQueue.Add(UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId));
        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        _captchaStore.Add(challenge);
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

        Assert.Null(_captchaStore.TryGet(userId, chatId));
    }

    [Fact]
    public async Task Callback_StaleOrForgedToken_TreatedAsRandomUser()
    {
        var coreBot = CreateBot();
        const long userId = 1;
        const int chatId = 1;

        coreBot.UsersBanQueue.Add(UserBanQueueDtoTestData.UserBanQueueDto(chatId, userId));
        var challenge = CaptchaChallengeBuilder.Build(userId, chatId, TimeSpan.FromSeconds(90));
        _captchaStore.Add(challenge);

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

        // Challenge still active — no resolution happened.
        Assert.NotNull(_captchaStore.TryGet(userId, chatId));
    }

    [Fact]
    public async Task Callback_FromDifferentUser_TreatedAsRandomUser()
    {
        var coreBot = CreateBot();
        const long ownerId = 1;
        const long otherId = 99;
        const int chatId = 1;

        coreBot.UsersBanQueue.Add(UserBanQueueDtoTestData.UserBanQueueDto(chatId, ownerId));
        var challenge = CaptchaChallengeBuilder.Build(ownerId, chatId, TimeSpan.FromSeconds(90));
        _captchaStore.Add(challenge);
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

        // Owner's challenge still active; no ban.
        Assert.NotNull(_captchaStore.TryGet(ownerId, chatId));
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

        coreBot.UsersBanQueue.Add(UserBanQueueDtoTestData.UserBanQueueDto(chatId, newUserId));

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
}
