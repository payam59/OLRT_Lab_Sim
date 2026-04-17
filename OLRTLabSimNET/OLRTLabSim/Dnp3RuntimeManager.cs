using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using dnp3;
using dnp3.functional;
using OLRTLabSim.Models;

namespace OLRTLabSim.Services
{
    public class Dnp3RuntimeManager
    {
        private readonly ConcurrentDictionary<string, ServerContext> _servers = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _endpointAssets = new();
        private readonly ConcurrentDictionary<string, AssetMapping> _assetIndex = new();
        private readonly ConcurrentDictionary<string, double> _pointValues = new();
        public ConcurrentDictionary<string, string> StatusMessages { get; } = new();

        private readonly Runtime _runtime;

        public bool Installed => true;

        private sealed class ServerContext
        {
            public required OutstationServer Server { get; init; }
            public required Outstation Outstation { get; init; }
            public required ControlHandlerImpl ControlHandler { get; init; }
            public required string Endpoint { get; init; }
            public required ushort OutstationAddress { get; init; }
            public required ushort MasterAddress { get; init; }
        }

        public class AssetMapping
        {
            public string Endpoint { get; set; }
            public string PointClass { get; set; }
            public ushort PointIndex { get; set; }
            public int Group { get; set; }
            public int Variation { get; set; }
            public bool Writable { get; set; }
            public string DbName { get; set; }
            public ushort OutstationAddress { get; set; }
            public ushort MasterAddress { get; set; }
            public string KepwareAddress { get; set; }
        }

        private static readonly Dictionary<string, (int group, int variation, bool writable, string db)> Profiles = new()
        {
            { "device_attributes", (0, 254, false, "DeviceAttributes") },
            { "binary_input", (1, 1, false, "Binary") },
            { "double_bit_input", (3, 2, false, "DoubleBitBinary") },
            { "binary_output", (10, 2, true, "BinaryOutputStatus") },
            { "binary_output_command", (12, 1, true, "BinaryOutputStatus") },
            { "counter", (20, 1, false, "Counter") },
            { "frozen_counter", (21, 1, false, "FrozenCounter") },
            { "analog_input", (30, 5, false, "Analog") },
            { "analog_input_deadband", (34, 1, false, "AnalogInputDeadband") },
            { "analog_output", (40, 4, true, "AnalogOutputStatus") },
            { "analog_output_command", (41, 2, true, "AnalogOutputStatus") },
            { "time_and_date", (50, 1, false, "TimeAndDate") },
            { "class_poll_data_request", (60, 1, true, "ClassPoll") },
            { "file_identifiers", (70, 1, true, "FileIdentifier") },
            { "internal_indications", (80, 1, false, "InternalIndication") },
            { "data_sets", (87, 1, true, "DataSet") },
            { "octet_string", (110, 0, true, "OctetString") },
            { "authentication", (120, 1, true, "Authentication") }
        };

        public Dnp3RuntimeManager()
        {
            _runtime = new Runtime(new RuntimeConfig { NumCoreThreads = 0 });
        }

        private static ulong UnixMillisNow()
        {
            return (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static Timestamp NowTimestamp()
        {
            return Timestamp.SynchronizedTimestamp(UnixMillisNow());
        }

        private static Flags OnlineFlags()
        {
            return new Flags(0x01);
        }

        private sealed class OutstationAppImpl : IOutstationApplication
        {
            public ushort GetProcessingDelayMs() => 0;
            public WriteTimeResult WriteAbsoluteTime(ulong time) => WriteTimeResult.Ok;
            public ApplicationIin GetApplicationIin() => new();
            public RestartDelay ColdRestart() => RestartDelay.NotSupported();
            public RestartDelay WarmRestart() => RestartDelay.NotSupported();

            public FreezeResult FreezeCountersAll(FreezeType freezeType, DatabaseHandle database) => FreezeResult.NotSupported;
            public FreezeResult FreezeCountersAllAtTime(DatabaseHandle databaseHandle, ulong time, uint interval) => FreezeResult.NotSupported;
            public FreezeResult FreezeCountersRange(ushort start, ushort stop, FreezeType freezeType, DatabaseHandle database) => FreezeResult.NotSupported;
            public FreezeResult FreezeCountersRangeAtTime(ushort start, ushort stop, DatabaseHandle databaseHandle, ulong time, uint interval) => FreezeResult.NotSupported;
            public bool SupportWriteAnalogDeadBands() => false;
            public void BeginWriteAnalogDeadBands() { }
            public void WriteAnalogDeadBand(ushort index, double deadBand) { }
            public void EndWriteAnalogDeadBands() { }
            public bool WriteStringAttr(byte set, byte variation, StringAttr attrType, string value) => false;
            public bool WriteFloatAttr(byte set, byte variation, FloatAttr attrType, float value) => false;
            public bool WriteDoubleAttr(byte set, byte variation, FloatAttr attrType, double value) => false;
            public bool WriteUintAttr(byte set, byte variation, UintAttr attrType, uint value) => false;
            public bool WriteIntAttr(byte set, byte variation, IntAttr attrType, int value) => false;
            public bool WriteOctetStringAttr(byte set, byte variation, OctetStringAttr attrType, ICollection<byte> value) => false;
            public bool WriteBitStringAttr(byte set, byte variation, BitStringAttr attrType, ICollection<byte> value) => false;
            public bool WriteTimeAttr(byte set, byte variation, TimeAttr attrType, ulong value) => false;
            public void BeginConfirm() { }
            public void EventCleared(ulong id) { }
            public void EndConfirm(BufferState state) { }
        }

        private sealed class OutstationInfoImpl : IOutstationInformation
        {
            public void ProcessRequestFromIdle(RequestHeader header) { }
            public void BroadcastReceived(FunctionCode functionCode, BroadcastAction action) { }
            public void EnterSolicitedConfirmWait(byte ecsn) { }
            public void SolicitedConfirmTimeout(byte ecsn) { }
            public void SolicitedConfirmReceived(byte ecsn) { }
            public void SolicitedConfirmWaitNewRequest() { }
            public void WrongSolicitedConfirmSeq(byte ecsn, byte seq) { }
            public void UnexpectedConfirm(bool unsolicited, byte seq) { }
            public void EnterUnsolicitedConfirmWait(byte ecsn) { }
            public void UnsolicitedConfirmTimeout(byte ecsn, bool retry) { }
            public void UnsolicitedConfirmed(byte ecsn) { }
            public void ClearRestartIin() { }
        }

        private sealed class ControlHandlerImpl : IControlHandler
        {
            private readonly ConcurrentDictionary<string, AssetMapping> _assetIndex;
            private readonly ConcurrentDictionary<string, double> _pointValues;
            private readonly string _endpoint;

            public ControlHandlerImpl(ConcurrentDictionary<string, AssetMapping> assetIndex, ConcurrentDictionary<string, double> pointValues, string endpoint)
            {
                _assetIndex = assetIndex;
                _pointValues = pointValues;
                _endpoint = endpoint;
            }

            public void BeginFragment() { }
            public void EndFragment(DatabaseHandle database) { }

            private bool SupportsBinary(ushort index)
            {
                return _assetIndex.Values.Any(m => m.Endpoint == _endpoint && m.PointIndex == index && (m.PointClass == "binary_output" || m.PointClass == "binary_output_command"));
            }

            private bool SupportsAnalog(ushort index)
            {
                return _assetIndex.Values.Any(m => m.Endpoint == _endpoint && m.PointIndex == index &&
                    (m.PointClass == "analog_output" || m.PointClass == "analog_output_command"));
            }

            private void CommitValue(ushort index, double value)
            {
                foreach (var entry in _assetIndex)
                {
                    var mapping = entry.Value;
                    if (mapping.Endpoint != _endpoint || mapping.PointIndex != index) continue;
                    _pointValues[entry.Key] = value;
                }
            }

            private CommandStatus SelectAnalog(ushort index)
            {
                return SupportsAnalog(index) ? CommandStatus.Success : CommandStatus.NotSupported;
            }

            private CommandStatus OperateAnalog(double value, ushort index, DatabaseHandle database)
            {
                if (!SupportsAnalog(index)) return CommandStatus.NotSupported;

                database.Transaction(db => db.UpdateAnalogOutputStatus(
                    new AnalogOutputStatus(index, value, OnlineFlags(), NowTimestamp()),
                    UpdateOptions.DetectEvent()));
                CommitValue(index, value);
                return CommandStatus.Success;
            }

            public CommandStatus SelectG12v1(Group12Var1 control, ushort index, DatabaseHandle database)
            {
                return SupportsBinary(index) ? CommandStatus.Success : CommandStatus.NotSupported;
            }

            public CommandStatus OperateG12v1(Group12Var1 control, ushort index, OperateType opType, DatabaseHandle database)
            {
                if (!SupportsBinary(index)) return CommandStatus.NotSupported;

                var value = control.Code.OpType == OpType.LatchOn || control.Code.OpType == OpType.PulseOn;
                database.Transaction(db => db.UpdateBinaryOutputStatus(
                    new BinaryOutputStatus(index, value, OnlineFlags(), NowTimestamp()),
                    UpdateOptions.DetectEvent()));
                CommitValue(index, value ? 1.0 : 0.0);
                return CommandStatus.Success;
            }

            public CommandStatus SelectG41v1(int control, ushort index, DatabaseHandle database) => SelectAnalog(index);
            public CommandStatus SelectG41v2(short value, ushort index, DatabaseHandle database) => SelectAnalog(index);
            public CommandStatus SelectG41v3(float value, ushort index, DatabaseHandle database) => SelectAnalog(index);
            public CommandStatus SelectG41v4(double value, ushort index, DatabaseHandle database) => SelectAnalog(index);

            public CommandStatus OperateG41v1(int control, ushort index, OperateType opType, DatabaseHandle database) => OperateAnalog(control, index, database);
            public CommandStatus OperateG41v2(short value, ushort index, OperateType opType, DatabaseHandle database) => OperateAnalog(value, index, database);
            public CommandStatus OperateG41v3(float value, ushort index, OperateType opType, DatabaseHandle database) => OperateAnalog(value, index, database);
            public CommandStatus OperateG41v4(double value, ushort index, OperateType opType, DatabaseHandle database) => OperateAnalog(value, index, database);
        }

        public async Task EnsureEndpoint(string ip, int port)
        {
            string endpoint = $"{ip}:{port}";
            if (_servers.ContainsKey(endpoint))
            {
                StatusMessages[endpoint] = "running";
                return;
            }

            if (!_endpointAssets.TryGetValue(endpoint, out var assetsAtEndpoint) || assetsAtEndpoint.Count == 0)
                return;

            AssetMapping firstMapping;
            lock (assetsAtEndpoint)
            {
                firstMapping = _assetIndex[assetsAtEndpoint.First()];
            }

            var mixedAddress = false;
            lock (assetsAtEndpoint)
            {
                foreach (var assetName in assetsAtEndpoint)
                {
                    var mapping = _assetIndex[assetName];
                    if (mapping.MasterAddress != firstMapping.MasterAddress || mapping.OutstationAddress != firstMapping.OutstationAddress)
                    {
                        mixedAddress = true;
                        break;
                    }
                }
            }

            if (mixedAddress)
            {
                StatusMessages[endpoint] = "error: mixed master/outstation addresses on same endpoint";
                return;
            }

            try
            {
                var server = OutstationServer.CreateTcpServer(_runtime, LinkErrorMode.Close, endpoint);
                var controlHandler = new ControlHandlerImpl(_assetIndex, _pointValues, endpoint);

                var outstation = server.AddOutstation(
                    new OutstationConfig(firstMapping.OutstationAddress, firstMapping.MasterAddress, new EventBufferConfig(100, 100, 100, 100, 100, 100, 100, 100)),

                    new OutstationAppImpl(),
                    new OutstationInfoImpl(),
                    controlHandler,
                    ConnectionStateListener.create(state =>
                    {
                        StatusMessages[endpoint] = state == ConnectionState.Connected ? "connected" : "running";
                    }),
                    AddressFilter.Any());

                outstation.Transaction(db => InitializeDatabase(db, endpoint));
                server.Bind();

                _servers[endpoint] = new ServerContext
                {
                    Server = server,
                    Outstation = outstation,
                    ControlHandler = controlHandler,
                    Endpoint = endpoint,
                    OutstationAddress = firstMapping.OutstationAddress,
                    MasterAddress = firstMapping.MasterAddress
                };

                StatusMessages[endpoint] = "running";
            }
            catch (Exception ex)
            {
                StatusMessages[endpoint] = $"error: {ex.Message}";
            }

            await Task.CompletedTask;
        }

        private void InitializeDatabase(Database db, string endpoint)
        {
            var assets = _assetIndex.Where(kv => kv.Value.Endpoint == endpoint).Select(kv => kv).ToList();

            foreach (var entry in assets)
            {
                var name = entry.Key;
                var mapping = entry.Value;
                var index = mapping.PointIndex;

                switch (mapping.PointClass)
                {
                    case "binary_input":
                        db.AddBinaryInput(index, EventClass.Class1, new BinaryInputConfig());
                        db.UpdateBinaryInput(new BinaryInput(index, (_pointValues.GetValueOrDefault(name) >= 0.5), OnlineFlags(), NowTimestamp()), UpdateOptions.DetectEvent());
                        break;
                    case "binary_output":
                    case "binary_output_command":
                        db.AddBinaryOutputStatus(index, EventClass.Class1, new BinaryOutputStatusConfig());
                        db.UpdateBinaryOutputStatus(new BinaryOutputStatus(index, (_pointValues.GetValueOrDefault(name) >= 0.5), OnlineFlags(), NowTimestamp()), UpdateOptions.DetectEvent());
                        break;
                    case "analog_input":
                        db.AddAnalogInput(index, EventClass.Class1, new AnalogInputConfig());
                        db.UpdateAnalogInput(new AnalogInput(index, _pointValues.GetValueOrDefault(name), OnlineFlags(), NowTimestamp()), UpdateOptions.DetectEvent());
                        break;
                    case "analog_output":
                    case "analog_output_command":
                        db.AddAnalogOutputStatus(index, EventClass.Class1, new AnalogOutputStatusConfig());
                        db.UpdateAnalogOutputStatus(new AnalogOutputStatus(index, _pointValues.GetValueOrDefault(name), OnlineFlags(), NowTimestamp()), UpdateOptions.DetectEvent());
                        break;
                }
            }
        }

        public async Task RegisterAsset(Asset asset)
        {
            string name = asset.Name;
            string ip = string.IsNullOrWhiteSpace(asset.Dnp3Ip) ? "0.0.0.0" : asset.Dnp3Ip;
            int port = asset.Dnp3Port <= 0 ? 20000 : (int)asset.Dnp3Port;
            string endpoint = $"{ip}:{port}";

            if (_assetIndex.ContainsKey(name))
            {
                await UnregisterAsset(name);
            }

            string pointClass = string.IsNullOrWhiteSpace(asset.Dnp3PointClass) ? "analog_output" : asset.Dnp3PointClass.Trim().ToLower();
            if (!Profiles.TryGetValue(pointClass, out var profile))
                profile = Profiles["analog_output"];

            ushort pointIndex = (ushort)Math.Max(0, asset.Address);
            ushort outstationAddress = (ushort)(asset.Dnp3OutstationAddress <= 0 ? 10 : asset.Dnp3OutstationAddress);
            ushort masterAddress = (ushort)(asset.Dnp3MasterAddress <= 0 ? 1 : asset.Dnp3MasterAddress);

            var mapping = new AssetMapping
            {
                Endpoint = endpoint,
                PointClass = pointClass,
                PointIndex = pointIndex,
                Group = profile.group,
                Variation = profile.variation,
                Writable = profile.writable,
                DbName = profile.db,
                OutstationAddress = outstationAddress,
                MasterAddress = masterAddress,
                KepwareAddress = $"{profile.group}.{profile.variation}.{pointIndex}.Value"
            };

            _assetIndex[name] = mapping;
            _endpointAssets.AddOrUpdate(endpoint, new HashSet<string> { name }, (k, v) => { lock (v) v.Add(name); return v; });

            double val = asset.CurrentValue;
            if (pointClass is "binary_input" or "binary_output" or "binary_output_command")
                val = val >= 0.5 ? 1.0 : 0.0;
            _pointValues[name] = val;

            if (_servers.ContainsKey(endpoint))
            {
                await RebuildEndpoint(endpoint);
            }
            else
            {
                await EnsureEndpoint(ip, port);
            }
        }

        private async Task RebuildEndpoint(string endpoint)
        {
            if (_servers.TryRemove(endpoint, out var existing))
            {
                try { existing.Server.Shutdown(); } catch { }
            }

            var parts = endpoint.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port)) return;
            await EnsureEndpoint(parts[0], port);
        }

        public async Task UnregisterAsset(string name)
        {
            if (_assetIndex.TryRemove(name, out var mapping))
            {
                _pointValues.TryRemove(name, out _);
                string endpoint = mapping.Endpoint;

                if (_endpointAssets.TryGetValue(endpoint, out var set))
                {
                    lock (set)
                    {
                        set.Remove(name);
                    }

                    if (!set.Any())
                    {
                        _endpointAssets.TryRemove(endpoint, out _);
                        if (_servers.TryRemove(endpoint, out var ctx))
                        {
                            try { ctx.Server.Shutdown(); } catch { }
                        }
                        StatusMessages[endpoint] = "stopped";
                    }
                    else if (_servers.ContainsKey(endpoint))
                    {
                        await RebuildEndpoint(endpoint);
                    }
                }
            }
            await Task.CompletedTask;
        }

        public void WriteValue(Asset asset)
        {
            if (!_assetIndex.TryGetValue(asset.Name, out var mapping)) return;

            string pointClass = mapping.PointClass;
            double val = asset.CurrentValue;
            if (pointClass is "binary_input" or "binary_output" or "binary_output_command")
                val = val >= 0.5 ? 1.0 : 0.0;

            _pointValues[asset.Name] = val;

            if (!_servers.TryGetValue(mapping.Endpoint, out var server)) return;

            try
            {
                server.Outstation.Transaction(db =>
                {
                    if (pointClass is "analog_input")
                    {
                        db.UpdateAnalogInput(new AnalogInput(mapping.PointIndex, val, OnlineFlags(), NowTimestamp()), UpdateOptions.DetectEvent());
                    }
                    else if (pointClass is "analog_output" or "analog_output_command")
                    {
                        db.UpdateAnalogOutputStatus(new AnalogOutputStatus(mapping.PointIndex, val, OnlineFlags(), NowTimestamp()), UpdateOptions.DetectEvent());
                    }
                    else if (pointClass is "binary_input")
                    {
                        db.UpdateBinaryInput(new BinaryInput(mapping.PointIndex, val >= 0.5, OnlineFlags(), NowTimestamp()), UpdateOptions.DetectEvent());
                    }
                    else if (pointClass is "binary_output" or "binary_output_command")
                    {
                        db.UpdateBinaryOutputStatus(new BinaryOutputStatus(mapping.PointIndex, val >= 0.5, OnlineFlags(), NowTimestamp()), UpdateOptions.DetectEvent());
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessages[mapping.Endpoint] = $"error: {ex.Message}";
            }
        }

        public double? ReadRemoteValue(Asset asset)
        {
            if (_pointValues.TryGetValue(asset.Name, out var val)) return val;
            return null;
        }

        public string? GetKepwareAddress(string assetName)
        {
            if (_assetIndex.TryGetValue(assetName, out var mapping)) return mapping.KepwareAddress;
            return null;
        }

        public async Task Bootstrap(List<Asset> assets)
        {
            foreach (var asset in assets)
            {
                if (asset.Protocol == "dnp3")
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

            foreach (var endpoint in _servers.Keys.ToList())
            {
                if (_servers.TryRemove(endpoint, out var ctx))
                {
                    try { ctx.Server.Shutdown(); } catch { }
                }
            }

            try { _runtime.Shutdown(); } catch { }
        }

        public object Status()
        {
            return new
            {
                dnp3_runtime_ready = Installed,
                transport_mode = "native_outstation",
                endpoints = _endpointAssets.Keys.ToList(),
                asset_count = _assetIndex.Count,
                status_messages = StatusMessages,
                assets = _assetIndex
            };
        }
    }
}
