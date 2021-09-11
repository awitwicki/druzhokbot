# druzhokbot
Antispam bot for telegram groups

## Install

Use next environment variables:

* `DRUZHOKBOT_TELEGRAM_TOKEN={YOUR_TOKEN}` - telegram token


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