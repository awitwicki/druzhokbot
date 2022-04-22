# druzhokbot
Antispam bot for telegram groups

![License](https://img.shields.io/badge/License-Apache%20License%202.0-blue)
![Tests](https://img.shields.io/github/languages/top/awitwicki/druzhokbot)
![Tests](https://img.shields.io/badge/dotnet%20version-6.0-blue)
![Tests](https://img.shields.io/github/last-commit/awitwicki/druzhokbot)

ADD TO CHAT IMAGE URL TG://ADDTOCHAT AS ADMIN

## Install

Use next environment variables:

* `DRUZHOKBOT_TELEGRAM_TOKEN={YOUR_TOKEN}` - telegram token
* `DRUZHOKBOT_INFLUX_QUERY={url}` - url to your influxDB server for storing logs, you choose not to define that env variable, if you don't need to log bot events


**Python:** Add to system environment this.

**Docker compose:**  create `.env` file and fill it with that variable.

## Run


### Docker compose

```
docker-compose up -d
```

### Python

```
pip3 install -r requirements.txt
python3 main.py
```

### Telegram

Add you bot to the chat(s) ang give him admin privileges (**including allowing to delete messages and ban users**)