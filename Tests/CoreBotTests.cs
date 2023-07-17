using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using druzhokbot;
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
         var coreBot = new CoreBot(_telegramBotClientWrapperMock.Object);

         const long userJoinedId = 1;
         const long userSenderId = 1;
         const int chatId = 1;

         var update = UpdateTestData.UserAddedOtherUser(userJoinedId, userSenderId, chatId);
         
         // Act
         await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, update, new CancellationToken());
        
         // Deletes user Join message
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

        const long userId = 1;
        const int chatId = 1;
        const int message1Id = 1;
        const int message2Id = 2;
        const int message3Id = 3;
        const long userJoinedId = 1;
         
        var updateUserJoined = UpdateTestData.UserJoined(userJoinedId, chatId);
        var updateMessage1 = UpdateTestData.RandomMessage(userId, chatId, message1Id);
        var updateMessage2 = UpdateTestData.RandomMessage(userId, chatId, message2Id);
        var updateMessage3 = UpdateTestData.RandomMessage(userId, chatId, message3Id);
         
        // Acts
        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, updateUserJoined, new CancellationToken());
        Thread.Sleep(500);
        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, updateMessage1, new CancellationToken());
        Thread.Sleep(500);
        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, updateMessage2, new CancellationToken());
        Thread.Sleep(500);
        await coreBot.HandleUpdateAsync(_telegramBotClientWrapperMock.Object, updateMessage3, new CancellationToken());
        
        // Deletes userJoin and user messages
        _telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                chatId,
                It.IsIn(updateUserJoined.Message.MessageId, message1Id, message2Id, message3Id),
                It.IsAny<CancellationToken>()),
            Times.Exactly(4));

        Assert.True(true);
    }
}
