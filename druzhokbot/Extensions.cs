using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace druzhokbot
{
    public static class TelegramUserExtensions
    {
        public static string GetUserMention(this User user) =>
            user.Username;
    }
}
