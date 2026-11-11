import configparser
from flask import Flask, request, jsonify, g
import requests
import logging
from functools import wraps
from sqlalchemy import create_engine, text
from sqlalchemy.orm import sessionmaker
import uuid
import hashlib
import secrets
import base64
import hmac
from waitress import serve
from formatter import save_feedback

from classifier import (
    classify_source,
    ClassificationFailed,
    ClassifierBusy
)
from formatter import (
    format_source,
    FormatterBusy,
    FormattingFailed,
    FormatterConfigurationError
)
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

config = configparser.ConfigParser()
with open('configOllama.ini', 'r', encoding='utf-8') as config_file:
    config.read_file(config_file)

DATABASE_URL = config['server']['database_api']

engine = create_engine(DATABASE_URL)

SessionLocal = sessionmaker(
    autocommit=False,
    autoflush=False,
    bind=engine
)

app = Flask(__name__)

MAX_INPUT_LENGTH = 2048

hash_alg = "sha256"
hash_iterations = 600000
hash_dklen = 32
salt_length = 16
AVAILABLE_TEMPLATES = ("strict","default")
default_template = "strict"

@app.before_request
def add_request_id():
    g.request_id = request.headers.get("X-Request-ID") or uuid.uuid4().hex
    
@app.after_request
def add_request_id_header(response):
    response.headers["X-Request-ID"] = g.request_id
    return response

def generate_key():
    key = secrets.token_urlsafe(32)
    return key

def sha256_hex(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()
    
def hash_key(key: str) -> str:
    salt = os.urandom(salt_length)
    hashed_key = hashlib.pbkdf2_hmac(hash_alg, key.encode("utf-8"), salt, hash_iterations, dklen = hash_dklen)
    salt_b64 = base64.urlsafe_b64encode(salt).decode("ascii")
    saved_key_b64 = base64.urlsafe_b64encode(hashed_key).decode("ascii")

    return f"pbkdf2_{hash_alg}${hash_iterations}${salt_b64}${saved_key_b64}"

def verify_key(key_plain: str, stored: str) -> bool:
    try:
        scheme, iterations, salt_b64, saved_key_b64 = stored.split("$", 3)
        if not scheme.startswith("pbkdf2_"):
            return False
        hashing_algorytmn = scheme.replace("pbkdf2_", "", 1)
        iterations = int(iterations)
        salt = base64.urlsafe_b64decode(salt_b64.encode("ascii"))
        saved_key_expected = base64.urlsafe_b64decode(saved_key_b64.encode("ascii"))
    except Exception:
        return False

    saved_key_current = hashlib.pbkdf2_hmac(
        hashing_algorytmn,
        key_plain.encode("utf-8"),
        salt,
        iterations,
        dklen=len(saved_key_expected),
    )
    return hmac.compare_digest(saved_key_current, saved_key_expected)
    
def require_api_key(f):
    @wraps(f)
    def decorated(*args, **kwargs):

        api_key_plain = (request.headers.get("X-API-Key") or "").strip()
        if not api_key_plain:
            logger.warning(f"[{g.request_id}] отсутствует API ключ. IP: {request.remote_addr}")
            return jsonify({"error": "Неверный или отсутствующий API ключ", "request_id": g.request_id}), 401
        db = SessionLocal()
        try:
            
            rows = db.execute(text("""
                SELECT key_hash
                FROM api_keys
            """)).scalars().all()
            matched = None
            for stored in rows:
                if verify_key(api_key_plain, stored):
                    matched = stored
                    break
            if not matched:
                logger.warning(f"[{g.request_id}] неверный API ключ. IP: {request.remote_addr}")
                return jsonify({"error": "Неверный или отсутствующий API ключ", "request_id": g.request_id}), 401
            g.api_key_hash = matched
            user = db.execute(text("""
                SELECT u.id, u.user_name
                FROM users u
                JOIN api_keys k ON k.user_id = u.id
                WHERE k.key_hash = :key_hash
            """), {
                "key_hash": matched
            }).mappings().first()

            if user is None:
                return jsonify({
                    "error": "Пользователь для API-ключа не найден",
                    "request_id": g.request_id
                }), 401

            g.user_id = user["id"]
            g.user_name = user["user_name"]
            return f(*args, **kwargs)
        except Exception:
            logger.exception(f"[{g.request_id}] ошибка проверки API ключа")
            return jsonify({"error": "Внутренняя ошибка сервера", "request_id": g.request_id}), 500
        finally:
            db.close()
    return decorated
    
@app.route("/register", methods=["POST"])
def register():
    data = request.get_json(silent=True) or {}
    invite_code = (data.get("invite_code") or "").strip()
    user_name = (data.get("user_name") or "").strip()
    role = "user"  
    logger.info(f"Начался запрос от пользователя: {user_name}")
    if not invite_code or not user_name:
        return jsonify({"error": "Нужны invite_code и user_name", "request_id": g.request_id}), 400

    invite_hash = sha256_hex(invite_code)
    db = SessionLocal()
    try:
        
        with db.begin():
            
            row = db.execute(text("""
                select id, expires_at, used_at
                from invites
                where code_hash = :h
                for update
            """), {"h": invite_hash}).mappings().first()
            if row is None:
                logger.info(f"Приглашения никогда не существовало.")
                return jsonify({"error": "Код-приглашение не найден", "request_id": g.request_id}), 400

            if row["used_at"] is not None:
                logger.info(f"Приглашение уже использовано.")
                return jsonify({"error": "Приглашение уже использовано", "request_id": g.request_id}), 400

            expired = db.execute(text("""
                select (expires_at <= now()) as expired
                from invites
                where id = :id
            """), {"id": row["id"]}).scalar()
            
            if expired:
                logger.info(f"Код истёк.")
                return jsonify({"error": "Срок действия инвайта истёк", "request_id": g.request_id}), 400

            user_id = db.execute(text("""
                insert into users (user_name, role)
                values (:user_name, :role)
                returning id
            """), {"user_name": user_name, "role": role}).scalar()
                
            
            api_key_plain = generate_key()
            api_key_hash = hash_key(api_key_plain)

            db.execute(text("""
                insert into api_keys (user_id, key_hash)
                values (:user_id, :key_hash)
            """), {"user_id": user_id, "key_hash": api_key_hash})

            
            db.execute(text("""
                update invites
                set used_at = now(), used_by_user_id = :user_id
                where id = :id
            """), {"user_id": user_id, "id": row["id"]})

            logger.info(
                f"[{g.request_id}] успешно зарегистрировал пользователя ={user_name}"
            )
        return jsonify({
            "user_name": user_name,
            "api_key": api_key_plain
        }), 201

    except Exception:
        logger.exception(f"[{g.request_id}] register failed")
        return jsonify({"error": "Внутренняя ошибка сервера", "request_id": g.request_id}), 500
    finally:
        db.close()
        
@app.route("/templates", methods=["GET"])
@require_api_key
def list_templates():
    items = [
        {"id": template_id, "title": template_id}
        for template_id in AVAILABLE_TEMPLATES
    ]

    return jsonify({"templates": items})


@app.route("/format", methods=["POST"])
@require_api_key
def format_text():
    try:
        logger.info(
            "[%s] Получен запрос от %s",
            g.request_id,
            request.remote_addr
        )

        data = request.get_json(silent=True)

        if not isinstance(data, dict):
            return jsonify({
                "error": "Некорректное тело запроса",
                "request_id": g.request_id
            }), 400

        # Проверка исходной записи

        source_text = data.get("text")

        if not isinstance(source_text, str):
            return jsonify({
                "error": "Текст записи должен быть строкой",
                "request_id": g.request_id
            }), 400

        source_text = source_text.strip()

        if not source_text:
            return jsonify({
                "error": "Запрос не должен быть пуст",
                "request_id": g.request_id
            }), 400

        if len(source_text) > MAX_INPUT_LENGTH:
            return jsonify({
                "error": (
                    f"Слишком большой объем текста. "
                    f"Максимальная длина: "
                    f"{MAX_INPUT_LENGTH} знаков."
                ),
                "request_id": g.request_id
            }), 413

        # Проверка языка

        language = data.get("language")

        if not isinstance(language, str):
            return jsonify({
                "error": "Язык записи должен быть строкой",
                "request_id": g.request_id
            }), 400

        language = language.strip().upper()

        if language not in ("RU", "EN"):
            return jsonify({
                "error": "Неподдерживаемый язык записи",
                "request_id": g.request_id
            }), 400

        # Проверка выбранного шаблона

        template_id = data.get(
            "templateId",
            default_template
        )

        if not isinstance(template_id, str):
            return jsonify({
                "error": "Некорректный шаблон",
                "request_id": g.request_id
            }), 400

        template_id = template_id.strip()

        if template_id not in AVAILABLE_TEMPLATES:
            return jsonify({
                "error": "Неизвестный вид шаблона",
                "request_id": g.request_id
            }), 400

        logger.info(
            "[%s] using_template=%s",
            g.request_id,
            template_id
        )


        source_type = classify_source(
            source_text,
            language,
            g.request_id
        )

        logger.info(
            "[%s] classified_type=%s",
            g.request_id,
            source_type
        )

        if source_type == "undefined":
            return jsonify({
                "error": "Тип источника не определён",
                "source_type": source_type,
                "request_id": g.request_id
            }), 422


        formatted_text = format_source(
            source_text,
            language,
            source_type,
            template_id,
            g.request_id,
            user_id=g.user_id
        )

        return jsonify({
            "result": formatted_text,
            "source_type": source_type
        })

    except ClassifierBusy:
        return jsonify({
            "error": "Классификатор занят",
            "request_id": g.request_id
        }), 429

    except FormatterBusy:
        return jsonify({
            "error": "Форматировщик занят",
            "request_id": g.request_id
        }), 429

    except ClassificationFailed:
        logger.exception(
            "[%s] Некорректный ответ классификатора",
            g.request_id
        )

        return jsonify({
            "error": "Некорректный ответ классификатора",
            "request_id": g.request_id
        }), 502

    except FormattingFailed:
        logger.exception(
            "[%s] Ошибка ответа форматировщика",
            g.request_id
        )

        return jsonify({
            "error": "Некорректный ответ форматировщика",
            "request_id": g.request_id
        }), 502

    except FormatterConfigurationError:
        logger.exception(
            "[%s] Ошибка конфигурации форматировщика",
            g.request_id
        )

        return jsonify({
            "error": "Шаблон форматирования недоступен",
            "request_id": g.request_id
        }), 503

    except requests.exceptions.Timeout:
        logger.exception(
            "[%s] Таймаут Ollama",
            g.request_id
        )

        return jsonify({
            "error": "Таймаут при обращении к модели",
            "request_id": g.request_id
        }), 504

    except requests.exceptions.ConnectionError:
        logger.exception(
            "[%s] Ollama недоступна",
            g.request_id
        )

        return jsonify({
            "error": "Модель недоступна",
            "request_id": g.request_id
        }), 502

    except requests.exceptions.RequestException:
        logger.exception(
            "[%s] Ошибка обращения к Ollama",
            g.request_id
        )

        return jsonify({
            "error": "Ошибка в работе модели",
            "request_id": g.request_id
        }), 502

    except Exception:
        logger.exception(
            "[%s] Внутренняя ошибка сервера",
            g.request_id
        )

        return jsonify({
            "error": "Внутренняя ошибка сервера",
            "request_id": g.request_id
        }), 500
        
@app.route("/feedback", methods=["POST"])
@require_api_key
def receive_feedback():
    data = request.get_json(silent=True)

    if not isinstance(data, dict):
        return jsonify({
            "error": "Ожидается JSON-объект"
        }), 400

    required_fields = (
        "templateId",
        "source_type",
        "language",
        "original_text",
        "model_output",
        "expected_output"
    )

    for field in required_fields:
        if not isinstance(data.get(field), str) or not data[field].strip():
            return jsonify({
                "error": f"Отсутствует или пустое поле: {field}"
            }), 400

    template_id = data["templateId"]
    source_type = data["source_type"]
    language = data["language"]

    if template_id not in ("default", "test", "strict"):
        return jsonify({
            "error": "Неизвестный шаблон"
        }), 400

    if source_type not in (
        "book", "journal", "proceedings",
        "patent", "standard", "law",
        "dissertation", "website", "webpage"
    ):
        return jsonify({
            "error": "Неизвестный тип источника"
        }), 400

    if language not in ("RU", "EN"):
        return jsonify({
            "error": "Неизвестный язык"
        }), 400

    logger.info(
        "FEEDBACK_RECEIVED template=%s type=%s language=%s",
        template_id,
        source_type,
        language
    )

    try:
        save_feedback(
            template_id=template_id,
            source_type=source_type,
            language=language,
            user_id=g.user_id,
            original_text=data["original_text"],
            model_output=data["model_output"],
            expected_output=data["expected_output"]
        )

        logger.info(
            "[%s] FEEDBACK_SAVED user_id=%s "
            "template=%s type=%s language=%s",
            g.request_id,
            g.user_id,
            template_id,
            source_type,
            language
        )

        return jsonify({
            "status": "saved",
            "message": "Исправление сохранено",
            "request_id": g.request_id
        }), 201

    except (OSError, ValueError):
        logger.exception(
            "[%s] Ошибка сохранения фидбека",
            g.request_id
        )

        return jsonify({
            "error": "Не удалось сохранить исправление",
            "request_id": g.request_id
        }), 500

if __name__ == "__main__":
    observer = None
    try:
        
        logger.info("Сервер запускается...")
        
        serve(
            app,
            host="127.0.0.1",
            port=44753,
            threads=4
        )
        
    except KeyboardInterrupt:
        logger.info("Остановка сервера...")

    except Exception:
        logger.exception("Ошибка при запуске сервера")

    finally:
        if observer is not None:
            logger.info("Остановка наблюдателя...")
            observer.stop()
            observer.join()