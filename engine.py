import asyncio
import time
import random
import json

from database import get_db_connection


def check_alarm_condition(asset_dict):
    """Check if asset is in alarm state based on threshold violations"""
    current_value = asset_dict['current_value']
    min_range = asset_dict['min_range']
    max_range = asset_dict['max_range']

    if asset_dict['sub_type'] == 'Analog':
        if current_value < min_range:
            return True, f"Low Alarm: {current_value:.2f} < {min_range:.2f}"
        elif current_value > max_range:
            return True, f"High Alarm: {current_value:.2f} > {max_range:.2f}"

    return False, None


async def simulation_loop(modbus_block, bacnet_manager, ws_manager=None):
    while True:
        conn = None
        try:
            conn = get_db_connection()
            cursor = conn.cursor()
            assets = cursor.execute("SELECT * FROM assets").fetchall()
            now = time.time()
            updated_assets = []
            any_global_change = False

            for a in assets:
                asset_dict = dict(a)
                original_value = float(asset_dict['current_value'])
                asset_changed = False
                alarm_changed = False

                # 1. BACnet/Modbus remote write detection.
                # Keep this active even during manual override so repeated external writes
                # are reflected in DB/state and not ignored after the first write.
                if asset_dict['protocol'] == "bacnet" and asset_dict.get('object_type') in ['output', 'value']:
                    remote_val = bacnet_manager.get_value(asset_dict['name'])
                    if remote_val is not None and abs(remote_val - original_value) > 0.01:
                        cursor.execute("UPDATE assets SET current_value = ?, manual_override = 1 WHERE id = ?",
                                       (remote_val, asset_dict['id']))
                        asset_dict['current_value'] = remote_val
                        asset_dict['manual_override'] = 1
                        asset_changed = True
                elif asset_dict['protocol'] == "modbus" and modbus_block:
                    if asset_dict.get('modbus_register_type') in ['holding', 'coil']:
                        remote_val = modbus_block.read_remote_value(asset_dict)
                        if remote_val is not None and abs(remote_val - original_value) > 0.01:
                            cursor.execute("UPDATE assets SET current_value = ?, manual_override = 1 WHERE id = ?",
                                           (remote_val, asset_dict['id']))
                            asset_dict['current_value'] = remote_val
                            asset_dict['manual_override'] = 1
                            asset_changed = True

                # 2. Automation Logic (only if not manually overridden)
                if not asset_dict['manual_override']:
                    if asset_dict['sub_type'] == "Digital":
                        last_check = asset_dict['last_flip_check'] or 0
                        interval_sec = asset_dict['change_interval'] * 60

                        if (now - last_check) >= interval_sec:
                            cursor.execute("UPDATE assets SET last_flip_check = ? WHERE id = ?",
                                           (now, asset_dict['id']))
                            asset_dict['last_flip_check'] = now

                            if random.random() < (asset_dict['change_probability'] / 100.0):
                                new_val = 1.0 if original_value < 0.5 else 0.0
                                cursor.execute("UPDATE assets SET current_value = ? WHERE id = ?",
                                               (new_val, asset_dict['id']))
                                asset_dict['current_value'] = new_val
                                asset_changed = True
                    else:
                        # Analog Drift
                        noise = random.uniform(-asset_dict['drift_rate'], asset_dict['drift_rate'])
                        new_val = original_value + noise
                        # Allow value to exceed limits to trigger alarms
                        if abs(new_val - original_value) > 0.001:
                            cursor.execute("UPDATE assets SET current_value = ? WHERE id = ?",
                                           (new_val, asset_dict['id']))
                            asset_dict['current_value'] = new_val
                            asset_changed = True

                # 3. Alarm Detection
                in_alarm, alarm_msg = check_alarm_condition(asset_dict)
                if in_alarm != bool(asset_dict.get('alarm_state', 0)):
                    cursor.execute("UPDATE assets SET alarm_state = ?, alarm_message = ? WHERE id = ?",
                                   (1 if in_alarm else 0, alarm_msg if in_alarm else None, asset_dict['id']))
                    asset_dict['alarm_state'] = 1 if in_alarm else 0
                    asset_dict['alarm_message'] = alarm_msg if in_alarm else None
                    if in_alarm:
                        cursor.execute(
                            """
                            INSERT INTO alarm_events (asset_id, asset_name, message, active, created_at)
                            VALUES (?, ?, ?, 1, ?)
                            """,
                            (asset_dict['id'], asset_dict['name'], alarm_msg, now)
                        )
                    else:
                        cursor.execute(
                            """
                            UPDATE alarm_events
                            SET active = 0, cleared_at = ?
                            WHERE asset_id = ? AND active = 1
                            """,
                            (now, asset_dict['id'])
                        )
                    alarm_changed = True
                    asset_changed = True

                # 4. Update BACnet object value
                if asset_dict['protocol'] == "bacnet":
                    bacnet_manager.update_value(asset_dict['name'], asset_dict['current_value'], asset_dict['sub_type'])
                elif asset_dict['protocol'] == "modbus" and modbus_block:
                    register_type = (asset_dict.get('modbus_register_type') or '').lower()
                    writable = register_type in ['holding', 'coil']
                    # Avoid clobbering freshly-written external values on writable points.
                    # For writable points we only push when simulator/alarm state changed.
                    if asset_changed or alarm_changed or not writable:
                        modbus_block.write_value(asset_dict)

                updated_assets.append(asset_dict)
                if asset_changed:
                    any_global_change = True
                if alarm_changed:
                    print(f"[ALARM] {asset_dict['name']}: {alarm_msg if in_alarm else 'CLEARED'}")

            conn.commit()
            if any_global_change and ws_manager:
                await ws_manager.broadcast(json.dumps(updated_assets))

        except Exception as e:
            print(f"[Engine Error] {e}")
        finally:
            if conn: conn.close()
        await asyncio.sleep(1)
