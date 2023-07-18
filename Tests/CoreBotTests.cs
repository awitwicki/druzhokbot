using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using druzhokbot;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Domain;
using DruzhokBot.Domain.Interfaces;
using Moq;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;
using Tests.TestData;

namespace Tests;

public class CoreBotTests
{
    private readonly Mock<ITelegramBotClientWrapper> _telegramBotClientWrapperMock;
    
    public CoreBotTests()
    {
        _telegramBotClientWrapperMock = new Mock<ITelegramBotClientWrapper>();
         
        _telegramBotClientWrapperMock
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Username = "abc"});

        _telegramBotClientWrapperMock
            .Setup(c => c.SendTextMessageAsync(
                It.IsAny<ChatId>(),
                It.IsAny<string>(),
                It.IsAny<ParseMode?>(),
                It.IsAny<IEnumerable<MessageEntity>>(),
                It.IsAny<bool?>(),
                It.IsAny<bool?>(),
                It.IsAny<int?>(),
                It.IsAny<bool?>(),
                It.IsAny<IReplyMarkup>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(default(Message));
    }

    [Fact]
    public async Task OnStartMessage_ShouldResponseWithHelloMessage()
    {
        // Arrange
        var coreBot = new CoreBot(_telegramBotClientWrapperMock.Object);
        var update = UpdateTestData.StartMessage();

        // Act
        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken());

        // Assert
        _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                update.Message.Chat.Id,
                TextResources.StartMessage,
                It.IsAny<ParseMode>(),
                It.IsAny<IEnumerable<MessageEntity>>(),
                It.IsAny<bool?>(),
                It.IsAny<bool?>(),
                It.IsAny<int?>(),
                It.IsAny<bool?>(),
                It.IsAny<IReplyMarkup>(),
                It.IsAny<CancellationToken>()),
            Times.Once());

        Assert.True(_telegramBotClientWrapperMock.Invocations.Count == 3);
    }

    [Fact]
    public async Task OnNewUser_ShouldRemoveUserJoinMessageAndSendCaptcha()
    {
        // Arrange
         var coreBot = new CoreBot(_telegramBotClientWrapperMock.Object);

         const long userJoinedId = 1;
         const long userSenderId = 1;
         const int chatId = 1;

         var update = UpdateTestData.UserAddedOtherUser(userJoinedId, userSenderId, chatId);
         
         // Act
         await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken());
        
         // Assert
         // Deleted user Join message
         _telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                 chatId,
                 update.Message.MessageId,
                 It.IsAny<CancellationToken>()),
             Times.Once());
         
         // Send captcha
         _telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                 It.IsAny<ChatId>(),
                 It.IsAny<string>(),
                 ParseMode.Markdown,
                 It.IsAny<IEnumerable<MessageEntity>>(),
                 It.IsAny<bool?>(),
                 It.IsAny<bool?>(),
                 It.IsAny<int?>(),
                 It.IsAny<bool?>(),
                 It.IsNotNull<IReplyMarkup>(),
                 It.IsAny<CancellationToken>()),
             Times.Once());

         Assert.True(true);
    }

    [Fact]
    public async Task OnNewUser_ShouldRemoveUnverifiedUserMessagesAndNotRemoveOtherUsersMessages()
    {
        // Arrange
        var coreBot = new CoreBot(_telegramBotClientWrapperMock.Object);

        const long newUserId = 1;
        const long oldUserId = 2;
        const int chatId = 1;
         
        var updateUserJoined = UpdateTestData.UserJoined(newUserId, chatId);
        var messages = UpdateTestData.RandomMessagesFromTwoUsersInSingleChat(newUserId, oldUserId, chatId);

        var oldUserMessagesIds = messages.Where(x => x.Message!.From!.Id == oldUserId)
            .Select(x => x.Message.MessageId).ToArray();

        // 1 is user joined event
        var expectedMessageRemovedCount = 1 + messages.Count(x => x.Message!.From!.Id == newUserId);
        
        // Act
        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, updateUserJoined, new CancellationToken());
        Thread.Sleep(500);
        
        foreach (var msg in messages)
        {
            await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, msg, new CancellationToken());
            Thread.Sleep(500);
        }
        
        // Assert
        _telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                chatId,
                It.IsNotIn(oldUserMessagesIds),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedMessageRemovedCount));

        Assert.True(true);
    }
    
    [Fact]
    public async Task OnNewUser_WithTwoChats_ShouldRemoveUnverifiedUserMessagesOnlyInOneChat()
    {
        // Arrange
        var coreBot = new CoreBot(_telegramBotClientWrapperMock.Object);

        const long newUserId = 1;
        const long oldUserId = 2;
        const int firstChatId = 1;
        const int secondChatId = 2;
         
        var updateUserJoined = UpdateTestData.UserJoined(newUserId, firstChatId);
        var messagesInFirstChat = UpdateTestData.RandomMessagesFromTwoUsersInSingleChat(newUserId, newUserId, firstChatId);
        var messagesInSecondChat = UpdateTestData.RandomMessagesFromTwoUsersInSingleChat(newUserId, oldUserId, secondChatId);

        // 1 is user joined event
        var expectedMessageRemovedCount = 1 + messagesInFirstChat.Length;
        
        // Act
        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, updateUserJoined, new CancellationToken());
        Thread.Sleep(500);
        
        foreach (var msg in messagesInFirstChat.Union(messagesInSecondChat))
        {
            await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, msg, new CancellationToken());
            Thread.Sleep(500);
        }
        
        // Assert
        _telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                firstChatId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedMessageRemovedCount));

        Assert.True(true);
    }

    [Fact]
    public async Task UserSucceedVerification_ShouldStopRemoveUserMessagesAfterUserVerification()
    {
        // Arrange
        var coreBot = new CoreBot(_telegramBotClientWrapperMock.Object);

        const long userId = 1;
        const int chatId = 1;

        var callbackQueryData = CallbackDataStringBuilder.BuildNewUserCallbackData(userId);
        var updateUserJoined = UpdateTestData.UserJoined(userId, chatId);
        var userMessageExpectedToBeDeleted = UpdateTestData.RandomMessage(userId, chatId, 4);
        var updateUserVerified = UpdateTestData.UserCallbackQuery(userId, chatId, 2, callbackQueryData);
        var userMessage = UpdateTestData.RandomMessage(userId, chatId, 3);


        // Removes user joined event, user message, captcha (after verification)
        var expectedMessageRemovedCount = 3;

        // Act
        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, updateUserJoined,
            new CancellationToken());
        Thread.Sleep(500);

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, userMessageExpectedToBeDeleted,
            new CancellationToken());
        Thread.Sleep(500);

        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, updateUserVerified);
        Thread.Sleep(500);

        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, userMessage, new CancellationToken());
        Thread.Sleep(500);

        // Assert
        _telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                chatId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedMessageRemovedCount));
        
        _telegramBotClientWrapperMock.Verify(mock => mock.AnswerCallbackQueryAsync(
                updateUserVerified.Id,
                TextResources.VerificationSuccessfull,
                true,
                null,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        Assert.True(true);
    }

    [Fact]
    public async Task UserFailedVerification_ShouldRemoveUserFromChat()
    {
        // Arrange
        var coreBot = new CoreBot(_telegramBotClientWrapperMock.Object);

        const long userId = 1;
        const int chatId = 1;

        var callbackQueryData = CallbackDataStringBuilder.BuildBanUserCallbackData(userId);
        var updateUserJoined = UpdateTestData.UserJoined(userId, chatId);
        var updateUserVerifyFail = UpdateTestData.UserCallbackQuery(userId, chatId, 2, callbackQueryData);

        // Removes user joined event, captcha (after verification)
        var expectedMessageRemovedCount = 2;

        // Act
        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, updateUserJoined,
            new CancellationToken());
        Thread.Sleep(500);

        await coreBot.BotOnCallbackQueryReceived(_telegramBotClientWrapperMock.Object, updateUserVerifyFail);
        Thread.Sleep(500);

        // Assert
        _telegramBotClientWrapperMock.Verify(mock => mock.BanChatMemberAsync(
                chatId,
                userId,
                It.IsAny<DateTime?>(),
                It.IsAny<bool?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));
        
        _telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                chatId,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(expectedMessageRemovedCount));
        
        _telegramBotClientWrapperMock.Verify(mock => mock.AnswerCallbackQueryAsync(
                updateUserVerifyFail.Id,
                TextResources.VerificationFailed,
                true,
                null,
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        Assert.True(true);
    }
}
