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
            Console.WriteLine(exception.StackTrace);

            return Task.CompletedTask;
        }

        private (string, string) ConvertUserChatName(User user, Chat chat)
        {
            string userFullName = (user.FirstName + " " + user.LastName).Replace(" ", "\\ ").Replace("=", "\\=");
            string chatTitle = (chat.Title).Replace(" ", "\\ ").Replace("=", "\\=");

            return (userFullName, chatTitle);
        }

        private void LogUserJoined(User user, Chat chat)
        {
            (string userFullName, string chatTitle) = ConvertUserChatName(user, chat);
            string userName = user.Username ?? "none";
            long userId = user.Id;
            long chatId = chat.Id;

            InfluxDBLiteClient.Query($"bots,botname=druzhokbot,chatname={chatTitle},chat_id={chatId},user_id={userId},user_name={userName},user_fullname={userFullName} user_joined=1");
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
            string responseText = "Привіт, я Дружок!\nДодай мене в свій чат, дай права адміна, і я провірятиму щоб група була завжди захищена від спам-ботів.";

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: responseText,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken);
        }

        private async Task KickUser(ITelegramBotClient botClient, User user, Chat chat)
        {
            try
            {
                Console.WriteLine($"Try to kick user {user.GetUserMention()}");

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
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private async Task OnNewUser(ITelegramBotClient botClient, User user, Update update, CancellationToken cancellationToken)
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
                long userId = user.Id;
                string userMention = user.GetUserMention();

                // Get chat
                Chat chat = update.Message.Chat;

                // Ignore continuous joining chat
                if (usersBanQueue.Contains(userId))
                {
                    return;
                }

                // Restrict user
                await botClient.RestrictChatMemberAsync(chat.Id, userId, new ChatPermissions { CanSendMessages = false });

                // Generate captcha keyboard
                InlineKeyboardMarkup keyboardMarkup = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(userId);

                string responseText = $"Ласкаво просимо, {userMention}! Щоб група була захищена від ботів, "
                     + "пройдіть просту верифікацію. Натисніть на кнопку «🚫🤖» під цим повідомленням. "
                     + "Поспішіть, у вас є 90 секунд до автоматичного виліту з чату.";

                Message helloMessage = await botClient.SendTextMessageAsync(
                    chatId: chat.Id,
                    text: responseText,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: keyboardMarkup,
                    cancellationToken: cancellationToken);

                // Add user to kick queue
                usersBanQueue.Add(userId);

                // Wait for two minutes
                Thread.Sleep(90 * 1000);

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

        private async Task BotOnCallbackQueryReceived(ITelegramBotClient botClient, CallbackQuery callbackQuery)
        {
            try
            {
                // Get user
                User user = callbackQuery.From;
                long userId = user.Id;

                // Get chat
                Chat chat = callbackQuery.Message.Chat;
                long chatId = chat.Id;

                int captchaMessageId = callbackQuery.Message.MessageId;
                long joinRequestUserId = long.Parse(callbackQuery.Data.Split('|').Last());

                LogUserJoined(user, chat);

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
                        Console.WriteLine($"User {user.GetUserMention()} have successfully verified chat {chat.Title} ({chat.Id})");

                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Верифікація пройдена. Ласкаво просимо!", true);

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
                        Console.WriteLine($"User {user.GetUserMention()} have unsuccessfully verified chat {chat.Title} ({chat.Id}) and gets banned");

                        await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Верифікація не пройдена. Спробуйте пройти ще раз через хвилину.", true);

                        // Try kick user from chat
                        await KickUser(botClient, user, chat);
                    }

                    // Delete captcha message
                    await botClient.DeleteMessageAsync(chatId, captchaMessageId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}
