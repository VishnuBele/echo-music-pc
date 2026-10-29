using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace EchoMusic.Core;

/// <summary>Signals Windows endpoint additions/removals and render-default changes.</summary>
internal sealed class WindowsAudioEndpointWatcher : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private bool _disposed;

    public event Action? OutputEndpointsChanged;

    public WindowsAudioEndpointWatcher()
    {
        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => OutputEndpointsChanged?.Invoke();
    public void OnDeviceAdded(string deviceId) => OutputEndpointsChanged?.Invoke();
    public void OnDeviceRemoved(string deviceId) => OutputEndpointsChanged?.Invoke();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render) OutputEndpointsChanged?.Invoke();
    }
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
        _enumerator.Dispose();
    }
}
