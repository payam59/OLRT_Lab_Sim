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

app = FastAPI()
app.mount("/static", StaticFiles(directory="static"), name="static")
templates = Jinja2Templates(directory="templates")

# Modbus Context
modbus_block = ModbusSequentialDataBlock(0, [0] * 1000)
modbus_context = ModbusServerContext(devices=ModbusDeviceContext(hr=modbus_block), single=True)

class AssetIn(BaseModel):
    name: str; type: str; protocol: str; address: int
    min_range: float; max_range: float; drift_rate: float; icon: str

@app.get("/", response_class=HTMLResponse)
async def index(request: Request):
    # Pass request as a keyword argument to avoid version conflicts
    return templates.TemplateResponse(
        request=request,
        name="index.html",
        context={}  # You can add more data here if needed
    )

@app.get("/api/assets")
async def get_assets():
    conn = get_db_connection()
    assets = conn.execute("SELECT * FROM assets").fetchall()
    conn.close()
    return [dict(a) for a in assets]


@app.post("/api/assets")
async def add_asset(a: AssetIn):
    conn = get_db_connection()
    try:
        # Start at 50% of the range so it's never 0 unless min is 0
        initial_val = (a.min_range + a.max_range) / 2

        conn.execute('''
                     INSERT INTO assets (name, type, protocol, address, min_range, max_range, current_value, drift_rate,
                                         icon)
                     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                     ''', (a.name, a.type, a.protocol, a.address, a.min_range, a.max_range, initial_val, a.drift_rate,
                           a.icon))
        conn.commit()
    except Exception as e:
        conn.close()
        raise HTTPException(status_code=400, detail=str(e))
    finally:
        conn.close()

    return {"status": "ok"}

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

@app.delete("/api/assets/{name}")
async def delete_asset(name: str):
    conn = get_db_connection()
    conn.execute("DELETE FROM assets WHERE name = ?", (name,))
    conn.commit()
    conn.close()
    return {"status": "removed"}
@app.get("/api/assets/{name}")
async def get_single_asset(name: str):
    conn = get_db_connection()
    asset = conn.execute("SELECT * FROM assets WHERE name = ?", (name,)).fetchone()
    conn.close()
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")
    return dict(asset)

@app.put("/api/assets/{name}")
async def update_asset(name: str, a: AssetIn):
    conn = get_db_connection()
    try:
        conn.execute('''
            UPDATE assets 
            SET type = ?, protocol = ?, address = ?, min_range = ?, 
                max_range = ?, drift_rate = ?, icon = ?
            WHERE name = ?
        ''', (a.type, a.protocol, a.address, a.min_range, a.max_range, a.drift_rate, a.icon, name))
        conn.commit()
    except Exception as e:
        conn.close()
        raise HTTPException(status_code=400, detail=str(e))
    finally:
        conn.close()
    return {"status": "updated"}

async def main_task():
    init_db()
    asyncio.create_task(simulation_loop(modbus_block))
    asyncio.create_task(StartAsyncTcpServer(context=modbus_context, address=("0.0.0.0", 5020)))
    config = Config(app=app, host="0.0.0.0", port=8000)
    await Server(config).serve()

if __name__ == "__main__":
    asyncio.run(main_task())