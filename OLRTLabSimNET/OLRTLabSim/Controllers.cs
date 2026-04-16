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
                    name = reader["name"],
                    type = reader["type"],
                    sub_type = reader["sub_type"],
                    protocol = reader["protocol"],
                    address = reader["address"],
                    min_range = reader["min_range"],
                    max_range = reader["max_range"],
                    current_value = reader["current_value"],
                    drift_rate = reader["drift_rate"],
                    manual_override = reader["manual_override"],
                    icon = reader["icon"],
                    filename = reader["filename"],
                    bacnet_port = reader["bacnet_port"],
                    bacnet_device_id = reader["bacnet_device_id"],
                    is_normally_open = reader["is_normally_open"],
                    change_probability = reader["change_probability"],
                    change_interval = reader["change_interval"],
                    last_flip_check = reader["last_flip_check"],
                    bbmd_id = reader["bbmd_id"],
                    object_type = reader["object_type"],
                    bacnet_properties = reader["bacnet_properties"],
                    modbus_unit_id = reader["modbus_unit_id"],
                    modbus_register_type = reader["modbus_register_type"],
                    modbus_ip = reader["modbus_ip"],
                    modbus_port = reader["modbus_port"],
                    modbus_alarm_address = reader["modbus_alarm_address"],
                    modbus_alarm_bit = reader["modbus_alarm_bit"],
                    modbus_zero_based = reader["modbus_zero_based"],
                    modbus_word_order = reader["modbus_word_order"],
                    dnp3_ip = reader["dnp3_ip"],
                    dnp3_port = reader["dnp3_port"],
                    dnp3_outstation_address = reader["dnp3_outstation_address"],
                    dnp3_master_address = reader["dnp3_master_address"],
                    dnp3_point_class = reader["dnp3_point_class"],
                    dnp3_event_class = reader["dnp3_event_class"],
                    dnp3_static_variation = reader["dnp3_static_variation"],
                    alarm_state = reader["alarm_state"],
                    alarm_message = reader["alarm_message"]
                });
            }
            return Ok(assets);
        }

        [HttpPost("assets")]
        public async Task<IActionResult> CreateAsset([FromBody] Asset asset)
        {
            if (string.IsNullOrWhiteSpace(asset.Name))
                return BadRequest(new { detail = "Asset name is required" });

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
            cmd.Parameters.AddWithValue("@p19", normType);
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

            return Ok(new { message = "Asset added successfully" });
        }

        [HttpPut("assets/{name}")]
        public async Task<IActionResult> UpdateAsset(string name, [FromBody] Asset asset)
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = "SELECT COUNT(*) FROM assets WHERE name = @name";
            cmd.Parameters.AddWithValue("@name", name);
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                return NotFound(new { detail = "Asset not found" });

            if (asset.Protocol == "bacnet" && asset.BbmdId.HasValue)
                await _bacnetManager.UnregisterAsset(name);
            else if (asset.Protocol == "modbus")
                await _modbusManager.UnregisterAsset(name);
            else if (asset.Protocol == "dnp3")
                await _dnp3Manager.UnregisterAsset(name);

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
            cmd.Parameters.AddWithValue("@p19", normType);
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
                    name = reader["name"],
                    description = reader["description"],
                    port = reader["port"],
                    device_id = reader["device_id"],
                    ip_address = reader["ip_address"],
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
        public IActionResult GetAlarms()
        {
            var alarms = new List<object>();
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM alarm_events ORDER BY created_at DESC LIMIT 100";
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
