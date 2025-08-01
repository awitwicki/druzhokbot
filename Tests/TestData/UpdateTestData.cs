using System;
using DruzhokBot.Domain;
using Telegram.Bot.Types;

namespace Tests.TestData;

public static class UpdateTestData
{
    public static Update StartMessage(int chatId = 1, long userSenderId = 1)
    {
        return new Update
        {
            Message = new Message
            {
                Id = new Random().Next(1, 1000),
                Date = DateTime.Now,
                Chat = new Chat
                {
                    Id = chatId
                },
                From = new User
                {
                    Id = userSenderId
                },
                Text = Consts.StartCommand
            }
        };
    }
    
    public static Update UserAddedOtherUser(long userJoinedId, long userSenderId, int chatId)
    {
        return new Update
        {
            Message = MessageTestData.UserAddedOtherUserMessage(userJoinedId, userSenderId, chatId)
        };
    }

    public static Update UserJoined(long userJoinedId, int chatId)
    {
        return new Update
        {
            Message = MessageTestData.UserJoinedMessage(userJoinedId, chatId)
        };
    }
    
    public static Update RandomMessage(long userId, int chatId, int messageId)
    {
        return new Update
        {
            Message = MessageTestData.RandomMessage(userId, chatId, messageId)
        };
    }

    public static Update[] RandomMessagesFromTwoUsersInSingleChat(long firstUserId, long secondUserId, int chatId)
    {
        return new Update[]
        {
            RandomMessage(firstUserId, chatId, 3),
            RandomMessage(secondUserId, chatId, 4),
            RandomMessage(firstUserId, chatId, 5),
            RandomMessage(secondUserId, chatId, 6),
            RandomMessage(firstUserId, chatId, 7),
            RandomMessage(secondUserId, chatId, 8),
        };
    }
    
    public static CallbackQuery UserCallbackQuery(long userId, int chatId, string callbackQueryData)
    {
        return new CallbackQuery
        {
            Id = "123",
            Message = new Message
            {
                Id = 56,
                Chat = new Chat
                {
                    Id = chatId,
                    Title = "Lorem ipsum"
                }
            },
            Data = callbackQueryData,
            From = new User
            {
                Id = userId,
                Username = "Lorem_ipsum"
            }
        };
    }
}
