using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OLRTLabSim.Models;
using OLRTLabSim.Data;
using OLRTLabSim.Services;

namespace OLRTLabSim.Controllers
{
    [ApiController]
    [Route("api")]
    public class ApiController : ControllerBase
    {
        private readonly BacnetRuntimeManager _bacnetManager;
        private readonly ModbusRuntimeManager _modbusManager;
        private readonly Dnp3RuntimeManager _dnp3Manager;

        public ApiController(BacnetRuntimeManager bacnetManager, ModbusRuntimeManager modbusManager, Dnp3RuntimeManager dnp3Manager)
        {
            _bacnetManager = bacnetManager;
            _modbusManager = modbusManager;
            _dnp3Manager = dnp3Manager;
        }

        private Asset? LoadAssetByName(string name)
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM assets WHERE name = @name";
            cmd.Parameters.AddWithValue("@name", name);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new Asset
            {
                Id = Convert.ToInt64(reader["id"]),
                Name = reader["name"].ToString(),
                Type = reader["type"].ToString(),
                SubType = reader["sub_type"].ToString(),
                Protocol = reader["protocol"].ToString(),
                Address = Convert.ToInt64(reader["address"]),
                MinRange = Convert.ToDouble(reader["min_range"]),
                MaxRange = Convert.ToDouble(reader["max_range"]),
                CurrentValue = Convert.ToDouble(reader["current_value"]),
                DriftRate = Convert.ToDouble(reader["drift_rate"]),
                ManualOverride = Convert.ToInt64(reader["manual_override"]),
                Icon = reader["icon"] == DBNull.Value ? null : reader["icon"].ToString(),
                Filename = reader["filename"] == DBNull.Value ? null : reader["filename"].ToString(),
                BacnetPort = Convert.ToInt64(reader["bacnet_port"]),
                BacnetDeviceId = Convert.ToInt64(reader["bacnet_device_id"]),
                IsNormallyOpen = Convert.ToInt64(reader["is_normally_open"]),
                ChangeProbability = Convert.ToDouble(reader["change_probability"]),
                ChangeInterval = Convert.ToInt64(reader["change_interval"]),
                LastFlipCheck = Convert.ToDouble(reader["last_flip_check"]),
                BbmdId = reader["bbmd_id"] == DBNull.Value ? null : Convert.ToInt64(reader["bbmd_id"]),
                ObjectType = reader["object_type"].ToString(),
                BacnetProperties = reader["bacnet_properties"] == DBNull.Value ? "{}" : reader["bacnet_properties"].ToString(),
                ModbusUnitId = Convert.ToInt64(reader["modbus_unit_id"]),
                ModbusRegisterType = reader["modbus_register_type"].ToString(),
                ModbusIp = reader["modbus_ip"] == DBNull.Value ? "0.0.0.0" : reader["modbus_ip"].ToString(),
                ModbusPort = Convert.ToInt64(reader["modbus_port"]),
                ModbusAlarmAddress = reader["modbus_alarm_address"] == DBNull.Value ? null : Convert.ToInt64(reader["modbus_alarm_address"]),
                ModbusAlarmBit = Convert.ToInt64(reader["modbus_alarm_bit"]),
                ModbusZeroBased = Convert.ToInt64(reader["modbus_zero_based"]),
                ModbusWordOrder = reader["modbus_word_order"].ToString(),
                Dnp3Ip = reader["dnp3_ip"] == DBNull.Value ? "0.0.0.0" : reader["dnp3_ip"].ToString(),
                Dnp3Port = Convert.ToInt64(reader["dnp3_port"]),
                Dnp3OutstationAddress = Convert.ToInt64(reader["dnp3_outstation_address"]),
                Dnp3MasterAddress = Convert.ToInt64(reader["dnp3_master_address"]),
                Dnp3PointClass = reader["dnp3_point_class"].ToString(),
                Dnp3EventClass = Convert.ToInt64(reader["dnp3_event_class"]),
                Dnp3StaticVariation = Convert.ToInt64(reader["dnp3_static_variation"]),
                AlarmState = Convert.ToInt64(reader["alarm_state"]),
                AlarmMessage = reader["alarm_message"] == DBNull.Value ? null : reader["alarm_message"].ToString()
            };
        }

        private void PushToRuntime(Asset asset)
        {
            if (asset.Protocol == "bacnet")
                _bacnetManager.UpdateValue(asset.Name, asset.CurrentValue, asset.SubType, asset.IsNormallyOpen);
            else if (asset.Protocol == "modbus")
                _modbusManager.WriteValue(asset);
            else if (asset.Protocol == "dnp3")
                _dnp3Manager.WriteValue(asset);
        }

        private static string? InferModbusRegisterTypeFromAddress(long address)
        {
            var token = Math.Abs(address).ToString();
            if (token.Length < 5) return null;
            return token[0] switch
            {
                '0' => "coil",
                '1' => "discrete",
                '3' => "input",
                '4' => "holding",
                _ => null
            };
        }

        [HttpGet("assets")]
        public IActionResult GetAssets()
        {
            var assets = new List<object>();
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM assets";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                assets.Add(new {
                    id = reader["id"],
                    name = reader["name"]?.ToString(),
                    type = reader["type"]?.ToString(),
                    sub_type = reader["sub_type"]?.ToString(),
                    protocol = reader["protocol"]?.ToString(),
                    address = reader["address"],
                    min_range = reader["min_range"],
                    max_range = reader["max_range"],
                    current_value = reader["current_value"],
                    drift_rate = reader["drift_rate"],
                    manual_override = reader["manual_override"],
                    icon = reader["icon"] == DBNull.Value ? null : reader["icon"]?.ToString(),
                    filename = reader["filename"] == DBNull.Value ? null : reader["filename"]?.ToString(),
                    bacnet_port = reader["bacnet_port"],
                    bacnet_device_id = reader["bacnet_device_id"],
                    is_normally_open = reader["is_normally_open"],
                    change_probability = reader["change_probability"],
                    change_interval = reader["change_interval"],
                    last_flip_check = reader["last_flip_check"],
                    bbmd_id = reader["bbmd_id"] == DBNull.Value ? null : reader["bbmd_id"],
                    object_type = reader["object_type"]?.ToString(),
                    bacnet_properties = reader["bacnet_properties"] == DBNull.Value ? "{}" : reader["bacnet_properties"]?.ToString(),
                    modbus_unit_id = reader["modbus_unit_id"],
                    modbus_register_type = reader["modbus_register_type"]?.ToString(),
                    modbus_ip = reader["modbus_ip"] == DBNull.Value ? null : reader["modbus_ip"]?.ToString(),
                    modbus_port = reader["modbus_port"],
                    modbus_alarm_address = reader["modbus_alarm_address"] == DBNull.Value ? null : reader["modbus_alarm_address"],
                    modbus_alarm_bit = reader["modbus_alarm_bit"],
                    modbus_zero_based = reader["modbus_zero_based"],
                    modbus_word_order = reader["modbus_word_order"]?.ToString(),
                    dnp3_ip = reader["dnp3_ip"] == DBNull.Value ? null : reader["dnp3_ip"]?.ToString(),
                    dnp3_port = reader["dnp3_port"],
                    dnp3_outstation_address = reader["dnp3_outstation_address"],
                    dnp3_master_address = reader["dnp3_master_address"],
                    dnp3_point_class = reader["dnp3_point_class"]?.ToString(),
                    dnp3_event_class = reader["dnp3_event_class"],
                    dnp3_static_variation = reader["dnp3_static_variation"],
                    alarm_state = reader["alarm_state"],
                    alarm_message = reader["alarm_message"] == DBNull.Value ? null : reader["alarm_message"]?.ToString(),
                    dnp3_kepware_address = _dnp3Manager.GetKepwareAddress(reader["name"]?.ToString() ?? "")
                });
            }
            return Ok(assets);
        }

        [HttpGet("assets/{name}")]
        public IActionResult GetAsset(string name)
        {
            var asset = LoadAssetByName(name);
            if (asset == null) return NotFound(new { detail = "Asset not found" });

            return Ok(new {
                id = asset.Id,
                name = asset.Name,
                type = asset.Type,
                sub_type = asset.SubType,
                protocol = asset.Protocol,
                address = asset.Address,
                min_range = asset.MinRange,
                max_range = asset.MaxRange,
                current_value = asset.CurrentValue,
                drift_rate = asset.DriftRate,
                manual_override = asset.ManualOverride,
                icon = asset.Icon,
                filename = asset.Filename,
                bacnet_port = asset.BacnetPort,
                bacnet_device_id = asset.BacnetDeviceId,
                is_normally_open = asset.IsNormallyOpen,
                change_probability = asset.ChangeProbability,
                change_interval = asset.ChangeInterval,
                bbmd_id = asset.BbmdId,
                object_type = asset.ObjectType,
                bacnet_properties = asset.BacnetProperties,
                modbus_unit_id = asset.ModbusUnitId,
                modbus_register_type = asset.ModbusRegisterType,
                modbus_ip = asset.ModbusIp,
                modbus_port = asset.ModbusPort,
                modbus_alarm_address = asset.ModbusAlarmAddress,
                modbus_alarm_bit = asset.ModbusAlarmBit,
                modbus_zero_based = asset.ModbusZeroBased,
                modbus_word_order = asset.ModbusWordOrder,
                dnp3_ip = asset.Dnp3Ip,
                dnp3_port = asset.Dnp3Port,
                dnp3_outstation_address = asset.Dnp3OutstationAddress,
                dnp3_master_address = asset.Dnp3MasterAddress,
                dnp3_point_class = asset.Dnp3PointClass,
                dnp3_event_class = asset.Dnp3EventClass,
                dnp3_static_variation = asset.Dnp3StaticVariation,
                alarm_state = asset.AlarmState,
                alarm_message = asset.AlarmMessage,
                dnp3_kepware_address = _dnp3Manager.GetKepwareAddress(asset.Name)
            });
        }

        [HttpPut("override/{name}")]
        public IActionResult OverrideAsset(string name, [FromQuery] double value)
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE assets SET current_value = @value, manual_override = 1 WHERE name = @name";
            cmd.Parameters.AddWithValue("@value", value);
            cmd.Parameters.AddWithValue("@name", name);
            if (cmd.ExecuteNonQuery() == 0)
                return NotFound(new { detail = "Asset not found" });

            var asset = LoadAssetByName(name);
            if (asset != null)
            {
                PushToRuntime(asset);
            }

            return Ok(new { message = $"{name} manually overridden to {value}" });
        }

        [HttpPut("release/{name}")]
        public IActionResult ReleaseAsset(string name)
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE assets SET manual_override = 0 WHERE name = @name";
            cmd.Parameters.AddWithValue("@name", name);
            if (cmd.ExecuteNonQuery() == 0)
                return NotFound(new { detail = "Asset not found" });

            return Ok(new { message = $"{name} released to automation" });
        }

        [HttpPost("assets")]
        public async Task<IActionResult> CreateAsset([FromBody] Asset asset)
        {
            if (string.IsNullOrWhiteSpace(asset.Name))
                return BadRequest(new { detail = "Asset name is required" });

            if ((asset.Protocol ?? "").Equals("modbus", StringComparison.OrdinalIgnoreCase))
            {
                var inferred = InferModbusRegisterTypeFromAddress(asset.Address);
                if (!string.IsNullOrWhiteSpace(inferred))
                {
                    var configured = (asset.ModbusRegisterType ?? "").Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(configured) && configured != inferred)
                    {
                        return BadRequest(new
                        {
                            detail = $"Address {asset.Address} maps to Modbus '{inferred}' table. Register type cannot be '{configured}'."
                        });
                    }
                    asset.ModbusRegisterType = inferred;
                }
            }

            asset.Name = asset.Name.Trim().Replace(" ", "_");

            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT COUNT(*) FROM assets WHERE name = @name";
            cmd.Parameters.AddWithValue("@name", asset.Name);
            if (Convert.ToInt64(cmd.ExecuteScalar()) > 0)
                return BadRequest(new { detail = "Asset with this name already exists" });

            string normType = (asset.Protocol == "bacnet" ? asset.ObjectType : "value");

            cmd.CommandText = @"
                INSERT INTO assets (
                    name, type, sub_type, protocol, address, min_range, max_range,
                    current_value, drift_rate, icon, filename, bacnet_port,
                    bacnet_device_id, is_normally_open, change_probability,
                    change_interval, last_flip_check, bbmd_id, object_type, bacnet_properties,
                    modbus_unit_id, modbus_register_type, modbus_ip, modbus_port,
                    modbus_alarm_address, modbus_alarm_bit,
                    dnp3_ip, dnp3_port, dnp3_outstation_address, dnp3_master_address,
                    dnp3_point_class, dnp3_event_class, dnp3_static_variation, alarm_state
                )
                VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, @p14, @p15, @p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23, @p24, @p25, @p26, @p27, @p28, @p29, @p30, @p31, @p32, @p33, 0)
            ";

            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@p1", asset.Name);
            cmd.Parameters.AddWithValue("@p2", (object)(asset.Type ?? "General"));
            cmd.Parameters.AddWithValue("@p3", (object)(asset.SubType ?? "Analog"));
            cmd.Parameters.AddWithValue("@p4", (object)(asset.Protocol ?? "bacnet"));
            cmd.Parameters.AddWithValue("@p5", asset.Address);
            cmd.Parameters.AddWithValue("@p6", asset.MinRange);
            cmd.Parameters.AddWithValue("@p7", asset.MaxRange);
            cmd.Parameters.AddWithValue("@p8", asset.CurrentValue);
            cmd.Parameters.AddWithValue("@p9", asset.DriftRate);
            cmd.Parameters.AddWithValue("@p10", asset.Icon ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p11", asset.Filename ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p12", asset.BacnetPort > 0 ? asset.BacnetPort : 47808);
            cmd.Parameters.AddWithValue("@p13", asset.BacnetDeviceId > 0 ? asset.BacnetDeviceId : 1234);
            cmd.Parameters.AddWithValue("@p14", asset.IsNormallyOpen);
            cmd.Parameters.AddWithValue("@p15", asset.ChangeProbability);
            cmd.Parameters.AddWithValue("@p16", asset.ChangeInterval > 0 ? asset.ChangeInterval : 15);
            cmd.Parameters.AddWithValue("@p17", Database.GetCurrentUnixTime());
            cmd.Parameters.AddWithValue("@p18", asset.BbmdId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p19", (object)(normType ?? "value"));
            cmd.Parameters.AddWithValue("@p20", string.IsNullOrWhiteSpace(asset.BacnetProperties) ? "{}" : asset.BacnetProperties);
            cmd.Parameters.AddWithValue("@p21", asset.ModbusUnitId > 0 ? asset.ModbusUnitId : 1);
            cmd.Parameters.AddWithValue("@p22", (object)(asset.ModbusRegisterType ?? "holding"));
            cmd.Parameters.AddWithValue("@p23", (object)(asset.ModbusIp ?? "0.0.0.0"));
            cmd.Parameters.AddWithValue("@p24", asset.ModbusPort > 0 ? asset.ModbusPort : 5020);
            cmd.Parameters.AddWithValue("@p25", asset.ModbusAlarmAddress ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p26", asset.ModbusAlarmBit);
            cmd.Parameters.AddWithValue("@p27", (object)(asset.Dnp3Ip ?? "0.0.0.0"));
            cmd.Parameters.AddWithValue("@p28", asset.Dnp3Port > 0 ? asset.Dnp3Port : 20000);
            cmd.Parameters.AddWithValue("@p29", asset.Dnp3OutstationAddress > 0 ? asset.Dnp3OutstationAddress : 10);
            cmd.Parameters.AddWithValue("@p30", asset.Dnp3MasterAddress > 0 ? asset.Dnp3MasterAddress : 1);
            cmd.Parameters.AddWithValue("@p31", (object)(asset.Dnp3PointClass ?? "analog_output"));
            cmd.Parameters.AddWithValue("@p32", asset.Dnp3EventClass > 0 ? asset.Dnp3EventClass : 1);
            cmd.Parameters.AddWithValue("@p33", asset.Dnp3StaticVariation);

            cmd.ExecuteNonQuery();

            if (asset.Protocol == "bacnet" && asset.BbmdId.HasValue)
                await _bacnetManager.RegisterAsset(asset);
            else if (asset.Protocol == "modbus")
                await _modbusManager.RegisterAsset(asset);
            else if (asset.Protocol == "dnp3")
                await _dnp3Manager.RegisterAsset(asset);

            PushToRuntime(asset);
            return Ok(new { message = "Asset added successfully" });
        }

        [HttpPut("assets/{name}")]
        public async Task<IActionResult> UpdateAsset(string name, [FromBody] Asset asset)
        {
            var existing = LoadAssetByName(name);
            if (existing == null)
                return NotFound(new { detail = "Asset not found" });

            if ((asset.Protocol ?? "").Equals("modbus", StringComparison.OrdinalIgnoreCase))
            {
                var inferred = InferModbusRegisterTypeFromAddress(asset.Address);
                if (!string.IsNullOrWhiteSpace(inferred))
                {
                    var configured = (asset.ModbusRegisterType ?? "").Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(configured) && configured != inferred)
                    {
                        return BadRequest(new
                        {
                            detail = $"Address {asset.Address} maps to Modbus '{inferred}' table. Register type cannot be '{configured}'."
                        });
                    }
                    asset.ModbusRegisterType = inferred;
                }
            }

            if (existing.Protocol == "bacnet" && existing.BbmdId.HasValue)
                await _bacnetManager.UnregisterAsset(name);
            else if (existing.Protocol == "modbus")
                await _modbusManager.UnregisterAsset(name);
            else if (existing.Protocol == "dnp3")
                await _dnp3Manager.UnregisterAsset(name);

            var resolvedDnp3Ip = string.IsNullOrWhiteSpace(asset.Dnp3Ip) ? existing.Dnp3Ip : asset.Dnp3Ip;
            var resolvedDnp3Port = asset.Dnp3Port > 0 ? asset.Dnp3Port : existing.Dnp3Port;
            var resolvedDnp3OutstationAddress = asset.Dnp3OutstationAddress > 0 ? asset.Dnp3OutstationAddress : existing.Dnp3OutstationAddress;
            var resolvedDnp3MasterAddress = asset.Dnp3MasterAddress > 0 ? asset.Dnp3MasterAddress : existing.Dnp3MasterAddress;
            var resolvedDnp3PointClass = string.IsNullOrWhiteSpace(asset.Dnp3PointClass) ? existing.Dnp3PointClass : asset.Dnp3PointClass;
            var resolvedDnp3EventClass = asset.Dnp3EventClass > 0 ? asset.Dnp3EventClass : existing.Dnp3EventClass;
            var resolvedDnp3StaticVariation = asset.Dnp3StaticVariation >= 0 ? asset.Dnp3StaticVariation : existing.Dnp3StaticVariation;

            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            string normType = (asset.Protocol == "bacnet" ? asset.ObjectType : "value");

            cmd.CommandText = @"
                UPDATE assets
                SET type = @p2, sub_type = @p3, protocol = @p4, address = @p5, min_range = @p6,
                    max_range = @p7, drift_rate = @p9, icon = @p10, filename = @p11, bacnet_port = @p12,
                    bacnet_device_id = @p13, is_normally_open = @p14, change_probability = @p15,
                    change_interval = @p16, bbmd_id = @p18, object_type = @p19, modbus_unit_id = @p21,
                    bacnet_properties = @p20, modbus_register_type = @p22, modbus_ip = @p23, modbus_port = @p24,
                    modbus_alarm_address = @p25, modbus_alarm_bit = @p26,
                    dnp3_ip = @p27, dnp3_port = @p28, dnp3_outstation_address = @p29, dnp3_master_address = @p30,
                    dnp3_point_class = @p31, dnp3_event_class = @p32, dnp3_static_variation = @p33
                WHERE name = @p1
            ";

            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@p1", name);
            cmd.Parameters.AddWithValue("@p2", (object)(asset.Type ?? "General"));
            cmd.Parameters.AddWithValue("@p3", (object)(asset.SubType ?? "Analog"));
            cmd.Parameters.AddWithValue("@p4", (object)(asset.Protocol ?? "bacnet"));
            cmd.Parameters.AddWithValue("@p5", asset.Address);
            cmd.Parameters.AddWithValue("@p6", asset.MinRange);
            cmd.Parameters.AddWithValue("@p7", asset.MaxRange);
            cmd.Parameters.AddWithValue("@p9", asset.DriftRate);
            cmd.Parameters.AddWithValue("@p10", asset.Icon ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p11", asset.Filename ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p12", asset.BacnetPort > 0 ? asset.BacnetPort : 47808);
            cmd.Parameters.AddWithValue("@p13", asset.BacnetDeviceId > 0 ? asset.BacnetDeviceId : 1234);
            cmd.Parameters.AddWithValue("@p14", asset.IsNormallyOpen);
            cmd.Parameters.AddWithValue("@p15", asset.ChangeProbability);
            cmd.Parameters.AddWithValue("@p16", asset.ChangeInterval > 0 ? asset.ChangeInterval : 15);
            cmd.Parameters.AddWithValue("@p18", asset.BbmdId ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p19", (object)(normType ?? "value"));
            cmd.Parameters.AddWithValue("@p20", string.IsNullOrWhiteSpace(asset.BacnetProperties) ? "{}" : asset.BacnetProperties);
            cmd.Parameters.AddWithValue("@p21", asset.ModbusUnitId > 0 ? asset.ModbusUnitId : 1);
            cmd.Parameters.AddWithValue("@p22", (object)(asset.ModbusRegisterType ?? "holding"));
            cmd.Parameters.AddWithValue("@p23", (object)(asset.ModbusIp ?? "0.0.0.0"));
            cmd.Parameters.AddWithValue("@p24", asset.ModbusPort > 0 ? asset.ModbusPort : 5020);
            cmd.Parameters.AddWithValue("@p25", asset.ModbusAlarmAddress ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p26", asset.ModbusAlarmBit);
            cmd.Parameters.AddWithValue("@p27", (object)(resolvedDnp3Ip ?? "0.0.0.0"));
            cmd.Parameters.AddWithValue("@p28", resolvedDnp3Port > 0 ? resolvedDnp3Port : 20000);
            cmd.Parameters.AddWithValue("@p29", resolvedDnp3OutstationAddress > 0 ? resolvedDnp3OutstationAddress : 10);
            cmd.Parameters.AddWithValue("@p30", resolvedDnp3MasterAddress > 0 ? resolvedDnp3MasterAddress : 1);
            cmd.Parameters.AddWithValue("@p31", (object)(resolvedDnp3PointClass ?? "analog_output"));
            cmd.Parameters.AddWithValue("@p32", resolvedDnp3EventClass > 0 ? resolvedDnp3EventClass : 1);
            cmd.Parameters.AddWithValue("@p33", resolvedDnp3StaticVariation);

            cmd.ExecuteNonQuery();

            if (asset.Protocol == "bacnet" && asset.BbmdId.HasValue)
                await _bacnetManager.RegisterAsset(asset);
            else if (asset.Protocol == "modbus")
                await _modbusManager.RegisterAsset(asset);
            else if (asset.Protocol == "dnp3")
                await _dnp3Manager.RegisterAsset(asset);

            var updated = LoadAssetByName(name);
            if (updated != null) PushToRuntime(updated);

            return Ok(new { message = "Asset updated successfully" });
        }

        [HttpDelete("assets/{name}")]
        public async Task<IActionResult> DeleteAsset(string name)
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT protocol FROM assets WHERE name = @name";
            cmd.Parameters.AddWithValue("@name", name);
            var protoObj = cmd.ExecuteScalar();
            if (protoObj == null)
                return NotFound(new { detail = "Asset not found" });

            string protocol = protoObj.ToString();

            if (protocol == "bacnet")
                await _bacnetManager.UnregisterAsset(name);
            else if (protocol == "modbus")
                await _modbusManager.UnregisterAsset(name);
            else if (protocol == "dnp3")
                await _dnp3Manager.UnregisterAsset(name);

            cmd.CommandText = "DELETE FROM assets WHERE name = @name";
            cmd.ExecuteNonQuery();

            return Ok(new { message = "Asset deleted" });
        }

        [HttpGet("bbmd")]
        public IActionResult GetBbmds()
        {
            var bbmds = new List<object>();
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM bbmd";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                bbmds.Add(new {
                    id = reader["id"],
                    name = reader["name"]?.ToString(),
                    description = reader["description"] == DBNull.Value ? null : reader["description"]?.ToString(),
                    port = reader["port"],
                    device_id = reader["device_id"],
                    ip_address = reader["ip_address"] == DBNull.Value ? null : reader["ip_address"]?.ToString(),
                    enabled = reader["enabled"],
                    created_at = reader["created_at"]
                });
            }
            return Ok(bbmds);
        }

        [HttpPost("bbmd")]
        public async Task<IActionResult> CreateBbmd([FromBody] Bbmd bbmd)
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "INSERT INTO bbmd (name, description, port, device_id, ip_address, enabled, created_at) VALUES (@p1, @p2, @p3, @p4, @p5, @p6, @p7)";
            cmd.Parameters.AddWithValue("@p1", bbmd.Name ?? (object)"Unknown");
            cmd.Parameters.AddWithValue("@p2", bbmd.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p3", bbmd.Port);
            cmd.Parameters.AddWithValue("@p4", bbmd.DeviceId);
            cmd.Parameters.AddWithValue("@p5", bbmd.IpAddress ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p6", bbmd.Enabled);
            cmd.Parameters.AddWithValue("@p7", Database.GetCurrentUnixTime());

            try
            {
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT last_insert_rowid()";
                long id = (long)cmd.ExecuteScalar();

                await _bacnetManager.StartBbmd(id);

                return Ok(new { message = "BBMD definition created successfully", id });
            }
            catch (Exception ex)
            {
                return BadRequest(new { detail = ex.Message });
            }
        }

        [HttpPut("bbmd/{id}")]
        public async Task<IActionResult> UpdateBbmd(long id, [FromBody] Bbmd bbmd)
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "UPDATE bbmd SET name = @p1, description = @p2, port = @p3, device_id = @p4, ip_address = @p5, enabled = @p6 WHERE id = @id";
            cmd.Parameters.AddWithValue("@p1", bbmd.Name ?? (object)"Unknown");
            cmd.Parameters.AddWithValue("@p2", bbmd.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p3", bbmd.Port);
            cmd.Parameters.AddWithValue("@p4", bbmd.DeviceId);
            cmd.Parameters.AddWithValue("@p5", bbmd.IpAddress ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@p6", bbmd.Enabled);
            cmd.Parameters.AddWithValue("@id", id);

            if (cmd.ExecuteNonQuery() == 0)
                return NotFound(new { detail = "BBMD not found" });

            await _bacnetManager.StopBbmd(id);
            if (bbmd.Enabled == 1)
            {
                await _bacnetManager.StartBbmd(id);
            }

            return Ok(new { message = "BBMD updated successfully" });
        }

        [HttpDelete("bbmd/{id}")]
        public async Task<IActionResult> DeleteBbmd(long id)
        {
            await _bacnetManager.StopBbmd(id);

            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM bbmd WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            return Ok(new { message = "BBMD deleted successfully" });
        }

        [HttpGet("alarms")]
        public IActionResult GetAlarms([FromQuery] int active_only = 0)
        {
            var alarms = new List<object>();
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = active_only == 1
                ? "SELECT * FROM alarm_events WHERE active = 1 ORDER BY created_at DESC LIMIT 100"
                : "SELECT * FROM alarm_events ORDER BY created_at DESC LIMIT 100";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                alarms.Add(new {
                    id = reader["id"],
                    asset_id = reader["asset_id"],
                    asset_name = reader["asset_name"],
                    message = reader["message"],
                    active = reader["active"],
                    created_at = reader["created_at"],
                    cleared_at = reader["cleared_at"]
                });
            }
            return Ok(alarms);
        }

        [HttpGet("bacnet/status")]
        public IActionResult GetBacnetStatus() => Ok(_bacnetManager.Status());

        [HttpGet("modbus/status")]
        public IActionResult GetModbusStatus() => Ok(_modbusManager.Status());

        [HttpGet("dnp3/status")]
        public IActionResult GetDnp3Status() => Ok(_dnp3Manager.Status());
    }
}
