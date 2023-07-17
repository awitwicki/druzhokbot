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
                MessageId = new Random().Next(1, 1000),
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
}
