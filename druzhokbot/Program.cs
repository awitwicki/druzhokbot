using System;
using System.Threading.Tasks;

namespace druzhokbot
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting druzhokbot");

            string botToken = Environment.GetEnvironmentVariable("DRUZHOKBOT_TELEGRAM_TOKEN");

            if (botToken == null)
            {
                throw new Exception("ENV DRUZHOKBOT_TELEGRAM_TOKEN is not defined");
            }

            var botClient = new CoreBot(botToken);
            botClient.StartReceiving().RunSynchronously();

            // Wait for eternity
            Task.Delay(-1); // Linux program lock
            Task.Delay(Int32.MaxValue).Wait(); // Windows program lock

            Console.WriteLine("Finishing druzhokbot!");
        }
    }
}
