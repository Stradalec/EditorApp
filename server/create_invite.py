import configparser
import hashlib
import secrets
from sqlalchemy import create_engine, text
from sqlalchemy.orm import sessionmaker

def sha256_hex(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()

def generate_invite_code() -> str:
    return secrets.token_urlsafe(24)

def main():
    config = configparser.ConfigParser()
    with open("config.ini", "r", encoding="utf-8") as f:
        config.read_file(f)

    engine = create_engine(config["server"]["database_api"])
    SessionLocal = sessionmaker(bind=engine, autocommit=False, autoflush=False)

    expires_interval = "7 days"

    invite_code = generate_invite_code()
    code_hash = sha256_hex(invite_code)

    db = SessionLocal()
    try:
        with db.begin():
            db.execute(text("""
                insert into invites (code_hash, expires_at)
                values (:code_hash, now() + interval :interval)
            """), {"code_hash": code_hash, "interval": expires_interval})

        print("Создано приглашение:.")
        print("Код приглашения:")
        print(invite_code)

    finally:
        db.close()

if __name__ == "__main__":
    main()
