using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using druzhokbot;
using DruzhokBot.Domain;
using DruzhokBot.Domain.Interfaces;
using Moq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Xunit;

namespace Tests;

public class CoreBotTests
{
    [Fact]
    public async Task OnStartMessage_ShouldResponseWithHelloMessage()
    {
        var telegramBotClientWrapperMock = new Mock<ITelegramBotClientWrapper>();

        telegramBotClientWrapperMock
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Username = "abc" });

        telegramBotClientWrapperMock
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

        var coreBot = new CoreBot(telegramBotClientWrapperMock.Object);

        var update = new Update
        {
            Message = new Message
            {
                MessageId = 1,
                Date = DateTime.Now,
                Chat = new Chat
                {
                    Id = 1
                },
                From = new User
                {
                    Id = 1
                },
                Text = Consts.StartCommand
            }
        };

        // Act
        await coreBot.HandleUpdateAsync(telegramBotClientWrapperMock.Object, update, new CancellationToken());

        // Assert
        telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
                It.IsAny<ChatId>(),
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

        Assert.True(true);
    }

    [Fact]
    public async Task OnNewUser_ShouldReactProperly()
    {
      var telegramBotClientWrapperMock = new Mock<ITelegramBotClientWrapper>();
         
      telegramBotClientWrapperMock
          .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new User { Username = "abc"});

         telegramBotClientWrapperMock
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
                 
         var coreBot = new CoreBot(telegramBotClientWrapperMock.Object);

         const int newUserMessageId = 1;
         const int newUserChatId = 1;
         
         var update = new Update
         {
            Message = new Message
            {
                MessageId = newUserMessageId,
                Date = DateTime.Now,
                Chat = new Chat
                {
                    Id = newUserChatId
                },
                From = new User
                {
                    Id = 1
                },
                NewChatMembers = new []{ 
                    new User
                    {
                        Id = 2,
                        Username = "Jack"
                    }
                }
            }
         };
         
         // Act
         await coreBot.HandleUpdateAsync(telegramBotClientWrapperMock.Object, update, new CancellationToken());
        
         // Deletes userJoin message
         telegramBotClientWrapperMock.Verify(mock => mock.DeleteMessageAsync(
                 newUserChatId,
                 newUserMessageId,
                 It.IsAny<CancellationToken>()),
             Times.Once());
         
         // Deletes sends verification message
         telegramBotClientWrapperMock.Verify(mock => mock.SendTextMessageAsync(
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
}
