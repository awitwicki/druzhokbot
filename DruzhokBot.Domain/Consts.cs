namespace DruzhokBot.Domain;

public abstract class Consts
{
    public const string StartCommand = "/start";

    public const string CaptchaCallbackPrefix = "captcha";

    public const string LogsDbName = "bots";
    public const string AppLogsTableName = "druzhokbot_logs";

    public const string AppEventType = "event_type";
    public const string AppStarted = "bot_started";
    public const string AppEventTypeNewUser = "user_joined";
    public const string AppEventTypeBanUser = "ban_user";
    public const string AppEventTypeNewUserVerified = "user_verified";
    public const string AppEventTypeRemoveSpam = "remove_spam_message";

    public const string RemovedMessageText = "removed_message_text";

    // Value fot proper counting some metrics in grafana
    public const string AppEventValue = "value";

    public const int CaptchaTimeoutSecondsNormal = 60;
    public const int CaptchaTimeoutSecondsAngryMode = 30;
    public const int CaptchaTimeoutSecondsJoinRequest = 86_400;

    public const string GreetingCommand = "/greeting";
    public const string GreetingOnCommand = "/greeting_on";
    public const string GreetingOffCommand = "/greeting_off";
    public const string GreetingSetCommand = "/greeting_set";
    public const string GreetingResetCommand = "/greeting_reset";
    public const string GreetingUserPlaceholder = "{user}";
    public const int GreetingTextMaxLength = 2048;

    public const string DbPathEnvVariable = "DRUZHOKBOT_DB_PATH";
    public const string DefaultDbPath = "data/druzhokbot.db";
}
