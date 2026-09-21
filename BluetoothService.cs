using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace SpigenAudioCTRL
{
    public class BluetoothService
    {
        private BluetoothLEDevice? _device;
        private GattSession? _gattSession;
        private readonly List<GattDeviceService> _services = new();
        private GattCharacteristic? _writeChar;
        private GattCharacteristic? _notifyChar;
        private GattCharacteristic? _batteryChar;
        private byte _snCounter = 1;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public bool IsConnected => _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected && _writeChar != null;
        public string DeviceName => _device?.Name ?? "Spigen SA-HP P10";
        public int? BatteryLevel { get; private set; }
        public HardwareAdvInfo? LastHardwareInfo { get; private set; }

        public event Action<bool, string>? OnConnectionStateChanged;
        public event Action<string, byte[]>? OnNotificationReceived;
        public event Action<HardwareAdvInfo>? OnHardwareSyncReceived;
        public event Action<int>? OnBatteryLevelUpdated;
        public event Action<string, string>? OnLog;

        // Fix #9 (audit): NextSn is internal implementation detail, not part of public API
        private byte NextSn()
        {
            _snCounter = (byte)((_snCounter + 1) % 255);
            if (_snCounter == 0) _snCounter = 1;
            return _snCounter;
        }

        private void Log(string type, string msg)
        {
            Debug.WriteLine($"[{type}] {msg}");
            OnLog?.Invoke(type, msg);
        }

        /// <summary>
        /// Sets the battery level and fires the OnBatteryLevelUpdated event.
        /// Centralizes all battery updates to avoid duplicating event dispatch logic.
        /// </summary>
        private void SetBatteryLevel(int level)
        {
            BatteryLevel = level;
            OnBatteryLevelUpdated?.Invoke(level);
        }

        // Fix #1 (audit): Removed hardcoded MAC address (0x84AC60D74CE9).
        // The app now scans for the device by name. Pass a known address to skip scanning.
        public async Task<bool> ConnectAsync(ulong? targetBluetoothAddress = null)
        {
            await _lock.WaitAsync();
            try
            {
                if (IsConnected)
                {
                    _ = QueryHardwareStateAsync();
                    return true;
                }

                // Try a caller-supplied address first (e.g. from a previous pairing stored in settings)
                if (targetBluetoothAddress.HasValue)
                {
                    Log("BLE", $"Connecting to Bluetooth Address: {targetBluetoothAddress.Value:X12}...");
                    try
                    {
                        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(targetBluetoothAddress.Value);
                    }
                    catch (Exception ex)
                    {
                        Log("BLE", $"Known address lookup error: {ex.Message}");
                    }
                }

                if (_device == null)
                {
                    Log("BLE", "Scanning for nearby Spigen SA-HP P10 headphones...");
                    ulong? discoveredMac = await ScanForSpigenDeviceAsync();
                    if (discoveredMac.HasValue)
                    {
                        Log("BLE", $"Discovered Spigen at {discoveredMac.Value:X12}");
                        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(discoveredMac.Value);
                    }
                }

                if (_device == null)
                {
                    Log("BLE", "Device not found. Ensure headphones are powered on and in Bluetooth range.");
                    OnConnectionStateChanged?.Invoke(false, "Disconnected");
                    return false;
                }

                try
                {
                    _gattSession = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId);
                    if (_gattSession != null)
                    {
                        _gattSession.MaintainConnection = true;
                    }
                }
                catch (Exception ex)
                {
                    // Fix #7 (audit): Log instead of silently swallowing
                    Log("BLE", $"GattSession setup note: {ex.Message}");
                }

                _device.ConnectionStatusChanged += (dev, args) =>
                {
                    bool connected = dev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                    Log("BLE", $"Connection status changed: {dev.ConnectionStatus}");
                    OnConnectionStateChanged?.Invoke(connected, dev.Name);
                    if (connected)
                    {
                        _ = QueryHardwareStateAsync();
                    }
                };

                Log("BLE", $"Connected to {_device.Name}! Discovering GATT services...");

                for (int attempt = 1; attempt <= 4; attempt++)
                {
                    var servicesResult = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                    if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                    {
                        servicesResult = await _device.GetGattServicesAsync(BluetoothCacheMode.Cached);
                    }

                    if (servicesResult.Status == GattCommunicationStatus.Success && servicesResult.Services.Count > 0)
                    {
                        foreach (var s in servicesResult.Services)
                        {
                            if (!_services.Contains(s))
                            {
                                _services.Add(s);
                            }

                            string uuid = s.Uuid.ToString().ToLower();
                            if (uuid.Contains("180f") && _batteryChar == null) // Standard BLE Battery Service
                            {
                                try
                                {
                                    var bChars = await s.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                                    foreach (var bc in bChars.Characteristics)
                                    {
                                        if (bc.Uuid.ToString().ToLower().Contains("2a19"))
                                        {
                                            _batteryChar = bc;
                                            var readRes = await bc.ReadValueAsync(BluetoothCacheMode.Uncached);
                                            if (readRes.Status == GattCommunicationStatus.Success && readRes.Value.Length > 0)
                                            {
                                                SetBatteryLevel(readRes.Value.ToArray()[0]);
                                                Log("BATTERY", $"Standard BLE Battery Level: {BatteryLevel}%");
                                            }
                                            bc.ValueChanged += (sender, args) =>
                                            {
                                                byte[] bytes = args.CharacteristicValue.ToArray();
                                                if (bytes.Length > 0)
                                                {
                                                    SetBatteryLevel(bytes[0]);
                                                    Log("BATTERY", $"Live BLE Battery update: {BatteryLevel}%");
                                                    OnNotificationReceived?.Invoke("BATTERY", bytes);
                                                }
                                            };
                                            await bc.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                                        }
                                    }
                                }
                                catch (Exception bex)
                                {
                                    Log("BATTERY", $"Standard battery service note: {bex.Message}");
                                }
                            }

                            // Query characteristics in service
                            try
                            {
                                var chars = await s.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                                if (chars.Characteristics.Count == 0)
                                {
                                    chars = await s.GetCharacteristicsAsync(BluetoothCacheMode.Cached);
                                }

                                foreach (var c in chars.Characteristics)
                                {
                                    string cuuid = c.Uuid.ToString().ToLower();
                                    if ((cuuid.Contains("ae01") || cuuid.Contains("0001")) &&
                                        (c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) || c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)))
                                    {
                                        _writeChar = c;
                                    }
                                    if ((cuuid.Contains("ae02") || cuuid.Contains("0002")) && c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                                    {
                                        _notifyChar = c;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // Fix #7 (audit): Log instead of silently swallowing
                                Log("BLE", $"Characteristic discovery error in service {s.Uuid}: {ex.Message}");
                            }
                        }
                    }

                    if (_writeChar != null)
                    {
                        break;
                    }

                    Log("BLE", $"GATT table synchronizing, retrying discovery ({attempt}/4)...");
                    await Task.Delay(350 * attempt);
                }

                if (_writeChar == null)
                {
                    Log("BLE", "Write Characteristic missing.");
                    return false;
                }

                if (_notifyChar != null)
                {
                    try
                    {
                        await _notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                        _notifyChar.ValueChanged += OnCharacteristicValueChanged;
                        Log("BLE", "Subscribed to real-time notifications on [0002]!");
                    }
                    catch (Exception nex)
                    {
                        Log("BLE", $"Notification setup note: {nex.Message}");
                    }
                }

                Log("BLE", "Hardware engine successfully attached and ready!");
                OnConnectionStateChanged?.Invoke(true, _device.Name);

                // Initial hardware query sequence
                await Task.Delay(100);
                await QueryHardwareStateAsync();
                return true;
            }
            catch (Exception ex)
            {
                Log("ERR", $"Connection exception: {ex.Message}");
                OnConnectionStateChanged?.Invoke(false, "Error");
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        // Fix #1 (audit): Tightened scanner to only match confirmed Spigen/SA-HP P10 name prefixes.
        // Removed generic "QCY" and "JL_" matches that could incorrectly grab unrelated BLE devices.
        private async Task<ulong?> ScanForSpigenDeviceAsync()
        {
            var tcs = new TaskCompletionSource<ulong?>();
            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            watcher.Received += (sender, args) =>
            {
                string name = args.Advertisement.LocalName?.ToUpperInvariant() ?? "";
                if (name.Contains("SPIGEN") || name.Contains("SA-HP") || name.Contains("P10"))
                {
                    watcher.Stop();
                    tcs.TrySetResult(args.BluetoothAddress);
                }
            };

            watcher.Start();
            var delayTask = Task.Delay(4000);
            var completed = await Task.WhenAny(tcs.Task, delayTask);
            watcher.Stop();

            return (completed == tcs.Task) ? await tcs.Task : null;
        }

        private void OnCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                byte[] data = args.CharacteristicValue.ToArray();
                string hex = BitConverter.ToString(data).Replace("-", "");
                Log("RX", hex);

                OnNotificationReceived?.Invoke(hex, data);

                // Parse 0x03 Target Info / Battery
                if (hex.StartsWith("FEDCBA0003") || hex.StartsWith("FEDCBA8003") || hex.StartsWith("FEDCBA0103") || hex.StartsWith("FEDCBA0203"))
                {
                    if (data.Length >= 10)
                    {
                        int ptr = 8;
                        while (ptr + 2 < data.Length - 1)
                        {
                            int tagLen = data[ptr];
                            if (tagLen <= 0 || ptr + 1 >= data.Length) break;
                            byte tagType = data[ptr + 1];
                            int valLen = tagLen - 1;
                            int valStart = ptr + 2;
                            if (valStart + valLen > data.Length) break;

                            if (tagType == 1 && valLen >= 1)
                            {
                                int b = data[valStart];
                                if (b > 0 && b <= 100)
                                {
                                    SetBatteryLevel(b);
                                    Log("BATTERY", $"Parsed Target Info Battery: {BatteryLevel}%");
                                }
                            }
                            ptr += (tagLen + 1);
                        }
                    }
                }

                // Parse 0xC1 ADV Info (Device Name, Key Settings, Gaming Mode, Firmware)
                if (hex.StartsWith("FEDCBA00C1") || hex.StartsWith("FEDCBA80C1"))
                {
                    var info = RcspProtocol.ParseHardwareAdvInfo(data);
                    LastHardwareInfo = info;
                    OnHardwareSyncReceived?.Invoke(info);
                }
            }
            catch (Exception ex)
            {
                Log("ERR", $"Notification processing error: {ex.Message}");
            }
        }

        public async Task<bool> SendPacketAsync(byte[] pkt)
        {
            if (_writeChar == null) return false;

            try
            {
                var buffer = pkt.AsBuffer();
                var writeOption = _writeChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                    ? GattWriteOption.WriteWithoutResponse
                    : GattWriteOption.WriteWithResponse;

                var result = await _writeChar.WriteValueWithResultAsync(buffer, writeOption);
                Log("TX", BitConverter.ToString(pkt).Replace("-", ""));
                return result.Status == GattCommunicationStatus.Success;
            }
            catch (Exception ex)
            {
                Log("ERR", $"Write error: {ex.Message}");
                return false;
            }
        }

        public async Task SetAncModeAsync(int ancId)
        {
            byte[] pkt = RcspProtocol.EncodeAncCommand(ancId, NextSn());
            await SendPacketAsync(pkt);
        }

        public async Task SetEqualizerAsync(EqBand[] bands, float masterGain = 0.0f)
        {
            byte[] pkt = RcspProtocol.EncodeEqCommand(bands, 126, masterGain, NextSn());
            await SendPacketAsync(pkt);
        }

        public async Task SetEqualizerAsync(float[] gains, byte mode = 126)
        {
            float masterGain = RcspProtocol.CalculateAntiClippingPreAmp(gains);
            byte[] pkt = RcspProtocol.EncodeEqCommand(gains, mode, masterGain, NextSn());
            await SendPacketAsync(pkt);
        }

        public async Task SetGamingModeAsync(bool enabled)
        {
            byte[] pkt = RcspProtocol.EncodeGamingMode(enabled, NextSn());
            await SendPacketAsync(pkt);
        }

        public async Task SetKeyMappingAsync(int keyNum, int action, int func)
        {
            byte[] pkt = RcspProtocol.EncodeKeyMapping(keyNum, action, func, NextSn());
            await SendPacketAsync(pkt);
        }

        public async Task QueryHardwareStateAsync()
        {
            // Query all target info attributes (mask -1 / 0xFFFFFFFF)
            byte[] targetInfo = RcspProtocol.PackRcsp(0x03, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00 }, NextSn());
            await SendPacketAsync(targetInfo);
            await Task.Delay(80);
            byte[] pkt = RcspProtocol.PackRcsp(0xC1, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, NextSn());
            await SendPacketAsync(pkt);
        }

        public void Disconnect()
        {
            try
            {
                if (_notifyChar != null)
                {
                    try { _notifyChar.ValueChanged -= OnCharacteristicValueChanged; } catch { }
                }

                _writeChar = null;
                _notifyChar = null;
                _batteryChar = null;
                BatteryLevel = null;

                foreach (var s in _services)
                {
                    try { s.Dispose(); } catch { }
                }
                _services.Clear();

                if (_gattSession != null)
                {
                    try
                    {
                        _gattSession.MaintainConnection = false;
                        _gattSession.Dispose();
                    }
                    catch { }
                    _gattSession = null;
                }

                _device?.Dispose();
                _device = null;

                // Fix #2 (audit): Removed GC.Collect() / GC.WaitForPendingFinalizers().
                // WinRT COM objects are reference-counted; Dispose() above is sufficient.
                // Forcing GC causes unnecessary pauses and does not reliably accelerate BLE teardown.

                OnConnectionStateChanged?.Invoke(false, "Disconnected");
                Log("BLE", "Disconnected and released Bluetooth session.");
            }
            catch (Exception ex)
            {
                Log("BLE", $"Disconnect note: {ex.Message}");
            }
        }
    }
}
