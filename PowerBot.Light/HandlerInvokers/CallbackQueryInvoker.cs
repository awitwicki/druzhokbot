using PowerBot.Lite.Attributes;
using PowerBot.Lite.Attributes.AttributeValidators;
using PowerBot.Lite.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace PowerBot.Lite.HandlerInvokers
{
    internal class CallbackQueryInvoker
    {
        //public async static Task InvokeCallbackQuery(ITelegramBotClient botClient, CallbackQuery callbackQuery)
        //{
        //    // 

        //    // Get all handlers
        //    var handlers = ReflectiveEnumerator.GetEnumerableOfType<BaseCallbackQueryHandler>();

        //    foreach (var handlerType in handlers)
        //    {
        //        //Find method in handler
        //        MethodInfo[] handlerMethods = handlerType.GetMethods();

        //        foreach (var method in handlerMethods)
        //        {
        //            //Pattern matching for message text
        //            if (AttributeValidators.MatchMethod(method, callbackQuery.Data))
        //            {
        //                try
        //                {
        //                    //Get and send chatAction from attributes
        //                    var chatAction = AttributeValidators.GetChatActionAttributes(method);
        //                    if (chatAction.HasValue)
        //                        await botClient.SendChatActionAsync(message.Chat.Id, chatAction.Value);

        //                    //Cast handler object
        //                    var handler = Activator.CreateInstance(handlerType);

        //                    //Set params
        //                    ((BaseCallbackQueryHandler)handler).Init(botClient, CallbackQuery: );

        //                    //Invoke method
        //                    await (Task)method.Invoke(handler, parameters: new object[] { });
        //                }
        //                catch (Exception ex)
        //                {
        //                    Console.WriteLine($"Invoker error: {ex}");
        //                }
        //            }
        //        }
        //    }
        //}
    }
}
