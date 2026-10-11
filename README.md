# DruzhokBot
Antispam bot for telegram groups

## Install

Use next environment variables:

* `DRUZHOKBOT_TELEGRAM_TOKEN={YOUR_TOKEN}` - Telegram token
* `DRUZHOKBOT_DB_PATH={path}` - Optional path to the SQLite database with per-chat settings (default `data/druzhokbot.db`). Docker compose stores it in the `druzhokbot-data` volume.
* `DRUZHOKBOT_INFLUX_QUERY={url}` - Url to your influxDB server for storing logs, you choose not to define that env variable, if you don't need to log bot events

**Docker compose:**  create `.env` file and fill it with that variable.

## Run


### Docker compose

```
docker-compose up -d
```

### Telegram

Add you bot to the chat(s) ang give him admin privileges (**including allowing to delete messages and ban users**)

### Greeting verified users

Chat admins can make the bot greet every user who passes the captcha. Settings are stored per chat.

* `/greeting` - show current settings
* `/greeting_on` / `/greeting_off` - enable / disable greeting
* `/greeting_set <text>` - set greeting text; `{user}` is replaced with a mention of the new user
* `/greeting_reset` - restore the default greeting text
