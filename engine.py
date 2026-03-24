import asyncio
import struct
import time
import os
import random

from database import get_db_connection

LOG_DIR = "simulation_logs"


def pack_protocol_data(asset):
    """
    Packs data into industry-standard binary structures.
    Formats:
    - Modbus: [Timestamp(d)][UnitID(B)][FC(B)][Addr(H)][Value(H)]
    - DNP3:   [Timestamp(d)][Index(H)][Grp(B)][Var(B)][Value(f)][Flags(B)]
    - BACnet: [Timestamp(d)][Type(H)][Inst(I)][Prop(H)][Value(f)]
    """
    ts = time.time()
    val = asset['current_value']
    protocol = asset['protocol'].lower()

    if protocol == "modbus":
        # Scale to 16-bit Int (e.g., 25.5 -> 255)
        scaled_val = int(val * 10)
        return struct.pack('>dBBHH', ts, 1, 3, asset['address'], scaled_val)

    elif protocol == "dnp3":
        # Group 30 (Analog Input), Var 5 (Float32 with Flags), Flag 0x01 (Online)
        return struct.pack('>dHBBfB', ts, asset['address'], 30, 5, float(val), 0x01)

    elif protocol == "bacnet":
        # Obj Type 2 (Analog Value), Prop 85 (Present Value)
        return struct.pack('>dHIHf', ts, 2, asset['address'], 85, float(val))

    elif protocol == "opcua":
        # Basic OPC UA DataValue: [Timestamp(d)][StatusCode(I)][Value(f)]
        return struct.pack('>dIf', ts, 0x00000000, float(val))


async def simulation_loop(modbus_block):
    while True:
        conn = get_db_connection()
        cursor = conn.cursor()
        assets = cursor.execute("SELECT * FROM assets").fetchall()

        for a in assets:
            if not a['manual_override']:
                # Physics calculation
                noise = random.uniform(-a['drift_rate'], a['drift_rate'])
                current = a['current_value'] if a['current_value'] is not None else a['min_range']
                new_val = max(a['min_range'], min(a['max_range'], current + noise))

                cursor.execute("UPDATE assets SET current_value = ? WHERE id = ?", (new_val, a['id']))

                # Update Modbus Memory for live polling
                if a['protocol'] == "modbus":
                    modbus_block.setValues(a['address'], [int(new_val * 10)])

                # Binary Logging to the specific file defined in GUI
                fname = a['filename'] if a['filename'] else f"{a['name']}.bin"
                if not fname.endswith(".bin"): fname += ".bin"

                log_path = os.path.join(LOG_DIR, fname)
                with open(log_path, "ab") as f:
                    f.write(pack_protocol_data({**dict(a), 'current_value': new_val}))

        conn.commit()
        conn.close()
        await asyncio.sleep(1)