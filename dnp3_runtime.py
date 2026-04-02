from __future__ import annotations

from collections import defaultdict


class DNP3RuntimeManager:
    """
    DNP3 runtime abstraction.

    This implementation keeps point state and endpoint mapping in-process and
    is intentionally API-compatible with the simulation engine, so a real DNP3
    transport backend can be wired in without changing engine/main call paths.
    """

    def __init__(self):
        self.endpoint_assets: dict[tuple[str, int], set[str]] = defaultdict(set)
        self.asset_index: dict[str, dict] = {}
        self.point_values: dict[str, float] = {}
        self.status_messages: dict[str, str] = {}

    @property
    def installed(self) -> bool:
        # Transport backend can be introduced later while preserving API shape.
        return True

    async def ensure_endpoint(self, ip: str, port: int):
        endpoint_key = f"{ip}:{port}"
        self.status_messages[endpoint_key] = "running"

    async def register_asset(self, asset: dict):
        name = asset["name"]
        ip = asset.get("dnp3_ip") or "0.0.0.0"
        port = int(asset.get("dnp3_port") or 20000)
        endpoint = (ip, port)

        old = self.asset_index.get(name)
        if old:
            await self.unregister_asset(name)

        point_class = (asset.get("dnp3_point_class") or "").strip().lower() or "analog_output"
        point_index = max(0, int(asset.get("address") or 0))
        self.asset_index[name] = {
            "endpoint": endpoint,
            "point_class": point_class,
            "point_index": point_index,
            "outstation_address": int(asset.get("dnp3_outstation_address") or 10),
            "master_address": int(asset.get("dnp3_master_address") or 1),
            "event_class": int(asset.get("dnp3_event_class") or 1),
            "static_variation": int(asset.get("dnp3_static_variation") or 0),
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
        self.endpoint_assets.clear()
        self.asset_index.clear()
        self.point_values.clear()

    def status(self):
        return {
            "dnp3_runtime_ready": self.installed,
            "endpoints": [f"{ip}:{port}" for ip, port in self.endpoint_assets.keys()],
            "asset_count": len(self.asset_index),
            "status_messages": self.status_messages,
            "assets": self.asset_index,
        }
