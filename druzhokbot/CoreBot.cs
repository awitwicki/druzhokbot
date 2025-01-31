using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DruzhokBot.Common.Extensions;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Common.Services;
using DruzhokBot.Domain;
using DruzhokBot.Domain.DTO;
using DruzhokBot.Domain.Interfaces;
using NLog;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace druzhokbot;

public class CoreBot
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly ITelegramBotClientWrapper _botClientWrapper;
    internal readonly ConcurrentBag<UserBanQueueDto> UsersBanQueue = new();
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

        Logger.Info(string.Format(LogTemplates.StartListeningDruzhoBbot, me.Username));
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

                if (UsersBanQueue.Any(x => x.UserId == userId && x.ChatId == chatId))
                {
                    try
                    {
                        await botClient.DeleteMessageAsync(update.Message.Chat.Id, update.Message.MessageId);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex);
                    }
                }
                // Is comment on channel
                else if (update.Message?.ReplyToMessage?.SenderChat?.Type is ChatType.Channel) 
                {
                    // Regex check if message text or caption contains url "opensea.io
                    const string regexPattern = @"opensea\.io";
                    var messageText = update.Message.Text ?? update.Message.Caption;
                    if (!string.IsNullOrEmpty(messageText) && Regex.IsMatch(messageText, regexPattern, RegexOptions.Compiled))
                    {
                        try
                        {
                            await botClient.DeleteMessageAsync(update.Message.Chat.Id, update.Message.MessageId);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex);
                        }
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

        Logger.Error(errorMessage);
        Logger.Error(exception.StackTrace);

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
            var msg = string.Format(LogTemplates.TryToKickUser, userBanDto.User.GetUserMention());
            Logger.Info(msg);
            
            // Check if user if actually exists in queue to ban
            var userInQueueToBan = UsersBanQueue.TryTake(out userBanDto);

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
            Logger.Error(ex);
        }
    }

    private async Task OnNewUser(ITelegramBotClientWrapper botClient, User user, Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            var msg = string.Format(LogTemplates.NewUserJoinedChat, user.GetUserMention(), update.Message!.Chat.Title, update.Message.Chat.Id);
            Logger.Info(msg);
            
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
            if (UsersBanQueue.Any(x => x.UserId == userId && x.ChatId == chat.Id))
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

            UsersBanQueue.Add(userBanDto);

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
            Logger.Error(ex);
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
                var userBanDto = UsersBanQueue.First(x => x.UserId == userId && x.ChatId == chatId);

                var buttonCommand = callbackQuery.Data.Split('|').First();

                // User have successfully verified
                if (buttonCommand == Consts.NewUserString)
                {
                    await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, TextResources.VerificationSuccessfull, true);

                    UsersBanQueue.TryTake(out userBanDto);

                    await _botLogger.LogUserVerified(user, chat);
                }
                // User have fail verification
                else if (buttonCommand == Consts.BanUserString)
                {
                    var msg = string.Format(LogTemplates.VerificationFailed, user.GetUserMention(), chat.Title, chat.Id);
                    Logger.Info(msg);
                    
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
            Logger.Error(ex);
        }
    }
}
