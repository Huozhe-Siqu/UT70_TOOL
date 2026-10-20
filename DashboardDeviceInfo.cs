using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace UT70Dashboard
{
    internal sealed partial class DashboardForm
    {
        private enum DeviceInfoKind { Pin, Serial, Screen, Frequency }

        private readonly object _infoReplyLock = new object();
        private readonly int[] _infoReplyVersions = new int[4];

        private void ConfirmDeviceInfoReply(DeviceInfoKind kind)
        {
            // 接收线程先确认回包，再投递界面更新，避免等待界面消息造成查询超时。
            lock (_infoReplyLock)
            {
                _infoReplyVersions[(int)kind]++;
                Monitor.PulseAll(_infoReplyLock);
            }
        }

        private void WakeDeviceInfoWaiters()
        {
            lock (_infoReplyLock) Monitor.PulseAll(_infoReplyLock);
        }

        private void RefreshDeviceInformation()
        {
            if (!_connected || _connectionBusy || _commandBusy || _closing) return;
            _refreshInfoButton.Text = "查询中…";
            foreach (DeviceInfoKind kind in Enum.GetValues(typeof(DeviceInfoKind)))
                SetDeviceInfoPlaceholder(kind, "查询中…");

            List<string> missing = new List<string>();
            RunDeviceAction("读取设备信息", delegate
            {
                string[] names = { "通讯握手", "序列号与硬件版本", "设备界面", "采样率" };
                Func<bool>[] requests = {
                    delegate { return _device.Pin(1); },
                    _device.GetDevVer, _device.GetDevUI, _device.GetFrequency
                };
                // DLL 共用当前应答类型；等待本项回包后才查询下一项。
                for (int i = 0; i < requests.Length; i++)
                {
                    if (_closing) return;
                    if (!ReadDeviceInfoItem((DeviceInfoKind)i, names[i], requests[i])) missing.Add(names[i]);
                }
            }, delegate
            {
                _refreshInfoButton.Text = "刷新信息";
                AppendLog(missing.Count == 0 ? "设备信息已全部更新" :
                    "设备信息未完整返回：" + String.Join("、", missing.ToArray()) + "，可点击刷新信息重试",
                    missing.Count == 0 ? Theme.Cyan : Theme.Amber);
            }, delegate(Exception ex)
            {
                _refreshInfoButton.Text = "刷新信息";
                foreach (DeviceInfoKind kind in Enum.GetValues(typeof(DeviceInfoKind)))
                    SetDeviceInfoPlaceholder(kind, "未完成");
            });
        }

        private bool ReadDeviceInfoItem(DeviceInfoKind kind, string name, Func<bool> request)
        {
            string reason = "未收到设备回包";
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (_closing) return false;
                if (!_device.ConnState) throw new InvalidOperationException("设备已断开连接");
                int previous;
                lock (_infoReplyLock) previous = _infoReplyVersions[(int)kind];
                Stopwatch elapsed = Stopwatch.StartNew();
                try
                {
                    // DLL 内部只等待 100～200 毫秒；保留当前查询，让迟到回包仍可被接收。
                    bool acknowledged = request();
                    lock (_infoReplyLock)
                    {
                        while (!_closing && _infoReplyVersions[(int)kind] == previous && elapsed.ElapsedMilliseconds < 750)
                        {
                            if (!_device.ConnState) throw new InvalidOperationException("设备已断开连接");
                            long remaining = 750 - elapsed.ElapsedMilliseconds;
                            if (remaining <= 0) break;
                            Monitor.Wait(_infoReplyLock, (int)Math.Min(100, remaining));
                        }
                        if (_closing) return false;
                        if (_infoReplyVersions[(int)kind] != previous) return true;
                    }
                    reason = acknowledged ? "DLL 已确认，但未收到信息事件" : "查询超时，未收到设备确认";
                }
                catch (Exception ex) { reason = ex.Message; }
                if (!_device.ConnState) throw new InvalidOperationException("设备已断开连接");
            }
            string detail = reason;
            PostToUi(delegate
            {
                // 刷新失败时保留本次连接已收到的信息，不用占位文字覆盖有效读数。
                SetDeviceInfoPlaceholder(kind, "未响应");
                AppendLog(name + "查询失败（已尝试 2 次）：" + detail, Theme.Amber);
            });
            return false;
        }

        private void SetDeviceInfoPlaceholder(DeviceInfoKind kind, string text)
        {
            lock (_infoReplyLock) if (_infoReplyVersions[(int)kind] != 0) return;
            switch (kind)
            {
                case DeviceInfoKind.Pin: _pinLabel.Text = "通讯  " + text; break;
                case DeviceInfoKind.Serial:
                    _serialLabel.Text = "SN  " + text; _hardwareLabel.Text = "硬件  " + text; break;
                case DeviceInfoKind.Screen: _deviceModeLabel.Text = "界面  " + text; break;
                case DeviceInfoKind.Frequency: _frequencyCombo.Text = text + " ▾"; break;
            }
        }
    }
}
