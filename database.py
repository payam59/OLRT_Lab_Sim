import sqlite3
import os
import time

DB_FILE = "lab_assets.db"
LOG_DIR = "simulation_logs"

def init_db():
    if not os.path.exists(LOG_DIR): os.makedirs(LOG_DIR)
    conn = sqlite3.connect(DB_FILE)

    # Create BBMD table
    conn.execute('''
        CREATE TABLE IF NOT EXISTS bbmd (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT UNIQUE NOT NULL,
            description TEXT,
            port INTEGER UNIQUE NOT NULL,
            device_id INTEGER NOT NULL,
            ip_address TEXT DEFAULT '0.0.0.0',
            enabled INTEGER DEFAULT 1,
            created_at REAL DEFAULT 0.0
        )
    ''')

    # Create assets table with BBMD relationship
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
            is_normally_open INTEGER DEFAULT 1,
            change_probability REAL DEFAULT 0.0,
            change_interval INTEGER DEFAULT 15,
            last_flip_check REAL DEFAULT 0.0,
            bbmd_id INTEGER,
            object_type TEXT DEFAULT 'value',
            bacnet_properties TEXT DEFAULT '{}',
            modbus_unit_id INTEGER DEFAULT 1,
            modbus_register_type TEXT DEFAULT 'holding',
            modbus_ip TEXT DEFAULT '0.0.0.0',
            modbus_port INTEGER DEFAULT 5020,
            modbus_alarm_address INTEGER,
            modbus_alarm_bit INTEGER DEFAULT 0,
            modbus_zero_based INTEGER DEFAULT 1,
            modbus_word_order TEXT DEFAULT 'low_high',
            alarm_state INTEGER DEFAULT 0,
            alarm_message TEXT,
            FOREIGN KEY (bbmd_id) REFERENCES bbmd(id) ON DELETE SET NULL
        )
    ''')

    conn.execute('''
        CREATE TABLE IF NOT EXISTS alarm_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            asset_id INTEGER NOT NULL,
            asset_name TEXT NOT NULL,
            message TEXT NOT NULL,
            active INTEGER DEFAULT 1,
            created_at REAL NOT NULL,
            cleared_at REAL,
            FOREIGN KEY (asset_id) REFERENCES assets(id) ON DELETE CASCADE
        )
    ''')

    # Migrations for assets table
    cursor = conn.execute("PRAGMA table_info(assets)")
    columns = [column[1] for column in cursor.fetchall()]

    migrations = {
        'sub_type': 'TEXT DEFAULT "Analog"',
        'is_normally_open': 'INTEGER DEFAULT 1',
        'bacnet_port': 'INTEGER DEFAULT 47808',
        'bacnet_device_id': 'INTEGER DEFAULT 1234',
        'filename': 'TEXT',
        'change_probability': 'REAL DEFAULT 0.0',
        'change_interval': 'INTEGER DEFAULT 15',
        'last_flip_check': 'REAL DEFAULT 0.0',
        'bbmd_id': 'INTEGER',
        'object_type': 'TEXT DEFAULT "value"',
        'bacnet_properties': 'TEXT DEFAULT "{}"',
        'modbus_unit_id': 'INTEGER DEFAULT 1',
        'modbus_register_type': 'TEXT DEFAULT "holding"',
        'modbus_ip': 'TEXT DEFAULT "0.0.0.0"',
        'modbus_port': 'INTEGER DEFAULT 5020',
        'modbus_alarm_address': 'INTEGER',
        'modbus_alarm_bit': 'INTEGER DEFAULT 0',
        'modbus_zero_based': 'INTEGER DEFAULT 1',
        'modbus_word_order': 'TEXT DEFAULT "low_high"',
        'alarm_state': 'INTEGER DEFAULT 0',
        'alarm_message': 'TEXT'
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
