using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using rodbus;
using OLRTLabSim.Models;
using OLRTLabSim.Data;

namespace OLRTLabSim.Services
{

    public class NullWriteHandler : IWriteHandler
    {
        public rodbus.WriteResult WriteSingleCoil(ushort index, bool value, rodbus.Database database) { database.UpdateCoil(index, value); return rodbus.WriteResult.SuccessInit(); }
        public rodbus.WriteResult WriteSingleRegister(ushort index, ushort value, rodbus.Database database) { database.UpdateHoldingRegister(index, value); return rodbus.WriteResult.SuccessInit(); }
        public rodbus.WriteResult WriteMultipleCoils(ushort start, System.Collections.Generic.ICollection<rodbus.BitValue> values, rodbus.Database database) { foreach(var v in values) database.UpdateCoil(v.Index, v.Value); return rodbus.WriteResult.SuccessInit(); }
        public rodbus.WriteResult WriteMultipleRegisters(ushort start, System.Collections.Generic.ICollection<rodbus.RegisterValue> values, rodbus.Database database) { foreach(var v in values) database.UpdateHoldingRegister(v.Index, v.Value); return rodbus.WriteResult.SuccessInit(); }
    }

    public class ModbusRuntimeManager
    {
        private readonly rodbus.Runtime _rodbusRuntime;
        private readonly ConcurrentDictionary<string, rodbus.Server> _tcpServers = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _endpointAssets = new();
        private readonly ConcurrentDictionary<string, HashSet<byte>> _endpointUnitIds = new();
        private readonly ConcurrentDictionary<string, AssetMapping> _assetIndex = new();

        public ConcurrentDictionary<string, string> StatusMessages { get; } = new();

        public bool Installed => true; // using rodbus native

        public ModbusRuntimeManager()
        {
            _rodbusRuntime = new rodbus.Runtime(new rodbus.RuntimeConfig()); // create a thread pool runtime with 2 workers
        }

        public class AssetMapping
        {
            public string Endpoint { get; set; }
            public int UnitId { get; set; }
            public string RegisterType { get; set; }
            public ushort Address { get; set; }
            public int RawAddress { get; set; }
            public int? AlarmAddress { get; set; }
            public int AlarmBit { get; set; }
            public string SubType { get; set; }
            public bool ZeroBased { get; set; }
            public string WordOrder { get; set; }
        }

        private static bool IsDigitalMapping(AssetMapping mapping)
        {
            return string.Equals(mapping.SubType, "Digital", StringComparison.OrdinalIgnoreCase)
                   || mapping.RegisterType == "coil"
                   || mapping.RegisterType == "discrete";
        }

        private (string Type, ushort Offset) NormalizeReference(int address, string registerType, bool zeroBased)
        {
            int raw = address;
            if (raw < 0) throw new ArgumentException("Modbus address must be >= 0");

            string configured = string.IsNullOrWhiteSpace(registerType) ? "holding" : registerType.Trim().ToLower();
            var tableToDigit = new Dictionary<string, string> { { "coil", "0" }, { "discrete", "1" }, { "input", "3" }, { "holding", "4" } };
            var digitToTable = tableToDigit.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

            if (raw == 0) return (configured, 0);

            string token = raw.ToString();
            string inferredType = configured;
            int item = raw;

            // Kepware-style table-prefixed addresses:
            // 41 / 401 / 4001 / 40001 / 400001 are equivalent for holding item 1.
            if (token.Length >= 2 && digitToTable.ContainsKey(token.Substring(0, 1)))
            {
                inferredType = digitToTable[token.Substring(0, 1)];
                item = int.Parse(token.Substring(1));
            }
            else
            {
                inferredType = configured;
                item = int.Parse(token);
            }

            int offset = zeroBased ? item - 1 : item;
            if (offset < 0 || offset > 65535) throw new ArgumentException("Modbus address resolves to an invalid offset");

            return (inferredType, (ushort)offset);
        }

        private async Task RebuildEndpoint(string endpoint, string ip, int port)
        {
            if (_tcpServers.TryRemove(endpoint, out var existing))
            {
                existing.Shutdown();
            }

            try
            {
                var endpointsMap = new rodbus.DeviceMap();
                var unitIds = _assetIndex.Values
                    .Where(m => m.Endpoint == endpoint)
                    .Select(m => (byte)m.UnitId)
                    .Distinct()
                    .ToList();

                if (!unitIds.Any()) unitIds.Add(1);
                _endpointUnitIds[endpoint] = unitIds.ToHashSet();

                foreach (var uid in unitIds)
                {
                    endpointsMap.AddEndpoint(uid, new NullWriteHandler(), db => {});
                }

                string bindIp = ip == "0.0.0.0" ? "0.0.0.0" : ip;
                var server = rodbus.Server.CreateTcp(
                    _rodbusRuntime,
                    bindIp,
                    (ushort)port,
                    rodbus.AddressFilter.Any(),
                    100, // max sessions
                    endpointsMap,
                    rodbus.DecodeLevel.Nothing()
                );

                _tcpServers[endpoint] = server;

                // Pre-populate databases for all existing assets on this endpoint
                foreach (var uid in unitIds)
                {
                    var assetsForUnit = _assetIndex.Values.Where(m => m.Endpoint == endpoint && m.UnitId == uid);
                    server.UpdateDatabase(uid, db =>
                    {
                        foreach (var mapping in assetsForUnit)
                        {
                            if (mapping.RegisterType == "coil") db.AddCoil(mapping.Address, false);
                            else if (mapping.RegisterType == "discrete") db.AddDiscreteInput(mapping.Address, false);
                            else if (mapping.RegisterType == "holding")
                            {
                                db.AddHoldingRegister(mapping.Address, 0);
                                if (!IsDigitalMapping(mapping))
                                    db.AddHoldingRegister((ushort)(mapping.Address + 1), 0);
                            }
                            else if (mapping.RegisterType == "input")
                            {
                                db.AddInputRegister(mapping.Address, 0);
                                if (!IsDigitalMapping(mapping))
                                    db.AddInputRegister((ushort)(mapping.Address + 1), 0);
                            }
                        }
                    });
                }

                StatusMessages[endpoint] = "running";
            }
            catch (Exception ex)
            {
                StatusMessages[endpoint] = $"error: {ex.Message}";
            }
            await Task.CompletedTask;
        }

        public async Task EnsureEndpoint(string ip, int port, int unitId)
        {
            string endpoint = $"{ip}:{port}";
            if (_tcpServers.ContainsKey(endpoint)) return;
            await RebuildEndpoint(endpoint, ip, port);
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

            var isNewUnitForEndpoint = !_endpointUnitIds.TryGetValue(endpoint, out var existingUnits) || !existingUnits.Contains((byte)unitId);

            _assetIndex[name] = mapping;
            _endpointAssets.AddOrUpdate(endpoint, new HashSet<string> { name }, (k, v) => { v.Add(name); return v; });
            _endpointUnitIds.AddOrUpdate(endpoint, new HashSet<byte> { (byte)unitId }, (k, v) => { v.Add((byte)unitId); return v; });

            if (_tcpServers.TryGetValue(endpoint, out var server))
            {
                if (isNewUnitForEndpoint)
                {
                    await RebuildEndpoint(endpoint, ip, port);
                }
                else
                {
                    server.UpdateDatabase((byte)unitId, db =>
                    {
                        if (mapping.RegisterType == "coil") db.AddCoil(mapping.Address, false);
                        else if (mapping.RegisterType == "discrete") db.AddDiscreteInput(mapping.Address, false);
                        else if (mapping.RegisterType == "holding")
                        {
                            db.AddHoldingRegister(mapping.Address, 0);
                            if (!IsDigitalMapping(mapping))
                                db.AddHoldingRegister((ushort)(mapping.Address + 1), 0);
                        }
                        else if (mapping.RegisterType == "input")
                        {
                            db.AddInputRegister(mapping.Address, 0);
                            if (!IsDigitalMapping(mapping))
                                db.AddInputRegister((ushort)(mapping.Address + 1), 0);
                        }
                    });
                }
            }
            else
            {
                await EnsureEndpoint(ip, port, unitId);
            }

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
                        _endpointUnitIds.TryRemove(endpoint, out _);
                        if (_tcpServers.TryRemove(endpoint, out var server))
                        {
                            server.Shutdown();
                        }
                        StatusMessages[endpoint] = "stopped";
                    }
                }
            }
        }

        public void WriteValue(Asset asset)
        {
            if (!_assetIndex.TryGetValue(asset.Name, out var mapping)) return;
            if (!_tcpServers.TryGetValue(mapping.Endpoint, out var server)) return;

            ushort addr = mapping.Address;
            string regType = mapping.RegisterType;
            double value = asset.CurrentValue;

            try
            {
                server.UpdateDatabase((byte)mapping.UnitId, db =>
                {
                    if (regType == "coil" || regType == "discrete")
                    {
                        bool bVal = value >= 0.5;
                        if (regType == "coil") db.UpdateCoil(addr, bVal);
                        else db.UpdateDiscreteInput(addr, bVal);
                    }
                    else
                    {
                        // Float to 2 registers
                        byte[] bytes = BitConverter.GetBytes((float)value);
                        if (BitConverter.IsLittleEndian) Array.Reverse(bytes); // float to Big-endian IEEE-754

                        ushort regHi = (ushort)((bytes[0] << 8) | bytes[1]);
                        ushort regLo = (ushort)((bytes[2] << 8) | bytes[3]);

                        ushort firstWord = mapping.WordOrder == "high_low" ? regHi : regLo;
                        ushort secondWord = mapping.WordOrder == "high_low" ? regLo : regHi;

                        if (regType == "holding")
                        {
                            db.AddHoldingRegister(addr, 0);
                            db.AddHoldingRegister((ushort)(addr + 1), 0);
                            db.UpdateHoldingRegister(addr, firstWord);
                            db.UpdateHoldingRegister((ushort)(addr + 1), secondWord);
                        }
                        else
                        {
                            db.AddInputRegister(addr, 0);
                            db.AddInputRegister((ushort)(addr + 1), 0);
                            db.UpdateInputRegister(addr, firstWord);
                            db.UpdateInputRegister((ushort)(addr + 1), secondWord);
                        }
                    }

                    // Write alarm bit
                    if (mapping.AlarmAddress.HasValue)
                    {
                        ushort alarmAddr = (ushort)mapping.AlarmAddress.Value;
                        int bit = mapping.AlarmBit;
                        bool inAlarm = asset.AlarmState == 1;

                        if (regType == "coil" || regType == "discrete")
                        {
                            if (regType == "coil") db.UpdateCoil(alarmAddr, inAlarm);
                            else db.UpdateDiscreteInput(alarmAddr, inAlarm);
                        }
                        else
                        {
                            ushort existing = regType == "holding" ? db.GetHoldingRegister(alarmAddr) : db.GetInputRegister(alarmAddr);
                            if (inAlarm)
                                existing = (ushort)(existing | (1 << bit));
                            else
                                existing = (ushort)(existing & ~(1 << bit));

                            if (regType == "holding") db.UpdateHoldingRegister(alarmAddr, existing);
                            else db.UpdateInputRegister(alarmAddr, existing);
                        }
                    }
                });
            }
            catch { /* Ignore database misses during concurrency */ }
        }

        public double? ReadRemoteValue(Asset asset)
        {
            if (!_assetIndex.TryGetValue(asset.Name, out var mapping)) return null;
            if (!_tcpServers.TryGetValue(mapping.Endpoint, out var server)) return null;

            ushort addr = mapping.Address;
            string regType = mapping.RegisterType;

            try
            {
                double? result = null;
                // Wait, Rodbus exposes Get methods on Database but requires transaction callback
                // Actually, reading from Database requires passing a callback to UpdateDatabase or we might need a separate mechanism.
                // For now, since `UpdateDatabase` executes synchronously with access to `db`:
                server.UpdateDatabase((byte)mapping.UnitId, db =>
                {
                    if (regType == "coil" || regType == "discrete")
                    {
                        bool bVal = regType == "coil" ? db.GetCoil(addr) : db.GetDiscreteInput(addr);
                        result = bVal ? 1.0 : 0.0;
                    }
                    else
                    {
                        ushort firstWord = regType == "holding" ? db.GetHoldingRegister(addr) : db.GetInputRegister(addr);
                        ushort secondWord = regType == "holding" ? db.GetHoldingRegister((ushort)(addr + 1)) : db.GetInputRegister((ushort)(addr + 1));

                        ushort regHi = mapping.WordOrder == "high_low" ? firstWord : secondWord;
                        ushort regLo = mapping.WordOrder == "high_low" ? secondWord : firstWord;

                        byte[] bytes = new byte[4];
                        bytes[0] = (byte)(regHi >> 8);
                        bytes[1] = (byte)(regHi & 0xFF);
                        bytes[2] = (byte)(regLo >> 8);
                        bytes[3] = (byte)(regLo & 0xFF);

                        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                        result = BitConverter.ToSingle(bytes, 0);
                    }
                });
                return result;
            }
            catch
            {
                return null;
            }
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
                rodbus_installed = Installed,
                endpoints = _endpointAssets.Keys.ToList(),
                asset_count = _assetIndex.Count,
                status_messages = StatusMessages,
                assets = _assetIndex
            };
        }
    }
}
