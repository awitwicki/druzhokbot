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

namespace druzhokbot;

public class CoreBot
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly ITelegramBotClientWrapper _botClientWrapper;
    internal readonly ConcurrentBag<UserBanQueueDto> UsersBanQueue = new();
    private readonly IBotLogger _botLogger;
    private readonly IUserRiskScorer _riskScorer;
    private readonly ICaptchaChallengeStore _captchaStore;

    public CoreBot(
        ITelegramBotClientWrapper botClientWrapper,
        IUserRiskScorer? riskScorer = null,
        ICaptchaChallengeStore? captchaStore = null,
        IBotLogger? botLogger = null)
    {
        _botLogger = botLogger ?? new BotLogger();
        _botClientWrapper = botClientWrapper;
        _riskScorer = riskScorer ?? new UserRiskScorer();
        _captchaStore = captchaStore ?? new InMemoryCaptchaChallengeStore();

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
            var userInQueueToBan = UsersBanQueue.TryTake(out userBanDto);

            if (userInQueueToBan)
            {
                await botClient.BanChatMemberAsync(userBanDto.ChatId, userBanDto.UserId, DateTime.Now.AddSeconds(45));
                await _botLogger.LogUserBanned(userBanDto);
            }
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

            var userId = user.Id;
            var userMention = user.GetUserMention();

            if (UsersBanQueue.Any(x => x.UserId == userId && x.ChatId == chat.Id))
                return;

            // Heuristic pre-filter: silent ban for obvious bots.
            var assessment = await _riskScorer.ScoreAsync(user, botClient, cancellationToken);
            if (assessment.Level == UserRiskLevel.High)
            {
                try
                {
                    await botClient.BanChatMemberAsync(chat.Id, userId, DateTime.Now.AddSeconds(45));
                    await _botLogger.LogUserAutoBanned(user, chat, assessment.Reason);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
                return;
            }

            var userBanDto = new UserBanQueueDto { Chat = chat, User = user };

            var challenge = CaptchaChallengeBuilder.Build(userId, chat.Id, TimeSpan.FromSeconds(90));
            _captchaStore.Add(challenge);

            var keyboardMarkup = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(challenge);
            var targetName = EmojiPool.GetUkrainianName(challenge.TargetEmoji);
            var responseText = string.Format(TextResources.NewUserVerificationMessage, userMention, targetName);

            UsersBanQueue.Add(userBanDto);

            Thread.Sleep(2 * 1000);

            var helloMessage = await botClient.SendTextMessageAsync(
                chatId: chat.Id,
                text: responseText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboardMarkup,
                cancellationToken: cancellationToken);

            Thread.Sleep(90 * 1000);

            // If challenge is still in the store, the user never clicked — treat as timeout.
            if (_captchaStore.TryGet(userId, chat.Id) != null)
            {
                _captchaStore.Remove(userId, chat.Id);
                await KickUser(botClient, userBanDto);
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
            var challenge = _captchaStore.TryGet(userId, chatId);

            // The clicker has no active challenge (different user, expired, or already resolved).
            if (challenge == null)
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                    TextResources.RandomUserClickedVerifyButtonResponse, true);
                return;
            }

            var option = challenge.Options.FirstOrDefault(o => o.Token == token);

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

                _captchaStore.Remove(userId, chatId);

                var userBanDto = UsersBanQueue.FirstOrDefault(x => x.UserId == userId && x.ChatId == chatId);
                if (userBanDto != null)
                    UsersBanQueue.TryTake(out userBanDto);

                await _botLogger.LogUserVerified(user, chat);
            }
            else
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id,
                    TextResources.VerificationFailed, true);

                _captchaStore.Remove(userId, chatId);

                var userBanDto = UsersBanQueue.FirstOrDefault(x => x.UserId == userId && x.ChatId == chatId);
                if (userBanDto != null)
                    await KickUser(botClient, userBanDto);
            }

            await botClient.DeleteMessageAsync(chatId, captchaMessageId);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
    }
}
