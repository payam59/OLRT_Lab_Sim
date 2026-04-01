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

    @staticmethod
    def _normalize_reference(address: int, register_type: str, zero_based: bool) -> tuple[str, int]:
        """
        Normalize simulator addressing so users can enter raw offsets (0,1,2)
        or Kepware-style references (40001, 30001, 10001, 1/00001, 400001).
        """
        raw = int(address)
        if raw < 0:
            raise ValueError("Modbus address must be >= 0")

        inferred_type = register_type
        offset = raw
        # Kepware/reference style (e.g. 40001, 30001, 10001, 00001, 400001)
        if raw >= 10000:
            ref = str(raw)
            primary_table = ref[0]
            item = int(ref[1:])
            inferred_type = {
                "0": "coil",
                "1": "discrete",
                "3": "input",
                "4": "holding",
            }.get(primary_table, register_type)
            offset = item - 1 if zero_based else item

        if offset < 0:
            raise ValueError("Modbus address resolves to a negative offset")
        return inferred_type, offset

    async def _serve_endpoint(self, endpoint: tuple[str, int], context):
        ip, port = endpoint
        await StartAsyncTcpServer(context=context, address=(ip, port))

    def _new_device(self):
        # 65536 follows the full Modbus two-byte reference space.
        blocks = dict(
            di=ModbusSequentialDataBlock(0, [0] * 65536),
            co=ModbusSequentialDataBlock(0, [0] * 65536),
            hr=ModbusSequentialDataBlock(0, [0] * 65536),
            ir=ModbusSequentialDataBlock(0, [0] * 65536),
        )
        if ModbusSlaveContext is not None:
            return ModbusSlaveContext(**blocks)
        return ModbusDeviceContext(**blocks)

    def _new_context(self, unit_ids: set[int]):
        normalized_units = {max(0, min(int(u), 255)) for u in unit_ids} or {1}
        if 0 not in normalized_units:
            normalized_units.add(0)  # fallback/unit-0 compatibility
        devices = {uid: self._new_device() for uid in normalized_units}

        server_params = inspect.signature(ModbusServerContext.__init__).parameters
        if "devices" in server_params:
            return ModbusServerContext(devices=devices, single=False)
        return ModbusServerContext(slaves=devices, single=False)

    async def ensure_endpoint(self, ip: str, port: int, unit_id: int):
        endpoint = (ip, port)
        if not self.installed:
            return

        if endpoint not in self.contexts:
            context = self._new_context({unit_id})
            self.contexts[endpoint] = context
        else:
            context = self.contexts[endpoint]
            try:
                context[unit_id]
            except Exception:
                try:
                    context[unit_id] = self._new_device()
                except Exception:
                    # Compatibility fallback for old context implementations.
                    pass

        if endpoint in self.server_tasks:
            return

        task = asyncio.create_task(self._serve_endpoint(endpoint, context))
        self.server_tasks[endpoint] = task
        self.status_messages[f"{ip}:{port}"] = "running"

    async def register_asset(self, asset: dict):
        name = asset["name"]
        ip = asset.get("modbus_ip") or "0.0.0.0"
        port = int(asset.get("modbus_port") or 5020)
        endpoint = (ip, port)

        old = self.asset_index.get(name)
        if old:
            await self.unregister_asset(name)

        unit_id = max(0, min(int(asset.get("modbus_unit_id") or 1), 255))
        zero_based = int(asset.get("modbus_zero_based") if asset.get("modbus_zero_based") is not None else 1) == 1
        configured_type = asset.get("modbus_register_type") or "holding"
        normalized_type, normalized_address = self._normalize_reference(
            int(asset.get("address") or 0), configured_type, zero_based
        )

        self.asset_index[name] = {
            "endpoint": endpoint,
            "unit_id": unit_id,
            "register_type": normalized_type,
            "address": normalized_address,
            "raw_address": int(asset.get("address") or 0),
            "alarm_address": asset.get("modbus_alarm_address"),
            "alarm_bit": int(asset.get("modbus_alarm_bit") or 0),
            "sub_type": asset.get("sub_type"),
            "zero_based": zero_based,
            "word_order": (asset.get("modbus_word_order") or "low_high"),
        }
        self.endpoint_assets[endpoint].add(name)
        await self.ensure_endpoint(ip, port, unit_id)
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

    def _context_for_asset(self, mapping: dict):
        context = self.contexts.get(mapping["endpoint"])
        if not context:
            return None
        try:
            return context[mapping["unit_id"]]
        except Exception:
            try:
                return context[0]
            except Exception:
                return None

    @staticmethod
    def _safe_values(raw_values):
        """
        Normalize pymodbus datastore reads.
        Some pymodbus versions may return ExcCodes/exception objects instead of a list.
        """
        if raw_values is None:
            return None
        if isinstance(raw_values, (list, tuple)):
            return list(raw_values)
        # Defensive guard for exception enums/objects like ExcCodes.IllegalAddress.
        return None

    def write_value(self, asset: dict):
        if not self.installed:
            return
        mapping = self.asset_index.get(asset["name"])
        if not mapping:
            return
        unit_context = self._context_for_asset(mapping)
        if not unit_context:
            return

        addr = mapping["address"]
        register_type = mapping["register_type"]
        value = asset.get("current_value", 0)
        if register_type in ("coil", "discrete"):
            value = 1 if float(value) >= 0.5 else 0
            unit_context.setValues(self._set_for_type(register_type), addr, [value])
        else:
            packed = struct.pack(">f", float(value))
            reg_hi, reg_lo = struct.unpack(">HH", packed)
            if mapping.get("word_order") == "high_low":
                words = [reg_hi, reg_lo]
            else:
                # Kepware default: First Word Low (low word at first address).
                words = [reg_lo, reg_hi]
            unit_context.setValues(self._set_for_type(register_type), addr, words)

        self._write_alarm_point(unit_context, mapping, asset)

    def _write_alarm_point(self, unit_context, mapping: dict, asset: dict):
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
            unit_context.setValues(1, alarm_addr, [alarm_active])
            return
        existing = self._safe_values(unit_context.getValues(3, alarm_addr, count=1))
        word = int(existing[0]) if existing else 0
        if alarm_active:
            word |= (1 << bit)
        else:
            word &= ~(1 << bit)
        unit_context.setValues(3, alarm_addr, [word])

    def read_remote_value(self, asset: dict):
        if not self.installed:
            return None
        mapping = self.asset_index.get(asset["name"])
        if not mapping:
            return None
        unit_context = self._context_for_asset(mapping)
        if not unit_context:
            return None
        addr = mapping["address"]
        register_type = mapping["register_type"]
        count = 1 if register_type in ("coil", "discrete") else 2
        values = self._safe_values(
            unit_context.getValues(self._fc_for_type(register_type), addr, count=count)
        )
        if not values:
            return None
        if register_type in ("coil", "discrete"):
            return float(values[0])
        if len(values) < 2:
            return None
        if mapping.get("word_order") == "high_low":
            packed = struct.pack(">HH", int(values[0]), int(values[1]))
        else:
            packed = struct.pack(">HH", int(values[1]), int(values[0]))
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
            "assets": self.asset_index,
        }
