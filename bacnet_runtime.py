from __future__ import annotations
import json

BAC0_IMPORT_ERROR = None
try:
    import BAC0
    from bacpypes3.local.analog import AnalogInputObject, AnalogOutputObject, AnalogValueObject
    from bacpypes3.local.binary import BinaryInputObject, BinaryOutputObject, BinaryValueObject
    from BAC0.core.devices.local.factory import ObjectFactory
except Exception as e:
    BAC0 = None
    BAC0_IMPORT_ERROR = str(e)
    AnalogInputObject = AnalogOutputObject = AnalogValueObject = None
    BinaryInputObject = BinaryOutputObject = BinaryValueObject = None
    ObjectFactory = None


class BACnetManager:
    def __init__(self):
        self.bbmd_instances = {}
        self.objects = {}
        self.asset_to_bbmd = {}
        self.bbmd_status = {}

    def _get_object_class(self, sub_type, object_type):
        if sub_type == "Digital":
            if object_type == "input":
                return BinaryInputObject
            elif object_type == "output":
                return BinaryOutputObject
            return BinaryValueObject
        else:
            if object_type == "input":
                return AnalogInputObject
            elif object_type == "output":
                return AnalogOutputObject
            return AnalogValueObject

    def start_bbmd(self, bbmd):
        bbmd_id = bbmd["id"]
        if bbmd_id in self.bbmd_instances:
            return
        if not BAC0:
            self.bbmd_status[bbmd_id] = {"running": False, "message": "BAC0 is not installed in the runtime environment."}
            return
        try:
            lite_args = dict(port=bbmd["port"], deviceId=bbmd["device_id"], localObjName=bbmd["name"])
            ip_address = (bbmd.get("ip_address") or "").strip()
            if ip_address and ip_address != "0.0.0.0":
                lite_args["ip"] = ip_address
            stack = BAC0.lite(**lite_args)
            self.bbmd_instances[bbmd_id] = stack
            self.bbmd_status[bbmd_id] = {"running": True, "message": f"Listening on UDP {ip_address or 'auto-detected'}:{bbmd['port']}"}
        except Exception as e:
            self.bbmd_status[bbmd_id] = {"running": False, "message": str(e)}

    def stop_bbmd(self, bbmd_id):
        if bbmd_id in self.bbmd_instances:
            try:
                self.bbmd_instances[bbmd_id].disconnect()
                del self.bbmd_instances[bbmd_id]
                self.bbmd_status[bbmd_id] = {"running": False, "message": "Stopped"}
            except Exception:
                pass

    def add_asset_to_bbmd(self, asset):
        if not BAC0:
            return
        bbmd_id = asset.get("bbmd_id")
        if not bbmd_id or bbmd_id not in self.bbmd_instances:
            return
        stack = self.bbmd_instances[bbmd_id]
        obj_class = self._get_object_class(asset["sub_type"], asset["object_type"])
        if not obj_class or ObjectFactory is None:
            self.bbmd_status[bbmd_id] = {"running": False, "message": "BACnet object classes/ObjectFactory unavailable."}
            return

        if asset["sub_type"] == "Digital":
            present_val = "active" if asset["current_value"] >= 0.5 else "inactive"
            properties = self._parse_properties(asset.get("bacnet_properties"))
            factory = ObjectFactory(obj_class, instance=asset["address"], objectName=asset["name"], presentValue=present_val, properties=properties)
        else:
            properties = self._parse_properties(asset.get("bacnet_properties"))
            properties.setdefault("units", "noUnits")
            factory = ObjectFactory(
                obj_class,
                instance=asset["address"],
                objectName=asset["name"],
                presentValue=float(asset["current_value"]),
                properties=properties,
            )
        factory.add_objects_to_application(stack)
        obj = factory.objects.get(asset["name"])
        if obj:
            self.objects[asset["name"]] = obj
            self.asset_to_bbmd[asset["name"]] = bbmd_id

    def update_value(self, name, val, sub_type):
        if name in self.objects:
            if sub_type == "Digital":
                self.objects[name].presentValue = "active" if val >= 0.5 else "inactive"
            else:
                self.objects[name].presentValue = float(val)

    def get_value(self, name):
        if name in self.objects and hasattr(self.objects[name], "presentValue"):
            val = self.objects[name].presentValue
            if str(val).lower() in ("active", "1", "true"):
                return 1.0
            if str(val).lower() in ("inactive", "0", "false"):
                return 0.0
            return float(val)
        return None

    def object_details(self):
        details = []
        for name, obj in self.objects.items():
            object_identifier = getattr(obj, "objectIdentifier", None)
            object_type = None
            instance = None
            if isinstance(object_identifier, (list, tuple)) and len(object_identifier) == 2:
                object_type, instance = object_identifier[0], object_identifier[1]
            present = getattr(obj, "presentValue", None)
            details.append(
                {
                    "name": name,
                    "object_type": str(object_type) if object_type is not None else None,
                    "instance": int(instance) if isinstance(instance, (int, float)) else instance,
                    "present_value": str(present) if present is not None else None,
                }
            )
        return details

    def remove_asset(self, name):
        self.objects.pop(name, None)
        self.asset_to_bbmd.pop(name, None)

    def _parse_properties(self, raw):
        if not raw:
            return {}
        if isinstance(raw, dict):
            return raw
        if isinstance(raw, str):
            try:
                parsed = json.loads(raw)
                if isinstance(parsed, dict):
                    return parsed
            except Exception:
                return {}
        return {}
