using Telegram.Bot.Types;

namespace DruzhokBot.Domain.DTO;

public class UserBanQueueDto
{
    public Chat Chat { get; set; }
    public User User { get; set; }
    public long ChatId => Chat.Id;
    public long UserId => User.Id;
}
