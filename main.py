from fastapi import FastAPI, Request, HTTPException
from fastapi.responses import HTMLResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from pydantic import BaseModel
import asyncio
from uvicorn import Config, Server
from database import init_db, get_db_connection
from engine import simulation_loop
from pymodbus.server import StartAsyncTcpServer
from pymodbus.datastore import ModbusDeviceContext, ModbusServerContext, ModbusSequentialDataBlock

# BACnet Stack Logic
try:
    import BAC0
    from bacpypes.basetypes import BinaryValue, AnalogValue
except ImportError:
    BAC0 = None

app = FastAPI(title="OLRT Lab Simulation Core")
app.mount("/static", StaticFiles(directory="static"), name="static")
templates = Jinja2Templates(directory="templates")

modbus_block = ModbusSequentialDataBlock(0, [0] * 1000)
modbus_context = ModbusServerContext(devices=ModbusDeviceContext(hr=modbus_block), single=True)

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
    bacnet_port: int = 47808
    bacnet_device_id: int = 1234
    is_normally_open: int = 1

class BACnetManager:
    """Manages virtual BACnet devices with strict Read/Write constraints."""
    def __init__(self):
        self.instances = {}
        self.objects = {}

    def start_asset_stack(self, asset):
        if not BAC0 or asset['name'] in self.instances: return
        try:
            new_stack = BAC0.lite(port=asset['bacnet_port'], deviceId=asset['bacnet_device_id'])
            if asset['sub_type'] == "Digital":
                obj = BAC0.core.devices.Device.ObjectFactory(
                    BinaryValue(instance=asset['address'], objectName=asset['name'],
                                presentValue='active' if asset['current_value'] >= 0.5 else 'inactive')
                )
            else:
                obj = BAC0.core.devices.Device.ObjectFactory(
                    AnalogValue(instance=asset['address'], objectName=asset['name'],
                                presentValue=float(asset['current_value']))
                )
            new_stack.add_object(obj)
            self.instances[asset['name']] = new_stack
            self.objects[asset['name']] = obj
        except Exception as e:
            print(f"[BACnet] Failed to start {asset['name']}: {e}")

    def update_value(self, name, val, sub_type):
        if name in self.objects:
            if sub_type == "Digital":
                self.objects[name].presentValue = 'active' if val >= 0.5 else 'inactive'
            else:
                self.objects[name].presentValue = float(val)

    def get_value(self, name):
        if name in self.objects:
            obj = self.objects[name]
            if hasattr(obj, 'presentValue'):
                val = obj.presentValue
                return 1.0 if val == 'active' else (0.0 if val == 'inactive' else float(val))
        return None

bac_manager = BACnetManager()

@app.get("/", response_class=HTMLResponse)
async def index(request: Request):
    return templates.TemplateResponse(request=request, name="index.html")

@app.get("/api/assets")
async def get_assets():
    conn = get_db_connection()
    assets = conn.execute("SELECT * FROM assets").fetchall()
    conn.close()
    return [dict(a) for a in assets]

@app.get("/api/assets/{name}")
async def get_asset(name: str):
    conn = get_db_connection()
    asset = conn.execute("SELECT * FROM assets WHERE name = ?", (name,)).fetchone()
    conn.close()
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")
    return dict(asset)

@app.post("/api/assets")
async def add_asset(a: AssetIn):
    conn = get_db_connection()
    try:
        initial_val = 0.0 if (a.sub_type == "Digital" and a.is_normally_open) else (1.0 if a.sub_type == "Digital" else (a.min_range + a.max_range) / 2)
        conn.execute('''
            INSERT INTO assets (name, type, sub_type, protocol, address, min_range, max_range, current_value, drift_rate, icon, filename, bacnet_port, bacnet_device_id, is_normally_open)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ''', (a.name, a.type, a.sub_type, a.protocol, a.address, a.min_range, a.max_range, initial_val, a.drift_rate, a.icon, a.filename, a.bacnet_port, a.bacnet_device_id, a.is_normally_open))
        conn.commit()
        if a.protocol == "bacnet": bac_manager.start_asset_stack(dict(a))
    except Exception as e:
        conn.close()
        raise HTTPException(status_code=400, detail=str(e))
    finally: conn.close()
    return {"status": "ok"}

@app.put("/api/assets/{name}")
async def update_asset(name: str, a: AssetIn):
    conn = get_db_connection()
    conn.execute('''
        UPDATE assets SET type=?, sub_type=?, protocol=?, address=?, min_range=?, 
        max_range=?, drift_rate=?, icon=?, filename=?, bacnet_port=?, 
        bacnet_device_id=?, is_normally_open=? WHERE name=?
    ''', (a.type, a.sub_type, a.protocol, a.address, a.min_range, a.max_range,
          a.drift_rate, a.icon, a.filename, a.bacnet_port, a.bacnet_device_id,
          a.is_normally_open, name))
    conn.commit()
    conn.close()
    return {"status": "updated"}

@app.delete("/api/assets/{name}")
async def delete_asset(name: str):
    conn = get_db_connection()
    conn.execute("DELETE FROM assets WHERE name = ?", (name,))
    conn.commit()
    conn.close()
    return {"status": "removed"}

@app.put("/api/override/{name}")
async def override(name: str, value: float):
    conn = get_db_connection()
    conn.execute("UPDATE assets SET current_value = ?, manual_override = 1 WHERE name = ?", (value, name))
    conn.commit()
    conn.close()
    return {"status": "locked"}

@app.put("/api/release/{name}")
async def release(name: str):
    conn = get_db_connection()
    conn.execute("UPDATE assets SET manual_override = 0 WHERE name = ?", (name,))
    conn.commit()
    conn.close()
    return {"status": "auto"}

async def main_task():
    init_db()
    conn = get_db_connection()
    existing = conn.execute("SELECT * FROM assets WHERE protocol = 'bacnet'").fetchall()
    for row in existing: bac_manager.start_asset_stack(dict(row))
    conn.close()
    asyncio.create_task(simulation_loop(modbus_block, bac_manager))
    asyncio.create_task(StartAsyncTcpServer(context=modbus_context, address=("0.0.0.0", 5020)))
    config = Config(app=app, host="0.0.0.0", port=8000)
    await Server(config).serve()

if __name__ == "__main__":
    asyncio.run(main_task())