using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace druzhokbot
{
    internal class CoreBot
    {
        public TelegramBotClient botClient { get; set; }
        
        public object locked { get; set; }

        private ConcurrentBag<long> usersBanQueue = new ConcurrentBag<long>();

        public CoreBot(string botToken)
        {
            botClient = new TelegramBotClient(botToken);

            // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = { } // receive all update types
            };

            botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions);

            var me = botClient.GetMeAsync().GetAwaiter().GetResult();

            Console.WriteLine($"Start listening druzhokbot for @{me.Username}");
        }
        async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // Ignore old updates
                if (update.Message?.Date.AddSeconds(60) < DateTime.UtcNow)
                {
                    return;
                }

                // Start bot, get info
                if (update.Type == UpdateType.Message && update.Message?.Text == "/start")
                {
                    await OnStart(botClient, update, cancellationToken);
                }

                // New user in chat
                if (update.Message?.Type == MessageType.ChatMembersAdded)
                {
                    // Process each new user in chat
                    foreach (var newUser in update.Message.NewChatMembers)
                    {
                        Task t = Task.Run(async () =>
                        {
                            await OnNewUser(botClient, newUser, update, cancellationToken);
                        });
                    }

                    // Delete "User joined" message, but some other bots already deleted this
                    try
                    {
                        await botClient.DeleteMessageAsync(update.Message.Chat.Id, update.Message.MessageId);
                    }
                    catch { }
                }

                // User leave chat
                if (update.Message?.Type == MessageType.ChatMemberLeft)
                {
                    // Delete "User left" message, but some other bots already deleted this
                    try
                    {
                        await botClient.DeleteMessageAsync(update.Message.Chat.Id, update.Message.MessageId);
                    }
                    catch { }
                }

                // Button clicked
                if (update.Type == UpdateType.CallbackQuery)
                {
                    await BotOnCallbackQueryReceived(botClient, update.CallbackQuery);
                }
            }
            catch (Exception exception)
            {
                await HandleErrorAsync(botClient, exception, cancellationToken);
            }
        }

        Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }

        private (string, string) ConvertUserChatName(User user, Chat chat)
        {
            string userFullName = (user.FirstName + " " + user.LastName).Replace(" ", "\\ ").Replace("=", "\\=");
            string chatTitle = (chat.Title).Replace(" ", "\\ ").Replace("=", "\\=");

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

        private async Task OnStart(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            long chatId = update.Message.Chat.Id;
            string responseText = "Привет, меня зовут Дружок!\nДобавь меня в свой чат, дай права админа, и я буду проверять чтобы группа всегда была защищена от спам-ботов.";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: responseText,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);
        }

        private async Task KickUser(ITelegramBotClient botClient, User user, Chat chat)
        {
            // Check if user if actually exists in queue to ban
            long userId = user.Id;
            bool userInQueueToBan = usersBanQueue.TryTake(out userId);

            // Ban user
            if (userInQueueToBan)
            {
                await botClient.BanChatMemberAsync(chat.Id, user.Id, DateTime.Now.AddSeconds(45));
            
                // Log user banned
                LogUserBanned(user, chat);
            }
        }
        
        private async Task OnNewUser(ITelegramBotClient botClient, User user, Update update, CancellationToken cancellationToken)
        {
            // Ignore bots
            if (user.IsBot)
            {
                return;
            }

            // Get user info
            long userId = user.Id;
            string userMention = user.GetUserMention();

            // Get chat
            Chat chat = update.Message.Chat;

            // Restrict user
            await botClient.RestrictChatMemberAsync(chat.Id, userId, new ChatPermissions { CanSendMessages = false });

            // Generate captcha keyboard
            InlineKeyboardMarkup keyboardMarkup = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(userId);

            string responseText = $"Добро пожаловать организм {userMention}! Чтобы группа была защищена от ботов, "
                 + "пройдите простую верификацию, нажав на кнопку «🚫🤖» под этим сообщением. "
                 + "Поторопитесь, у вас есть 2 минуты до автоматического кика из чата";

            await botClient.SendTextMessageAsync(
                chatId: chat.Id,
                text: responseText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboardMarkup,
                cancellationToken: cancellationToken);

            // Add user to kick queue
            usersBanQueue.Add(userId);

            // Wait for two minutes
            Thread.Sleep(120 * 1000);

            // Try kick user from chat
            await KickUser(botClient, user, chat);
        }

        private async Task BotOnCallbackQueryReceived(ITelegramBotClient botClient, CallbackQuery callbackQuery)
        {
            // Get user
            User user = callbackQuery.From;
            long userId = user.Id;

            // Get chat
            Chat chat = callbackQuery.Message.Chat;
            long chatId = chat.Id;

            int captchaMessageId = callbackQuery.Message.MessageId;
            long joinRequestUserId = long.Parse(callbackQuery.Data.Split('|').Last());

            // Random user click
            if (userId != joinRequestUserId)
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Robots will rule the world :)", true);
            }
            // Verify user
            else
            {
                string buttonCommand = callbackQuery.Data.Split('|').First();

                // User have successfully verified
                if (buttonCommand == "new_user")
                {   
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Верификация пройдена, кожаный мешок. Добро пожаловать!", true);

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

                    await botClient.RestrictChatMemberAsync(chat.Id, userId, chatPermissions);

                    usersBanQueue.TryTake(out userId);

                    LogUserVerified(user, chat);
                }
                // User have fail verification
                else if (buttonCommand == "ban_user")
                {
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Верификация не пройдена, кожаный мешок. Попробуй через 60 секунд.", true);

                    // Try kick user from chat
                    await KickUser(botClient, user, chat);
                }

                // Delete captcha message
                await botClient.DeleteMessageAsync(chatId, captchaMessageId);
            }
        }
    }
}
