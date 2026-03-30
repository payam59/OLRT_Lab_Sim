from __future__ import annotations

import asyncio
import time
from contextlib import asynccontextmanager, suppress
from typing import Optional

from fastapi import FastAPI, HTTPException, Request, WebSocket, WebSocketDisconnect
from fastapi.responses import HTMLResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from pydantic import BaseModel
from uvicorn import Config, Server

from database import get_db_connection, init_db
from engine import simulation_loop
from bacnet_runtime import BAC0, BAC0_IMPORT_ERROR, BACnetManager
from modbus_runtime import ModbusRuntimeManager

APP_TITLE = "OLRT Lab Simulation Core"
STATIC_DIR = "static"
TEMPLATES_DIR = "templates"
SERVER_HOST = "0.0.0.0"
SERVER_PORT = 8000
BACNET_PROTOCOL = "bacnet"
DEFAULT_BACNET_PORT = 47808
DEFAULT_BACNET_DEVICE_ID = 1234

@asynccontextmanager
async def app_lifespan(_app: FastAPI):
    await start_runtime()
    try:
        yield
    finally:
        await stop_runtime()


app = FastAPI(title=APP_TITLE, lifespan=app_lifespan)
app.mount("/static", StaticFiles(directory=STATIC_DIR), name="static")
templates = Jinja2Templates(directory=TEMPLATES_DIR)


class ConnectionManager:
    def __init__(self):
        self.active_connections: list[WebSocket] = []

    async def connect(self, websocket: WebSocket):
        await websocket.accept()
        self.active_connections.append(websocket)

    def disconnect(self, websocket: WebSocket):
        if websocket in self.active_connections:
            self.active_connections.remove(websocket)

    async def broadcast(self, message: str):
        for connection in self.active_connections:
            try:
                await connection.send_text(message)
            except Exception:
                continue


ws_manager = ConnectionManager()


class BBMDIn(BaseModel):
    name: str
    description: str = ""
    port: int
    device_id: int
    ip_address: str = "0.0.0.0"
    enabled: int = 1


class AssetIn(BaseModel):
    name: str
    type: str
    sub_type: str
    protocol: str
    address: int
    min_range: float = 0
    max_range: float = 100
    drift_rate: float = 0
    icon: str
    filename: str = ""
    bacnet_port: int = DEFAULT_BACNET_PORT
    bacnet_device_id: int = DEFAULT_BACNET_DEVICE_ID
    is_normally_open: int = 1
    change_probability: float = 0.0
    change_interval: int = 15
    bbmd_id: Optional[int] = None
    object_type: str = "value"
    bacnet_properties: str = "{}"
    modbus_unit_id: Optional[int] = 1
    modbus_register_type: Optional[str] = "holding"
    modbus_ip: str = "0.0.0.0"
    modbus_port: int = 5020
    modbus_alarm_address: Optional[int] = None
    modbus_alarm_bit: Optional[int] = 0


def _close_connection(conn) -> None:
    if conn:
        conn.close()


def _initial_asset_value(asset: AssetIn) -> float:
    if asset.sub_type == "Digital":
        return 0.0 if asset.is_normally_open else 1.0
    return (asset.min_range + asset.max_range) / 2


class LegacyBACnetManager:
    """Manages BBMD devices and their associated BACnet objects"""
    def __init__(self):
        self.bbmd_instances = {}  # {bbmd_id: BAC0_instance}
        self.objects = {}  # {asset_name: BACnet_object}
        self.asset_to_bbmd = {}  # {asset_name: bbmd_id}
        self.bbmd_status = {}  # {bbmd_id: {running: bool, message: str}}

    def _get_object_class(self, sub_type, object_type):
        """Returns the appropriate BACnet object class based on sub_type and object_type"""
        if sub_type == "Digital":
            if object_type == "input":
                return BinaryInputObject
            elif object_type == "output":
                return BinaryOutputObject
            else:  # value
                return BinaryValueObject
        else:  # Analog
            if object_type == "input":
                return AnalogInputObject
            elif object_type == "output":
                return AnalogOutputObject
            else:  # value
                return AnalogValueObject

    def start_bbmd(self, bbmd):
        """Start a BBMD device"""
        bbmd_id = bbmd["id"]
        if bbmd_id in self.bbmd_instances:
            return
        if not BAC0:
            self.bbmd_status[bbmd_id] = {
                "running": False,
                "message": "BAC0 is not installed in the runtime environment.",
            }
            print(f"[BACnet] Cannot start BBMD '{bbmd['name']}': BAC0 not installed.")
            return

        try:
            lite_args = dict(
                port=bbmd["port"],
                deviceId=bbmd["device_id"],
                localObjName=bbmd["name"],
            )
            ip_address = (bbmd.get("ip_address") or "").strip()
            if ip_address and ip_address != "0.0.0.0":
                lite_args["ip"] = ip_address

            new_stack = BAC0.lite(**lite_args)
            self.bbmd_instances[bbmd_id] = new_stack
            self.bbmd_status[bbmd_id] = {
                "running": True,
                "message": f"Listening on UDP {ip_address or 'auto-detected'}:{bbmd['port']}",
            }
            print(f"[BACnet] Started BBMD '{bbmd['name']}' on port {bbmd['port']} with device ID {bbmd['device_id']}")
        except Exception as e:
            self.bbmd_status[bbmd_id] = {"running": False, "message": str(e)}
            print(f"[BACnet] Failed to start BBMD {bbmd['name']}: {e}")

    def stop_bbmd(self, bbmd_id):
        """Stop a BBMD device"""
        if bbmd_id in self.bbmd_instances:
            try:
                self.bbmd_instances[bbmd_id].disconnect()
                del self.bbmd_instances[bbmd_id]
                self.bbmd_status[bbmd_id] = {"running": False, "message": "Stopped"}
                print(f"[BACnet] Stopped BBMD {bbmd_id}")
            except Exception as e:
                print(f"[BACnet] Error stopping BBMD {bbmd_id}: {e}")

    def add_asset_to_bbmd(self, asset):
        """Add a BACnet object to an existing BBMD"""
        if not BAC0:
            return

        bbmd_id = asset.get("bbmd_id")
        if not bbmd_id or bbmd_id not in self.bbmd_instances:
            print(f"[BACnet] BBMD {bbmd_id} not found for asset {asset['name']}")
            return

        try:
            stack = self.bbmd_instances[bbmd_id]
            obj_class = self._get_object_class(asset["sub_type"], asset["object_type"])
            if obj_class is None:
                self.bbmd_status[bbmd_id] = {
                    "running": False,
                    "message": "BACnet object classes unavailable in this Python/runtime. Install BAC0-compatible dependencies.",
                }
                print(f"[BACnet] Cannot create object class for {asset['name']}.")
                return

            if ObjectFactory is None:
                self.bbmd_status[bbmd_id] = {
                    "running": False,
                    "message": "BAC0 ObjectFactory is unavailable.",
                }
                print(f"[BACnet] Cannot create object for {asset['name']}: ObjectFactory missing.")
                return

            if asset["sub_type"] == "Digital":
                present_val = "active" if asset["current_value"] >= 0.5 else "inactive"
                factory = ObjectFactory(
                    obj_class,
                    instance=asset["address"],
                    objectName=asset["name"],
                    presentValue=present_val,
                    properties={},
                )
            else:
                factory = ObjectFactory(
                    obj_class,
                    instance=asset["address"],
                    objectName=asset["name"],
                    presentValue=float(asset["current_value"]),
                    properties={"units": "noUnits"},
                )

            factory.add_objects_to_application(stack)
            obj = factory.objects.get(asset["name"])
            if not obj:
                print(f"[BACnet] ObjectFactory did not return object for {asset['name']}")
                return

            self.objects[asset["name"]] = obj
            self.asset_to_bbmd[asset["name"]] = bbmd_id
            print(f"[BACnet] Added {asset['sub_type']} {asset['object_type']} '{asset['name']}' (instance {asset['address']}) to BBMD {bbmd_id}")
        except Exception as e:
            print(f"[BACnet] Failed to add asset {asset['name']}: {e}")

    def update_value(self, name, val, sub_type):
        """Update BACnet object value"""
        if name in self.objects:
            try:
                if sub_type == "Digital":
                    self.objects[name].presentValue = "active" if val >= 0.5 else "inactive"
                else:
                    self.objects[name].presentValue = float(val)
            except Exception as e:
                print(f"[BACnet] Error updating {name}: {e}")

    def get_value(self, name):
        """Read BACnet object value (useful for detecting external writes)"""
        if name in self.objects:
            try:
                obj = self.objects[name]
                if hasattr(obj, "presentValue"):
                    val = obj.presentValue
                    if str(val).lower() in ("active", "1", "true"):
                        return 1.0
                    if str(val).lower() in ("inactive", "0", "false"):
                        return 0.0
                    return float(val)
            except Exception as e:
                print(f"[BACnet] Error reading {name}: {e}")
        return None

    def remove_asset(self, name):
        """Remove an asset from its BBMD"""
        if name in self.objects:
            del self.objects[name]
        if name in self.asset_to_bbmd:
            del self.asset_to_bbmd[name]


bacnet_manager = BACnetManager()
modbus_manager = ModbusRuntimeManager()
simulation_task: asyncio.Task | None = None


@app.get("/", response_class=HTMLResponse)
async def index(request: Request):
    return templates.TemplateResponse(request=request, name="index.html")


@app.get("/bacnet/status", response_class=HTMLResponse)
async def bacnet_status_page(request: Request):
    return templates.TemplateResponse(request=request, name="bacnet_status.html")


@app.get("/status", response_class=HTMLResponse)
async def status_page(request: Request):
    return templates.TemplateResponse(request=request, name="bacnet_status.html")


@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    await ws_manager.connect(websocket)
    try:
        while True:
            await websocket.receive_text()
    except WebSocketDisconnect:
        ws_manager.disconnect(websocket)


# ============ BBMD Endpoints ============
@app.get("/api/bbmd")
async def get_bbmds():
    conn = get_db_connection()
    try:
        bbmds = conn.execute("SELECT * FROM bbmd").fetchall()
        return [dict(b) for b in bbmds]
    finally:
        _close_connection(conn)


@app.get("/api/bbmd/{bbmd_id}")
async def get_bbmd(bbmd_id: int):
    conn = get_db_connection()
    try:
        bbmd = conn.execute("SELECT * FROM bbmd WHERE id = ?", (bbmd_id,)).fetchone()
        if not bbmd:
            raise HTTPException(status_code=404, detail="BBMD not found")
        return dict(bbmd)
    finally:
        _close_connection(conn)


@app.post("/api/bbmd")
async def add_bbmd(bbmd: BBMDIn):
    conn = get_db_connection()
    try:
        cursor = conn.execute(
            """
            INSERT INTO bbmd (name, description, port, device_id, ip_address, enabled, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            (bbmd.name, bbmd.description, bbmd.port, bbmd.device_id, bbmd.ip_address, bbmd.enabled, time.time())
        )
        conn.commit()
        bbmd_id = cursor.lastrowid

        # Start the BBMD BACnet device if enabled
        if bbmd.enabled:
            bbmd_data = conn.execute("SELECT * FROM bbmd WHERE id = ?", (bbmd_id,)).fetchone()
            if bbmd_data:
                bacnet_manager.start_bbmd(dict(bbmd_data))

        return {"status": "ok", "id": bbmd_id}
    except Exception as e:
        print(f"[API] Error creating BBMD: {e}")
        raise HTTPException(status_code=400, detail=str(e))
    finally:
        _close_connection(conn)


@app.put("/api/bbmd/{bbmd_id}")
async def update_bbmd(bbmd_id: int, bbmd: BBMDIn):
    conn = get_db_connection()
    try:
        conn.execute(
            """
            UPDATE bbmd
            SET name = ?, description = ?, port = ?, device_id = ?, ip_address = ?, enabled = ?
            WHERE id = ?
            """,
            (bbmd.name, bbmd.description, bbmd.port, bbmd.device_id, bbmd.ip_address, bbmd.enabled, bbmd_id)
        )
        conn.commit()

        # Restart BBMD (stop and start with new config)
        try:
            bacnet_manager.stop_bbmd(bbmd_id)
        except Exception as e:
            print(f"[BBMD] Warning: Could not stop BBMD {bbmd_id}: {e}")

        if bbmd.enabled:
            bbmd_data = conn.execute("SELECT * FROM bbmd WHERE id = ?", (bbmd_id,)).fetchone()
            if bbmd_data:
                bacnet_manager.start_bbmd(dict(bbmd_data))

        return {"status": "updated"}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
    finally:
        _close_connection(conn)


@app.delete("/api/bbmd/{bbmd_id}")
async def delete_bbmd(bbmd_id: int):
    conn = get_db_connection()
    try:
        # Stop the BBMD
        bacnet_manager.stop_bbmd(bbmd_id)

        # Delete associated assets' BBMD reference
        conn.execute("UPDATE assets SET bbmd_id = NULL WHERE bbmd_id = ?", (bbmd_id,))
        conn.execute("DELETE FROM bbmd WHERE id = ?", (bbmd_id,))
        conn.commit()
        return {"status": "removed"}
    finally:
        _close_connection(conn)


# ============ Asset Endpoints ============
@app.get("/api/assets")
async def get_assets():
    conn = get_db_connection()
    try:
        assets = conn.execute("SELECT * FROM assets").fetchall()
        return [dict(asset) for asset in assets]
    finally:
        _close_connection(conn)


@app.get("/api/alarms")
async def get_alarms(active_only: int = 1):
    conn = get_db_connection()
    try:
        if active_only:
            alarms = conn.execute(
                "SELECT * FROM alarm_events WHERE active = 1 ORDER BY created_at DESC LIMIT 100"
            ).fetchall()
        else:
            alarms = conn.execute(
                "SELECT * FROM alarm_events ORDER BY created_at DESC LIMIT 200"
            ).fetchall()
        return [dict(a) for a in alarms]
    finally:
        _close_connection(conn)


@app.get("/api/bacnet/status")
async def get_bacnet_status():
    return {
        "bac0_installed": BAC0 is not None,
        "bac0_import_error": BAC0_IMPORT_ERROR,
        "running_bbmd_ids": list(bacnet_manager.bbmd_instances.keys()),
        "bbmd_status": bacnet_manager.bbmd_status,
        "registered_object_names": list(bacnet_manager.objects.keys()),
    }


@app.get("/api/modbus/status")
async def get_modbus_status():
    return modbus_manager.status()


@app.get("/api/assets/{name}")
async def get_asset(name: str):
    conn = get_db_connection()
    try:
        asset = conn.execute("SELECT * FROM assets WHERE name = ?", (name,)).fetchone()
        if not asset:
            raise HTTPException(status_code=404, detail="Asset not found")
        return dict(asset)
    finally:
        _close_connection(conn)


@app.post("/api/assets")
async def add_asset(asset: AssetIn):
    conn = get_db_connection()
    try:
        cleaned_name = (asset.name or "").strip()
        if not cleaned_name:
            raise HTTPException(status_code=400, detail="Asset name is required")

        existing = conn.execute("SELECT 1 FROM assets WHERE name = ?", (cleaned_name,)).fetchone()
        if existing:
            raise HTTPException(status_code=409, detail=f"Asset name '{cleaned_name}' already exists")

        is_bacnet = asset.protocol == BACNET_PROTOCOL
        if is_bacnet and not asset.bbmd_id:
            raise HTTPException(status_code=400, detail="BACnet assets require a BBMD assignment")

        if asset.protocol == "modbus" and not (asset.modbus_ip or "").strip():
            raise HTTPException(status_code=400, detail="Modbus IP is required for Modbus assets")

        normalized_bbmd_id = asset.bbmd_id if is_bacnet else None
        normalized_object_type = asset.object_type if is_bacnet else "value"

        initial_value = _initial_asset_value(asset)
        conn.execute(
            """
            INSERT INTO assets (
                name, type, sub_type, protocol, address, min_range, max_range,
                current_value, drift_rate, icon, filename, bacnet_port,
                bacnet_device_id, is_normally_open, change_probability,
                change_interval, last_flip_check, bbmd_id, object_type, bacnet_properties,
                modbus_unit_id, modbus_register_type, modbus_ip, modbus_port,
                modbus_alarm_address, modbus_alarm_bit, alarm_state
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                cleaned_name,
                asset.type,
                asset.sub_type,
                asset.protocol,
                asset.address,
                asset.min_range,
                asset.max_range,
                initial_value,
                asset.drift_rate,
                asset.icon,
                asset.filename,
                asset.bacnet_port,
                asset.bacnet_device_id,
                asset.is_normally_open,
                asset.change_probability,
                asset.change_interval,
                time.time(),
                normalized_bbmd_id,
                normalized_object_type,
                asset.bacnet_properties,
                asset.modbus_unit_id,
                asset.modbus_register_type,
                asset.modbus_ip,
                asset.modbus_port,
                asset.modbus_alarm_address,
                asset.modbus_alarm_bit or 0,
                0,
            ),
        )
        conn.commit()

        # Add to BBMD if protocol is BACnet
        if is_bacnet and normalized_bbmd_id:
            asset_data = conn.execute("SELECT * FROM assets WHERE name = ?", (asset.name,)).fetchone()
            bacnet_manager.add_asset_to_bbmd(dict(asset_data))
        elif asset.protocol == "modbus":
            asset_data = conn.execute("SELECT * FROM assets WHERE name = ?", (asset.name,)).fetchone()
            await modbus_manager.register_asset(dict(asset_data))

    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))
    finally:
        _close_connection(conn)

    return {"status": "ok"}


@app.put("/api/assets/{name}")
async def update_asset(name: str, asset: AssetIn):
    conn = get_db_connection()
    try:
        is_bacnet = asset.protocol == BACNET_PROTOCOL
        if is_bacnet and not asset.bbmd_id:
            raise HTTPException(status_code=400, detail="BACnet assets require a BBMD assignment")

        normalized_bbmd_id = asset.bbmd_id if is_bacnet else None
        normalized_object_type = asset.object_type if is_bacnet else "value"

        conn.execute(
            """
            UPDATE assets
            SET type = ?, sub_type = ?, protocol = ?, address = ?, min_range = ?,
                max_range = ?, drift_rate = ?, icon = ?, filename = ?, bacnet_port = ?,
                bacnet_device_id = ?, is_normally_open = ?, change_probability = ?,
                change_interval = ?, bbmd_id = ?, object_type = ?, modbus_unit_id = ?,
                bacnet_properties = ?, modbus_register_type = ?, modbus_ip = ?, modbus_port = ?,
                modbus_alarm_address = ?, modbus_alarm_bit = ?
            WHERE name = ?
            """,
            (
                asset.type,
                asset.sub_type,
                asset.protocol,
                asset.address,
                asset.min_range,
                asset.max_range,
                asset.drift_rate,
                asset.icon,
                asset.filename,
                asset.bacnet_port,
                asset.bacnet_device_id,
                asset.is_normally_open,
                asset.change_probability,
                asset.change_interval,
                normalized_bbmd_id,
                normalized_object_type,
                asset.modbus_unit_id,
                asset.bacnet_properties,
                asset.modbus_register_type,
                asset.modbus_ip,
                asset.modbus_port,
                asset.modbus_alarm_address,
                asset.modbus_alarm_bit or 0,
                name,
            ),
        )
        conn.commit()

        # Re-add to BACnet if needed
        if is_bacnet and normalized_bbmd_id:
            bacnet_manager.remove_asset(name)
            asset_data = conn.execute("SELECT * FROM assets WHERE name = ?", (name,)).fetchone()
            bacnet_manager.add_asset_to_bbmd(dict(asset_data))
        else:
            bacnet_manager.remove_asset(name)
            if asset.protocol == "modbus":
                asset_data = conn.execute("SELECT * FROM assets WHERE name = ?", (name,)).fetchone()
                await modbus_manager.register_asset(dict(asset_data))
            else:
                await modbus_manager.unregister_asset(name)

    finally:
        _close_connection(conn)

    return {"status": "updated"}


@app.delete("/api/assets/{name}")
async def delete_asset(name: str):
    conn = get_db_connection()
    try:
        bacnet_manager.remove_asset(name)
        await modbus_manager.unregister_asset(name)
        conn.execute("DELETE FROM assets WHERE name = ?", (name,))
        conn.commit()
    finally:
        _close_connection(conn)

    return {"status": "removed"}


@app.put("/api/override/{name}")
async def override(name: str, value: float):
    conn = get_db_connection()
    try:
        conn.execute(
            "UPDATE assets SET current_value = ?, manual_override = 1 WHERE name = ?",
            (value, name),
        )
        conn.commit()
    finally:
        _close_connection(conn)

    return {"status": "locked"}

@app.put("/api/release/{name}")
async def release(name: str):
    conn = get_db_connection()
    try:
        conn.execute("UPDATE assets SET manual_override = 0 WHERE name = ?", (name,))
        conn.commit()
    finally:
        _close_connection(conn)

    return {"status": "auto"}


async def _start_bacnet_devices():
    """Initialize all BBMD devices and their assets on startup"""
    conn = get_db_connection()
    try:
        # Start all enabled BBMDs
        bbmds = conn.execute("SELECT * FROM bbmd WHERE enabled = 1").fetchall()
        for bbmd in bbmds:
            bacnet_manager.start_bbmd(dict(bbmd))

        # Add all BACnet assets to their respective BBMDs
        assets = conn.execute("SELECT * FROM assets WHERE protocol = 'bacnet' AND bbmd_id IS NOT NULL").fetchall()
        for asset in assets:
            bacnet_manager.add_asset_to_bbmd(dict(asset))

        modbus_assets = conn.execute("SELECT * FROM assets WHERE protocol = 'modbus'").fetchall()
        await modbus_manager.bootstrap([dict(a) for a in modbus_assets])
    finally:
        _close_connection(conn)


async def start_runtime():
    global simulation_task
    if simulation_task and not simulation_task.done():
        return
    init_db()
    await _start_bacnet_devices()
    simulation_task = asyncio.create_task(
        simulation_loop(modbus_manager, bacnet_manager, ws_manager)
    )


async def stop_runtime():
    global simulation_task
    if simulation_task:
        simulation_task.cancel()
        with suppress(asyncio.CancelledError):
            await simulation_task
        simulation_task = None

    for bbmd_id in list(bacnet_manager.bbmd_instances.keys()):
        bacnet_manager.stop_bbmd(bbmd_id)
    await modbus_manager.shutdown()


async def main_task():
    config = Config(app=app, host=SERVER_HOST, port=SERVER_PORT)
    await Server(config).serve()


if __name__ == "__main__":
    try:
        asyncio.run(main_task())
    except KeyboardInterrupt:
        pass
