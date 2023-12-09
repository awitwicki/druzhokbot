using System;
using System.Threading.Tasks;
using DruzhokBot.Common.Services;
using DruzhokBot.Domain;
using NLog;

namespace druzhokbot
{
    internal class Program
    {
        static void Main(string[] args)
        {
            LogManager.Setup().LoadConfiguration(builder => {
                builder.ForLogger()
                    .FilterMinLevel(LogLevel.Info)
                    .WriteToConsole();
            });
            
            var logger = LogManager.GetCurrentClassLogger();

            logger.Info(LogTemplates.StartingDruzhokBot);

            var botToken = Environment.GetEnvironmentVariable("DRUZHOKBOT_TELEGRAM_TOKEN");

            if (botToken == null)
            {
                throw new Exception("ENV DRUZHOKBOT_TELEGRAM_TOKEN is not defined");
            }

            var bot = new TelegramBotClientWrapper(botToken);
            var botClient = new CoreBot(bot);

            // Wait for eternity
            Task.Delay(-1).Wait(); // Linux program lock
            Task.Delay(Int32.MaxValue).Wait(); // Windows program lock

            logger.Info(LogTemplates.FinishingDruzhokBot);
        }
    }
}
