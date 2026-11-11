import configparser
import json
import logging
import os
import time
from pathlib import Path
from threading import Semaphore
import re
import requests
from threading import Lock


logger = logging.getLogger(__name__)

BASE_DIR = Path(__file__).resolve().parent

config = configparser.ConfigParser()
config.read(BASE_DIR / "configOllama.ini", encoding="utf-8")

MODEL_API = config["server"]["model_api"]

FORMATTER_MODEL = config["server"].get(
    "formatter_model",
    "qwen2.5:3b"
)

FORMATTER_GATE = Semaphore(
    config["server"].getint("formatter_parallel", fallback=1)
)
FEEDBACK_LOCK = Lock()

session = requests.Session()
session.trust_env = False


class FormatterBusy(Exception):
    pass


class FormattingFailed(Exception):
    pass


class FormatterConfigurationError(Exception):
    pass


FORMATTER_TYPES = frozenset({
    "book",
    "journal",
    "proceedings",
    "patent",
    "standard",
    "law",
    "dissertation",
    "website",
    "webpage"
})


def load_formatting_prompt(
    language: str,
    source_type: str,
    template_id: str
) -> str:

    if language not in ("RU", "EN"):
        raise FormatterConfigurationError(
            "Unsupported language"
        )

    if source_type not in FORMATTER_TYPES:
        raise FormatterConfigurationError(
            "Unsupported source type"
        )

    if template_id not in ("default", "test", "strict"):
        raise FormatterConfigurationError(
            "Unsupported template"
        )

    lang = language.lower()

    common_path = (
        BASE_DIR / "common" / f"common_{lang}.txt"
    )

    specific_path = (
        BASE_DIR
        / "formatter"
        / template_id
        / source_type
        / f"{lang}.txt"
    )

    for path in (common_path, specific_path):
        if not path.is_file():
            raise FormatterConfigurationError(
                f"Prompt not found: {path}"
            )

    common_prompt = common_path.read_text(
        encoding="utf-8"
    )

    specific_prompt = specific_path.read_text(
        encoding="utf-8"
    )

    if not common_prompt.strip() or not specific_prompt.strip():
        raise FormatterConfigurationError(
            "Formatting prompt is empty"
        )

    return (
        common_prompt
        + "\n\n"
        + specific_prompt
    )


NAME = r"[А-Яа-яЁёA-Za-z][А-Яа-яЁёA-Za-z-]*"
INITIALS = r"(?P<initials>(?:[А-ЯЁA-Z]\s*\.\s*){2})"


INITIALS_FIRST = re.compile(
    rf"(?<!\w){INITIALS}(?P<surname>{NAME})\b"
)

SURNAME_FIRST = re.compile(
    rf"^\s*(?P<surname>{NAME}),\s*{INITIALS}(?!\w)"
)

def restore_surnames_by_initials(
    source_text: str,
    formatted_text: str
) -> str:

    def initials_key(value: str) -> str:
        return "".join(re.findall(r"[А-ЯЁA-Z]", value))

    source_names = {}

    for pattern in (INITIALS_FIRST, SURNAME_FIRST):
        for match in pattern.finditer(source_text):
            key = initials_key(match.group("initials"))
            surname = match.group("surname")

            source_names.setdefault(key, set()).add(surname)

    known_names = {
        key: next(iter(surnames))
        for key, surnames in source_names.items()
        if len(surnames) == 1
    }

    def replace_match(match):
        key = initials_key(match.group("initials"))
        original = known_names.get(key)

        if original is None:
            return match.group(0)

        generated = match.group("surname")

        if generated == original:
            return match.group(0)

        start, end = match.span("surname")

        relative_start = start - match.start()
        relative_end = end - match.start()

        return (
            match.group(0)[:relative_start]
            + original
            + match.group(0)[relative_end:]
        )

    formatted_text = INITIALS_FIRST.sub(
        replace_match,
        formatted_text
    )

    formatted_text = SURNAME_FIRST.sub(
        replace_match,
        formatted_text
    )

    return formatted_text

def save_feedback(
    template_id: str,
    source_type: str,
    language: str,
    user_id: int,
    original_text: str,
    model_output: str,
    expected_output: str
) -> None:

    if template_id not in ("default", "test", "strict"):
        raise FormatterConfigurationError("Неизвестный шаблон")

    if source_type not in FORMATTER_TYPES:
        raise FormatterConfigurationError("Неизвестный тип источника")

    if language not in ("RU", "EN"):
        raise FormatterConfigurationError("Неизвестный язык")

    user_id = int(user_id)

    if user_id <= 0:
        raise ValueError("Некорректный ID пользователя")

    feedback_dir = (
        BASE_DIR
        / "formatter"
        / template_id
        / source_type
        / "feedback"
        / str(user_id)
    )

    with FEEDBACK_LOCK:
        feedback_dir.mkdir(
            parents=True,
            exist_ok=True
        )

        # Оба файла существуют с первого обращения пользователя
        for lang in ("ru", "en"):
            (feedback_dir / f"{lang}.txt").touch(
                exist_ok=True
            )

        target_path = (
            feedback_dir / f"{language.lower()}.txt"
        )

        entry = (
            "========================================\n"
            "ВХОД:\n"
            f"{original_text}\n\n"
            "ОТВЕТ МОДЕЛИ:\n"
            f"{model_output}\n\n"
            "ИСПРАВЛЕНИЕ РЕДАКТОРА:\n"
            f"{expected_output}\n"
            "========================================\n\n"
        )

        with target_path.open(
            "a",
            encoding="utf-8"
        ) as file:
            file.write(entry)
FEEDBACK_LIMIT = 3
FEEDBACK_SEPARATOR = "========================================"


def load_feedback_examples(
    template_id: str,
    source_type: str,
    language: str,
    user_id: int
) -> str:
    if template_id not in ("default", "test", "strict"):
        raise FormatterConfigurationError("Неизвестный шаблон")

    if source_type not in FORMATTER_TYPES:
        raise FormatterConfigurationError("Неизвестный тип источника")

    if language not in ("RU", "EN"):
        raise FormatterConfigurationError("Неизвестный язык")

    if int(user_id) <= 0:
        raise ValueError("Некорректный ID пользователя")

    feedback_path = (
        BASE_DIR
        / "formatter"
        / template_id
        / source_type
        / "feedback"
        / str(int(user_id))
        / f"{language.lower()}.txt"
    )

    if not feedback_path.is_file():
        return ""

    with FEEDBACK_LOCK:
        text = feedback_path.read_text(encoding="utf-8")

    if not text.strip():
        return ""

    examples = []

    for block in text.split(FEEDBACK_SEPARATOR):
        if "ВХОД:\n" not in block:
            continue

        try:
            original = block.split(
                "ВХОД:\n", 1
            )[1].split(
                "\n\nОТВЕТ МОДЕЛИ:\n", 1
            )[0].strip()

            expected = block.split(
                "ИСПРАВЛЕНИЕ РЕДАКТОРА:\n", 1
            )[1].strip()

        except IndexError:
            continue

        if original and expected:
            examples.append((original, expected))

    if not examples:
        return ""

    examples = examples[-FEEDBACK_LIMIT:]

    lines = [
        "ПЕРСОНАЛЬНЫЕ ПРИМЕРЫ ОФОРМЛЕНИЯ",
        "Это исправления данного редактора.",
        "Используй их как примеры оформления, "
        "но не переноси из них фактические сведения "
        "в другие записи.",
        "Они не отменяют общие запреты и правила.",
        "",
    ]

    for number, (original, expected) in enumerate(examples, 1):
        lines.extend([
            f"ПРИМЕР {number}",
            "Вход:",
            original,
            "Правильный выход:",
            expected,
            "",
        ])

    return "\n".join(lines)
def format_source(
    source_text: str,
    language: str,
    source_type: str,
    template_id: str,
    request_id: str,
    user_id: int
) -> str:

    system_prompt_used = load_formatting_prompt(
        language,
        source_type,
        template_id
    )
    feedback_prompt = load_feedback_examples(
        template_id=template_id,
        source_type=source_type,
        language=language,
        user_id=user_id
    )

    if feedback_prompt:
        system_prompt_used += "\n\n" + feedback_prompt

        logger.info(
            "[%s] FEEDBACK_APPLIED user_id=%s "
            "template=%s type=%s language=%s",
            request_id,
            user_id,
            template_id,
            source_type,
            language
        )

    if not FORMATTER_GATE.acquire(timeout=60):
        raise FormatterBusy("Очередь форматирования переполнена")

    try:
        wrapped_text = f"LANG={language}\n{source_text}"

        payload = {
            "model": FORMATTER_MODEL,
            "think": False,
            "messages": [
                {
                    "role": "system",
                    "content": system_prompt_used
                },
                {
                    "role": "user",
                    "content": wrapped_text
                }
            ],
            "stream": False,
            "options": {
                "temperature": 0
            }
        }

        body = json.dumps(
            payload,
            ensure_ascii=False
        ).encode("utf-8")

        logger.info(
            "[%s] FORMAT_START model=%s type=%s "
            "language=%s template=%s sys_chars=%s",
            request_id,
            FORMATTER_MODEL,
            source_type,
            language,
            template_id,
            len(system_prompt_used)
        )

        t0 = time.perf_counter()

        response = requests.post(
            MODEL_API,
            data=body,
            headers={"Content-Type": "application/json"},
            timeout=(5, 240)
        )

        dt = time.perf_counter() - t0

        logger.info(
            "[%s] FORMAT_END model=%s status=%s "
            "duration_ms=%.0f",
            request_id,
            FORMATTER_MODEL,
            response.status_code,
            dt * 1000
        )

        response.raise_for_status()

        result = response.json()

        logger.info(
            "[%s] FORMAT_STATS "
            "prompt_tokens=%s output_tokens=%s "
            "prompt_eval_ms=%.0f eval_ms=%.0f load_ms=%.0f",
            request_id,
            result.get("prompt_eval_count"),
            result.get("eval_count"),
            (result.get("prompt_eval_duration") or 0) / 1_000_000,
            (result.get("eval_duration") or 0) / 1_000_000,
            (result.get("load_duration") or 0) / 1_000_000
        )

        message = result.get("message") or {}
        content = (message.get("content") or "").strip()

        if not content:
            raise FormattingFailed(
                "Модель форматирования вернула пустой ответ"
            )
        if language == "RU":
            corrected = restore_surnames_by_initials(
                source_text,
                content
            )
            logger.warning(
                "[%s] SURNAME_CHECK changed=%s source=%r output=%r",
                request_id,
                corrected != content,
                source_text,
                corrected
            )
            if corrected != content:
                logger.warning(
                    "[%s] Исправлены фамилии по исходным инициалам",
                    request_id
                )

            content = corrected
        return content

    finally:
        FORMATTER_GATE.release()