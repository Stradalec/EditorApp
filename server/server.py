import configparser
from flask import Flask, request, jsonify
import requests
import logging
from functools import wraps
from sqlalchemy import create_engine, text
from sqlalchemy.orm import sessionmaker
import os  
import threading
from watchdog.observers import Observer
from watchdog.events import FileSystemEventHandler  
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)
config = configparser.ConfigParser()
with open('config.ini', 'r', encoding='utf-8') as config_file:
    config.read_file(config_file)
DATABASE_URL = config['server']['database_api']
engine = create_engine(DATABASE_URL)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
def is_valid_api_key(api_key: str) -> bool:
    if not api_key:
        return False

    db = SessionLocal()
    try:
        
        result = db.execute(
            text("SELECT 1 FROM users WHERE api_key = :api_key"),
            {"api_key": api_key}
        ).first()
        
        return result is not None  
    except Exception as e:
        logger.error(f"Ошибка при доступе к БД: {e}")
        return False
    finally:
        db.close()
server_model_api = config['server']['model_api']
system_prompt = ""
prompt_path = config['model']['system_prompt_path']
with open(prompt_path, 'r', encoding='utf-8') as f:
    system_prompt = f.read()

app = Flask(__name__)
API_KEY = config['server']['api_key']
MODEL_API = server_model_api
MAX_INPUT_LENGTH = 2048
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

def require_api_key(f):
    @wraps(f)
    def decorated(*args, **kwargs):

        key = request.headers.get("X-API-Key")
        if not key or key != API_KEY:
            logger.warning(f"Неверный или отсутствующий API ключ. IP: {request.remote_addr}")
            return jsonify({"error": "Invalid or missing API key"}), 401
        return f(*args, **kwargs)
    return decorated

@app.route("/format", methods=["POST"])
@require_api_key
def format_text():
    try:
        logger.info(f"Получен запрос от {request.remote_addr}")
        data = request.get_json()
        text = data.get("text", "")
        if len(text) > MAX_INPUT_LENGTH:
            return jsonify({"error": f"Text too long. Max {MAX_INPUT_LENGTH} chars."}), 413
        full_prompt = (
            f"<|im_start|>system\n{system_prompt}<|im_end|>\n"
            f"<|im_start|>user\n{text}<|im_end|>\n"
            f"<|im_start|>assistant\n"
        )
        payload = {
            "model": "qwen3-vl-8b-instruct", 
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": text}
            ],
            "temperature": 0.1,
            "max_tokens": 512,
            "stop": ["<|im_end|>", "<tool_call>"],
            "stream": False
        }
        response = requests.post(MODEL_API, json=payload, timeout = 60)
        response.raise_for_status()
        result = response.json()

        content = result["choices"][0]["message"]["content"].strip()
        return jsonify({"result": content})
    except requests.exceptions.Timeout:
        logger.error("Таймаут при обращении к LLM")
        return jsonify({"error": "Model timed out"}), 504

    except requests.exceptions.ConnectionError:
        logger.error("Нет связи с моделью (возможно, не запущена)")
        return jsonify({"error": "Model service unreachable"}), 502

    except requests.exceptions.RequestException as e:
        logger.error(f"Ошибка запроса к модели: {type(e).__name__}: {e}", exc_info=True)
        if hasattr(e, 'response') and e.response is not None:
            logger.error(f"Ответ от модели: {e.response.status_code}, {e.response.text}")
        return jsonify({"error": "Model error"}), 500

    except Exception as e:
        logger.exception("Неизвестная ошибка во Flask")
        return jsonify({"error": "Internal server error"}), 500

if __name__ == "__main__":
    try:
        observer = start_file_watcher()
        
        logger.info("Сервер запускается...")
        
        app.run(host="0.0.0.0", port=44752, debug=False, threaded=True)
        
    except KeyboardInterrupt:
        logger.info("Остановка наблюдателя...")
        observer.stop()
        
    except Exception as e:
        logger.error(f"Ошибка при запуске: {e}")