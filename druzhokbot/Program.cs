using System;
using System.Threading.Tasks;
using DruzhokBot.Domain;

namespace druzhokbot
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(LogTemplates.StartingDruzhokBot);

            var botToken = Environment.GetEnvironmentVariable("DRUZHOKBOT_TELEGRAM_TOKEN");

            if (botToken == null)
            {
                throw new Exception("ENV DRUZHOKBOT_TELEGRAM_TOKEN is not defined");
            }

            var botClient = new CoreBot(botToken);

            // Wait for eternity
            Task.Delay(-1).Wait(); // Linux program lock
            Task.Delay(Int32.MaxValue).Wait(); // Windows program lock

            Console.WriteLine(LogTemplates.FinishingDruzhokBot);
        }
    }
}
