import requests
import json
import configparser
config = configparser.ConfigParser()
SYSTEM_PROMPT_PATH = 'system_prompt.txt'
MODEL_URL = "http://127.0.0.1:1234/v1/completions"  
SERVER_URL = "http://127.0.0.1:44752/format"         
API_KEY = "eAISU92zIiY2amiwf37U6n7ebZbJnydb;"
def build_prompt(system_prompt: str, user_text: str) -> str:
    return (
        f"<|im_start|>system\n{system_prompt}<|im_end|>\n"
        f"<|im_start|>user\n{user_text}<|im_end|>\n"
        f"<|im_start|>assistant\n"
    )
try:
    with open('system_prompt.txt', 'r', encoding='utf-8') as f:
        system_prompt = f.read().strip()
except Exception as e:
    print(f"Не удалось прочитать system_prompt.txt: {e}")
    exit(1)
with open('system_prompt.txt', 'r', encoding='utf-8') as f:
    system_prompt = f.read().strip()

print("Введите источник")
user_text = input("")
if not user_text:
    print("Пустой ввод!")
    exit(1)


full_prompt = build_prompt(system_prompt, user_text)

payload = {
    "model": "Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
    "prompt": full_prompt,
    "temperature": 0.1,
    "max_tokens": 256,
    "stop": ["<|im_end|>", "<tool_call>", "<tool_call>"],
    "stream": False
}


# --- Отправляем напрямую в llama.cpp (для теста модели) ---
print("\n🔹 Тестируем модель напрямую (llama.cpp)...")
try:
    response = requests.post(MODEL_URL, json=payload, timeout=30)
    if response.status_code == 200:
        result = response.json()
        text = result["choices"][0].get("text", "").strip()
        print("Модель работает. Ответ:")
        print(text)
    else:
        print(f"Ошибка модели: {response.status_code}, {response.text}")
except Exception as e:
    print(f"Нет связи с llama.cpp (запущен ли сервер?): {e}")

# --- Теперь тестируем ТВОЙ Flask API (с API Key!) ---
print("\n🔹 Тестируем Flask API (/format) с API Key...")
try:
    response = requests.post(
        SERVER_URL,
        json={"text": user_text},
        headers={"X-API-Key": API_KEY},  # ← Вот он, ключ!
        timeout=30
    )

    if response.status_code == 200:
        result = response.json()
        formatted = result.get("result", "").strip()
        print("Flask API работает. Ответ:")
        print(formatted)
    elif response.status_code == 401:
        print("Ошибка авторизации: неверный или отсутствующий API Key")
        print("Проверь: совпадает ли ключ в test_model.py и config.ini?")
    else:
        print(f"Ошибка Flask: {response.status_code}, {response.json()}")
except Exception as e:
    print(f"Нет связи с Flask (запущен ли server.py?): {e}")