using DruzhokBot.Domain.DTO;
using Telegram.Bot.Types;

namespace Tests.TestData;

public static class UserBanQueueDtoTestData
{
    public static UserBanQueueDto UserBanQueueDto(int chatId, long userId)
    {
        return new UserBanQueueDto
        {
            Chat = new Chat
            {
                Id = chatId,
                Title = "Lorem ipsum"
            },
            User = new User
            {
                Id = userId,
                Username = "lorem"
            }
        };
    }
}
