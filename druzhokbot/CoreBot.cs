using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DruzhokBot.Common.Extensions;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Common.Services;
using DruzhokBot.Domain;
using DruzhokBot.Domain.DTO;
using DruzhokBot.Domain.Interfaces;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace druzhokbot;

public class CoreBot
{
    private readonly ITelegramBotClientWrapper _botClientWrapper;
    private readonly ConcurrentBag<UserBanQueueDto> _usersBanQueue = new();
    private readonly IBotLogger _botLogger;

    public CoreBot(ITelegramBotClientWrapper botClientWrapper)
    {
        _botLogger = new BotLogger();
        _botClientWrapper = botClientWrapper;

        // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = { } // receive all update types
        };

        _botClientWrapper.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions);

        var me = _botClientWrapper.GetMeAsync().GetAwaiter().GetResult();

        Console.WriteLine(LogTemplates.StartListeningDruzhoBbot, me.Username);
    }

    public async Task HandleUpdateAsync(ITelegramBotClientWrapper botClient, Update update, CancellationToken cancellationToken)
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
                var userId = update.Message!.From!.Id;
                var chatId = update.Message.Chat.Id;

                if (_usersBanQueue.Any(x => x.UserId == userId && x.ChatId == chatId))
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
                foreach (var newUser in update.Message.NewChatMembers!)
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

    Task HandleErrorAsync(ITelegramBotClientWrapper botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(errorMessage);
        Console.WriteLine(exception.StackTrace);

        return Task.CompletedTask;
    }
    
    private async Task OnStart(ITelegramBotClientWrapper botClient, Update update, CancellationToken cancellationToken)
    {
        var chatId = update.Message!.Chat.Id;

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: TextResources.StartMessage,
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private async Task KickUser(ITelegramBotClientWrapper botClient, UserBanQueueDto userBanDto)
    {
        try
        {
            Console.WriteLine(LogTemplates.TryToKickUser, userBanDto.User.GetUserMention());

            // Check if user if actually exists in queue to ban
            var userInQueueToBan = _usersBanQueue.TryTake(out userBanDto);

            // Ban user
            if (userInQueueToBan)
            {
                await botClient.BanChatMemberAsync(userBanDto.ChatId, userBanDto.UserId, DateTime.Now.AddSeconds(45));

                // Log user banned
                await _botLogger.LogUserBanned(userBanDto);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }

    private async Task OnNewUser(ITelegramBotClientWrapper botClient, User user, Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine(LogTemplates.NewUserJoinedChat, user.GetUserMention(), update.Message!.Chat.Title, update.Message.Chat.Id);

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
            if (_usersBanQueue.Any(x => x.UserId == userId && x.ChatId == chat.Id))
            {
                return;
            }

            // Generate captcha keyboard
            var keyboardMarkup = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(userId);

            var responseText =
                string.Format(TextResources.NewUserVerificationMessage, userMention);

            var helloMessage = await botClient.SendTextMessageAsync(
                chatId: chat.Id,
                text: responseText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboardMarkup,
                cancellationToken: cancellationToken);

            // Add user to kick queue
            var userBanDto = new UserBanQueueDto
            {
                Chat = chat,
                User = user
            };

            _usersBanQueue.Add(userBanDto);

            // Wait for two minutes
            Thread.Sleep(90 * 1000);

            // Try kick user from chat
            await KickUser(botClient, userBanDto);

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

    public async Task BotOnCallbackQueryReceived(ITelegramBotClientWrapper botClient, CallbackQuery callbackQuery)
    {
        try
        {
            // Get user
            var user = callbackQuery.From;
            var userId = user.Id;

            // Get chat
            var chat = callbackQuery.Message!.Chat;
            var chatId = chat.Id;

            var captchaMessageId = callbackQuery.Message.MessageId;
            var joinRequestUserId = long.Parse(callbackQuery.Data!.Split('|').Last());

            await _botLogger.LogUserJoined(user, chat);

            // Random user click
            if (userId != joinRequestUserId)
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                    TextResources.RandomUserClickedVerifyButtonResponse, true);
            }
            // Verify user
            else
            {
                var userBanDto = _usersBanQueue.First(x => x.UserId == userId && x.ChatId == chatId);

                var buttonCommand = callbackQuery.Data.Split('|').First();

                // User have successfully verified
                if (buttonCommand == Consts.NewUserString)
                {
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, TextResources.VerificationSuccessfull, true);

                    _usersBanQueue.TryTake(out userBanDto);

                    await _botLogger.LogUserVerified(user, chat);
                }
                // User have fail verification
                else if (buttonCommand == Consts.BanUserString)
                {
                    Console.WriteLine(LogTemplates.VerificationFailed, user.GetUserMention(), chat.Title, chat.Id);

                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, 
                        TextResources.VerificationFailed, true);

                    // Try kick user from chat
                    await KickUser(botClient, userBanDto);
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
