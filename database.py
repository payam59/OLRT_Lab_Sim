import sqlite3
import os

DB_FILE = "lab_assets.db"
LOG_DIR = "simulation_logs"

def init_db():
    if not os.path.exists(LOG_DIR): os.makedirs(LOG_DIR)
    conn = sqlite3.connect(DB_FILE)

    conn.execute('''
        CREATE TABLE IF NOT EXISTS assets (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT UNIQUE,
            type TEXT,
            sub_type TEXT DEFAULT 'Analog',
            protocol TEXT,
            address INTEGER,
            min_range REAL DEFAULT 0.0,
            max_range REAL DEFAULT 100.0,
            current_value REAL DEFAULT 0.0,
            drift_rate REAL DEFAULT 0.1,
            manual_override INTEGER DEFAULT 0,
            icon TEXT,
            filename TEXT,
            bacnet_port INTEGER DEFAULT 47808,
            bacnet_device_id INTEGER DEFAULT 1234,
            is_normally_open INTEGER DEFAULT 1
        )
    ''')

    cursor = conn.execute("PRAGMA table_info(assets)")
    columns = [column[1] for column in cursor.fetchall()]

    migrations = {
        'sub_type': 'TEXT DEFAULT "Analog"',
        'is_normally_open': 'INTEGER DEFAULT 1',
        'bacnet_port': 'INTEGER DEFAULT 47808',
        'bacnet_device_id': 'INTEGER DEFAULT 1234',
        'filename': 'TEXT'
    }

    for col, col_def in migrations.items():
        if col not in columns:
            conn.execute(f"ALTER TABLE assets ADD COLUMN {col} {col_def}")

    conn.commit()
    conn.close()

def get_db_connection():
    conn = sqlite3.connect(DB_FILE)
    conn.row_factory = sqlite3.Row
    return conn