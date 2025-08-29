using DruzhokBot.Domain.DTO;
using Telegram.Bot.Types;

namespace DruzhokBot.Domain.Interfaces;

public interface IBotLogger
{
     public Task LogUserJoined(User user, Chat chat);
     public Task LogUserVerified(User user, Chat chat);
     public Task LogUserBanned(UserBanQueueDto userBanDto);
     public Task LogRemoveSpam(Message message);
}
