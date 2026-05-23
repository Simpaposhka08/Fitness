import json
import pickle
import random
from dataclasses import asdict, dataclass
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import List, Optional

import pandas as pd

import torch
import torch.nn as nn
import torch.optim as optim

BASE_DIR = Path(__file__).resolve().parent
DATA_DIR = BASE_DIR / "data"

SAMPLES_PATH = DATA_DIR / "training_samples.json"
MODEL_PATH = DATA_DIR / "neural_model.pth"
META_PATH = DATA_DIR / "model_meta.json"

HOST = "127.0.0.1"
PORT = 8008

MODEL = None
FEATURE_COLUMNS: List[str] = []


@dataclass
class TrainingSample:
    level: str
    workouts_per_week: int
    title: str
    trainer: str
    weekday: str
    hour: int
    price: float
    label: int = 1


class FitnessNet(nn.Module):

    def __init__(self, input_size):
        super().__init__()

        self.network = nn.Sequential(

            nn.Linear(input_size, 16),
            nn.ReLU(),

            nn.Linear(16, 8),
            nn.ReLU(),

            nn.Linear(8, 1),
            nn.Sigmoid()
        )

    def forward(self, x):
        return self.network(x)


def ensure_storage() -> None:

    DATA_DIR.mkdir(exist_ok=True, parents=True)

    if not SAMPLES_PATH.exists():
        SAMPLES_PATH.write_text("[]", encoding="utf-8")

    if not META_PATH.exists():

        META_PATH.write_text(
            json.dumps({
                "updated_at": datetime.utcnow().isoformat(),
                "model_type": "none"
            }, indent=2),
            encoding="utf-8"
        )


def load_samples() -> List[TrainingSample]:

    ensure_storage()

    raw = json.loads(
        SAMPLES_PATH.read_text(encoding="utf-8")
    )

    return [TrainingSample(**item) for item in raw]


def save_samples(samples: List[TrainingSample]) -> None:

    ensure_storage()

    payload = [asdict(s) for s in samples]

    SAMPLES_PATH.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2),
        encoding="utf-8"
    )


def save_metadata(model_type: str, sample_count: int):

    payload = {
        "updated_at": datetime.utcnow().isoformat(),
        "model_type": model_type,
        "sample_count": sample_count
    }

    META_PATH.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2),
        encoding="utf-8"
    )


def normalize_text(value: str) -> str:
    return (value or "").strip().lower().replace("ё", "е")


def to_feature_dict(sample: TrainingSample) -> dict:

    return {
        "level": normalize_text(sample.level),

        "workouts_per_week":
            max(1, min(int(sample.workouts_per_week), 7)),

        "title": normalize_text(sample.title),

        "trainer": normalize_text(sample.trainer),

        "weekday": normalize_text(sample.weekday),

        "hour":
            max(0, min(int(sample.hour), 23)),

        "price":
            max(0.0, float(sample.price)),
    }


def heuristic_score(sample: TrainingSample) -> float:

    week_factor = max(
        0.0,
        min(sample.workouts_per_week / 7.0, 1.0)
    )

    hour_factor = max(
        0.0,
        1.0 - abs(sample.hour - 18) / 12.0
    )

    price_factor = max(
        0.0,
        1.0 - float(sample.price) / 3000.0
    )

    level_map = {
        "новичок": 0.15,
        "средний": 0.25,
        "продвинутый": 0.35
    }

    level_bonus = level_map.get(
        normalize_text(sample.level),
        0.2
    )

    score = (
        0.2
        + level_bonus
        + week_factor * 0.2
        + hour_factor * 0.2
        + price_factor * 0.2
    )

    return max(0.0, min(score, 1.0))


def build_training_frame(
        samples: List[TrainingSample]
):

    rows = [to_feature_dict(s) for s in samples]

    labels = pd.Series(
        [int(s.label) for s in samples],
        dtype="int64"
    )

    frame = pd.DataFrame(rows)

    encoded = pd.get_dummies(
        frame,
        columns=["level", "title", "trainer", "weekday"],
        dtype=float
    )

    return encoded, labels



def bootstrap_negative_samples(
        samples: List[TrainingSample]
):

    if any(s.label == 0 for s in samples):
        return samples

    weekdays = [
        "понедельник",
        "вторник",
        "среда",
        "четверг",
        "пятница",
        "суббота",
        "воскресенье"
    ]

    titles = list({s.title for s in samples}) \
        or ["кроссфит"]

    trainers = list({s.trainer for s in samples}) \
        or ["дежурный тренер"]

    augmented = list(samples)

    for sample in samples:

        augmented.append(

            TrainingSample(
                level=sample.level,

                workouts_per_week=
                    sample.workouts_per_week,

                title=random.choice(titles),

                trainer=random.choice(trainers),

                weekday=random.choice(weekdays),

                hour=max(
                    6,
                    min(
                        22,
                        sample.hour +
                        random.choice([-6, -5, 5, 6])
                    )
                ),

                price=min(
                    5000.0,
                    sample.price *
                    random.uniform(1.4, 2.0)
                ),

                label=0
            )
        )

    return augmented


def train_model(samples: List[TrainingSample]) -> dict:

    global MODEL
    global FEATURE_COLUMNS

    if len(samples) < 4:

        save_metadata("heuristic", len(samples))

        MODEL = None
        FEATURE_COLUMNS = []

        return {
            "trained": False,
            "reason": "not_enough_samples"
        }

    dataset = bootstrap_negative_samples(samples)

    x_train, y_train = build_training_frame(dataset)

    FEATURE_COLUMNS = list(x_train.columns)

    x_tensor = torch.tensor(
        x_train.values,
        dtype=torch.float32
    )

    y_tensor = torch.tensor(
        y_train.values,
        dtype=torch.float32
    ).view(-1, 1)

    model = FitnessNet(x_tensor.shape[1])

    criterion = nn.BCELoss()

    optimizer = optim.Adam(
        model.parameters(),
        lr=0.001
    )

    epochs = 300

    for epoch in range(epochs):

        predictions = model(x_tensor)

        loss = criterion(
            predictions,
            y_tensor
        )

        optimizer.zero_grad()

        loss.backward()

        optimizer.step()

    MODEL = model

    torch.save(
        {
            "model_state": model.state_dict(),
            "feature_columns": FEATURE_COLUMNS
        },
        MODEL_PATH
    )

    save_metadata("neural_network", len(samples))

    return {
        "trained": True,
        "epochs": epochs,
        "loss": float(loss.item()),
        "sample_count": len(samples)
    }



def load_model_if_exists() -> bool:

    global MODEL
    global FEATURE_COLUMNS

    if not MODEL_PATH.exists():
        return False

    payload = torch.load(MODEL_PATH)

    FEATURE_COLUMNS = payload["feature_columns"]

    model = FitnessNet(len(FEATURE_COLUMNS))

    model.load_state_dict(
        payload["model_state"]
    )

    model.eval()

    MODEL = model

    return True



def predict_score(
        sample: TrainingSample
):

    if MODEL is None or not FEATURE_COLUMNS:

        return (
            heuristic_score(sample),
            "heuristic"
        )

    row = pd.DataFrame([
        to_feature_dict(sample)
    ])

    encoded = pd.get_dummies(
        row,
        columns=[
            "level",
            "title",
            "trainer",
            "weekday"
        ],
        dtype=float
    )

    aligned = encoded.reindex(
        columns=FEATURE_COLUMNS,
        fill_value=0.0
    )

    x_tensor = torch.tensor(
        aligned.values,
        dtype=torch.float32
    )

    with torch.no_grad():

        prediction = MODEL(
            x_tensor
        ).item()

    return (
        max(0.0, min(float(prediction), 1.0)),
        "neural_network"
    )


# =========================
# HTTP SERVER
# =========================

class MlHandler(BaseHTTPRequestHandler):

    def _send_json(
            self,
            status_code: int,
            payload: dict
    ) -> None:

        body = json.dumps(
            payload,
            ensure_ascii=False
        ).encode("utf-8")

        self.send_response(status_code)

        self.send_header(
            "Content-Type",
            "application/json; charset=utf-8"
        )

        self.send_header(
            "Content-Length",
            str(len(body))
        )

        self.end_headers()

        self.wfile.write(body)

    # =========================
    # GET
    # =========================

    def do_GET(self):

        if self.path == "/health":

            ensure_storage()

            loaded = (
                MODEL is not None
                and len(FEATURE_COLUMNS) > 0
            )

            self._send_json(
                200,
                {
                    "status": "ok",
                    "model_loaded": loaded
                }
            )

            return

        self._send_json(
            404,
            {"error": "not_found"}
        )

    # =========================
    # POST
    # =========================

    def do_POST(self):

        length = int(
            self.headers.get(
                "Content-Length",
                0
            )
        )

        raw = self.rfile.read(length)

        try:

            data = json.loads(raw) if raw else {}

        except json.JSONDecodeError:

            self._send_json(
                400,
                {"error": "invalid_json"}
            )

            return

        # =========================
        # PREDICT
        # =========================

        if self.path == "/predict":

            try:

                sample = TrainingSample(
                    **data,
                    label=1
                )

            except TypeError as ex:

                self._send_json(
                    400,
                    {
                        "error":
                            f"bad_payload: {ex}"
                    }
                )

                return

            score, mode = predict_score(sample)

            self._send_json(
                200,
                {
                    "score": score,
                    "mode": mode
                }
            )

            return

        # =========================
        # FEEDBACK
        # =========================

        if self.path == "/feedback":

            try:

                sample = TrainingSample(**data)

            except TypeError as ex:

                self._send_json(
                    400,
                    {
                        "error":
                            f"bad_payload: {ex}"
                    }
                )

                return

            samples = load_samples()

            samples.append(sample)

            save_samples(samples)

            train_info = train_model(samples)

            self._send_json(
                200,
                {
                    "status": "saved",
                    "samples_count": len(samples),
                    "train_info": train_info
                }
            )

            return

        # =========================
        # TRAIN
        # =========================

        if self.path == "/train":

            samples = load_samples()

            train_info = train_model(samples)

            self._send_json(
                200,
                {
                    "status": "trained",
                    "train_info": train_info
                }
            )

            return

        self._send_json(
            404,
            {"error": "not_found"}
        )


# =========================
# START SERVER
# =========================

if __name__ == "__main__":

    ensure_storage()

    load_model_if_exists()

    server = ThreadingHTTPServer(
        (HOST, PORT),
        MlHandler
    )

    print(
        f"Neural ML service running: "
        f"http://{HOST}:{PORT}"
    )

    server.serve_forever()