import asyncio
from datetime import datetime, timezone, timedelta
import os

from aiogram import Bot, types, executor
from aiogram.dispatcher import Dispatcher
from aiogram.types import InlineKeyboardMarkup, InlineKeyboardButton, ParseMode, chat_permissions
from aiogram.types.message import Message, User
from aiogram.dispatcher.filters import Filter
import requests


bot_token = os.getenv('DRUZHOKBOT_TELEGRAM_TOKEN')
influx_query_url = os.getenv('DRUZHOKBOT_INFLUX_QUERY')
users_to_kick = []


bot: Bot = Bot(token=bot_token)
dp: Dispatcher = Dispatcher(bot)


def influx_query(query_str: str):
    if influx_query_url:
        try:
            url = influx_query_url
            headers = {'Content-Type': 'application/Text'}

            x = requests.post(url, data=query_str.encode('utf-8'), headers=headers)
        except Exception as e:
            print(e)


class ignore_old_messages(Filter):
    async def check(self, message: types.Message):
        return (datetime.now() - message.date).seconds < 60


@dp.message_handler(ignore_old_messages(), commands=['start', 'help'])
async def start(message: types.Message):
    reply_text = "Привет, меня зовут Дружок!\nДобавь меня в свой чат, дай права админа, и я буду проверять чтобы группа всегда была защищена от спам-ботов."
    msg = await bot.send_message(message.chat.id, text=reply_text, reply_to_message_id=message.message_id, parse_mode=ParseMode.MARKDOWN)


@dp.callback_query_handler(lambda call: "new_user" in call.data)
async def new_user(call: types.CallbackQuery):
    user_id = call.data.split('|')[1]
    user_id = int(user_id)
    user_clicked_id = call.from_user.id

    if user_id == user_clicked_id:
        await call.answer("Верификация пройдена, кожаный мешок!", show_alert=True)

        await bot.delete_message(message_id=call.message.message_id, chat_id=call.message.chat.id)
        users_to_kick.remove(user_id)
        await bot.restrict_chat_member(
            chat_id=call.message.chat.id,
            user_id=call.from_user.id,
            permissions=chat_permissions.ChatPermissions(
                can_send_messages=True,
                can_send_media_messages=True,
                can_send_polls=True,
                can_send_other_messages=True,
                can_add_web_page_previews=True,
                can_change_info=True,
                can_invite_users=True,
                can_pin_messages=True
            ))

        user_name = call.from_user.mention
        user_fullname = call.from_user.full_name.replace(' ', '\ ').replace('=', '\=')
        chat_id = call.message.chat.id
        chat_title = call.message.chat.title.replace(' ', '\ ').replace('=', '\=')

        influx_query(f'bots,botname=druzhokbot,chatname={chat_title},chat_id={chat_id},user_id={user_clicked_id},user_name={user_name},user_fullname={user_fullname} user_verified=1')
    else:
        await call.answer("Robots will rule the world :)", show_alert=True)


@dp.message_handler(ignore_old_messages(), content_types=['new_chat_members'])
async def add_group(message: types.Message):
    if message.from_user.is_bot:
        return

    keyboard = types.InlineKeyboardMarkup()
    keyboard.add(types.InlineKeyboardButton(text="🚫🤖🦾🦾🦾", callback_data=f'new_user|{message.from_user.id}'))

    message_text = f"Добро пожаловать, {message.from_user.mention}! Чтобы группа была защищена от ботов, "\
                "пройдите простую верификацию, нажав на кнопку «🚫🤖» под этим сообщением. "\
                "Поторопитесь, у вас есть 2 минуты до автоматического кика из чата"

    msg = await bot.send_message(chat_id=message.chat.id, reply_to_message_id=message.message_id, text=message_text, reply_markup=keyboard)

    users_to_kick.append(message.from_user.id)
    await bot.restrict_chat_member(chat_id=message.chat.id, user_id=message.from_user.id, permissions=chat_permissions.ChatPermissions(can_send_messages = False))

    await asyncio.sleep(120)

    #kick user
    if message.from_user.id in users_to_kick:
        # delete "hello newbie" message
        await bot.delete_message(chat_id=message.chat.id, message_id=msg.message_id)

        gg = await bot.kick_chat_member(chat_id = message.chat.id, user_id = message.from_user.id, until_date = datetime.now(timezone.utc) + timedelta(0, 31))

        #some other bots already deleted this "user join" message
        try:
            # delete "user join" message
            await bot.delete_message(chat_id=message.chat.id, message_id=message.message_id)
        except:
            pass

        users_to_kick.remove(message.from_user.id)

        user_id = message.from_user.id
        user_name = message.from_user.mention.replace(' ', '\ ').replace('=', '\=')
        user_fullname = message.from_user.full_name.replace(' ', '\ ').replace('=', '\=')
        chat_id = message.chat.id
        chat_title = message.chat.title.replace(' ', '\ ').replace('=', '\=')

        influx_query(f'bots,botname=druzhokbot,chatname={chat_title},chat_id={chat_id},user_id={user_id},user_name={user_name},user_fullname={user_fullname} user_banned=1')


@dp.message_handler(ignore_old_messages(), content_types=['left_chat_member'])
async def add_group(message: types.Message):
    #some other bots already deleted this "user join" message
    try:
        # delete "user join" message
        await bot.delete_message(chat_id=message.chat.id, message_id=message.message_id)
    except:
        pass


async def bot_start(dispatcher):
    print(f"Druzhokbot is started")


if __name__ == '__main__':
    dp.bind_filter(ignore_old_messages)
    executor.start_polling(dp, on_startup=bot_start)
