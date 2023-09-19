# druzhokbot
Antispam bot for telegram groups

## Install

Use next environment variables:

* `DRUZHOKBOT_TELEGRAM_TOKEN={YOUR_TOKEN}` - Telegram token
* `DRUZHOKBOT_INFLUX_QUERY={url}` - Url to your influxDB server for storing logs, you choose not to define that env variable, if you don't need to log bot events

**Docker compose:**  create `.env` file and fill it with that variable.

## Run


### Docker compose

```
docker-compose up -d
```

### Telegram

Add you bot to the chat(s) ang give him admin privileges (**including allowing to delete messages and ban users**)
