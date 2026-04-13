from __future__ import annotations

import importlib
import importlib.util
from dataclasses import dataclass

VOLTTRON_DNP3_GIT = "git+https://github.com/VOLTTRON/dnp3-python.git"


@dataclass(frozen=True)
class NativeDNP3Bindings:
    available: bool
    outstation_application: object | None
    opendnp3: object | None


def load_native_bindings() -> NativeDNP3Bindings:
    dnp3_python_spec = importlib.util.find_spec("dnp3_python")
    pydnp3_spec = importlib.util.find_spec("pydnp3")

    if dnp3_python_spec is None or pydnp3_spec is None:
        return NativeDNP3Bindings(False, None, None)

    outstation_spec = importlib.util.find_spec("dnp3_python.dnp3station.outstation")
    if outstation_spec is None:
        return NativeDNP3Bindings(False, None, None)

    outstation_module = importlib.import_module("dnp3_python.dnp3station.outstation")
    pydnp3_module = importlib.import_module("pydnp3")

    return NativeDNP3Bindings(
        available=True,
        outstation_application=outstation_module.OutStationApplication,
        opendnp3=pydnp3_module.opendnp3,
    )
