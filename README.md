# OLRT_Lab_Sim

## Overview
OLRT_Lab_Sim (Online Real-Time Lab Simulation) is a FastAPI-based industrial asset simulator for OT/ICS lab scenarios. It provides:

- A web UI to create/manage simulated assets.
- A 1-second simulation loop for analog drift and digital state behavior.
- Protocol runtime integration for BACnet and Modbus TCP.
- Protocol runtime integration for BACnet, Modbus TCP, and DNP3.
- Alarm state detection, active alarm display, and alarm event history.
- Service status views for BACnet BBMD and Modbus runtime endpoints.

## Key Features

### Asset simulation
- Supports analog and digital assets.
- Analog assets drift continuously based on configured drift rates.
- Digital assets can flip state probabilistically on a configurable interval.
- Manual override logic prevents automation from changing externally written points.

### BACnet runtime
- BBMD lifecycle management (`start`, `stop`, status tracking).
- Dynamic object creation per asset with object types (`input`, `output`, `value`).
- Runtime value update and remote-write detection support.
- Optional BACnet object properties via JSON (`bacnet_properties`).

### Modbus TCP runtime
- In-process Modbus TCP server endpoints managed per configured IP/port.
- Asset registration/unregistration with dynamic endpoint bootstrapping.
- Register type support: `holding`, `input`, `coil`, `discrete`.
- Analog values encoded as IEEE-754 float32 across two 16-bit registers.
- Remote write detection for writable register types.
- Configurable protocol alarm mapping:
  - `modbus_alarm_address` (target alarm register/coil)
  - `modbus_alarm_bit` (bit position for register-backed alarms)

### DNP3 runtime
- Endpoint and point mapping for DNP3 assets.
- Point class support for analog/binary input and output style points.
- Runtime value update and remote-write detection support for writable classes.
- Kepware-style profile mapping per point class:
  - `binary_input` -> `1.2.<index>.Val` (read)
  - `binary_output` -> `10.2.<index>.Val` (read/write)
  - `analog_input` -> `30.5.<index>.Val` (read)
  - `analog_output` -> `40.4.<index>.Val` (read/write)
- Runtime currently operates in-process simulation mode (no external DNP3 wire stack by default).

### Alarming and observability
- Real-time threshold-based alarm detection for analog assets.
- Alarm state persistence on each asset (`alarm_state`, `alarm_message`).
- Alarm event lifecycle table (`alarm_events`) capturing raise/clear timestamps.
- REST endpoints and UI widgets for active alarms and service health.

### API and UI
- CRUD APIs for assets and BBMD definitions.
- Status endpoints for BACnet and Modbus runtime managers.
- WebSocket broadcast of asset updates for live dashboard refresh.
- UI pages for asset management and BACnet/Modbus status monitoring.

## Tech Stack
- **Language**: Python 3.14+
- **Backend**: FastAPI, Uvicorn, Pydantic
- **Protocols**: BAC0 / bacpypes3 (BACnet), pymodbus (Modbus TCP)
- **Database**: SQLite3 (with startup migrations)
- **Frontend**: Jinja2 templates + vanilla JavaScript/CSS

## Requirements
- Python 3.14+
- Recommended: virtual environment
- Python packages:
  - `fastapi`
  - `uvicorn`
  - `jinja2`
  - `pydantic`
  - `pymodbus`
  - `BAC0` (optional but required for BACnet runtime)
  - `bacpypes3` (BACnet dependency)

## Setup
1. **Clone repository**
   ```bash
   git clone <repository-url>
   cd OLRT_Lab_Sim
   ```
2. **Create and activate virtual environment**
   ```bash
   python -m venv .venv
   source .venv/bin/activate
   ```
   On Windows PowerShell:
   ```powershell
   .venv\Scripts\Activate.ps1
   ```
3. **Install dependencies**
   ```bash
   pip install fastapi uvicorn jinja2 pydantic pymodbus BAC0 bacpypes3
   ```

## Running the application
```bash
python main.py
```

- Web UI: `http://localhost:8000`
- API docs: `http://localhost:8000/docs`
- Default Modbus endpoint: `0.0.0.0:5020` (plus any additional configured endpoints)

## Project Structure
```text
OLRT_Lab_Sim/
├── main.py                  # FastAPI app, lifecycle, API routes
├── engine.py                # 1-second simulation loop + alarm detection
├── database.py              # SQLite schema creation + migrations
├── bacnet_runtime.py        # BACnet runtime manager (BBMD + objects)
├── modbus_runtime.py        # Modbus runtime manager (endpoint + registers)
├── dnp3_runtime.py          # DNP3 runtime manager (endpoint + points)
├── templates/
│   ├── index.html
│   └── bacnet_status.html
├── static/
│   ├── script.js
│   └── style.css
└── simulation_logs/         # Generated binary logs
```

## Notes
- Database file (`lab_assets.db`) and log files are generated at runtime.
- BACnet support is runtime-dependent: if BAC0 is not installed/importable, BACnet endpoints report status but cannot start.
- Configuration is currently code-defined; environment-based config can be added later.

## License
This project is licensed under the **MIT License**. See [LICENSE](./LICENSE) for full text.
