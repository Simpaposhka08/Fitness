# ML Service

Локальный ML-сервис для страницы AI Coach.

## Что умеет

- `GET /health` — проверка, что сервис запущен.
- `POST /predict` — возвращает `score` для тренировки.
- `POST /feedback` — сохраняет feedback и запускает переобучение XGBoost.
- `POST /train` — ручной запуск обучения XGBoost по накопленным samples.

## Запуск

```bash
pip install -r requirements.txt
python server.py
```

По умолчанию сервис слушает `http://127.0.0.1:8008`.

## Offline обучение

```bash
python train.py
```

Модель сохраняется в `data/xgb_model.pkl`, метаданные — в `data/model_meta.json`.
