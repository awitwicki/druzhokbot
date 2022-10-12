using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace druzhokbot.DTO
{
    internal class UserBanQueueDTO
    {
        public Chat Chat { get; set; }
        public User User { get; set; }
        public long ChatId => Chat.Id;
        public long UserId => User.Id;
    }
}
