using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using DruzhokBot.App;
using DruzhokBot.Common.Helpers;
using DruzhokBot.Common.Services;
using DruzhokBot.Domain;
using NLog;

LogManager.Setup().LoadConfiguration(builder => {
    builder.ForLogger()
        .FilterMinLevel(LogLevel.Info)
        .WriteToConsole();
});

var logger = LogManager.GetCurrentClassLogger();

var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;

InfluxDbLiteClient.Query(Consts.AppLogsTableName,
    new Dictionary<string, object>()
    {
        {  Consts.AppEventType,  Consts.AppStarted }
    }, new Dictionary<string, object>()
    {
        { Consts.AppEventValue, 1 }
    });

logger.Info($"{LogTemplates.StartingDruzhokBot} version: {version}");

var botToken = Environment.GetEnvironmentVariable("DRUZHOKBOT_TELEGRAM_TOKEN");

if (string.IsNullOrWhiteSpace(botToken))
{
    logger.Error("ENV DRUZHOKBOT_TELEGRAM_TOKEN is not defined");
    await Task.Delay(-1);
}

var dbPath = Environment.GetEnvironmentVariable(Consts.DbPathEnvVariable);
if (string.IsNullOrWhiteSpace(dbPath))
{
    dbPath = Consts.DefaultDbPath;
}

logger.Info($"Using database: {Path.GetFullPath(dbPath)}");
var chatSettingsRepository = new ChatSettingsRepository(dbPath);

var bot = new TelegramBotClientWrapper(botToken);
var botClient = new CoreBot(bot, chatSettingsRepository: chatSettingsRepository);

// Wait for eternity
await Task.Delay(-1);

logger.Info(LogTemplates.FinishingDruzhokBot);
