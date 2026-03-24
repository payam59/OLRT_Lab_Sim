# OLRT_Lab_Sim

## Overview
OLRT_Lab_Sim (Online Real-Time Lab Simulation) is a Python-based industrial asset simulation tool. It provides a web interface (FastAPI) to manage virtual assets, simulate their physical behavior (with random drift), and expose their data via common industrial protocols like Modbus TCP, DNP3, BACnet, and OPC UA.

The simulation engine updates asset values every second and logs the data in binary format, mimicking real-world industrial logging systems.

## Stack
- **Language**: Python 3.14+
- **Framework**: FastAPI (Web API), Pymodbus (Modbus Server), Jinja2 (Templating)
- **Database**: SQLite3
- **Frontend**: HTML, CSS, JavaScript (served by FastAPI)
- **Serialization**: `struct` (Binary packing for industrial protocols)

## Requirements
- Python 3.14+
- A virtual environment is recommended.
- Dependencies (estimated from imports):
  - `fastapi`
  - `uvicorn`
  - `jinja2`
  - `pydantic`
  - `pymodbus`
  - `sqlite3` (Built-in)

## Setup
1. **Clone the repository**:
   ```powershell
   git clone <repository-url>
   cd OLRT_Lab_Sim
   ```

2. **Create and activate a virtual environment**:
   ```powershell
   python -m venv .venv
   .venv\Scripts\Activate.ps1  # Windows PowerShell
   ```

3. **Install dependencies**:
   *(Note: No requirements.txt found. Install manually or generate one.)*
   ```powershell
   pip install fastapi uvicorn jinja2 pydantic pymodbus
   ```

## Running the Application
The main entry point is `main.py`. It starts the FastAPI web server, the Modbus TCP server, and the simulation loop simultaneously.

```powershell
python main.py
```

- **Web Interface**: `http://localhost:8000`
- **Modbus TCP Server**: `0.0.0.0:5020`
- **API Documentation**: `http://localhost:8000/docs`

## Scripts and Entry Points
- `main.py`: The main script that initializes the database, starts the Modbus server, and runs the FastAPI application.
- `engine.py`: Contains the `simulation_loop` and data packing logic for various protocols.
- `database.py`: Handles SQLite database initialization and migrations.

## Project Structure
```text
OLRT_Lab_Sim/
├── main.py              # Entry point: Web API + Modbus Server + Simulation Task
├── engine.py            # Simulation logic and protocol binary packing
├── database.py          # SQLite database schema and connection management
├── lab_assets.db        # SQLite database file (auto-generated)
├── simulation_logs/     # Directory for binary log files (auto-generated)
├── static/              # Frontend assets (CSS, JS)
│   ├── script.js
│   └── style.css
└── templates/           # HTML templates (Jinja2)
    └── index.html
```

## Environment Variables
Currently, no external environment variables are required. Configuration is hardcoded in `main.py` (e.g., host `0.0.0.0`, port `8000` for web, `5020` for Modbus).
- TODO: Implement `.env` support for server ports and host configurations.

## Tests
- TODO: Add unit tests for simulation logic and API endpoints.

## License
- TODO: Specify the license for this project.
