import asyncio
import time
import random

from database import get_db_connection


async def simulation_loop(modbus_block, bacnet_manager):
    while True:
        conn = None
        try:
            conn = get_db_connection()
            cursor = conn.cursor()
            assets = cursor.execute("SELECT * FROM assets").fetchall()

            for a in assets:
                asset_dict = dict(a)

                # BACnet Write-Back Logic
                if asset_dict['protocol'] == "bacnet" and asset_dict['sub_type'] == "Digital":
                    remote_val = bacnet_manager.get_value(asset_dict['name'])
                    if remote_val is not None and abs(remote_val - asset_dict['current_value']) > 0.01:
                        cursor.execute("UPDATE assets SET current_value = ?, manual_override = 1 WHERE id = ?",
                                       (remote_val, asset_dict['id']))
                        asset_dict['current_value'] = remote_val

                if not asset_dict['manual_override']:
                    if asset_dict['sub_type'] == "Digital":
                        # Discrete logic is static (NO=0, NC=1)
                        new_val = 0.0 if asset_dict['is_normally_open'] else 1.0
                    else:
                        # Analog drift
                        noise = random.uniform(-asset_dict['drift_rate'], asset_dict['drift_rate'])
                        current = asset_dict['current_value'] or asset_dict['min_range']
                        new_val = max(asset_dict['min_range'], min(asset_dict['max_range'], current + noise))

                    if abs(new_val - (asset_dict['current_value'] or 0)) > 0.0001:
                        cursor.execute("UPDATE assets SET current_value = ? WHERE id = ?", (new_val, asset_dict['id']))
                        asset_dict['current_value'] = new_val

                # Egress Sync
                if asset_dict['protocol'] == "modbus":
                    modbus_block.setValues(asset_dict['address'], [int(asset_dict['current_value'] * 10)])
                elif asset_dict['protocol'] == "bacnet":
                    bacnet_manager.update_value(asset_dict['name'], asset_dict['current_value'], asset_dict['sub_type'])

            conn.commit()
        except Exception as e:
            print(f"[Engine] Error: {e}")
        finally:
            if conn: conn.close()
        await asyncio.sleep(1)