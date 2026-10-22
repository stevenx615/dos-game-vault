using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DOSGameVault;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private RegisteredWaitHandle? listener;
    public bool IsOwner { get; }

    public SingleInstance(string executablePath)
    {
        var path = Path.GetFullPath(executablePath).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DOSGameVault.Activate." + key);
        mutex = new Mutex(false, @"Local\DOSGameVault.Instance." + key);
        try { IsOwner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
    }

    public void Listen(Action activate)
    {
        if (!IsOwner) throw new InvalidOperationException("Only the running instance can listen for activation.");
        listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => activate(), null, Timeout.Infinite, false);
    }

    public void ActivateExisting()
    {
        AllowSetForegroundWindow(-1);
        activation.Set();
    }

    public void Dispose()
    {
        listener?.Unregister(null);
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose();
        activation.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
