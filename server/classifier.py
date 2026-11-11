import configparser
import json
import logging
import os
import time
from threading import Semaphore

import requests

logger = logging.getLogger(__name__)

config = configparser.ConfigParser()
with open('configOllama.ini', 'r', encoding='utf-8') as config_file:
    config.read_file(config_file)

MODEL_API = config['server']['model_api']

session = requests.Session()
session.trust_env = False

CLASSIFIER_MODEL = config["server"].get(
    "classifier_model",
    "qwen3:4b"
)

CLASSIFIER_PROMPTS = {
    lang: os.path.join(
        os.path.dirname(os.path.abspath(__file__)),
        "classifier",
        filename
    )
    for lang, filename in {
        "RU": "classifier_ru.txt",
        "EN": "classifier_en.txt"
    }.items()
}

CLASSIFIER_TYPES = (
    "book",
    "journal",
    "proceedings",
    "patent",
    "standard",
    "law",
    "dissertation",
    "website",
    "webpage",
    "undefined"
)

CLASSIFIER_GATE = Semaphore(1)

CLASSIFIER_SCHEMA = {
    "type": "object",
    "properties": {
        "type": {
            "type": "string",
            "enum": list(CLASSIFIER_TYPES)
        }
    },
    "required": ["type"],
    "additionalProperties": False
}

def classify_source(
    source_text: str,
    language: str,
    request_id: str
) -> str:
    """Production classification, strictly validated before routing."""

    with open(
        CLASSIFIER_PROMPTS[language],
        "r",
        encoding="utf-8"
    ) as handle:
        prompt = handle.read()

    if not CLASSIFIER_GATE.acquire(timeout=60):
        raise ClassifierBusy("Очередь классификации переполнена")

    try:
        payload = {
            "model": CLASSIFIER_MODEL,
            "think": False,
            "messages": [
                {
                    "role": "system",
                    "content": prompt
                },
                {
                    "role": "user",
                    "content": source_text
                },
            ],
            "format": CLASSIFIER_SCHEMA,
            "stream": False,
            "options": {
                "temperature": 0,
                "num_ctx": 4096,
                "num_predict": 64
            },
        }

        t0 = time.perf_counter()

        response = requests.post(
            MODEL_API,
            json=payload,
            timeout=(5, 120)
        )

        response.raise_for_status()

        raw = (
            (
                response.json().get("message") or {}
            ).get("content") or ""
        ).strip()

        try:
            obj = json.loads(raw)

        except (json.JSONDecodeError, TypeError) as exc:
            raise ClassificationFailed(
                "Invalid classifier JSON"
            ) from exc

        if (
            not isinstance(obj, dict)
            or set(obj) != {"type"}
            or obj["type"] not in CLASSIFIER_TYPES
        ):
            raise ClassificationFailed(
                "Unexpected classifier result"
            )

        logger.info(
            "[%s] CLASSIFY model=%s type=%s duration_ms=%.0f",
            request_id,
            CLASSIFIER_MODEL,
            obj["type"],
            (time.perf_counter() - t0) * 1000
        )

        return obj["type"]

    finally:
        CLASSIFIER_GATE.release()
        
class ClassificationFailed(Exception):
    pass


class ClassifierBusy(Exception):
    pass