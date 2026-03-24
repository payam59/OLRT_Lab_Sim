import sqlite3
import os

DB_FILE = "lab_assets.db"
LOG_DIR = "simulation_logs"


def init_db():
    if not os.path.exists(LOG_DIR): os.makedirs(LOG_DIR)
    conn = sqlite3.connect(DB_FILE)

    # 1. Create the table if it doesn't exist
    conn.execute('''
                 CREATE TABLE IF NOT EXISTS assets
                 (
                     id
                     INTEGER
                     PRIMARY
                     KEY
                     AUTOINCREMENT,
                     name
                     TEXT
                     UNIQUE,
                     type
                     TEXT,
                     protocol
                     TEXT,
                     address
                     INTEGER,
                     min_range
                     REAL
                     DEFAULT
                     0.0,
                     max_range
                     REAL
                     DEFAULT
                     100.0,
                     current_value
                     REAL
                     DEFAULT
                     0.0,
                     drift_rate
                     REAL
                     DEFAULT
                     0.1,
                     manual_override
                     INTEGER
                     DEFAULT
                     0,
                     icon
                     TEXT
                 )
                 ''')

    # 2. MIGRATION: Check if 'filename' column exists, add it if not
    cursor = conn.execute("PRAGMA table_info(assets)")
    columns = [column[1] for column in cursor.fetchall()]

    if 'filename' not in columns:
        print("Migrating database: Adding 'filename' column...")
        conn.execute("ALTER TABLE assets ADD COLUMN filename TEXT")

    conn.commit()
    conn.close()

def get_db_connection():
    conn = sqlite3.connect(DB_FILE)
    conn.row_factory = sqlite3.Row
    return conn