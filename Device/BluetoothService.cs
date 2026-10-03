using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using SpigenAudioCTRL.Infrastructure;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace SpigenAudioCTRL.Device
{
    public enum ConnectionState { Connecting, Connected, Disconnected }

    public enum CommandResult
    {
        Ok,
        Rejected,     // the headset answered with a non-zero status
        NoResponse,   // written, but no answer within the timeout
        WriteFailed,
        NotConnected
    }

    public sealed class BluetoothService
    {
        // The P10 exposes RCSP on vendor service A002 (0001 write / 0002 notify).
        // Other Jieli firmware uses AE00 (AE01 / AE02).
        private static readonly (Guid Service, Guid Write, Guid Notify)[] RcspEndpoints =
        {
            (Uuid16(0xA002), Uuid16(0x0001), Uuid16(0x0002)),
            (Uuid16(0xAE00), Uuid16(0xAE01), Uuid16(0xAE02)),
        };

        private const int ResponseTimeoutMs = 2500;
        private const int ScanTimeoutMs = 6000;

        private BluetoothLEDevice? _device;
        private GattSession? _gattSession;
        private GattDeviceService? _service;
        private GattCharacteristic? _writeChar;
        private GattCharacteristic? _notifyChar;
        private ulong? _lastAddress;

        private readonly RcspFrameParser _parser = new();
        private readonly Dictionary<(byte Opcode, byte Sn), TaskCompletionSource<RcspFrame>> _pending = new();
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private int _sn;
        private volatile bool _userDisconnected;
        private int _reconnecting;

        public bool IsConnected =>
            _device != null && _device.ConnectionStatus == BluetoothConnectionStatus.Connected && _writeChar != null;

        public ulong? DeviceAddress => _device?.BluetoothAddress ?? _lastAddress;

        public event Action<ConnectionState>? ConnectionChanged;

        private static Guid Uuid16(ushort value) => new($"0000{value:x4}-0000-1000-8000-00805f9b34fb");

        #region Connection

        // Connects, and keeps retrying in the background until connected or Disconnect() is called.
        public async Task<bool> ConnectAsync(ulong? knownAddress = null)
        {
            _userDisconnected = false;
            if (knownAddress.HasValue) _lastAddress = knownAddress;

            bool connected = await TryConnectAsync();
            if (!connected) ScheduleReconnect();
            return connected;
        }

        public void Disconnect()
        {
            _userDisconnected = true;
            CleanupLink();
            ConnectionChanged?.Invoke(ConnectionState.Disconnected);
            Log.Info("BLE", "Disconnected by user.");
        }

        private async Task<bool> TryConnectAsync()
        {
            await _connectLock.WaitAsync();
            try
            {
                if (IsConnected) return true;
                if (_userDisconnected) return false;

                CleanupLink();
                ConnectionChanged?.Invoke(ConnectionState.Connecting);

                if (_lastAddress.HasValue && await AttachAsync(_lastAddress.Value)) return Connected();

                Log.Info("BLE", "Scanning for the SA-HP P10...");
                ulong? found = await ScanAsync();
                if (found.HasValue && await AttachAsync(found.Value)) return Connected();

                Log.Info("BLE", "Headphones not found. Make sure they are on and in range.");
                ConnectionChanged?.Invoke(ConnectionState.Disconnected);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("BLE", $"Connection failed: {ex.Message}");
                CleanupLink();
                ConnectionChanged?.Invoke(ConnectionState.Disconnected);
                return false;
            }
            finally
            {
                _connectLock.Release();
            }
        }

        private bool Connected()
        {
            _lastAddress = _device!.BluetoothAddress;
            Log.Info("BLE", $"Connected to {_device.Name}.");
            ConnectionChanged?.Invoke(ConnectionState.Connected);
            return true;
        }

        private async Task<bool> AttachAsync(ulong address)
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (device == null) return false;

            _device = device;
            _device.ConnectionStatusChanged += OnConnectionStatusChanged;

            try
            {
                _gattSession = await GattSession.FromDeviceIdAsync(_device.BluetoothDeviceId);
                _gattSession.MaintainConnection = true;
            }
            catch (Exception ex)
            {
                Log.Info("BLE", $"GATT session unavailable: {ex.Message}");
            }

            if (await DiscoverAsync() && await SubscribeAsync()) return true;

            CleanupLink();
            return false;
        }

        private async Task<bool> DiscoverAsync()
        {
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                foreach (var endpoint in RcspEndpoints)
                {
                    foreach (var mode in new[] { BluetoothCacheMode.Uncached, BluetoothCacheMode.Cached })
                    {
                        var services = await _device!.GetGattServicesForUuidAsync(endpoint.Service, mode);
                        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) continue;

                        var service = services.Services[0];
                        var chars = await service.GetCharacteristicsAsync(mode);
                        if (chars.Status != GattCommunicationStatus.Success)
                        {
                            service.Dispose();
                            continue;
                        }

                        GattCharacteristic? write = null, notify = null;
                        foreach (var c in chars.Characteristics)
                        {
                            if (c.Uuid == endpoint.Write) write = c;
                            else if (c.Uuid == endpoint.Notify) notify = c;
                        }

                        if (write != null && notify != null)
                        {
                            _service = service;
                            _writeChar = write;
                            _notifyChar = notify;
                            return true;
                        }
                        service.Dispose();
                    }
                }

                Log.Info("BLE", $"Control service not ready, retrying ({attempt}/4)...");
                await Task.Delay(350 * attempt);
            }

            Log.Info("BLE", "Control service not found on this device.");
            return false;
        }

        private async Task<bool> SubscribeAsync()
        {
            if (_notifyChar == null) return false;
            _notifyChar.ValueChanged -= OnNotification;
            _notifyChar.ValueChanged += OnNotification;
            var status = await _notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (status != GattCommunicationStatus.Success)
            {
                Log.Info("BLE", $"Notification subscription failed: {status}");
                return false;
            }
            return true;
        }

        private async Task<ulong?> ScanAsync()
        {
            var found = new TaskCompletionSource<ulong?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            watcher.Received += (_, args) =>
            {
                // The P10 advertises "SA-HP P10-APP" on its control endpoint.
                string name = args.Advertisement.LocalName?.ToUpperInvariant() ?? "";
                if (name.StartsWith("SA-HP") || name.Contains("SPIGEN")) found.TrySetResult(args.BluetoothAddress);
            };

            watcher.Start();
            var completed = await Task.WhenAny(found.Task, Task.Delay(ScanTimeoutMs));
            watcher.Stop();
            return completed == found.Task ? found.Task.Result : null;
        }

        private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender != _device) return;

            if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected)
            {
                Log.Info("BLE", "Link restored.");
                _ = RestoreSubscriptionAsync();
            }
            else
            {
                Log.Info("BLE", "Link lost.");
                FailPending();
                ConnectionChanged?.Invoke(ConnectionState.Disconnected);
                ScheduleReconnect();
            }
        }

        private async Task RestoreSubscriptionAsync()
        {
            try
            {
                if (await SubscribeAsync()) ConnectionChanged?.Invoke(ConnectionState.Connected);
            }
            catch (Exception ex)
            {
                Log.Info("BLE", $"Resubscribe failed: {ex.Message}");
            }
        }

        private void ScheduleReconnect()
        {
            if (_userDisconnected || Interlocked.Exchange(ref _reconnecting, 1) == 1) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    int delaySeconds = 5;
                    while (!_userDisconnected)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                        // Windows may restore the link by itself (MaintainConnection).
                        if (_userDisconnected || IsConnected) break;
                        if (await TryConnectAsync()) break;
                        delaySeconds = Math.Min(delaySeconds * 2, 60);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _reconnecting, 0);
                }
            });
        }

        private void CleanupLink()
        {
            FailPending();

            if (_notifyChar != null)
            {
                try { _notifyChar.ValueChanged -= OnNotification; } catch { }
            }
            if (_device != null)
            {
                try { _device.ConnectionStatusChanged -= OnConnectionStatusChanged; } catch { }
            }

            _writeChar = null;
            _notifyChar = null;
            try { _service?.Dispose(); } catch { }
            _service = null;

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

            try { _device?.Dispose(); } catch { }
            _device = null;
        }

        #endregion

        #region Transport

        private byte NextSn() => (byte)Interlocked.Increment(ref _sn);

        private void OnNotification(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                byte[] data = args.CharacteristicValue.ToArray();
                Log.Info("RX", Convert.ToHexString(data));

                List<RcspFrame> frames;
                lock (_parser) frames = _parser.Push(data);

                // Only responses are handled; the headset's own unsolicited commands are ignored.
                foreach (var frame in frames.Where(f => !f.IsCommand))
                {
                    TaskCompletionSource<RcspFrame>? waiter;
                    lock (_pending) _pending.TryGetValue((frame.Opcode, frame.Sequence), out waiter);
                    waiter?.TrySetResult(frame);
                }
            }
            catch (Exception ex)
            {
                Log.Error("BLE", $"Notification processing error: {ex.Message}");
            }
        }

        private void FailPending()
        {
            lock (_pending)
            {
                foreach (var waiter in _pending.Values) waiter.TrySetCanceled();
                _pending.Clear();
            }
        }

        private async Task<(CommandResult Result, RcspFrame? Frame)> SendAsync(byte opcode, Func<byte, byte[]> build)
        {
            if (!IsConnected) return (CommandResult.NotConnected, null);

            byte sn = NextSn();
            var waiter = new TaskCompletionSource<RcspFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending) _pending[(opcode, sn)] = waiter;

            try
            {
                if (!await WriteAsync(build(sn))) return (CommandResult.WriteFailed, null);

                var completed = await Task.WhenAny(waiter.Task, Task.Delay(ResponseTimeoutMs));
                if (completed != waiter.Task || !waiter.Task.IsCompletedSuccessfully) return (CommandResult.NoResponse, null);

                var frame = waiter.Task.Result;
                return (frame.Status == 0 ? CommandResult.Ok : CommandResult.Rejected, frame);
            }
            finally
            {
                lock (_pending) _pending.Remove((opcode, sn));
            }
        }

        private async Task<bool> WriteAsync(byte[] packet)
        {
            await _writeLock.WaitAsync();
            try
            {
                var characteristic = _writeChar;
                if (characteristic == null) return false;

                var option = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                    ? GattWriteOption.WriteWithoutResponse
                    : GattWriteOption.WriteWithResponse;

                // Frames longer than one ATT payload are split; the headset reassembles the stream.
                int chunkSize = packet.Length;
                if (_gattSession != null && _gattSession.MaxPduSize > 3)
                {
                    chunkSize = Math.Max(20, _gattSession.MaxPduSize - 3);
                }

                for (int offset = 0; offset < packet.Length; offset += chunkSize)
                {
                    int length = Math.Min(chunkSize, packet.Length - offset);
                    var result = await characteristic.WriteValueWithResultAsync(packet.AsBuffer(offset, length), option);
                    if (result.Status != GattCommunicationStatus.Success)
                    {
                        Log.Error("BLE", $"Write failed: {result.Status}");
                        return false;
                    }
                }

                Log.Info("TX", Convert.ToHexString(packet));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("BLE", $"Write error: {ex.Message}");
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        #endregion

        #region Commands

        public async Task<AdvInfo?> ReadAdvInfoAsync(uint mask = 0xFFFFFFFF)
        {
            var (result, frame) = await SendAsync(RcspOpcode.GetAdvInfo, sn => RcspProtocol.EncodeGetAdvInfo(mask, sn));
            return result == CommandResult.Ok && frame != null ? RcspProtocol.ParseAdvInfo(frame.Payload) : null;
        }

        public async Task<EqProfile?> ReadEqAsync()
        {
            var data = await ReadCustomAsync(CustomType.Eq);
            return data != null ? RcspProtocol.ParseEq(data) : null;
        }

        public async Task<AncSetting?> ReadAncAsync()
        {
            var data = await ReadCustomAsync(CustomType.Anc);
            return data != null ? RcspProtocol.ParseAnc(data) : null;
        }

        private async Task<byte[]?> ReadCustomAsync(byte type)
        {
            var (result, frame) = await SendAsync(RcspOpcode.Custom, sn => RcspProtocol.EncodeCustomRead(type, sn));
            if (result != CommandResult.Ok || frame == null) return null;

            var reply = RcspProtocol.ParseCustomReply(frame.Payload);
            return reply.HasValue && reply.Value.Type == type ? reply.Value.Data : null;
        }

        public async Task<CommandResult> SetEqAsync(EqProfile profile) =>
            (await SendAsync(RcspOpcode.Custom, sn => RcspProtocol.EncodeEq(profile, sn))).Result;

        public async Task<CommandResult> SetAncAsync(AncSetting setting) =>
            (await SendAsync(RcspOpcode.Custom, sn => RcspProtocol.EncodeAnc(setting, sn))).Result;

        public async Task<CommandResult> SetGamingModeAsync(bool enabled) =>
            (await SendAsync(RcspOpcode.SetAdvInfo, sn => RcspProtocol.EncodeGamingMode(enabled, sn))).Result;

        public async Task<CommandResult> SetKeyMappingAsync(byte key, byte action, byte function) =>
            (await SendAsync(RcspOpcode.SetAdvInfo, sn => RcspProtocol.EncodeKeyMapping(key, action, function, sn))).Result;

        public async Task<CommandResult> SetBalanceAsync(int value) =>
            (await SendAsync(RcspOpcode.SetAdvInfo, sn => RcspProtocol.EncodeBalance(value, sn))).Result;

        public async Task<SleepTimerState?> ReadSleepTimerAsync()
        {
            var data = await ReadCustomAsync(CustomType.SleepTimer);
            return data != null ? RcspProtocol.ParseSleepTimer(data) : null;
        }

        public async Task<CommandResult> SetSleepTimerAsync(int minutes) =>
            (await SendAsync(RcspOpcode.Custom, sn => RcspProtocol.EncodeSleepTimer(minutes, sn))).Result;

        public async Task<CommandResult> RunSystemOperationAsync(byte operation) =>
            (await SendAsync(RcspOpcode.SystemOperation, sn => RcspProtocol.EncodeSystemOperation(operation, sn))).Result;

        #endregion
    }
}
