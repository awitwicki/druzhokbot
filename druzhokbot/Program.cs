using System;

namespace druzhokbot
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            string botToken = Environment.GetEnvironmentVariable("DRUZHOKBOT_TELEGRAM_TOKEN");

            if (botToken == null)
            {
                throw new Exception("ENV DRUZHOKBOT_TELEGRAM_TOKEN is not defined");
            }

            var botClient = new CoreBot(botToken);
            botClient.StartReceiving().GetAwaiter().GetResult();
        }
    }
}
