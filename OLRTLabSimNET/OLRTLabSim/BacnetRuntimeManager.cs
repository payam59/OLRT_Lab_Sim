using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO.BACnet;
using OLRTLabSim.Models;
using OLRTLabSim.Data;

namespace OLRTLabSim.Services
{
    public class BacnetRuntimeManager
    {
        private readonly ConcurrentDictionary<long, BacnetClient> _bbmdLifecycles = new();
        private readonly ConcurrentDictionary<long, object> _bbmdStatus = new();
        private readonly ConcurrentDictionary<string, BacnetObject> _objectIndex = new();

        public bool Installed => true; // C# library loaded

        public class BacnetObject
        {
            public string Name { get; set; }
            public uint Instance { get; set; }
            public BacnetObjectTypes ObjectType { get; set; }
            public double PresentValue { get; set; }
            public long BbmdId { get; set; }
        }

        private async Task RebuildBbmdLifecycle(Bbmd bbmd)
        {
            if (_bbmdLifecycles.ContainsKey(bbmd.Id))
                await StopBbmd(bbmd.Id);

            if (bbmd.Enabled == 0) return;

            string ipAddress = string.IsNullOrWhiteSpace(bbmd.IpAddress) || bbmd.IpAddress == "0.0.0.0" ? "0.0.0.0" : bbmd.IpAddress;

            try
            {
                var transport = new BacnetIpUdpProtocolTransport((int)bbmd.Port, false, false, 1476, ipAddress);
                var client = new BacnetClient(transport);

                client.OnReadPropertyRequest += Client_OnReadPropertyRequest;
                client.OnWritePropertyRequest += Client_OnWritePropertyRequest;

                client.Start();

                _bbmdLifecycles[bbmd.Id] = client;
                _bbmdStatus[bbmd.Id] = new { running = true, message = $"Listening on UDP {ipAddress}:{bbmd.Port}" };
            }
            catch (SocketException ex)
            {
                _bbmdStatus[bbmd.Id] = new { running = false, message = $"Port {bbmd.Port} in use: {ex.Message}" };
            }
            catch (Exception ex)
            {
                _bbmdStatus[bbmd.Id] = new { running = false, message = $"Failed to start: {ex.Message}" };
            }
        }

        private void Client_OnWritePropertyRequest(BacnetClient sender, BacnetAddress adr, byte invokeId, BacnetObjectId objectId, BacnetPropertyValue value, BacnetMaxSegments maxSegments)
        {
            // Simple mapping - find object by id
            var obj = _objectIndex.Values.FirstOrDefault(o => o.Instance == objectId.Instance && o.ObjectType == objectId.Type);

            if (obj != null && value.property.propertyIdentifier == (uint)BacnetPropertyIds.PROP_PRESENT_VALUE)
            {
                if (value.value != null && value.value.Any())
                {
                    obj.PresentValue = Convert.ToDouble(value.value[0].Value);
                    sender.SimpleAckResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY, invokeId);
                    return;
                }
            }

            sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_WRITE_PROPERTY, invokeId, BacnetErrorClasses.ERROR_CLASS_PROPERTY, BacnetErrorCodes.ERROR_CODE_UNKNOWN_PROPERTY);
        }

        private void Client_OnReadPropertyRequest(BacnetClient sender, BacnetAddress adr, byte invokeId, BacnetObjectId objectId, BacnetPropertyReference property, BacnetMaxSegments maxSegments)
        {
            var obj = _objectIndex.Values.FirstOrDefault(o => o.Instance == objectId.Instance && o.ObjectType == objectId.Type);

            if (obj != null && property.propertyIdentifier == (uint)BacnetPropertyIds.PROP_PRESENT_VALUE)
            {
                var val = new List<BacnetValue> { new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, (float)obj.PresentValue) };
                sender.ReadPropertyResponse(adr, invokeId, sender.GetSegmentBuffer(maxSegments), objectId, property, val);
                return;
            }

            sender.ErrorResponse(adr, BacnetConfirmedServices.SERVICE_CONFIRMED_READ_PROPERTY, invokeId, BacnetErrorClasses.ERROR_CLASS_PROPERTY, BacnetErrorCodes.ERROR_CODE_UNKNOWN_PROPERTY);
        }

        public async Task StartBbmd(long id)
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM bbmd WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            Bbmd bbmd = null;
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    bbmd = new Bbmd
                    {
                        Id = Convert.ToInt64(reader["id"]),
                        Name = reader["name"].ToString(),
                        Description = reader["description"].ToString(),
                        Port = Convert.ToInt64(reader["port"]),
                        DeviceId = Convert.ToInt64(reader["device_id"]),
                        IpAddress = reader["ip_address"].ToString(),
                        Enabled = Convert.ToInt64(reader["enabled"])
                    };
                }
            }

            if (bbmd != null)
            {
                await RebuildBbmdLifecycle(bbmd);
            }
        }

        public async Task StopBbmd(long id)
        {
            if (_bbmdLifecycles.TryRemove(id, out var client))
            {
                client.Dispose();
                _bbmdStatus[id] = new { running = false, message = "Stopped" };
            }
        }

        public async Task RegisterAsset(Asset asset)
        {
            if (!asset.BbmdId.HasValue) return;

            long bbmdId = asset.BbmdId.Value;
            if (!_bbmdLifecycles.ContainsKey(bbmdId))
                return;

            string objTypeStr = (asset.ObjectType ?? "value").ToLower();
            BacnetObjectTypes objType = BacnetObjectTypes.OBJECT_ANALOG_VALUE;

            if (objTypeStr == "input")
            {
                objType = asset.SubType == "Digital" ? BacnetObjectTypes.OBJECT_BINARY_INPUT : BacnetObjectTypes.OBJECT_ANALOG_INPUT;
            }
            else if (objTypeStr == "output")
            {
                objType = asset.SubType == "Digital" ? BacnetObjectTypes.OBJECT_BINARY_OUTPUT : BacnetObjectTypes.OBJECT_ANALOG_OUTPUT;
            }
            else
            {
                objType = asset.SubType == "Digital" ? BacnetObjectTypes.OBJECT_BINARY_VALUE : BacnetObjectTypes.OBJECT_ANALOG_VALUE;
            }

            double presentVal = asset.CurrentValue;
            if (asset.SubType == "Digital")
            {
                bool isNormallyOpen = asset.IsNormallyOpen == 1;
                double simVal = asset.CurrentValue;
                if (!isNormallyOpen)
                    simVal = simVal >= 0.5 ? 0.0 : 1.0;
                presentVal = simVal >= 0.5 ? 1.0 : 0.0;
            }

            var bacnetObj = new BacnetObject
            {
                Name = asset.Name,
                Instance = (uint)asset.Address,
                ObjectType = objType,
                PresentValue = presentVal,
                BbmdId = bbmdId
            };

            _objectIndex[asset.Name] = bacnetObj;
        }

        public async Task UnregisterAsset(string name)
        {
            _objectIndex.TryRemove(name, out _);
        }

        public void UpdateValue(string name, double currentValue, string subType, long isNormallyOpen = 1)
        {
            if (!_objectIndex.TryGetValue(name, out var obj)) return;

            double presentVal = currentValue;
            if (subType == "Digital")
            {
                bool isNO = isNormallyOpen == 1;
                double simVal = currentValue;
                if (!isNO)
                    simVal = simVal >= 0.5 ? 0.0 : 1.0;
                presentVal = simVal >= 0.5 ? 1.0 : 0.0;
            }

            obj.PresentValue = presentVal;
        }

        public double? GetValue(string name)
        {
            if (_objectIndex.TryGetValue(name, out var obj))
            {
                return obj.PresentValue;
            }
            return null;
        }

        public async Task Bootstrap(List<Asset> assets, List<Bbmd> bbmds)
        {
            foreach (var bbmd in bbmds)
            {
                await StartBbmd(bbmd.Id);
            }

            foreach (var asset in assets)
            {
                if (asset.Protocol == "bacnet")
                {
                    await RegisterAsset(asset);
                }
            }
        }

        public async Task Shutdown()
        {
            foreach (var bbmd in _bbmdLifecycles.Keys.ToList())
            {
                await StopBbmd(bbmd);
            }
            _objectIndex.Clear();
        }

        public object Status()
        {
            return new
            {
                bacnet_installed = Installed,
                bbmd_status = _bbmdStatus,
                object_count = _objectIndex.Count
            };
        }
    }
}
