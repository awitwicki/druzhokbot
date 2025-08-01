using System;
using Telegram.Bot.Types;

namespace Tests.TestData;

public static class MessageTestData
{
    public static Message RandomMessage(long userId, int chatId, int messageId)
    {
        return new Message
        {
            Id = messageId,
            Date = DateTime.Now,
            Chat = new Chat
            {
                Id = chatId
            },
            From = new User
            {
                Id = userId
            },
            Text = "Lorem Ipsum"
        };
    }
    
    public static Message UserAddedOtherUserMessage(long userJoinedId, long userSenderId, int chatId)
    {
        return new Message
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
            NewChatMembers = new[]
            {
                new User
                {
                    Id = userJoinedId,
                    Username = "Jack"
                }
            }
        };
    }

    public static Message UserJoinedMessage(long userJoinedId, int chatId)
    {
        return new Message
        {
            Id = new Random().Next(1, 1000),
            Date = DateTime.Now,
            Chat = new Chat
            {
                Id = chatId
            },
            From = new User
            {
                Id = userJoinedId
            },
            NewChatMembers = new[]
            {
                new User
                {
                    Id = userJoinedId,
                    Username = "Jack"
                }
            }
        };
    }
}
