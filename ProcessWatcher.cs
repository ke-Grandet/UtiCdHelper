using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace UtiCdHelper;

public enum ProcessState
{
    NotRunning,
    Minimized,
    Normal,
}

/// <summary>
/// 按名字盯一个进程：有窗口就报 Normal，最小化报 Minimized，没在跑报 NotRunning。
/// 盯谁由默认配置里的 FollowProcessName 决定。
/// </summary>
public sealed class ProcessWatcher : IDisposable
{
    private readonly string _processName;
    private readonly DispatcherTimer _timer;
    private ProcessState _state = ProcessState.NotRunning;

    public ProcessWatcher(TimeSpan interval, string processName)
    {
        _processName = processName;
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => Poll();

        Poll();
        _timer.Start();
    }

    public ProcessState State => _state;

    public event Action<ProcessState>? StateChanged;

    private void Poll()
    {
        ProcessState state = Detect();
        if (state == _state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(state);
    }

    private ProcessState Detect()
    {
        foreach (Process process in Process.GetProcessesByName(_processName))
        {
            using (process)
            {
                IntPtr handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                return IsIconic(handle) ? ProcessState.Minimized : ProcessState.Normal;
            }
        }

        return ProcessState.NotRunning;
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
}
