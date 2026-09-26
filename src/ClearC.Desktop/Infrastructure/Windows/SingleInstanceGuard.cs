using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;

namespace ClearC.Desktop.Infrastructure.Windows;

/// <summary>
/// 单实例（§2.7）：命名 <see cref="Mutex"/> 判重，命名管道把已有实例带到前台。
/// 提权重启时旧实例先退出，新实例再接管，避免管道互斥死锁。
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string PipeName = "ClearC.Activate";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;

    private SingleInstanceGuard(Mutex mutex, bool isFirstInstance)
    {
        _mutex = mutex;
        IsFirstInstance = isFirstInstance;
    }

    public bool IsFirstInstance { get; }

    public static SingleInstanceGuard Acquire()
    {
        var name = $@"Local\ClearC.SingleInstance.{CurrentUserSid()}";
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        return new(mutex, createdNew);
    }

    /// <summary>已有实例：通知它把自己带到前台，然后本进程退出。</summary>
    public static void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            client.WriteByte(1);
            client.Flush();
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // 已有实例正在退出：静默忽略。
        }
    }

    public void StartListening(Action onActivate)
    {
        _listener = Task.Run(async () =>
        {
            while (!_cancellation.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_cancellation.Token);
                    onActivate();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(200, _cancellation.Token).ConfigureAwait(false);
                }
            }
        });
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 未持有互斥体（第二个实例）：无需释放。
        }

        _mutex.Dispose();
        _cancellation.Dispose();
        _ = _listener;
    }

    private static string CurrentUserSid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "default";
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? "default";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            return "default";
        }
    }
}
