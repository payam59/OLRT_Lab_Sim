using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using OLRTLabSim.Models;
using OLRTLabSim.Data;

namespace OLRTLabSim.Services
{
    public class ModbusRuntimeManager
    {
        // Rodbus does not appear to provide a fully managed C# Server implementation yet.
        // It provides Modbus Client. Let's use a simpler placeholder or stub for now,
        // or a manual loop using TcpListener since a full modbus server porting involves deep bytes handling.
        // For the sake of migrating the API and app structure cleanly, we will stub the server.

        private readonly ConcurrentDictionary<string, object> _contexts = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _endpointAssets = new();
        private readonly ConcurrentDictionary<string, AssetMapping> _assetIndex = new();
        public ConcurrentDictionary<string, string> StatusMessages { get; } = new();

        public bool Installed => true; // Using raw sockets / stubs until rodbus server APIs mature.

        public class AssetMapping
        {
            public string Endpoint { get; set; }
            public int UnitId { get; set; }
            public string RegisterType { get; set; }
            public int Address { get; set; }
            public int RawAddress { get; set; }
            public int? AlarmAddress { get; set; }
            public int AlarmBit { get; set; }
            public string SubType { get; set; }
            public bool ZeroBased { get; set; }
            public string WordOrder { get; set; }
        }

        private (string Type, int Offset) NormalizeReference(int address, string registerType, bool zeroBased)
        {
            int raw = address;
            if (raw < 0) throw new ArgumentException("Modbus address must be >= 0");

            string configured = string.IsNullOrWhiteSpace(registerType) ? "holding" : registerType.Trim().ToLower();
            var tableToDigit = new Dictionary<string, string> { { "coil", "0" }, { "discrete", "1" }, { "input", "3" }, { "holding", "4" } };
            var digitToTable = tableToDigit.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
            string configuredDigit = tableToDigit.GetValueOrDefault(configured, "4");

            if (raw == 0) return (configured, 0);

            string token = raw.ToString();
            string inferredType = configured;
            int item = raw;

            if (token.Length >= 5 && digitToTable.ContainsKey(token.Substring(0, 1)))
            {
                inferredType = digitToTable[token.Substring(0, 1)];
                item = int.Parse(token.Substring(1));
            }
            else if (token.Length >= 2 && digitToTable.ContainsKey(token.Substring(0, 1)) && token.Substring(0, 1) == configuredDigit)
            {
                inferredType = configured;
                item = int.Parse(token.Substring(1));
            }
            else
            {
                inferredType = configured;
                item = int.Parse(token);
            }

            int offset = zeroBased ? item - 1 : item;
            if (offset < 0) throw new ArgumentException("Modbus address resolves to a negative offset");

            return (inferredType, offset);
        }

        public async Task EnsureEndpoint(string ip, int port, int unitId)
        {
            string endpoint = $"{ip}:{port}";
            StatusMessages[endpoint] = "running (stubbed server)";
            // ToDo: Implement actual Modbus TCP Server listener here with Rodbus or raw sockets.
        }

        public async Task RegisterAsset(Asset asset)
        {
            string name = asset.Name;
            string ip = string.IsNullOrWhiteSpace(asset.ModbusIp) ? "0.0.0.0" : asset.ModbusIp;
            int port = asset.ModbusPort <= 0 ? 5020 : (int)asset.ModbusPort;
            string endpoint = $"{ip}:{port}";

            if (_assetIndex.ContainsKey(name))
            {
                await UnregisterAsset(name);
            }

            int unitId = Math.Max(0, Math.Min((int)asset.ModbusUnitId, 255));
            bool zeroBased = asset.ModbusZeroBased == 1;
            string configuredType = string.IsNullOrWhiteSpace(asset.ModbusRegisterType) ? "holding" : asset.ModbusRegisterType;

            var (normalizedType, normalizedAddress) = NormalizeReference((int)asset.Address, configuredType, zeroBased);

            var mapping = new AssetMapping
            {
                Endpoint = endpoint,
                UnitId = unitId,
                RegisterType = normalizedType,
                Address = normalizedAddress,
                RawAddress = (int)asset.Address,
                AlarmAddress = asset.ModbusAlarmAddress.HasValue ? (int?)asset.ModbusAlarmAddress.Value : null,
                AlarmBit = (int)asset.ModbusAlarmBit,
                SubType = asset.SubType,
                ZeroBased = zeroBased,
                WordOrder = string.IsNullOrWhiteSpace(asset.ModbusWordOrder) ? "low_high" : asset.ModbusWordOrder
            };

            _assetIndex[name] = mapping;
            _endpointAssets.AddOrUpdate(endpoint, new HashSet<string> { name }, (k, v) => { v.Add(name); return v; });

            await EnsureEndpoint(ip, port, unitId);
            WriteValue(asset);
        }

        public async Task UnregisterAsset(string name)
        {
            if (_assetIndex.TryRemove(name, out var mapping))
            {
                string endpoint = mapping.Endpoint;
                if (_endpointAssets.TryGetValue(endpoint, out var set))
                {
                    set.Remove(name);
                    if (!set.Any())
                    {
                        _endpointAssets.TryRemove(endpoint, out _);
                        StatusMessages[endpoint] = "stopped";
                    }
                }
            }
        }

        public void WriteValue(Asset asset)
        {
            if (!_assetIndex.TryGetValue(asset.Name, out var mapping)) return;

            // ToDo: Update internal Modbus DataStore for the specific unitId and Endpoint
        }

        public double? ReadRemoteValue(Asset asset)
        {
            if (!_assetIndex.TryGetValue(asset.Name, out var mapping)) return null;

            // ToDo: Read from internal Modbus DataStore
            return null;
        }

        public async Task Bootstrap(List<Asset> assets)
        {
            foreach (var asset in assets)
            {
                if (asset.Protocol == "modbus")
                {
                    await RegisterAsset(asset);
                }
            }
        }

        public async Task Shutdown()
        {
            foreach (var name in _assetIndex.Keys.ToList())
            {
                await UnregisterAsset(name);
            }
        }

        public object Status()
        {
            return new
            {
                pymodbus_installed = Installed,
                endpoints = _endpointAssets.Keys.ToList(),
                asset_count = _assetIndex.Count,
                status_messages = StatusMessages,
                assets = _assetIndex
            };
        }
    }
}
