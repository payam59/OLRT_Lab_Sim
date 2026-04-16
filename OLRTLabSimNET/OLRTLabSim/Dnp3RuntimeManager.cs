using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OLRTLabSim.Models;

namespace OLRTLabSim.Services
{
    public class Dnp3RuntimeManager
    {
        private readonly ConcurrentDictionary<string, TcpListener> _listeners = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _listenerTokens = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _endpointAssets = new();
        private readonly ConcurrentDictionary<string, AssetMapping> _assetIndex = new();
        private readonly ConcurrentDictionary<string, double> _pointValues = new();
        public ConcurrentDictionary<string, string> StatusMessages { get; } = new();

        public bool Installed => true;

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

        private static ushort CrcDnp(ReadOnlySpan<byte> data)
        {
            int crc = 0;
            foreach (var value in data)
            {
                crc ^= value;
                for (var i = 0; i < 8; i++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc = (crc >> 1) ^ 0xA6BC;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            crc = ~crc & 0xFFFF;
            return (ushort)crc;
        }

        private static byte[] BuildLinkResponse(ushort srcOutstation, ushort destMaster, byte function = 0x0B)
        {
            byte ctrl = (byte)(0x80 | (function & 0x0F));
            byte[] header =
            {
                0x05,
                0x64,
                0x05,
                ctrl,
                (byte)(destMaster & 0xFF),
                (byte)((destMaster >> 8) & 0xFF),
                (byte)(srcOutstation & 0xFF),
                (byte)((srcOutstation >> 8) & 0xFF)
            };

            var crc = CrcDnp(header.AsSpan(3, 5));
            return header.Concat(new[] { (byte)(crc & 0xFF), (byte)((crc >> 8) & 0xFF) }).ToArray();
        }

        private async Task HandleClient(TcpClient client, string endpoint, CancellationToken token)
        {
            try
            {
                StatusMessages[endpoint] = "connected";
                using var stream = client.GetStream();
                byte[] buffer = new byte[4096];

                while (!token.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (read <= 0) break;

                    if (read >= 10 && buffer[0] == 0x05 && buffer[1] == 0x64)
                    {
                        var control = buffer[3];
                        var dest = (ushort)(buffer[4] | (buffer[5] << 8));
                        var src = (ushort)(buffer[6] | (buffer[7] << 8));
                        bool isPrimary = (control & 0x40) != 0;
                        byte function = (byte)(control & 0x0F);

                        if (isPrimary)
                        {
                            var response = function == 0x09
                                ? BuildLinkResponse(dest, src, 0x0B)
                                : BuildLinkResponse(dest, src, 0x00);
                            await stream.WriteAsync(response, 0, response.Length, token);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (!token.IsCancellationRequested) StatusMessages[endpoint] = "running";
                client.Close();
            }
        }

        private async Task ListenLoop(TcpListener listener, string endpoint, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleClient(client, endpoint, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    StatusMessages[endpoint] = $"error: {ex.Message}";
                    break;
                }
            }
        }

        public async Task EnsureEndpoint(string ip, int port)
        {
            string endpoint = $"{ip}:{port}";
            if (_listeners.ContainsKey(endpoint))
            {
                StatusMessages[endpoint] = "running";
                return;
            }

            try
            {
                var bindIp = ip == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(ip);
                var listener = new TcpListener(bindIp, port);
                listener.Start();

                var cts = new CancellationTokenSource();
                _listeners[endpoint] = listener;
                _listenerTokens[endpoint] = cts;
                StatusMessages[endpoint] = "running";

                _ = Task.Run(() => ListenLoop(listener, endpoint, cts.Token), cts.Token);
            }
            catch (Exception ex)
            {
                StatusMessages[endpoint] = $"error: {ex.Message}";
            }
            await Task.CompletedTask;
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
            if (pointClass is "binary_input" or "binary_output")
                val = val >= 0.5 ? 1.0 : 0.0;
            _pointValues[name] = val;

            await EnsureEndpoint(ip, port);
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
                        if (_listenerTokens.TryRemove(endpoint, out var cts))
                        {
                            cts.Cancel();
                            cts.Dispose();
                        }
                        if (_listeners.TryRemove(endpoint, out var listener))
                        {
                            listener.Stop();
                        }
                        StatusMessages[endpoint] = "stopped";
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
            if (pointClass is "binary_input" or "binary_output")
                val = val >= 0.5 ? 1.0 : 0.0;

            _pointValues[asset.Name] = val;
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
        }

        public object Status()
        {
            return new
            {
                dnp3_runtime_ready = Installed,
                transport_mode = "tcp_listener",
                endpoints = _endpointAssets.Keys.ToList(),
                asset_count = _assetIndex.Count,
                status_messages = StatusMessages,
                assets = _assetIndex
            };
        }
    }
}
