#nullable disable
using System;
using System.Text.Json.Serialization;

namespace OLRTLabSim.Models
{
    public class Bbmd
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long Port { get; set; }
        public long DeviceId { get; set; }
        public string IpAddress { get; set; }
        public long Enabled { get; set; }
        public double CreatedAt { get; set; }
    }

    public class Asset
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string SubType { get; set; }
        public string Protocol { get; set; }
        public long Address { get; set; }
        public double MinRange { get; set; }
        public double MaxRange { get; set; }
        public double CurrentValue { get; set; }
        public double DriftRate { get; set; }
        public long ManualOverride { get; set; }
        public string Icon { get; set; }
        public string Filename { get; set; }
        public long BacnetPort { get; set; }
        public long BacnetDeviceId { get; set; }
        public long IsNormallyOpen { get; set; }
        public double ChangeProbability { get; set; }
        public long ChangeInterval { get; set; }
        public double LastFlipCheck { get; set; }
        public long? BbmdId { get; set; }
        public string ObjectType { get; set; }
        public string BacnetProperties { get; set; }

        public long ModbusUnitId { get; set; }
        public string ModbusRegisterType { get; set; }
        public string ModbusIp { get; set; }
        public long ModbusPort { get; set; }
        public long? ModbusAlarmAddress { get; set; }
        public long ModbusAlarmBit { get; set; }
        public long ModbusZeroBased { get; set; }
        public string ModbusWordOrder { get; set; }

        [JsonPropertyName("dnp3_ip")]
        public string Dnp3Ip { get; set; }
        [JsonPropertyName("dnp3_port")]
        public long Dnp3Port { get; set; }
        [JsonPropertyName("dnp3_outstation_address")]
        public long Dnp3OutstationAddress { get; set; }
        [JsonPropertyName("dnp3_master_address")]
        public long Dnp3MasterAddress { get; set; }
        [JsonPropertyName("dnp3_point_class")]
        public string Dnp3PointClass { get; set; }
        [JsonPropertyName("dnp3_event_class")]
        public long Dnp3EventClass { get; set; }
        [JsonPropertyName("dnp3_static_variation")]
        public long Dnp3StaticVariation { get; set; }

        public long AlarmState { get; set; }
        public string AlarmMessage { get; set; }

        public string Dnp3KepwareAddress { get; set; }
    }

    public class AlarmEvent
    {
        public long Id { get; set; }
        public long AssetId { get; set; }
        public string AssetName { get; set; }
        public string Message { get; set; }
        public long Active { get; set; }
        public double CreatedAt { get; set; }
        public double? ClearedAt { get; set; }
    }
}
