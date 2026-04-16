using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;

namespace OLRTLabSim.Data
{
    public static class Database
    {
        public static readonly string DbFile = "lab_assets.db";
        public static readonly string LogDir = "simulation_logs";

        public static void InitDb()
        {
            if (!Directory.Exists(LogDir))
                Directory.CreateDirectory(LogDir);

            using (var conn = GetConnection())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS bbmd (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            name TEXT UNIQUE NOT NULL,
                            description TEXT,
                            port INTEGER UNIQUE NOT NULL,
                            device_id INTEGER NOT NULL,
                            ip_address TEXT DEFAULT '0.0.0.0',
                            enabled INTEGER DEFAULT 1,
                            created_at REAL DEFAULT 0.0
                        )";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = @"
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
                            dnp3_ip TEXT DEFAULT '0.0.0.0',
                            dnp3_port INTEGER DEFAULT 20000,
                            dnp3_outstation_address INTEGER DEFAULT 10,
                            dnp3_master_address INTEGER DEFAULT 1,
                            dnp3_point_class TEXT DEFAULT 'analog_output',
                            dnp3_event_class INTEGER DEFAULT 1,
                            dnp3_static_variation INTEGER DEFAULT 0,
                            alarm_state INTEGER DEFAULT 0,
                            alarm_message TEXT,
                            FOREIGN KEY (bbmd_id) REFERENCES bbmd(id) ON DELETE SET NULL
                        )";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS alarm_events (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            asset_id INTEGER NOT NULL,
                            asset_name TEXT NOT NULL,
                            message TEXT NOT NULL,
                            active INTEGER DEFAULT 1,
                            created_at REAL NOT NULL,
                            cleared_at REAL,
                            FOREIGN KEY (asset_id) REFERENCES assets(id) ON DELETE CASCADE
                        )";
                    cmd.ExecuteNonQuery();
                }

                // SQLite doesn't natively support easy ALTER TABLE for multiple columns in old versions
                // But for basic string/int appends we can do this simply
                List<string> columns = new List<string>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(assets)";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            columns.Add(reader["name"].ToString());
                        }
                    }
                }

                var migrations = new Dictionary<string, string>
                {
                    { "sub_type", "TEXT DEFAULT 'Analog'" },
                    { "is_normally_open", "INTEGER DEFAULT 1" },
                    { "bacnet_port", "INTEGER DEFAULT 47808" },
                    { "bacnet_device_id", "INTEGER DEFAULT 1234" },
                    { "filename", "TEXT" },
                    { "change_probability", "REAL DEFAULT 0.0" },
                    { "change_interval", "INTEGER DEFAULT 15" },
                    { "last_flip_check", "REAL DEFAULT 0.0" },
                    { "bbmd_id", "INTEGER" },
                    { "object_type", "TEXT DEFAULT 'value'" },
                    { "bacnet_properties", "TEXT DEFAULT '{}'" },
                    { "modbus_unit_id", "INTEGER DEFAULT 1" },
                    { "modbus_register_type", "TEXT DEFAULT 'holding'" },
                    { "modbus_ip", "TEXT DEFAULT '0.0.0.0'" },
                    { "modbus_port", "INTEGER DEFAULT 5020" },
                    { "modbus_alarm_address", "INTEGER" },
                    { "modbus_alarm_bit", "INTEGER DEFAULT 0" },
                    { "modbus_zero_based", "INTEGER DEFAULT 1" },
                    { "modbus_word_order", "TEXT DEFAULT 'low_high'" },
                    { "dnp3_ip", "TEXT DEFAULT '0.0.0.0'" },
                    { "dnp3_port", "INTEGER DEFAULT 20000" },
                    { "dnp3_outstation_address", "INTEGER DEFAULT 10" },
                    { "dnp3_master_address", "INTEGER DEFAULT 1" },
                    { "dnp3_point_class", "TEXT DEFAULT 'analog_output'" },
                    { "dnp3_event_class", "INTEGER DEFAULT 1" },
                    { "dnp3_static_variation", "INTEGER DEFAULT 0" },
                    { "alarm_state", "INTEGER DEFAULT 0" },
                    { "alarm_message", "TEXT" }
                };

                using (var cmd = conn.CreateCommand())
                {
                    foreach (var kvp in migrations)
                    {
                        if (!columns.Contains(kvp.Key))
                        {
                            cmd.CommandText = $"ALTER TABLE assets ADD COLUMN {kvp.Key} {kvp.Value}";
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        public static SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection($"Data Source={DbFile};");
            conn.Open();
            return conn;
        }

        public static double GetCurrentUnixTime()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (DateTimeOffset.UtcNow.Millisecond / 1000.0);
        }
    }
}
