using DruzhokBot.Domain.DTO;
using Telegram.Bot.Types;

namespace DruzhokBot.Domain.Interfaces;

public interface IBotLogger
{
     Task LogUserJoined(User user, Chat chat);
     Task LogUserVerified(User user, Chat chat);
     Task LogUserBanned(UserBanQueueDto userBanDto);
}
