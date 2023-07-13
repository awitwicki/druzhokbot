using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DruzhokBot.Common.Extensions;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Domain;
using DruzhokBot.Domain.DTO;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace druzhokbot;

internal class CoreBot
{
    public TelegramBotClient BotClient { get; set; }

    public object locked { get; set; }

    private ConcurrentBag<UserBanQueueDto> usersBanQueue = new();

    public CoreBot(string botToken)
    {
        BotClient = new TelegramBotClient(botToken);

        // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = { } // receive all update types
        };

        BotClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions);

        var me = BotClient.GetMeAsync().GetAwaiter().GetResult();

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

            // Just normal messages (filter for newbies)
            if (update.Type == UpdateType.Message)
            {
                var userId = update.Message.From.Id;
                var chatId = update.Message.Chat.Id;

                if (usersBanQueue.Any(x => x.UserId == userId && x.ChatId == chatId))
                {
                    try
                    {
                        await botClient.DeleteMessageAsync(update.Message.Chat.Id, update.Message.MessageId);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
            }

            // Start bot, get info
            if (update.Type == UpdateType.Message && update.Message?.Text == Consts.StartCommand)
            {
                await OnStart(botClient, update, cancellationToken);
            }

            // New user in chat
            if (update.Message?.Type == MessageType.ChatMembersAdded)
            {
                // Process each new user in chat
                foreach (var newUser in update.Message.NewChatMembers)
                {
                    var t = Task.Run(async () => { await OnNewUser(botClient, newUser, update, cancellationToken); });
                }

                // Delete "User joined" message, but some other bots already deleted this
                try
                {
                    await botClient.DeleteMessageAsync(update.Message.Chat.Id, update.Message.MessageId);
                }
                catch
                {
                }
            }

            // User leave chat
            if (update.Message?.Type == MessageType.ChatMemberLeft)
            {
                // Delete "User left" message, but some other bots already deleted this
                try
                {
                    await botClient.DeleteMessageAsync(update.Message.Chat.Id, update.Message.MessageId);
                }
                catch
                {
                }
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

    

    private void LogUserJoined(User user, Chat chat)
    {
        (var userFullName, var chatTitle) = UserChatNameHelper.ConvertUserChatName(user, chat);
        var userName = user.Username ?? "none";
        var userId = user.Id;
        var chatId = chat.Id;

        InfluxDbLiteClient.Query(
            $"bots,botname=druzhokbot,chatname={chatTitle},chatusername={chat.Username ?? "null"},chat_id={chatId},user_id={userId},user_name={userName},user_fullname={userFullName} user_joined=1");
    }

    private void LogUserVerified(User user, Chat chat)
    {
        (var userFullName, var chatTitle) = UserChatNameHelper.ConvertUserChatName(user, chat);
        var userName = user.Username ?? "none";
        var userId = user.Id;
        var chatId = chat.Id;

        InfluxDbLiteClient.Query(
            $"bots,botname=druzhokbot,chatname={chatTitle},chatusername={chat.Username ?? "null"},chat_id={chatId},user_id={userId},user_name={userName},user_fullname={userFullName} user_verified=1");
    }

    private void LogUserBanned(UserBanQueueDto userBanDTO)
    {
        (var userFullName, var chatTitle) = UserChatNameHelper.ConvertUserChatName(userBanDTO.User, userBanDTO.Chat);
        var userName = userBanDTO.User.Username ?? "none";
        var userId = userBanDTO.UserId;
        var chatId = userBanDTO.ChatId;

        InfluxDbLiteClient.Query(
            $"bots,botname=druzhokbot,chatname={chatTitle},chat_id={chatId},user_id={userId},user_name={userName},user_fullname={userFullName} user_banned=1");
    }

    private async Task OnStart(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        var chatId = update.Message.Chat.Id;

        var responseText =
            "Привіт, я Дружок!\nДодай мене в свій чат, дай права адміна, і я перевірятиму щоб група була завжди захищена від спам-ботів.";

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: responseText,
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private async Task KickUser(ITelegramBotClient botClient, UserBanQueueDto userBanDTO)
    {
        try
        {
            Console.WriteLine($"Try to kick user {userBanDTO.User.GetUserMention()}");

            // Check if user if actually exists in queue to ban
            var userInQueueToBan = usersBanQueue.TryTake(out userBanDTO);

            // Ban user
            if (userInQueueToBan)
            {
                await botClient.BanChatMemberAsync(userBanDTO.ChatId, userBanDTO.UserId, DateTime.Now.AddSeconds(45));

                // Log user banned
                LogUserBanned(userBanDTO);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private async Task OnNewUser(ITelegramBotClient botClient, User user, Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine(
                $"New user {user.GetUserMention()} has joined chat {update.Message.Chat.Title} ({update.Message.Chat.Id})");

            // Ignore bots
            if (user.IsBot)
            {
                return;
            }

            // Get user info
            var userId = user.Id;
            var userMention = user.GetUserMention();

            // Get chat
            var chat = update.Message.Chat;

            // Ignore continuous joining chat
            if (usersBanQueue.Any(x => x.UserId == userId && x.ChatId == chat.Id))
            {
                return;
            }

            // Generate captcha keyboard
            var keyboardMarkup = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(userId);

            var responseText = $"Ласкаво просимо, {userMention}! Щоб група була захищена від ботів, "
                               + "пройдіть просту верифікацію. Натисніть на кнопку «🚫🤖» під цим повідомленням. "
                               + "Поспішіть, у вас є 90 секунд до автоматичного виліту з чату.";

            var helloMessage = await botClient.SendTextMessageAsync(
                chatId: chat.Id,
                text: responseText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboardMarkup,
                cancellationToken: cancellationToken);

            // Add user to kick queue
            var userBanDTO = new UserBanQueueDto
            {
                Chat = chat,
                User = user
            };

            usersBanQueue.Add(userBanDTO);

            // Wait for two minutes
            Thread.Sleep(90 * 1000);

            // Try kick user from chat
            await KickUser(botClient, userBanDTO);

            // Try to delete hello message
            try
            {
                await botClient.DeleteMessageAsync(helloMessage.Chat.Id, helloMessage.MessageId);
            }
            catch
            {
            }
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
            var user = callbackQuery.From;
            var userId = user.Id;

            // Get chat
            var chat = callbackQuery.Message.Chat;
            var chatId = chat.Id;

            var captchaMessageId = callbackQuery.Message.MessageId;
            var joinRequestUserId = long.Parse(callbackQuery.Data.Split('|').Last());

            LogUserJoined(user, chat);

            // Random user click
            if (userId != joinRequestUserId)
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Robots will rule the world :)", true);
            }
            // Verify user
            else
            {
                var userBanDTO = usersBanQueue.First(x => x.UserId == userId && x.ChatId == chatId);

                var buttonCommand = callbackQuery.Data.Split('|').First();

                // User have successfully verified
                if (buttonCommand == "new_user")
                {
                    Console.WriteLine(
                        $"User {user.GetUserMention()} have successfully verified chat {chat.Title} ({chat.Id})");

                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Верифікація пройдена. Ласкаво просимо!",
                        true);

                    usersBanQueue.TryTake(out userBanDTO);

                    LogUserVerified(user, chat);
                }
                // User have fail verification
                else if (buttonCommand == "ban_user")
                {
                    Console.WriteLine(
                        $"User {user.GetUserMention()} have unsuccessfully verified chat {chat.Title} ({chat.Id}) and gets banned");

                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                        "Верифікація не пройдена. Спробуйте пройти ще раз через 5 хвилин.", true);

                    // Try kick user from chat
                    await KickUser(botClient, userBanDTO);
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
