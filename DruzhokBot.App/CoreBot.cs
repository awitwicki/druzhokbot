using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
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
    internal readonly ConcurrentDictionary<long, int> AttackStartMessageIds = new();
    private readonly IBotLogger _botLogger;
    private readonly IAttackDetector _attackDetector;
    private readonly IChatSettingsRepository _chatSettingsRepository;
    private readonly string? _botUsername;

    public CoreBot(
        ITelegramBotClientWrapper botClientWrapper,
        IBotLogger? botLogger = null,
        IAttackDetector? attackDetector = null,
        IChatSettingsRepository? chatSettingsRepository = null)
    {
        _botLogger = botLogger ?? new BotLogger();
        _botClientWrapper = botClientWrapper;
        _attackDetector = attackDetector ?? new AttackDetector();
        _chatSettingsRepository = chatSettingsRepository ?? new ChatSettingsRepository(Consts.DefaultDbPath);

        _botClientWrapper.DropPendingUpdates().GetAwaiter().GetResult();

        _botClientWrapper.SubscribeHandlers(HandleUpdateAsync, HandleErrorAsync);

        var me = _botClientWrapper.GetMeAsync().GetAwaiter().GetResult();
        _botUsername = me.Username;

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

            if (update.Type == UpdateType.Message && TryParseCommand(update.Message!.Text, out var command, out var argument)
                && IsGreetingCommand(command))
            {
                await OnGreetingCommand(botClient, update.Message, command, argument, cancellationToken);
            }

            if (update.ChatMember?.NewChatMember.Status == ChatMemberStatus.Member
                && update.ChatMember.OldChatMember?.IsInChat != true)
            {
                await OnNewUser(botClient, update.ChatMember.NewChatMember.User, update, update.ChatMember.Chat,
                    update.ChatMember.ViaJoinRequest, cancellationToken);
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
            if (!userBanDto.ViaJoinRequest && _attackDetector.IsAngryModeActive(userBanDto.ChatId))
            {
                await botClient.BanChatMemberAsync(userBanDto.ChatId, userBanDto.UserId);
                var state = _attackDetector.RegisterBanInAngryMode(userBanDto.ChatId);

                if (state is { BannedCount: 1 })
                {
                    var startMessage = await botClient.SendTextMessageAsync(
                        chatId: userBanDto.ChatId,
                        text: TextResources.ChatUnderAttackMessage);
                    AttackStartMessageIds[userBanDto.ChatId] = startMessage.MessageId;
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
        bool viaJoinRequest, CancellationToken cancellationToken)
    {
        try
        {
            await _botLogger.LogUserJoined(user, chat);

            if (user.IsBot)
                return;

            int captchaTimeoutSeconds;

            if (viaJoinRequest)
            {
                captchaTimeoutSeconds = Consts.CaptchaTimeoutSecondsJoinRequest;
            }
            else
            {
                var angryModeTriggered = _attackDetector.RegisterJoin(chat.Id);

                if (angryModeTriggered)
                {
                    _ = RunAngryModeLifetime(botClient, chat.Id, cancellationToken);
                }

                var inAngryMode = angryModeTriggered || _attackDetector.IsAngryModeActive(chat.Id);
                captchaTimeoutSeconds = inAngryMode
                    ? Consts.CaptchaTimeoutSecondsAngryMode
                    : Consts.CaptchaTimeoutSecondsNormal;
            }

            var captchaTimeout = TimeSpan.FromSeconds(captchaTimeoutSeconds);

            var userId = user.Id;
            var userMention = user.GetUserMention();
            var key = (userId, chat.Id);

            var challenge = CaptchaChallengeBuilder.Build(userId, chat.Id, captchaTimeout);
            var userBanDto = new UserBanQueueDto
                { Chat = chat, User = user, Challenge = challenge, ViaJoinRequest = viaJoinRequest };

            if (!UsersBanQueue.TryAdd(key, userBanDto))
                return;

            var keyboardMarkup = CaptchaKeyboardBuilder.BuildCaptchaKeyboard(challenge);
            var targetName = EmojiPool.GetUkrainianName(challenge.TargetEmoji);
            var responseText = viaJoinRequest
                ? string.Format(TextResources.NewUserVerificationMessageJoinRequest, userMention, targetName)
                : string.Format(TextResources.NewUserVerificationMessage, userMention, targetName, captchaTimeoutSeconds);

            Thread.Sleep(2 * 1000);

            var helloMessage = await botClient.SendTextMessageAsync(
                chatId: chat.Id,
                text: responseText,
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboardMarkup,
                cancellationToken: cancellationToken);

            await Task.Delay(captchaTimeout, cancellationToken);

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

            if (AttackStartMessageIds.TryRemove(chatId, out var startMessageId))
            {
                try
                {
                    await botClient.DeleteMessageAsync(chatId, startMessageId, cancellationToken);
                }
                catch
                {
                }
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

                await SendGreetingIfEnabled(botClient, user, chat);
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

    private bool TryParseCommand(string? text, out string command, out string argument)
    {
        command = string.Empty;
        argument = string.Empty;

        if (string.IsNullOrEmpty(text) || text[0] != '/')
            return false;

        var separatorIndex = text.IndexOfAny(new[] { ' ', '\n', '\t' });
        var token = separatorIndex < 0 ? text : text[..separatorIndex];
        argument = separatorIndex < 0 ? string.Empty : text[(separatorIndex + 1)..].Trim();

        var atIndex = token.IndexOf('@');
        if (atIndex >= 0)
        {
            var mentionedBot = token[(atIndex + 1)..];
            if (!string.Equals(mentionedBot, _botUsername, StringComparison.OrdinalIgnoreCase))
                return false;

            token = token[..atIndex];
        }

        command = token.ToLowerInvariant();
        return true;
    }

    private static bool IsGreetingCommand(string command) => command is Consts.GreetingCommand
        or Consts.GreetingOnCommand
        or Consts.GreetingOffCommand
        or Consts.GreetingSetCommand
        or Consts.GreetingResetCommand;

    private async Task OnGreetingCommand(ITelegramBotClientWrapper botClient, Message message, string command,
        string argument, CancellationToken cancellationToken)
    {
        try
        {
            var chat = message.Chat;

            if (chat.Type is not (ChatType.Group or ChatType.Supergroup))
                return;

            if (!await IsChatAdmin(botClient, message, cancellationToken))
            {
                await Reply(botClient, message, TextResources.OnlyAdminsCanChangeSettingsMessage, cancellationToken);
                return;
            }

            string response;

            switch (command)
            {
                case Consts.GreetingOnCommand:
                    await _chatSettingsRepository.SetGreetingEnabledAsync(chat.Id, true, cancellationToken);
                    response = TextResources.GreetingEnabledMessage;
                    break;
                case Consts.GreetingOffCommand:
                    await _chatSettingsRepository.SetGreetingEnabledAsync(chat.Id, false, cancellationToken);
                    response = TextResources.GreetingDisabledMessage;
                    break;
                case Consts.GreetingSetCommand when string.IsNullOrWhiteSpace(argument):
                    response = TextResources.GreetingSetUsageMessage;
                    break;
                case Consts.GreetingSetCommand when argument.Length > Consts.GreetingTextMaxLength:
                    response = string.Format(TextResources.GreetingTextTooLongMessage, Consts.GreetingTextMaxLength);
                    break;
                case Consts.GreetingSetCommand:
                    await _chatSettingsRepository.SetGreetingTextAsync(chat.Id, argument, cancellationToken);
                    response = TextResources.GreetingTextSavedMessage;
                    break;
                case Consts.GreetingResetCommand:
                    await _chatSettingsRepository.SetGreetingTextAsync(chat.Id, null, cancellationToken);
                    response = TextResources.GreetingTextResetMessage;
                    break;
                default:
                    var settings = await _chatSettingsRepository.GetAsync(chat.Id, cancellationToken);
                    var status = settings?.GreetingEnabled == true
                        ? TextResources.GreetingStatusEnabled
                        : TextResources.GreetingStatusDisabled;
                    var text = string.IsNullOrWhiteSpace(settings?.GreetingText)
                        ? TextResources.DefaultGreetingMessage
                        : settings.GreetingText;
                    response = string.Format(TextResources.GreetingStatusMessage, status, text);
                    break;
            }

            await Reply(botClient, message, response, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
    }

    private static async Task<bool> IsChatAdmin(ITelegramBotClientWrapper botClient, Message message,
        CancellationToken cancellationToken)
    {
        // Anonymous admins post on behalf of the group itself.
        if (message.SenderChat?.Id == message.Chat.Id)
            return true;

        if (message.From == null)
            return false;

        var member = await botClient.GetChatMemberAsync(message.Chat.Id, message.From.Id, cancellationToken);
        return member.Status is ChatMemberStatus.Administrator or ChatMemberStatus.Creator;
    }

    private static Task<Message> Reply(ITelegramBotClientWrapper botClient, Message message, string text,
        CancellationToken cancellationToken)
    {
        return botClient.SendTextMessageAsync(
            chatId: message.Chat.Id,
            text: text,
            replyToMessageId: message.MessageId,
            cancellationToken: cancellationToken);
    }

    private async Task SendGreetingIfEnabled(ITelegramBotClientWrapper botClient, User user, Chat chat)
    {
        try
        {
            var settings = await _chatSettingsRepository.GetAsync(chat.Id);

            if (settings is not { GreetingEnabled: true })
                return;

            var template = string.IsNullOrWhiteSpace(settings.GreetingText)
                ? TextResources.DefaultGreetingMessage
                : settings.GreetingText;

            await botClient.SendTextMessageAsync(
                chatId: chat.Id,
                text: BuildGreetingHtml(template, user),
                parseMode: ParseMode.Html);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
    }

    internal static string BuildGreetingHtml(string template, User user)
    {
        var mention = $"<a href=\"tg://user?id={user.Id}\">{WebUtility.HtmlEncode(user.GetUserFullName().Trim())}</a>";

        return string.Join(mention, template.Split(Consts.GreetingUserPlaceholder).Select(WebUtility.HtmlEncode));
    }
}
