using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpigenAudioCTRL.Audio;
using SpigenAudioCTRL.Infrastructure;

namespace SpigenAudioCTRL.Device
{
    [Flags]
    public enum HeadsetChange
    {
        None = 0,
        Connection = 1,
        Battery = 2,
        Anc = 4,
        Gaming = 8,
        Eq = 16,
        Keys = 32,
        Balance = 64,
        SleepTimer = 128,
        All = Connection | Battery | Anc | Gaming | Eq | Keys | Balance | SleepTimer
    }

    // Single source of truth for the headset's state, shared by the window and the tray.
    // Every value here was read from, or confirmed by, the headset. Methods are called and
    // events are raised on the UI thread.
    public sealed class HeadsetController
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);
        private static readonly uint PollMask =
            AdvInfoType.Mask(AdvInfoType.Battery, AdvInfoType.WorkMode, AdvInfoType.VolumeBalance);

        private readonly BluetoothService _bt = new();
        private readonly SynchronizationContext _ui;
        private readonly Timer _pollTimer;
        private readonly Dictionary<(byte Key, byte Action), byte> _keys = new();
        private readonly HashSet<(byte Key, byte Action)> _unsupportedKeys = new();
        private bool _syncInProgress;
        private bool _pollInProgress;
        private bool _ancInFlight;
        private bool _gamingInFlight;

        public ConnectionState Connection { get; private set; } = ConnectionState.Disconnected;
        public ulong? DeviceAddress => _bt.DeviceAddress;
        public int? BatteryLevel { get; private set; }
        public bool IsCharging { get; private set; }
        public AncSetting? Anc { get; private set; }
        public bool? GamingMode { get; private set; }
        public IReadOnlyList<EqBand>? Eq { get; private set; }
        public bool KeysKnown { get; private set; }
        public int? Balance { get; private set; }
        public SleepTimerState? SleepTimer { get; private set; }

        public event Action<HeadsetChange>? Changed;
        public event Action<string>? Notice;

        public HeadsetController()
        {
            _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create on the UI thread.");
            _pollTimer = new Timer(_ => _ui.Post(__ => _ = PollAsync(), null));
            _bt.ConnectionChanged += state => _ui.Post(_ => OnConnectionChanged(state), null);
        }

        public bool IsConnected => Connection == ConnectionState.Connected;

        public Task ConnectAsync(ulong? knownAddress) => _bt.ConnectAsync(knownAddress);

        public void Disconnect() => _bt.Disconnect();

        public void Notify(string message) => Notice?.Invoke(message);

        // Gesture -> function code, or null when the headset reported it unsupported or unknown.
        public byte? GetKeyFunction((byte Key, byte Action) slot) => _keys.TryGetValue(slot, out var code) ? code : null;

        public bool IsKeySupported((byte Key, byte Action) slot) => KeysKnown && !_unsupportedKeys.Contains(slot);

        private void Raise(HeadsetChange change) => Changed?.Invoke(change);

        private static string FailureMessage(CommandResult result, string what) => result switch
        {
            CommandResult.NotConnected => $"Not connected: the {what} was not changed.",
            CommandResult.Rejected => $"The headset rejected the {what} change.",
            CommandResult.WriteFailed => $"Couldn't send the {what} change.",
            _ => $"The headset didn't confirm the {what} change."
        };

        #region Connection & sync

        private void OnConnectionChanged(ConnectionState state)
        {
            Connection = state;
            if (state == ConnectionState.Connected)
            {
                _pollTimer.Change(PollInterval, PollInterval);
                _ = SyncAllAsync();
            }
            else
            {
                _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
                BatteryLevel = null;
            }
            Raise(HeadsetChange.Connection | HeadsetChange.Battery);
        }

        public async Task SyncAllAsync()
        {
            if (_syncInProgress) return;
            _syncInProgress = true;
            try
            {
                var adv = await _bt.ReadAdvInfoAsync();
                if (adv != null) ApplyAdvInfo(adv, includeKeys: true);

                var anc = await _bt.ReadAncAsync();
                if (anc.HasValue)
                {
                    Anc = anc;
                    Raise(HeadsetChange.Anc);
                }

                var eq = await _bt.ReadEqAsync();
                if (eq != null)
                {
                    Eq = NormalizeBands(eq.Bands);
                    Raise(HeadsetChange.Eq);
                }

                var timer = await _bt.ReadSleepTimerAsync();
                if (timer.HasValue)
                {
                    SleepTimer = timer;
                    Raise(HeadsetChange.SleepTimer);
                }

                if (adv == null && anc == null && eq == null) Notify("The headset didn't answer the status request.");
            }
            catch (Exception ex)
            {
                Log.Error("Sync", "Reading headset state failed", ex);
            }
            finally
            {
                _syncInProgress = false;
            }
        }

        // Battery, gaming mode, balance and noise control can change from the headset itself.
        private async Task PollAsync()
        {
            if (_pollInProgress || _syncInProgress || !_bt.IsConnected) return;
            _pollInProgress = true;
            try
            {
                var adv = await _bt.ReadAdvInfoAsync(PollMask);
                if (adv != null) ApplyAdvInfo(adv, includeKeys: false);

                if (!_ancInFlight)
                {
                    var anc = await _bt.ReadAncAsync();
                    if (anc.HasValue && !_ancInFlight && anc != Anc)
                    {
                        Anc = anc;
                        Raise(HeadsetChange.Anc);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Poll", "Polling failed", ex);
            }
            finally
            {
                _pollInProgress = false;
            }
        }

        private void ApplyAdvInfo(AdvInfo adv, bool includeKeys)
        {
            var change = HeadsetChange.None;

            if (adv.BatteryLevel.HasValue)
            {
                BatteryLevel = adv.BatteryLevel;
                IsCharging = adv.IsCharging;
                change |= HeadsetChange.Battery;
            }
            if (adv.GamingMode.HasValue && !_gamingInFlight && adv.GamingMode != GamingMode)
            {
                GamingMode = adv.GamingMode;
                change |= HeadsetChange.Gaming;
            }
            if (adv.VolumeBalance.HasValue && adv.VolumeBalance != Balance)
            {
                Balance = adv.VolumeBalance;
                change |= HeadsetChange.Balance;
            }
            if (includeKeys && adv.KeySettings.Count > 0)
            {
                _keys.Clear();
                _unsupportedKeys.Clear();
                foreach (var setting in adv.KeySettings)
                {
                    var slot = (setting.Key, setting.Action);
                    if (setting.Function == KeyFunction.Unsupported) _unsupportedKeys.Add(slot);
                    else _keys[slot] = KeyFunction.FromDevice(setting.Function);
                }
                KeysKnown = true;
                change |= HeadsetChange.Keys;
            }

            if (change != HeadsetChange.None) Raise(change);
        }

        // The headset may hold fewer than ten bands (Spigen's presets use seven); pad with
        // zero-gain bands so the curve is unchanged.
        public static EqBand[] NormalizeBands(IReadOnlyList<EqBand> bands)
        {
            var result = bands.Take(EqPresets.Frequencies.Length).Select(EqMath.Clamp).ToList();
            foreach (var f in EqPresets.Frequencies)
            {
                if (result.Count >= EqPresets.Frequencies.Length) break;
                if (result.All(b => b.Frequency != f)) result.Add(new EqBand(f, 0f, 1.2f));
            }
            return result.OrderBy(b => b.Frequency).ToArray();
        }

        private static bool SameBands(IReadOnlyList<EqBand> a, IReadOnlyList<EqBand> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Frequency != b[i].Frequency ||
                    Math.Abs(a[i].Gain - b[i].Gain) > 0.011f ||
                    Math.Abs(a[i].Q - b[i].Q) > 0.011f)
                {
                    return false;
                }
            }
            return true;
        }

        #endregion

        #region Commands

        public async Task<bool> SetAncAsync(AncSetting setting)
        {
            _ancInFlight = true;
            try
            {
                var result = await _bt.SetAncAsync(setting);
                if (result == CommandResult.NoResponse)
                {
                    var actual = await _bt.ReadAncAsync();
                    if (actual.HasValue)
                    {
                        Anc = actual;
                        if (actual.Value == setting) result = CommandResult.Ok;
                    }
                }
                else if (result == CommandResult.Ok)
                {
                    Anc = setting;
                }

                if (result != CommandResult.Ok) Notify(FailureMessage(result, "noise control mode"));
                Raise(HeadsetChange.Anc);
                return result == CommandResult.Ok;
            }
            finally
            {
                _ancInFlight = false;
            }
        }

        public async Task<bool> SetGamingModeAsync(bool enabled)
        {
            _gamingInFlight = true;
            try
            {
                var result = await _bt.SetGamingModeAsync(enabled);
                if (result == CommandResult.NoResponse)
                {
                    var adv = await _bt.ReadAdvInfoAsync(AdvInfoType.Mask(AdvInfoType.WorkMode));
                    if (adv?.GamingMode != null)
                    {
                        GamingMode = adv.GamingMode;
                        if (adv.GamingMode == enabled) result = CommandResult.Ok;
                    }
                }
                else if (result == CommandResult.Ok)
                {
                    GamingMode = enabled;
                }

                if (result != CommandResult.Ok) Notify(FailureMessage(result, "gaming mode"));
                Raise(HeadsetChange.Gaming);
                return result == CommandResult.Ok;
            }
            finally
            {
                _gamingInFlight = false;
            }
        }

        public async Task<bool> SetEqAsync(IReadOnlyList<EqBand> bands)
        {
            var clamped = bands.Select(EqMath.Clamp).ToArray();
            var result = await _bt.SetEqAsync(new EqProfile(clamped, EqMath.HeadroomGain(clamped)));

            if (result == CommandResult.NoResponse)
            {
                var actual = await _bt.ReadEqAsync();
                if (actual != null)
                {
                    var actualBands = NormalizeBands(actual.Bands);
                    Eq = actualBands;
                    if (SameBands(actualBands, clamped.OrderBy(b => b.Frequency).ToArray()))
                    {
                        Eq = clamped;
                        result = CommandResult.Ok;
                    }
                }
            }
            else if (result == CommandResult.Ok)
            {
                Eq = clamped;
            }

            if (result != CommandResult.Ok) Notify(FailureMessage(result, "equalizer"));
            Raise(HeadsetChange.Eq);
            return result == CommandResult.Ok;
        }

        public async Task<bool> SetKeyAsync((byte Key, byte Action) slot, byte function)
        {
            var result = await _bt.SetKeyMappingAsync(slot.Key, slot.Action, function);
            if (result == CommandResult.NoResponse)
            {
                var adv = await _bt.ReadAdvInfoAsync(AdvInfoType.Mask(AdvInfoType.KeySettings));
                if (adv != null && adv.KeySettings.Count > 0)
                {
                    ApplyAdvInfo(adv, includeKeys: true);
                    if (GetKeyFunction(slot) == function) result = CommandResult.Ok;
                }
            }
            else if (result == CommandResult.Ok)
            {
                _keys[slot] = function;
            }

            if (result != CommandResult.Ok) Notify(FailureMessage(result, "button"));
            Raise(HeadsetChange.Keys);
            return result == CommandResult.Ok;
        }

        public async Task<bool> SetBalanceAsync(int value)
        {
            var result = await _bt.SetBalanceAsync(value);
            if (result == CommandResult.NoResponse)
            {
                var adv = await _bt.ReadAdvInfoAsync(AdvInfoType.Mask(AdvInfoType.VolumeBalance));
                if (adv?.VolumeBalance != null)
                {
                    Balance = adv.VolumeBalance;
                    if (Balance == value) result = CommandResult.Ok;
                }
            }
            else if (result == CommandResult.Ok)
            {
                Balance = value;
            }

            if (result != CommandResult.Ok) Notify(FailureMessage(result, "channel balance"));
            Raise(HeadsetChange.Balance);
            return result == CommandResult.Ok;
        }

        // minutes: 15..999, or SleepTimerState.Off to cancel.
        public async Task<bool> SetSleepTimerAsync(int minutes)
        {
            var result = await _bt.SetSleepTimerAsync(minutes);
            var actual = await _bt.ReadSleepTimerAsync();
            if (actual.HasValue)
            {
                SleepTimer = actual;
                if (result == CommandResult.NoResponse && actual.Value.Minutes == minutes) result = CommandResult.Ok;
            }

            if (result != CommandResult.Ok) Notify(FailureMessage(result, "sleep timer"));
            Raise(HeadsetChange.SleepTimer);
            return result == CommandResult.Ok;
        }

        public async Task<bool> RestoreDefaultsAsync()
        {
            var result = await _bt.RunSystemOperationAsync(SystemOperation.RestoreSettings);
            if (result is CommandResult.Ok or CommandResult.NoResponse)
            {
                Notify("Headset settings restored to defaults.");
                await Task.Delay(1500);
                await SyncAllAsync();
                return true;
            }
            Notify(FailureMessage(result, "reset"));
            return false;
        }

        public async Task<bool> ClearPairingRecordsAsync()
        {
            var result = await _bt.RunSystemOperationAsync(SystemOperation.ClearPairedRecords);
            if (result is CommandResult.Ok or CommandResult.NoResponse)
            {
                Notify("Pairing records cleared. Pair the headphones again in Windows Bluetooth settings.");
                return true;
            }
            Notify(FailureMessage(result, "pairing"));
            return false;
        }

        #endregion
    }
}
