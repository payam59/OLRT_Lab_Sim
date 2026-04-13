from __future__ import annotations

import asyncio
from collections import defaultdict


class DNP3RuntimeManager:
    """
    DNP3 runtime abstraction.

    This implementation keeps point state and endpoint mapping in-process and
    is intentionally API-compatible with the simulation engine, so a real DNP3
    transport backend can be wired in without changing engine/main call paths.
    """

    def __init__(self):
        self.server_tasks: dict[tuple[str, int], asyncio.Task] = {}
        self.servers: dict[tuple[str, int], asyncio.base_events.Server] = {}
        self.endpoint_assets: dict[tuple[str, int], set[str]] = defaultdict(set)
        self.asset_index: dict[str, dict] = {}
        self.point_values: dict[str, float] = {}
        self.status_messages: dict[str, str] = {}
        self.profiles = {
            "binary_input": {"group": 1, "variation": 2, "writable": False},
            "binary_output": {"group": 10, "variation": 2, "writable": True},
            "analog_input": {"group": 30, "variation": 5, "writable": False},
            "analog_output": {"group": 40, "variation": 4, "writable": True},
        }

    @property
    def installed(self) -> bool:
        # Transport backend can be introduced later while preserving API shape.
        return True

    @staticmethod
    def _dnp_crc(data: bytes) -> bytes:
        crc = 0
        for value in data:
            crc ^= value
            for _ in range(8):
                if crc & 0x0001:
                    crc = (crc >> 1) ^ 0xA6BC
                else:
                    crc >>= 1
        crc = (~crc) & 0xFFFF
        return bytes((crc & 0xFF, (crc >> 8) & 0xFF))

    def _build_link_response(self, src_outstation: int, dest_master: int, function: int = 0x0B) -> bytes:
        ctrl = 0x80 | (function & 0x0F)  # secondary frame, response from outstation
        header_without_crc = bytes(
            [
                0x05,
                0x64,
                0x05,  # length for control+dest+src
                ctrl,
                dest_master & 0xFF,
                (dest_master >> 8) & 0xFF,
                src_outstation & 0xFF,
                (src_outstation >> 8) & 0xFF,
            ]
        )
        crc = self._dnp_crc(header_without_crc[3:8])
        return header_without_crc + crc

    async def _handle_client(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter):
        endpoint = writer.get_extra_info("sockname")
        endpoint_key = f"{endpoint[0]}:{endpoint[1]}" if endpoint else "unknown"
        self.status_messages[endpoint_key] = "connected"
        try:
            while True:
                data = await reader.read(4096)
                if not data:
                    break
                # Basic link-layer response support for DNP3 TCP keepalive/link requests.
                if len(data) >= 10 and data[0] == 0x05 and data[1] == 0x64:
                    control = data[3]
                    dest = data[4] | (data[5] << 8)
                    src = data[6] | (data[7] << 8)
                    is_primary = bool(control & 0x40)
                    function = control & 0x0F
                    if is_primary:
                        if function == 0x09:  # request link status
                            writer.write(self._build_link_response(dest, src, 0x0B))
                        else:
                            writer.write(self._build_link_response(dest, src, 0x00))  # ACK
                        await writer.drain()
        except Exception:
            pass
        finally:
            self.status_messages[endpoint_key] = "running"
            writer.close()
            await writer.wait_closed()

    async def _serve_endpoint(self, endpoint: tuple[str, int]):
        ip, port = endpoint
        server = await asyncio.start_server(self._handle_client, host=ip, port=port)
        self.servers[endpoint] = server
        self.status_messages[f"{ip}:{port}"] = "running"
        async with server:
            await server.serve_forever()

    async def ensure_endpoint(self, ip: str, port: int):
        endpoint_key = f"{ip}:{port}"
        endpoint = (ip, port)
        if endpoint in self.server_tasks:
            self.status_messages[endpoint_key] = "running"
            return
        task = asyncio.create_task(self._serve_endpoint(endpoint))
        self.server_tasks[endpoint] = task
        self.status_messages[endpoint_key] = "starting"

    async def register_asset(self, asset: dict):
        name = asset["name"]
        ip = asset.get("dnp3_ip") or "0.0.0.0"
        port = int(asset.get("dnp3_port") or 20000)
        endpoint = (ip, port)

        old = self.asset_index.get(name)
        if old:
            await self.unregister_asset(name)

        point_class = (asset.get("dnp3_point_class") or "").strip().lower() or "analog_output"
        profile = self.profiles.get(point_class, self.profiles["analog_output"])
        point_index = max(0, int(asset.get("address") or 0))
        outstation_address = asset.get("dnp3_outstation_address")
        master_address = asset.get("dnp3_master_address")
        self.asset_index[name] = {
            "endpoint": endpoint,
            "point_class": point_class,
            "point_index": point_index,
            "group": int(profile["group"]),
            "variation": int(profile["variation"]),
            "writable": bool(profile["writable"]),
            "outstation_address": int(10 if outstation_address is None else outstation_address),
            "master_address": int(1 if master_address is None else master_address),
            "event_class": int(asset.get("dnp3_event_class") or 1),
            "static_variation": int(asset.get("dnp3_static_variation") or 0),
            "kepware_address": f"{profile['group']}.{profile['variation']}.{point_index}.Value",
        }
        self.endpoint_assets[endpoint].add(name)
        self.point_values[name] = float(asset.get("current_value") or 0.0)
        await self.ensure_endpoint(ip, port)

    async def unregister_asset(self, name: str):
        mapping = self.asset_index.pop(name, None)
        self.point_values.pop(name, None)
        if not mapping:
            return
        endpoint = mapping["endpoint"]
        self.endpoint_assets[endpoint].discard(name)
        if not self.endpoint_assets[endpoint]:
            self.endpoint_assets.pop(endpoint, None)
            server = self.servers.pop(endpoint, None)
            if server:
                server.close()
            task = self.server_tasks.pop(endpoint, None)
            if task:
                task.cancel()
            self.status_messages[f"{endpoint[0]}:{endpoint[1]}"] = "stopped"

    def write_value(self, asset: dict):
        name = asset["name"]
        if name not in self.asset_index:
            return
        point_class = self.asset_index[name]["point_class"]
        value = float(asset.get("current_value") or 0.0)
        if point_class in ("binary_input", "binary_output"):
            value = 1.0 if value >= 0.5 else 0.0
        self.point_values[name] = value

    def read_remote_value(self, asset: dict):
        name = asset["name"]
        if name not in self.asset_index:
            return None
        return self.point_values.get(name)

    async def bootstrap(self, assets: list[dict]):
        for asset in assets:
            if asset.get("protocol") == "dnp3":
                await self.register_asset(asset)

    async def shutdown(self):
        for name in list(self.asset_index.keys()):
            await self.unregister_asset(name)
        for endpoint, server in list(self.servers.items()):
            server.close()
            self.servers.pop(endpoint, None)
        for endpoint, task in list(self.server_tasks.items()):
            task.cancel()
            self.server_tasks.pop(endpoint, None)
        self.endpoint_assets.clear()
        self.asset_index.clear()
        self.point_values.clear()

    def status(self):
        return {
            "dnp3_runtime_ready": self.installed,
            "transport_mode": "tcp_listener_partial_dnp3",
            "transport_note": "TCP listener with basic DNP3 link-layer responses; full outstation application layer is limited.",
            "endpoints": [f"{ip}:{port}" for ip, port in self.endpoint_assets.keys()],
            "asset_count": len(self.asset_index),
            "status_messages": self.status_messages,
            "assets": self.asset_index,
        }
