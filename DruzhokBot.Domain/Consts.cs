namespace DruzhokBot.Domain;

public abstract class Consts
{
    public const string StartCommand = "/start";
    
    public const string BanUserString = "ban_user";
    public const string NewUserString = "new_user";
    
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
}
