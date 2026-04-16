using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using OLRTLabSim.Models;
using OLRTLabSim.Data;
using OLRTLabSim.Services;

namespace OLRTLabSim.Engine
{
    public class SimulationEngine : BackgroundService
    {
        private readonly ModbusRuntimeManager _modbusManager;
        private readonly BacnetRuntimeManager _bacnetManager;
        private readonly Dnp3RuntimeManager _dnp3Manager;

        public SimulationEngine(ModbusRuntimeManager modbusManager, BacnetRuntimeManager bacnetManager, Dnp3RuntimeManager dnp3Manager)
        {
            _modbusManager = modbusManager;
            _bacnetManager = bacnetManager;
            _dnp3Manager = dnp3Manager;
        }

        private (bool InAlarm, string Message) CheckAlarmCondition(Asset asset)
        {
            if (asset.SubType == "Analog")
            {
                if (asset.CurrentValue < asset.MinRange)
                {
                    return (true, $"Low Alarm: {asset.CurrentValue:F2} < {asset.MinRange:F2}");
                }
                else if (asset.CurrentValue > asset.MaxRange)
                {
                    return (true, $"High Alarm: {asset.CurrentValue:F2} > {asset.MaxRange:F2}");
                }
            }
            return (false, null);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var rnd = new Random();
            var bootstrapped = false;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var conn = Database.GetConnection();
                    var assets = new List<Asset>();

                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT * FROM assets";
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                assets.Add(new Asset
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
                                    Icon = reader["icon"].ToString(),
                                    Filename = reader["filename"].ToString(),
                                    BacnetPort = Convert.ToInt64(reader["bacnet_port"]),
                                    BacnetDeviceId = Convert.ToInt64(reader["bacnet_device_id"]),
                                    IsNormallyOpen = Convert.ToInt64(reader["is_normally_open"]),
                                    ChangeProbability = Convert.ToDouble(reader["change_probability"]),
                                    ChangeInterval = Convert.ToInt64(reader["change_interval"]),
                                    LastFlipCheck = Convert.ToDouble(reader["last_flip_check"]),
                                    BbmdId = reader["bbmd_id"] != DBNull.Value ? Convert.ToInt64(reader["bbmd_id"]) : (long?)null,
                                    ObjectType = reader["object_type"].ToString(),
                                    BacnetProperties = reader["bacnet_properties"].ToString(),
                                    ModbusUnitId = Convert.ToInt64(reader["modbus_unit_id"]),
                                    ModbusRegisterType = reader["modbus_register_type"].ToString(),
                                    ModbusIp = reader["modbus_ip"].ToString(),
                                    ModbusPort = Convert.ToInt64(reader["modbus_port"]),
                                    ModbusAlarmAddress = reader["modbus_alarm_address"] != DBNull.Value ? Convert.ToInt64(reader["modbus_alarm_address"]) : (long?)null,
                                    ModbusAlarmBit = Convert.ToInt64(reader["modbus_alarm_bit"]),
                                    ModbusZeroBased = Convert.ToInt64(reader["modbus_zero_based"]),
                                    ModbusWordOrder = reader["modbus_word_order"].ToString(),
                                    Dnp3Ip = reader["dnp3_ip"].ToString(),
                                    Dnp3Port = Convert.ToInt64(reader["dnp3_port"]),
                                    Dnp3OutstationAddress = Convert.ToInt64(reader["dnp3_outstation_address"]),
                                    Dnp3MasterAddress = Convert.ToInt64(reader["dnp3_master_address"]),
                                    Dnp3PointClass = reader["dnp3_point_class"].ToString(),
                                    Dnp3EventClass = Convert.ToInt64(reader["dnp3_event_class"]),
                                    Dnp3StaticVariation = Convert.ToInt64(reader["dnp3_static_variation"]),
                                    AlarmState = Convert.ToInt64(reader["alarm_state"]),
                                    AlarmMessage = reader["alarm_message"] != DBNull.Value ? reader["alarm_message"].ToString() : null,
                                });
                            }
                        }
                    }
                    if (!bootstrapped)
                    {
                        var bbmds = new List<Bbmd>();
                        using (var bbmdCmd = conn.CreateCommand())
                        {
                            bbmdCmd.CommandText = "SELECT * FROM bbmd";
                            using var bbmdReader = bbmdCmd.ExecuteReader();
                            while (bbmdReader.Read())
                            {
                                bbmds.Add(new Bbmd
                                {
                                    Id = Convert.ToInt64(bbmdReader["id"]),
                                    Name = bbmdReader["name"].ToString(),
                                    Description = bbmdReader["description"] == DBNull.Value ? null : bbmdReader["description"].ToString(),
                                    Port = Convert.ToInt64(bbmdReader["port"]),
                                    DeviceId = Convert.ToInt64(bbmdReader["device_id"]),
                                    IpAddress = bbmdReader["ip_address"] == DBNull.Value ? "0.0.0.0" : bbmdReader["ip_address"].ToString(),
                                    Enabled = Convert.ToInt64(bbmdReader["enabled"])
                                });
                            }
                        }

                        await _bacnetManager.Bootstrap(assets, bbmds);
                        await _modbusManager.Bootstrap(assets);
                        await _dnp3Manager.Bootstrap(assets);
                        bootstrapped = true;
                    }

                    double now = Database.GetCurrentUnixTime();
                    bool anyGlobalChange = false;

                    foreach (var asset in assets)
                    {
                        double originalValue = asset.CurrentValue;
                        bool assetChanged = false;
                        bool alarmChanged = false;

                        // 1. Remote write detection (keep active even during manual override)
                        if (asset.Protocol == "bacnet" && (asset.ObjectType == "output" || asset.ObjectType == "value"))
                        {
                            var remote = _bacnetManager.GetValue(asset.Name);
                            if (remote.HasValue && Math.Abs(remote.Value - originalValue) > 0.01)
                            {
                                using var remoteCmd = conn.CreateCommand();
                                remoteCmd.CommandText = "UPDATE assets SET current_value = @val, manual_override = 1 WHERE id = @id";
                                remoteCmd.Parameters.AddWithValue("@val", remote.Value);
                                remoteCmd.Parameters.AddWithValue("@id", asset.Id);
                                remoteCmd.ExecuteNonQuery();
                                asset.CurrentValue = remote.Value;
                                asset.ManualOverride = 1;
                                assetChanged = true;
                            }
                        }
                        else if (asset.Protocol == "modbus")
                        {
                            var registerType = (asset.ModbusRegisterType ?? "").ToLowerInvariant();
                            if (registerType == "holding" || registerType == "coil")
                            {
                                var remote = _modbusManager.ReadRemoteValue(asset);
                                if (remote.HasValue && Math.Abs(remote.Value - originalValue) > 0.01)
                                {
                                    using var remoteCmd = conn.CreateCommand();
                                    remoteCmd.CommandText = "UPDATE assets SET current_value = @val, manual_override = 1 WHERE id = @id";
                                    remoteCmd.Parameters.AddWithValue("@val", remote.Value);
                                    remoteCmd.Parameters.AddWithValue("@id", asset.Id);
                                    remoteCmd.ExecuteNonQuery();
                                    asset.CurrentValue = remote.Value;
                                    asset.ManualOverride = 1;
                                    assetChanged = true;
                                }
                            }
                        }
                        else if (asset.Protocol == "dnp3")
                        {
                            var pointClass = (asset.Dnp3PointClass ?? "").ToLowerInvariant();
                            if (pointClass == "analog_output" || pointClass == "binary_output")
                            {
                                var remote = _dnp3Manager.ReadRemoteValue(asset);
                                if (remote.HasValue && Math.Abs(remote.Value - originalValue) > 0.01)
                                {
                                    using var remoteCmd = conn.CreateCommand();
                                    remoteCmd.CommandText = "UPDATE assets SET current_value = @val, manual_override = 1 WHERE id = @id";
                                    remoteCmd.Parameters.AddWithValue("@val", remote.Value);
                                    remoteCmd.Parameters.AddWithValue("@id", asset.Id);
                                    remoteCmd.ExecuteNonQuery();
                                    asset.CurrentValue = remote.Value;
                                    asset.ManualOverride = 1;
                                    assetChanged = true;
                                }
                            }
                        }

                        // 2. Automation Logic
                        if (asset.ManualOverride == 0)
                        {
                            if (asset.SubType == "Digital")
                            {
                                double lastCheck = asset.LastFlipCheck;
                                long intervalSec = asset.ChangeInterval * 60;

                                if ((now - lastCheck) >= intervalSec)
                                {
                                    using (var cmd = conn.CreateCommand())
                                    {
                                        cmd.CommandText = "UPDATE assets SET last_flip_check = @now WHERE id = @id";
                                        cmd.Parameters.AddWithValue("@now", now);
                                        cmd.Parameters.AddWithValue("@id", asset.Id);
                                        cmd.ExecuteNonQuery();
                                    }
                                    asset.LastFlipCheck = now;

                                    if (rnd.NextDouble() < (asset.ChangeProbability / 100.0))
                                    {
                                        double newVal = originalValue < 0.5 ? 1.0 : 0.0;
                                        using (var cmd = conn.CreateCommand())
                                        {
                                            cmd.CommandText = "UPDATE assets SET current_value = @val WHERE id = @id";
                                            cmd.Parameters.AddWithValue("@val", newVal);
                                            cmd.Parameters.AddWithValue("@id", asset.Id);
                                            cmd.ExecuteNonQuery();
                                        }
                                        asset.CurrentValue = newVal;
                                        assetChanged = true;
                                    }
                                }
                            }
                            else
                            {
                                // Analog Drift
                                double noise = (rnd.NextDouble() * 2 * asset.DriftRate) - asset.DriftRate;
                                double newVal = originalValue + noise;

                                if (Math.Abs(newVal - originalValue) > 0.001)
                                {
                                    using (var cmd = conn.CreateCommand())
                                    {
                                        cmd.CommandText = "UPDATE assets SET current_value = @val WHERE id = @id";
                                        cmd.Parameters.AddWithValue("@val", newVal);
                                        cmd.Parameters.AddWithValue("@id", asset.Id);
                                        cmd.ExecuteNonQuery();
                                    }
                                    asset.CurrentValue = newVal;
                                    assetChanged = true;
                                }
                            }
                        }

                        // 3. Alarm Detection
                        var alarmResult = CheckAlarmCondition(asset);
                        bool inAlarm = alarmResult.InAlarm;
                        string alarmMsg = alarmResult.Message;

                        if (inAlarm != (asset.AlarmState == 1))
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = "UPDATE assets SET alarm_state = @state, alarm_message = @msg WHERE id = @id";
                                cmd.Parameters.AddWithValue("@state", inAlarm ? 1 : 0);
                                cmd.Parameters.AddWithValue("@msg", inAlarm ? alarmMsg : (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@id", asset.Id);
                                cmd.ExecuteNonQuery();
                            }
                            asset.AlarmState = inAlarm ? 1 : 0;
                            asset.AlarmMessage = inAlarm ? alarmMsg : null;

                            if (inAlarm)
                            {
                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.CommandText = "INSERT INTO alarm_events (asset_id, asset_name, message, active, created_at) VALUES (@aid, @aname, @msg, 1, @now)";
                                    cmd.Parameters.AddWithValue("@aid", asset.Id);
                                    cmd.Parameters.AddWithValue("@aname", asset.Name);
                                    cmd.Parameters.AddWithValue("@msg", alarmMsg);
                                    cmd.Parameters.AddWithValue("@now", now);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            else
                            {
                                using (var cmd = conn.CreateCommand())
                                {
                                    cmd.CommandText = "UPDATE alarm_events SET active = 0, cleared_at = @now WHERE asset_id = @id AND active = 1";
                                    cmd.Parameters.AddWithValue("@now", now);
                                    cmd.Parameters.AddWithValue("@id", asset.Id);
                                    cmd.ExecuteNonQuery();
                                }
                            }

                            alarmChanged = true;
                            assetChanged = true;
                            Console.WriteLine($"[ALARM] {asset.Name}: {(inAlarm ? alarmMsg : "CLEARED")}");
                        }

                        // 4. Update Protocol Runtimes
                        if (asset.Protocol == "bacnet")
                        {
                            bool writable = asset.ObjectType == "output" || asset.ObjectType == "value";
                            if (assetChanged || alarmChanged || !writable)
                            {
                                _bacnetManager.UpdateValue(asset.Name, asset.CurrentValue, asset.SubType, asset.IsNormallyOpen);
                            }
                        }
                        else if (asset.Protocol == "modbus")
                        {
                            var registerType = (asset.ModbusRegisterType ?? "").ToLowerInvariant();
                            bool writable = registerType == "holding" || registerType == "coil";
                            if (assetChanged || alarmChanged || !writable)
                            {
                                _modbusManager.WriteValue(asset);
                            }
                        }
                        else if (asset.Protocol == "dnp3")
                        {
                            var pointClass = (asset.Dnp3PointClass ?? "").ToLowerInvariant();
                            bool writable = pointClass == "analog_output" || pointClass == "binary_output";
                            if (assetChanged || alarmChanged || !writable)
                            {
                                _dnp3Manager.WriteValue(asset);
                            }
                        }

                        if (assetChanged)
                            anyGlobalChange = true;
                    }

                    // if (anyGlobalChange) _wsManager.Broadcast(assets);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Engine Error] {ex.Message}");
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
