using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DruzhokBot.Common.Extensions;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Common.Services;
using DruzhokBot.Domain;
using DruzhokBot.Domain.DTO;
using DruzhokBot.Domain.Interfaces;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DruzhokBot.App;

public class CoreBot
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly ITelegramBotClientWrapper _botClientWrapper;
    internal readonly ConcurrentDictionary<(long UserId, long ChatId), UserBanQueueDto> UsersBanQueue = new();
    private readonly IBotLogger _botLogger;
    private readonly IAttackDetector _attackDetector;

    public CoreBot(
        ITelegramBotClientWrapper botClientWrapper,
        IBotLogger? botLogger = null,
        IAttackDetector? attackDetector = null)
    {
        _botLogger = botLogger ?? new BotLogger();
        _botClientWrapper = botClientWrapper;
        _attackDetector = attackDetector ?? new AttackDetector();

        _botClientWrapper.DropPendingUpdates().GetAwaiter().GetResult();

        _botClientWrapper.SubscribeHandlers(HandleUpdateAsync, HandleErrorAsync);

        var me = _botClientWrapper.GetMeAsync().GetAwaiter().GetResult();

        Logger.Info(string.Format(LogTemplates.StartListeningDruzhoBbot, me.Username));
    }

    public async Task HandleUpdateAsync(ITelegramBotClientWrapper botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.Type == UpdateType.Message)
            {
                var userId = update.Message!.From!.Id;
                var chatId = update.Message.Chat.Id;

                if (UsersBanQueue.ContainsKey((userId, chatId)))
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
                else if (update.Message?.ReplyToMessage?.SenderChat?.Type is ChatType.Channel)
                {
                    var messageText = update.Message.Text ?? update.Message.Caption;
                    if (!string.IsNullOrEmpty(messageText) && SpamChecker.IsSpam(messageText))
                    {
                        try
                        {
                            await botClient.DeleteMessageAsync(update.Message.Chat.Id, update.Message.MessageId);
                            await _botLogger.LogRemoveSpam(update.Message);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex);
                        }
                    }
                }
            }

            if (update.Type == UpdateType.Message && update.Message?.Text == Consts.StartCommand)
            {
                await OnStart(botClient, update, cancellationToken);
            }

            if (update.ChatMember?.NewChatMember.Status == ChatMemberStatus.Member)
            {
                await OnNewUser(botClient, update.ChatMember.NewChatMember.User, update, update.ChatMember.Chat, cancellationToken);
            }

            if (update.Message?.Type == MessageType.NewChatMembers)
            {
                try
                {
                    await botClient.DeleteMessageAsync(update.Message.Chat.Id, update.Message.MessageId);
                }
                catch
                {
                }
            }

            if (update.Message?.Type == MessageType.LeftChatMember)
            {
                try
                {
                    await botClient.DeleteMessageAsync(update.Message.Chat.Id, update.Message.MessageId, cancellationToken);
                }
                catch
                {
                }
            }

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

        var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: string.Format(TextResources.StartMessage, version),
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    private async Task KickUser(ITelegramBotClientWrapper botClient, UserBanQueueDto userBanDto)
    {
        try
        {
            if (_attackDetector.IsAngryModeActive(userBanDto.ChatId))
            {
                await botClient.BanChatMemberAsync(userBanDto.ChatId, userBanDto.UserId);
                var state = _attackDetector.RegisterBanInAngryMode(userBanDto.ChatId);

                if (state is { BannedCount: 1 })
                {
                    await botClient.SendTextMessageAsync(
                        chatId: userBanDto.ChatId,
                        text: TextResources.ChatUnderAttackMessage);
                }
            }
            else
            {
                await botClient.BanChatMemberAsync(userBanDto.ChatId, userBanDto.UserId, DateTime.Now.AddSeconds(45));
            }

            await _botLogger.LogUserBanned(userBanDto);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
    }

    private async Task OnNewUser(ITelegramBotClientWrapper botClient, User user, Update update, Chat chat,
        CancellationToken cancellationToken)
    {
        try
        {
            await _botLogger.LogUserJoined(user, chat);

            if (user.IsBot)
                return;

            var angryModeTriggered = _attackDetector.RegisterJoin(chat.Id);

            if (angryModeTriggered)
            {
                _ = RunAngryModeLifetime(botClient, chat.Id, cancellationToken);
            }

            var inAngryMode = angryModeTriggered || _attackDetector.IsAngryModeActive(chat.Id);
            var captchaTimeout = inAngryMode ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(60);

            var userId = user.Id;
            var userMention = user.GetUserMention();
            var key = (userId, chat.Id);

            var challenge = CaptchaChallengeBuilder.Build(userId, chat.Id, captchaTimeout);
            var userBanDto = new UserBanQueueDto { Chat = chat, User = user, Challenge = challenge };

            if (!UsersBanQueue.TryAdd(key, userBanDto))
                return;

            var keyboardMarkup = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(challenge);
            var targetName = EmojiPool.GetUkrainianName(challenge.TargetEmoji);
            var responseText = string.Format(TextResources.NewUserVerificationMessage, userMention, targetName);

            Thread.Sleep(2 * 1000);

            var helloMessage = await botClient.SendTextMessageAsync(
                chatId: chat.Id,
                text: responseText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboardMarkup,
                cancellationToken: cancellationToken);

            Thread.Sleep(captchaTimeout);

            // If the entry is still in the queue, the user never clicked — treat as timeout.
            if (UsersBanQueue.TryRemove(key, out var timedOutDto))
            {
                await KickUser(botClient, timedOutDto);
            }

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

    private async Task RunAngryModeLifetime(ITelegramBotClientWrapper botClient, long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var finalState = await _attackDetector.StartAngryMode(chatId);

            if (finalState.BannedCount > 0)
            {
                var minutes = (int)Math.Round((finalState.EndTime - finalState.AttackStartTime).TotalMinutes);
                var text = string.Format(TextResources.AttackOverMessage, finalState.BannedCount, minutes);

                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: text,
                    cancellationToken: cancellationToken);
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
            var user = callbackQuery.From;
            var userId = user.Id;
            var chat = callbackQuery.Message!.Chat;
            var chatId = chat.Id;
            var captchaMessageId = callbackQuery.Message.MessageId;

            var data = callbackQuery.Data ?? string.Empty;
            var parts = data.Split('|');

            // Reject malformed or non-captcha payloads (including legacy new_user|… / ban_user|…).
            if (parts.Length != 2 || parts[0] != Consts.CaptchaCallbackPrefix)
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                    TextResources.RandomUserClickedVerifyButtonResponse, true);
                return;
            }

            var token = parts[1];

            // The clicker has no active entry (different user, expired/timed out, or already resolved).
            if (!UsersBanQueue.TryGetValue((userId, chatId), out var userBanDto))
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                    TextResources.RandomUserClickedVerifyButtonResponse, true);
                return;
            }

            var option = userBanDto.Challenge.Options.FirstOrDefault(o => o.Token == token);

            if (option == null)
            {
                // Token not from this challenge — stale or forged. Do not resolve.
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                    TextResources.RandomUserClickedVerifyButtonResponse, true);
                return;
            }

            if (option.IsCorrect)
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                    TextResources.VerificationSuccessfull, true);

                UsersBanQueue.TryRemove((userId, chatId), out _);

                await _botLogger.LogUserVerified(user, chat);
            }
            else
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                    TextResources.VerificationFailed, true);

                if (UsersBanQueue.TryRemove((userId, chatId), out var removedDto))
                {
                    await KickUser(botClient, removedDto);
                }
            }

            await botClient.DeleteMessageAsync(chatId, captchaMessageId);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
    }
}
