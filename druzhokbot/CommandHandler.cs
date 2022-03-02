using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Telegram.Bot;
using System.Threading.Tasks;
using Telegram.Bot.Types.Enums;
using PowerBot.Lite.Attributes;
using PowerBot.Lite.Handlers;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using System.Threading;
using PowerBot.Lite.Utils;

namespace druzhokbot
{
    internal class CommandHandler: BaseHandler
    {
        [MessageReaction(ChatAction.Typing)]
        [MessageHandler("/start")]
        public async Task Start()
        {
            string responseText = "Привет, меня зовут Дружок!\nДобавь меня в свой чат, дай права админа, и я буду проверять чтобы группа всегда была защищена от спам-ботов.";

            await BotClient.SendTextMessageAsync(
                chatId: ChatId,
                text: responseText,
                parseMode: ParseMode.Markdown);
        }

        [MessageReaction(ChatAction.Typing)]
        [MessageHandler("/test")]
        public async Task test()
        {
            OnNewUser(BotClient, User, Update);
        }

        // New user in chat
        [MessageReaction(ChatAction.Typing)]
        [MessageTypeFilter(MessageType.ChatMembersAdded)]
        public async Task OnChatMembersAdded()
        {
            // Process each new user in chat
            foreach (var newUser in Message.NewChatMembers)
            {
                Task t = Task.Run(async () =>
                {
                     await OnNewUser(BotClient, newUser, Update);
                });
            }

            // Delete "User joined" message, but some other bots already deleted this
            try
            {
                await BotClient.DeleteMessageAsync(ChatId, MessageId);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        // User leave chat
        [MessageTypeFilter(MessageType.ChatMemberLeft)]
        public async Task OnChatMemberLeft()
        {
            // Delete "User left" message, but some other bots already deleted this
            try
            {
                await BotClient.DeleteMessageAsync(ChatId, MessageId);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        // Good button callback click, User have successfully verified
        [CallbackQueryHandler("new_user")]
        public async Task OnUserAuthorized()
        {
            long joinRequestUserId = long.Parse(CallbackQuery.Data.Split('|').Last());
            //long userClickedId = CallbackQuery.From.Id;

            // Random user click
            if (User.Id != joinRequestUserId)
            {
                await BotClient.AnswerCallbackQueryAsync(CallbackQuery.Id, "Robots will rule the world :)", true);
                return;
            }

            Console.WriteLine($"User {CallbackQuery.From.GetUserMention()} have successfully verified chat {Chat.Title} ({ChatId})");

            await BotClient.AnswerCallbackQueryAsync(CallbackQuery.Id, "Верификация пройдена, кожаный мешок. Добро пожаловать!", true);

            // Take out ALL user restrictions
            ChatPermissions chatPermissions = new ChatPermissions
            {
                CanSendMessages = true,
                CanSendMediaMessages = true,
                CanSendPolls = true,
                CanSendOtherMessages = true,
                CanAddWebPagePreviews = true,
                CanChangeInfo = true,
                CanInviteUsers = true,
                CanPinMessages = true,
            };

            await BotClient.RestrictChatMemberAsync(ChatId, User.Id, chatPermissions);

            long userId = User.Id;
            AppCache.UsersBanQueue.TryTake(out userId);

            LogUserVerified(User, Chat);

            // Delete captcha message
            await BotClient.DeleteMessageAsync(ChatId, Message.MessageId);
        }

        [CallbackQueryHandler("ban_user")]
        public async Task btntest()
        {
            long joinRequestUserId = long.Parse(CallbackQuery.Data.Split('|').Last());

            // Random user click
            if (User.Id != joinRequestUserId)
            {
                await BotClient.AnswerCallbackQueryAsync(CallbackQuery.Id, "Robots will rule the world :)", true);
                return;
            }

            // User have fail verification
            Console.WriteLine($"User {User.GetUserMention()} have unsuccessfully verified chat {Chat.Title} ({Chat.Id}) and gets banned");

            await BotClient.AnswerCallbackQueryAsync(CallbackQuery.Id, "Верификация не пройдена, кожаный мешок. Попробуй через 60 секунд.", true);

            // Wait
            await Task.Delay(5 * 1000);

            // Try kick user from chat
            await KickUser(BotClient, User, Chat);

            // Delete captcha message
            await BotClient.DeleteMessageAsync(ChatId, Message.MessageId);
        }


        //https://autofac.org/

        private (string, string) ConvertUserChatName(User user, Chat chat)
        {
            string userFullName = (user.FirstName + " " + user.LastName)
                .Replace(" ", "\\ ")
                .Replace("=", "\\=");

            string chatTitle = (chat.Title)
                .Replace(" ", "\\ ")
                .Replace("=", "\\=");

            return (userFullName, chatTitle);
        }

        private void LogUserVerified(User user, Chat chat)
        {
            (string userFullName, string chatTitle) = ConvertUserChatName(user, chat);
            string userName = user.Username ?? "none";
            long userId = user.Id;
            long chatId = chat.Id;

            InfluxDBLiteClient.Query($"bots,botname=druzhokbot,chatname={chatTitle},chat_id={chatId},user_id={userId},user_name={userName},user_fullname={userFullName} user_verified=1");
        }

        private void LogUserBanned(User user, Chat chat)
        {
            (string userFullName, string chatTitle) = ConvertUserChatName(user, chat);
            string userName = user.Username ?? "none";
            long userId = user.Id;
            long chatId = chat.Id;

            InfluxDBLiteClient.Query($"bots,botname=druzhokbot,chatname={chatTitle},chat_id={chatId},user_id={userId},user_name={userName},user_fullname={userFullName} user_banned=1");
        }

        private async Task KickUser(ITelegramBotClient botClient, User user, Chat chat)
        {
            try
            {
                Console.WriteLine($"Try to kick user {user.GetUserMention()}");

                // Check if user if actually exists in queue to ban
                long userId = user.Id;
                bool userInQueueToBan = AppCache.UsersBanQueue.TryTake(out userId);

                // Ban user
                if (userInQueueToBan)
                {
                    await botClient.BanChatMemberAsync(chat.Id, user.Id, DateTime.Now.AddSeconds(45));

                    // Log user banned
                    LogUserBanned(user, chat);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private async Task OnNewUser(ITelegramBotClient botClient, User user, Update update)
        {
            try
            {
                Console.WriteLine($"New user {user.GetUserMention()} has joined chat {update.Message.Chat.Title} ({update.Message.Chat.Id})");

                // Ignore bots
                if (user.IsBot)
                {
                    return;
                }

                // Get user info
                string userMention = user.GetUserMention();

                // Get chat
                Chat chat = update.Message.Chat;

                // Ignore continuous joining chat
                if (AppCache.UsersBanQueue.Contains(User.Id))
                {
                    return;
                }

                // Restrict user
                await botClient.RestrictChatMemberAsync(chat.Id, User.Id, new ChatPermissions { CanSendMessages = false });

                // Generate captcha keyboard
                InlineKeyboardMarkup keyboardMarkup = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(User.Id);

                string responseText = $"Добро пожаловать организм {userMention}! Чтобы группа была защищена от ботов, "
                     + "пройдите простую верификацию, нажав на кнопку «🚫🤖» под этим сообщением. "
                     + "Поторопитесь, у вас есть 90 секунд до автоматического кика из чата";

                Message helloMessage = await botClient.SendTextMessageAsync(
                    chatId: chat.Id,
                    text: responseText,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboardMarkup);

                // Add user to kick queue
                AppCache.UsersBanQueue.Add(User.Id);

                // Wait
                await Task.Delay(90 * 1000);

                // Try kick user from chat
                await KickUser(botClient, user, chat);

                // Try to delete hello message
                try
                {
                    await botClient.DeleteMessageAsync(helloMessage.Chat.Id, helloMessage.MessageId);
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
