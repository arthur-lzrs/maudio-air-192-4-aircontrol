using System.Runtime.InteropServices;
using AirControl.Core;

namespace AirControl.Audio;

public class SingleInstanceGuard : ISingleInstanceGuard, IDisposable
{
    private const string MutexName = "Global\\AirControl-SingleInstance-Mutex";
    public static readonly uint ShowExistingInstanceMessage = RegisterWindowMessage("AirControl-ShowExistingInstance");

    private Mutex? _mutex;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

        if (createdNew)
        {
            return true;
        }

        PostMessage((IntPtr)HWND_BROADCAST, ShowExistingInstanceMessage, IntPtr.Zero, IntPtr.Zero);
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    public void Dispose()
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
    }

    private const int HWND_BROADCAST = 0xffff;

    [DllImport("user32", CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
