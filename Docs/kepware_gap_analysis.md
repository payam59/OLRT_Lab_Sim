# Kepware Alignment Gap Analysis (Modbus + BACnet)

## Sources reviewed
- `Docs/modbus.pdf` (Kepware Modbus TCP/IP Ethernet Driver Help).
- `Docs/bacnet.pdf` (Kepware BACnet/IP configuration and address model guidance).

## Key Kepware Modbus expectations (from PDF)
1. Operators commonly configure tags with **reference-style addresses** (e.g., `40001`, `30001`, `10001`, `00001`) while wire protocol uses zero-based offsets.
2. Devices are accessed by **IP + station/unit ID** (`IP.xxx` style), and station IDs are meaningful in server mode.
3. Word/float interpretation and write function behavior must be deterministic for SCADA tag mapping.
4. Address model should tolerate the 5/6-digit normalized forms (`40001`, `400001`, etc.) as equivalent table+item references.

## Current implementation gaps before this change
1. Addresses were treated as raw offsets only, so entering `40001` attempted to use literal offset 40001.
2. `modbus_unit_id` was stored but not actually isolated per unit in runtime context.
3. Address space was limited to 10,000 points per table, below the broader Kepware-oriented 65,536 reference range.

## Phase plan

### Phase 1 (minimum Kepware interoperability) — Implemented in this PR
- Parse and normalize Kepware-style references to internal offsets.
- Infer register table from reference prefix when applicable.
- Keep optional zero-based conversion switch (default enabled).
- Use per-unit contexts so station IDs can be addressed without register collisions.
- Expand datastore blocks to full 65,536 entries for each table.
- Expose/retain configuration in API + UI.

### Phase 2 (accuracy/completeness)
- Add explicit endianness controls (byte order + first-word-low toggles) per asset/template.
- Add bit-within-word addressing and packed coil helpers.
- Add richer diagnostics endpoints (unit map, exception counters, last FC seen).
- Add compatibility presets for common Kepware device profiles.
- Add protocol-visible alarm mapping templates (coil, discrete, holding-bit) tied to asset templates.

## Prompt update (operator-facing)
When configuring Modbus assets for Kepware interoperability:
- Prefer entering reference addresses in Kepware style (for example `40001` for first holding register).
- Keep **Zero-based reference addressing** enabled unless the target client profile requires one-based behavior.
- Use correct Unit ID for each simulated device endpoint to avoid cross-device memory overlap.
- For analog telemetry, use holding/input registers and document float encoding assumptions in tag templates.

