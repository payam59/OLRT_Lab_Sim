from __future__ import annotations

import asyncio
from collections import defaultdict
import inspect
import struct

try:
    from pymodbus.datastore import ModbusSequentialDataBlock, ModbusServerContext
    try:
        from pymodbus.datastore import ModbusSlaveContext  # pymodbus <=3.6
        ModbusDeviceContext = None
    except Exception:
        from pymodbus.datastore import ModbusDeviceContext  # pymodbus newer 3.x
        ModbusSlaveContext = None
    from pymodbus.server import StartAsyncTcpServer
except Exception:  # pragma: no cover - runtime dependency check
    ModbusSequentialDataBlock = None
    ModbusServerContext = None
    ModbusSlaveContext = None
    ModbusDeviceContext = None
    StartAsyncTcpServer = None


class ModbusRuntimeManager:
    def __init__(self):
        self.server_tasks: dict[tuple[str, int], asyncio.Task] = {}
        self.contexts: dict[tuple[str, int], object] = {}
        self.endpoint_assets: dict[tuple[str, int], set[str]] = defaultdict(set)
        self.asset_index: dict[str, dict] = {}
        self.status_messages: dict[str, str] = {}

    @property
    def installed(self) -> bool:
        return StartAsyncTcpServer is not None

    async def _serve_endpoint(self, endpoint: tuple[str, int], context):
        ip, port = endpoint
        await StartAsyncTcpServer(context=context, address=(ip, port))

    def _new_context(self):
        # Large enough blocks for simulator indexing by address.
        blocks = dict(
            di=ModbusSequentialDataBlock(0, [0] * 10000),
            co=ModbusSequentialDataBlock(0, [0] * 10000),
            hr=ModbusSequentialDataBlock(0, [0] * 10000),
            ir=ModbusSequentialDataBlock(0, [0] * 10000),
        )
        if ModbusSlaveContext is not None:
            slave = ModbusSlaveContext(**blocks)
        else:
            slave = ModbusDeviceContext(**blocks)

        server_params = inspect.signature(ModbusServerContext.__init__).parameters
        if "devices" in server_params:
            return ModbusServerContext(devices=slave, single=True)
        return ModbusServerContext(slaves=slave, single=True)

    async def ensure_endpoint(self, ip: str, port: int):
        endpoint = (ip, port)
        if endpoint in self.server_tasks or not self.installed:
            return

        context = self._new_context()
        self.contexts[endpoint] = context
        task = asyncio.create_task(self._serve_endpoint(endpoint, context))
        self.server_tasks[endpoint] = task
        self.status_messages[f"{ip}:{port}"] = "running"

    async def register_asset(self, asset: dict):
        name = asset["name"]
        ip = asset.get("modbus_ip") or "0.0.0.0"
        port = int(asset.get("modbus_port") or 5020)
        endpoint = (ip, port)

        # Remove old endpoint mapping if changed.
        old = self.asset_index.get(name)
        if old:
            await self.unregister_asset(name)

        self.asset_index[name] = {
            "endpoint": endpoint,
            "unit_id": int(asset.get("modbus_unit_id") or 1),
            "register_type": asset.get("modbus_register_type") or "holding",
            "address": int(asset.get("address") or 0),
            "alarm_address": asset.get("modbus_alarm_address"),
            "alarm_bit": int(asset.get("modbus_alarm_bit") or 0),
            "sub_type": asset.get("sub_type"),
        }
        self.endpoint_assets[endpoint].add(name)
        await self.ensure_endpoint(ip, port)
        self.write_value(asset)

    async def unregister_asset(self, name: str):
        mapping = self.asset_index.pop(name, None)
        if not mapping:
            return
        endpoint = mapping["endpoint"]
        if endpoint in self.endpoint_assets and name in self.endpoint_assets[endpoint]:
            self.endpoint_assets[endpoint].remove(name)
        if endpoint in self.endpoint_assets and not self.endpoint_assets[endpoint]:
            self.endpoint_assets.pop(endpoint, None)
            task = self.server_tasks.pop(endpoint, None)
            self.contexts.pop(endpoint, None)
            if task:
                task.cancel()
                self.status_messages[f"{endpoint[0]}:{endpoint[1]}"] = "stopped"

    def _fc_for_type(self, register_type: str) -> int:
        return {"holding": 3, "input": 4, "coil": 1, "discrete": 2}.get(register_type, 3)

    def _set_for_type(self, register_type: str) -> int:
        return {"holding": 3, "input": 4, "coil": 1, "discrete": 2}.get(register_type, 3)

    def write_value(self, asset: dict):
        if not self.installed:
            return
        mapping = self.asset_index.get(asset["name"])
        if not mapping:
            return
        endpoint = mapping["endpoint"]
        context = self.contexts.get(endpoint)
        if not context:
            return

        addr = mapping["address"]
        register_type = mapping["register_type"]
        value = asset.get("current_value", 0)
        if register_type in ("coil", "discrete"):
            value = 1 if float(value) >= 0.5 else 0
            context[0x00].setValues(self._set_for_type(register_type), addr, [value])
        else:
            # Preserve analog precision by storing IEEE-754 float32 across two registers.
            packed = struct.pack(">f", float(value))
            reg_hi, reg_lo = struct.unpack(">HH", packed)
            context[0x00].setValues(self._set_for_type(register_type), addr, [reg_hi, reg_lo])

        self._write_alarm_point(context, mapping, asset)

    def _write_alarm_point(self, context, mapping: dict, asset: dict):
        alarm_addr_raw = mapping.get("alarm_address")
        if alarm_addr_raw is None:
            return
        try:
            alarm_addr = int(alarm_addr_raw)
        except (TypeError, ValueError):
            return
        bit = mapping.get("alarm_bit", 0)
        try:
            bit = max(0, min(int(bit), 15))
        except (TypeError, ValueError):
            bit = 0
        alarm_active = 1 if int(asset.get("alarm_state") or 0) else 0
        if mapping["register_type"] in ("coil", "discrete"):
            context[0x00].setValues(1, alarm_addr, [alarm_active])
            return
        existing = context[0x00].getValues(3, alarm_addr, count=1)
        word = int(existing[0]) if existing else 0
        if alarm_active:
            word |= (1 << bit)
        else:
            word &= ~(1 << bit)
        context[0x00].setValues(3, alarm_addr, [word])

    def read_remote_value(self, asset: dict):
        if not self.installed:
            return None
        mapping = self.asset_index.get(asset["name"])
        if not mapping:
            return None
        endpoint = mapping["endpoint"]
        context = self.contexts.get(endpoint)
        if not context:
            return None
        addr = mapping["address"]
        register_type = mapping["register_type"]
        count = 1 if register_type in ("coil", "discrete") else 2
        values = context[0x00].getValues(self._fc_for_type(register_type), addr, count=count)
        if not values:
            return None
        if register_type in ("coil", "discrete"):
            return float(values[0])
        if len(values) < 2:
            return None
        packed = struct.pack(">HH", int(values[0]), int(values[1]))
        return float(struct.unpack(">f", packed)[0])

    async def bootstrap(self, assets: list[dict]):
        if not self.installed:
            self.status_messages["global"] = "pymodbus is not installed."
            return
        self.status_messages.pop("global", None)
        for asset in assets:
            if asset.get("protocol") == "modbus":
                await self.register_asset(asset)

    async def shutdown(self):
        for name in list(self.asset_index.keys()):
            await self.unregister_asset(name)
        for endpoint, task in list(self.server_tasks.items()):
            task.cancel()
            self.server_tasks.pop(endpoint, None)
        self.contexts.clear()

    def status(self):
        return {
            "pymodbus_installed": self.installed,
            "endpoints": [f"{ip}:{port}" for ip, port in self.server_tasks.keys()],
            "asset_count": len(self.asset_index),
            "status_messages": self.status_messages,
        }
