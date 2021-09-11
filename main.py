import os
from datetime import datetime, timezone, timedelta
import telegram
from telegram.ext import Updater, Filters, MessageHandler, CallbackQueryHandler
from telegram import InlineKeyboardMarkup, InlineKeyboardButton, ChatPermissions


# https://github.com/python-telegram-bot/python-telegram-bot/wiki/Transition-guide-to-Version-12.0
bot_token = bot_token = os.getenv('DRUZHOKBOT_TELEGRAM_TOKEN')
users_to_kick = []


def btn_clicked(update: telegram.Update, context):
    command = update.callback_query.data
 
    user_id = int(command)
    user_clicked_id = update.callback_query.from_user.id
    chat_id = update.callback_query.message.chat_id
    message_id = update.callback_query.message.message_id

    #check if new user clicked
    if user_id != user_clicked_id:
        context.bot.answer_callback_query(
            callback_query_id= update.callback_query.id,
            text= 'Robots will rule the world :)',
            show_alert=True)
    else:
        context.bot.delete_message(chat_id=chat_id, message_id=message_id)
        users_to_kick.remove(user_id)
        context.bot.restrictChatMember(
        chat_id = chat_id,
        user_id = user_id,
        permissions = ChatPermissions(can_send_messages = True, can_send_media_messages= True)
        )


def kick_user(context):
    _user_id = context.job.context[0]
    _chat_id = context.job.context[1]
    _message_id = context.job.context[2]
    _join_message_id = context.job.context[3]

    if _user_id in users_to_kick:
        context.bot.kickChatMember(chat_id = _chat_id, user_id = _user_id, until_date = datetime.now(timezone.utc) + timedelta(0, 31))
        context.bot.delete_message(chat_id=_chat_id, message_id=_message_id)
        context.bot.delete_message(chat_id=_chat_id, message_id=_join_message_id)

        users_to_kick.remove(_user_id)
        pass


def add_group(update, context):
    message: telegram.message.Message = update.message
    _chat_id = message.chat_id
    _message_id = message.message_id

    # ignore old messages
    if message.date and (datetime.now(timezone.utc) - message.date).seconds > 300:
        return

    for member in update.message.new_chat_members:
        if not member.is_bot:
            keyboard = [[InlineKeyboardButton("🚫🤖🦾🦾🦾", callback_data=member.id)]]
            reply_markup = InlineKeyboardMarkup(keyboard)
            message_text = f"Добро пожаловать, {member.name}! Чтобы группа была защищена от ботов, "\
                "пройдите простую верификацию, нажав на кнопку «🚫🤖» под этим сообщением. "\
                "Поторопитесь, у вас есть 2 минуты до автоматического кика из чата"

            msg = context.bot.send_message(_chat_id, text=message_text, reply_markup=reply_markup)
            users_to_kick.append(member.id)
            context.job_queue.run_once(kick_user, 120, context=[member.id, msg.chat_id, msg.message_id, _message_id])

            context.bot.restrictChatMember(
                chat_id = _chat_id,
                user_id = member.id,
                permissions = ChatPermissions(can_send_messages = False)
                )


def left_chat_member(update, context):
    _chat_id = update.message.chat_id
    _message_id = update.message.message_id

    context.bot.delete_message(chat_id=_chat_id, message_id=_message_id)


def main():
    updater = Updater(bot_token, use_context=True)

    dp = updater.dispatcher
    dp.add_handler(CallbackQueryHandler(btn_clicked))
    dp.add_handler(MessageHandler(Filters.status_update.new_chat_members, add_group))
    dp.add_handler(MessageHandler(Filters.status_update.left_chat_member, left_chat_member))

    updater.start_polling()
    bot_name = updater.bot.name
    print(f"Bot is started on id {bot_name}")
    updater.idle()


if __name__ == '__main__':
    main()
