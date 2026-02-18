import configparser
from flask import Flask, request, jsonify, g
import requests
import logging
from functools import wraps
from sqlalchemy import create_engine, text
from sqlalchemy.orm import sessionmaker
import os  
import threading
from watchdog.observers import Observer
from watchdog.events import FileSystemEventHandler
import uuid
import hashlib
import secrets 
from datetime import datetime, timezone
import base64
import hmac
import time
from threading import Semaphore
import json

llm_gate = Semaphore(1)

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)
config = configparser.ConfigParser()
with open('configOllama.ini', 'r', encoding='utf-8') as config_file:
    config.read_file(config_file)
DATABASE_URL = config['server']['database_api']
engine = create_engine(DATABASE_URL)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)


server_model_api = config['server']['model_api']
system_prompt = ""
prompt_path = config['model']['system_prompt_path']
with open(prompt_path, 'r', encoding='utf-8') as f:
    system_prompt = f.read()

app = Flask(__name__)

API_KEY = config['server']['api_key']
MODEL_API = server_model_api
MAX_INPUT_LENGTH = 2048
hash_alg = "sha256"
hash_iterations = 600000
hash_dklen = 32
salt_length = 16
@app.before_request
def add_request_id():
    g.request_id = request.headers.get("X-Request-ID") or uuid.uuid4().hex
    
@app.after_request
def add_request_id_header(response):
    response.headers["X-Request-ID"] = g.request_id
    return response


class PromptFileHandler(FileSystemEventHandler):
    def on_modified(self, event):
        if event.src_path == os.path.abspath(prompt_path):
            logger.info(f"Обнаружено изменение файла промпта: {prompt_path}")
            try:
                with open(prompt_path, 'r', encoding='utf-8') as f:
                    new_prompt = f.read()
                
                global system_prompt
                system_prompt = new_prompt
                
                logger.info("Системный промпт успешно обновлен без перезапуска сервера")
                
            except Exception as e:
                logger.error(f"Ошибка при обновлении промпта: {e}")
def start_file_watcher():
    event_handler = PromptFileHandler()
    
    observer = Observer()
    
    prompt_dir = os.path.dirname(os.path.abspath(prompt_path))
    if not prompt_dir:  
        prompt_dir = "."
    

    observer.schedule(event_handler, path=prompt_dir, recursive=False)
    

    observer.start()
    logger.info(f"Наблюдатель запущен для отслеживания файла: {prompt_path}")
    
    return observer

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

            ok = any(verify_key(api_key_plain, stored) for stored in rows)
            if not ok:
                logger.warning(f"[{g.request_id}] неверный API ключ. IP: {request.remote_addr}")
                return jsonify({"error": "Неверный или отсутствующий API ключ", "request_id": g.request_id}), 401

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
    logger.warning(f"Начался запрос")
    if not invite_code or not user_name:
        return jsonify({"error": "Нужны invite_code и user_name", "request_id": g.request_id}), 400

    invite_hash = sha256_hex(invite_code)
    logger.warning(f"Захешил инвайт")
    db = SessionLocal()
    try:
        
        with db.begin():
            
            row = db.execute(text("""
                select id, expires_at, used_at
                from invites
                where code_hash = :h
                for update
            """), {"h": invite_hash}).mappings().first()
            logger.warning(f"Залез в базу")
            if row is None:
                logger.warning(f"Нету такого")
                return jsonify({"error": "Инвайт-код не найден", "request_id": g.request_id}), 400

            if row["used_at"] is not None:
                logger.warning(f"Уже использован")
                return jsonify({"error": "Инвайт-код уже использован", "request_id": g.request_id}), 400

            logger.warning(f"Проверяю, не истёк ли")
            expired = db.execute(text("""
                select (expires_at <= now()) as expired
                from invites
                where id = :id
            """), {"id": row["id"]}).scalar()
            
            if expired:
                logger.warning(f"Уже истёк")
                return jsonify({"error": "Срок действия инвайта истёк", "request_id": g.request_id}), 400

            logger.warning(f"Создаю юзера")
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

        
        return jsonify({
            "user_name": user_name,
            "api_key": api_key_plain
        }), 201

    except Exception:
        logger.exception(f"[{g.request_id}] register failed")
        return jsonify({"error": "Внутренняя ошибка сервера", "request_id": g.request_id}), 500
    finally:
        db.close()



@app.route("/format", methods=["POST"])
@require_api_key
def format_text():
    try:
        logger.info(f"[{g.request_id}] Получен запрос от {request.remote_addr}")
        data = request.get_json(silent=True)
        if data is None:
            logger.warning(f"Не удалось получить данные из запроса")
            return jsonify({"error": "Некорректное тело запроса"}), 400
        text = data.get("text", "")
        if not isinstance(text, str):
            return jsonify({"error": "Не удалось получить данные из запроса: данные должны иметь вид текста"}), 400
        text = text.strip()
        if not text:
            return jsonify({"error": "Запрос не должен быть пуст"}), 400
        if len(text) > MAX_INPUT_LENGTH:
            return jsonify({"error": f"Слишком большой объем текста. Максимальная длина: {MAX_INPUT_LENGTH} знаков."}), 413
        if not llm_gate.acquire(timeout=1):
            return jsonify({"error": "Модель занята, попробуйте ещё раз", "request_id": g.request_id}), 429

        try:
            logger.error(f"MODEL_API={MODEL_API} model=qwen3:8b prompt_len={len(system_prompt)} text_len={len(text)}")
            payload = {
                "model": "qwen3-editor",
                "think": False,
                "messages": [{"role":"user","content": text}],
                "stream": False
            }





            t0 = time.time()
            body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
            headers = {"Content-Type": "application/json"}

            logger.error(f"OUTGOING bytes={len(body)} first200={body[:200]!r}")
            session = requests.Session()
            session.trust_env = False
            response = session.post(MODEL_API, data=body, headers=headers, timeout=(5, 240))
            logger.error(f"RESP status={response.status_code} headers={dict(response.headers)}")
            dt = time.time() - t0

            if response.status_code >= 400:
                logger.error(f"Ollama status={response.status_code} in {dt:.2f}s body_len={len(response.content)} body={response.text!r}")
                response.raise_for_status()

            result = response.json()
            message = result.get("message") or {}
            content = (message.get("content") or "").strip()
            if not content:
                content = (message.get("thinking") or "").strip()
            if not content:
                return jsonify({"error": "Модель вернула пустой ответ", "request_id": g.request_id}), 502
            return jsonify({"result": content})


        finally:
            llm_gate.release()
    except requests.exceptions.Timeout:
        logger.error("Таймаут при обращении к LLM")
        return jsonify({"error": "Таймаут при обращении к LLM"}), 504

    except requests.exceptions.ConnectionError:
        logger.error("Нет связи с моделью (возможно, не запущена)")
        return jsonify({"error": "Модель недоступна"}), 502

    except requests.exceptions.RequestException as e:
        logger.error(f"Ошибка запроса к модели: {type(e).__name__}: {e}", exc_info=True)
        if hasattr(e, 'response') and e.response is not None:
            logger.error(f"Ответ от модели: {e.response.status_code}")
        return jsonify({"error": "Ошибка в работе модели"}), 500

    except Exception as e:
        logger.exception("Неизвестная ошибка во Flask")
        return jsonify({"error": "Внутренняя ошибка сервера"}), 500

if __name__ == "__main__":
    try:
        observer = start_file_watcher()
        
        logger.info("Сервер запускается...")
        
        app.run(host="0.0.0.0", port=44752, debug=False, threaded=False)
        
    except KeyboardInterrupt:
        logger.info("Остановка наблюдателя...")
        observer.stop()
        
    except Exception as e:
        logger.error(f"Ошибка при запуске: {e}")