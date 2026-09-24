using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace UtiCdHelper;

/// <summary>
/// 五路相互独立的倒计时。五路共用一个计时区域，所以"表"的观测是全局的。
///
/// 计时模型（甲 + 乙）：
///   有效读数 = 节拍读数 + 亚秒            （节拍读数单调不减）
///   已走表秒 = 有效读数 − 启动读数 − 跳过拍数
///
/// · 节拍读数由锚点按表速度向前推，实测读数只用于"快进"校正，绝不回退，
///   因为 OCR 天生滞后半拍到一拍，若用实测覆盖就会反复回跳。
/// · 亚秒让跳变落在预测的拍边界上，五路共享同一个小数相位，所以跳变同步且准时。
/// · 超过阈值没观测到跳变就认定表停（暂停/最小化），停止推进，等于暂停。
/// </summary>
public sealed class CountdownService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private const int MaxStepSeconds = 10;
    private const int MaxRejectBeforeAccept = 3;

    // 认定表停所需的静默时间：既要看表速度，也要容纳 OCR 本身的滞后
    private static readonly TimeSpan MinStallWindow = TimeSpan.FromSeconds(1.0);
    private const double StalledBeats = 1.5;

    private readonly DotValues _dots;
    private readonly RecognitionSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly Slot[] _slots;
    private readonly DispatcherTimer _timer;

    private bool _polling;
    private DateTime _lastPollAt = DateTime.MinValue;
    private int? _lastReading;
    private string? _lastPollError;
    private int? _lastAcceptedReading;
    private int _rejectedCount;

    // 表的观测状态
    private int? _beatReading;      // 当前节拍读数，单调不减
    private DateTime _beatAnchor;   // 这一拍开始的时刻
    private double? _secondsPerBeat;
    private DateTime _lastReadingAt;
    private DateTime _lastJumpObservedAt;

    public CountdownService(DotValues dots, RecognitionSettings settings)
    {
        _dots = dots;
        _settings = settings;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _slots = new Slot[dots.Dots.Count];
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new Slot();
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// 启动第 index 路倒计时。total 是总耗时，baseline 是启动那一刻读到的时间数字。
    /// baseline 为 null 时退化成真实时钟。已经有别的一路在对表时，
    /// 这一路会等到下一个拍边界才真正开始，保证跳变同步。
    /// </summary>
    public void Start(int index, int total, int? baseline)
    {
        if (index < 0 || index >= _slots.Length)
        {
            return;
        }

        if (total <= 0)
        {
            Stop(index);
            return;
        }

        Slot slot = _slots[index];
        slot.Running = true;
        slot.Total = total;
        slot.Remaining = total;
        slot.StartBaseline = baseline;
        slot.SkippedBeats = 0;
        slot.Finished = false;
        slot.EndsAt = baseline is null ? DateTime.Now.AddSeconds(total) : null;

        // 有同步对象时才等拍对齐，第一路不等，避免白白延迟
        slot.PendingStartAt = baseline is not null && HasTrackedSlot() ? NextBeatTime() : null;

        DotValue dot = _dots.Dots[index];
        dot.Value = total;
        dot.IsRunning = true;

        _timer.Start();
    }

    /// <summary>结束第 index 路倒计时，圆点回到空闲状态。</summary>
    public void Stop(int index)
    {
        if (index < 0 || index >= _slots.Length)
        {
            return;
        }

        ResetSlot(_slots[index]);
        _slots[index].Finished = false;

        DotValue dot = _dots.Dots[index];
        dot.IsRunning = false;
        dot.Value = 0;

        StopTimerIfIdle();
    }

    /// <summary>中断全部倒计时，圆点全部回到空闲状态（复位时用）。</summary>
    public void StopAll()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            ResetSlot(_slots[i]);
            _slots[i].Finished = false;

            DotValue dot = _dots.Dots[i];
            dot.IsRunning = false;
            dot.Value = 0;
        }

        ResetObservation();
        _timer.Stop();
    }

    public bool IsRunning(int index) =>
        index >= 0 && index < _slots.Length && _slots[index].Running;

    /// <summary>这一路是否已经倒数到 0 自然结束。</summary>
    public bool IsFinished(int index) =>
        index >= 0 && index < _slots.Length && _slots[index].Finished;

    /// <summary>该路当前剩余秒数；没在倒计时就是 0。</summary>
    public int GetRemaining(int index) =>
        index >= 0 && index < _slots.Length ? _slots[index].Remaining : 0;

    private static void ResetSlot(Slot slot)
    {
        slot.Running = false;
        slot.StartBaseline = null;
        slot.EndsAt = null;
        slot.PendingStartAt = null;
        slot.SkippedBeats = 0;
        slot.Remaining = 0;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.Now;

        for (int i = 0; i < _slots.Length; i++)
        {
            Slot slot = _slots[i];
            if (!slot.Running || slot.EndsAt is not DateTime end)
            {
                continue;
            }

            int remaining = (int)Math.Ceiling((end - now).TotalSeconds);
            if (remaining <= 0)
            {
                Stop(i);
                _slots[i].Finished = true;
            }
            else
            {
                SetRemaining(i, remaining);
            }
        }

        AdvanceBeats(now);
        ActivatePendingSlots(now);
        RefreshTrackedSlots(now);

        if (!_polling && now - _lastPollAt >= PollInterval && AnyRunningSlot())
        {
            _lastPollAt = now;
            StartPoll();
        }

        StopTimerIfIdle();
    }

    private bool AnyRunningSlot()
    {
        foreach (Slot slot in _slots)
        {
            if (slot.Running)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasTrackedSlot()
    {
        foreach (Slot slot in _slots)
        {
            if (slot.Running && slot.StartBaseline is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>按表速度把节拍读数往前推；表停了就不推。</summary>
    private void AdvanceBeats(DateTime now)
    {
        if (_beatReading is not int reading
            || _secondsPerBeat is not double perBeat
            || perBeat <= 0)
        {
            return;
        }

        if (IsStalled(now, perBeat))
        {
            return;
        }

        while (now - _beatAnchor >= TimeSpan.FromSeconds(perBeat))
        {
            _beatAnchor = _beatAnchor.AddSeconds(perBeat);
            reading++;
        }

        _beatReading = reading;
    }

    private bool IsStalled(DateTime now, double perBeat)
    {
        TimeSpan window = TimeSpan.FromSeconds(perBeat * StalledBeats);
        if (window < MinStallWindow)
        {
            window = MinStallWindow;
        }

        return now - _lastJumpObservedAt > window;
    }

    private void ActivatePendingSlots(DateTime now)
    {
        if (_beatReading is not int reading)
        {
            return;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            Slot slot = _slots[i];
            if (!slot.Running || slot.PendingStartAt is not DateTime at || now < at)
            {
                continue;
            }

            // 等待期间表已经走过的拍数不计入这一路
            int baseline = slot.StartBaseline ?? reading;
            slot.SkippedBeats = Math.Max(0, reading - baseline);
            slot.PendingStartAt = null;
        }
    }

    /// <summary>下一个拍边界的时刻；表状态未知时返回 null。</summary>
    private DateTime? NextBeatTime()
    {
        if (_beatReading is null || _secondsPerBeat is not double perBeat || perBeat <= 0)
        {
            return null;
        }

        DateTime now = DateTime.Now;
        DateTime next = _beatAnchor.AddSeconds(perBeat);

        while (next <= now)
        {
            next = next.AddSeconds(perBeat);
        }

        return next;
    }

    /// <summary>在后台线程识别一次计时区域，避免 OCR 卡住界面。</summary>
    private void StartPoll()
    {
        _polling = true;

        Task.Run(() =>
        {
            int? reading;
            string? error = null;

            try
            {
                reading = _settings.RecognizeTimer();
                error = _settings.LastError;
            }
            catch (Exception exception)
            {
                reading = null;
                error = exception.GetType().Name + ": " + exception.Message;
            }

            _dispatcher.BeginInvoke(new Action(() =>
            {
                _polling = false;
                _lastReading = reading;
                _lastPollError = error;
                ApplyReading(reading);
            }));
        });
    }

    /// <summary>
    /// 用实测读数校正。读不到（null）说明最小化或没读到，表状态不动，等于暂停。
    /// 实测只用于把节拍读数"快进"，永远不会让它回退。
    /// </summary>
    private void ApplyReading(int? reading)
    {
        if (reading is not int current)
        {
            return;
        }

        if (_lastAcceptedReading is int last
            && current - last > MaxStepSeconds
            && _rejectedCount < MaxRejectBeforeAccept)
        {
            _rejectedCount++;
            return;
        }

        // 表只会前进，读数变小一定是读错了
        if (_lastAcceptedReading is int accepted && current < accepted)
        {
            return;
        }

        _lastAcceptedReading = current;
        _rejectedCount = 0;

        DateTime now = DateTime.Now;
        _lastReadingAt = now;

        if (_beatReading is not int beat)
        {
            // 第一次拿到读数，还没有相位信息
            _beatReading = current;
            _beatAnchor = now;
            _lastJumpObservedAt = now;
            return;
        }

        if (current <= beat)
        {
            // OCR 滞后或表停：不动，预测的推进继续生效
            return;
        }

        FastForward(beat, current, now);
    }

    /// <summary>实测读数超过节拍读数：快进校正，同时测表速度。</summary>
    private void FastForward(int beat, int current, DateTime now)
    {
        int delta = current - beat;

        // 跳变发生在上一次读数和这次之间，取中点补偿检测延迟
        DateTime jumpAt = _lastReadingAt != default && now > _lastReadingAt
            ? _lastReadingAt + TimeSpan.FromTicks((now - _lastReadingAt).Ticks / 2)
            : now;

        // 表速度由相邻两次跳变之间的真实时间来测；
        // 最小化恢复这类大跳（一次跳很多）不参与，免得把速度带偏
        bool reliable = delta <= 5 && (now - _lastReadingAt).TotalSeconds < 3.0;

        if (reliable)
        {
            double perBeat = (jumpAt - _beatAnchor).TotalSeconds / delta;
            if (perBeat > 0)
            {
                _secondsPerBeat = _secondsPerBeat is null
                    ? perBeat
                    : (_secondsPerBeat.Value * 0.7) + (perBeat * 0.3);
            }
        }

        _beatReading = current;
        _beatAnchor = jumpAt;
        _lastJumpObservedAt = now;
    }

    /// <summary>当前有效读数（节拍读数 + 亚秒）。</summary>
    private double? EffectiveReading(DateTime now)
    {
        if (_beatReading is not int reading)
        {
            return null;
        }

        return reading + SubBeatFraction(now);
    }

    /// <summary>当前这一拍里走了多少（0~1）。</summary>
    private double SubBeatFraction(DateTime now)
    {
        if (_secondsPerBeat is not double perBeat || perBeat <= 0)
        {
            return 0;
        }

        double beats = (now - _beatAnchor).TotalSeconds / perBeat;

        return beats <= 0 ? 0 : Math.Min(beats, 1.0);
    }

    /// <summary>重算所有对表中的路的剩余值。节拍读数是全局的，所以五路同步跳变。</summary>
    private void RefreshTrackedSlots(DateTime now)
    {
        if (EffectiveReading(now) is not double current)
        {
            return;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            Slot slot = _slots[i];
            if (!slot.Running || slot.StartBaseline is not int baseline || slot.PendingStartAt is not null)
            {
                continue;
            }

            double elapsed = current - baseline - slot.SkippedBeats;

            if (elapsed >= slot.Total)
            {
                Stop(i);

                // Stop 会把 Finished 清掉，自然结束要在这里补上标记
                _slots[i].Finished = true;
            }
            else
            {
                SetRemaining(i, (int)Math.Ceiling(slot.Total - elapsed));
            }
        }
    }

    private void SetRemaining(int index, int remaining)
    {
        Slot slot = _slots[index];
        if (slot.Remaining == remaining)
        {
            return;
        }

        slot.Remaining = remaining;
        _dots.Dots[index].Value = remaining;
    }

    private void ResetObservation()
    {
        _beatReading = null;
        _beatAnchor = default;
        _secondsPerBeat = null;
        _lastReadingAt = default;
        _lastJumpObservedAt = default;
        _lastAcceptedReading = null;
        _rejectedCount = 0;
    }

    private void StopTimerIfIdle()
    {
        foreach (Slot slot in _slots)
        {
            if (slot.Running)
            {
                return;
            }
        }

        _timer.Stop();
        ResetObservation();
    }

    /// <summary>给识图设置界面看的一行轮询诊断。</summary>
    public string DescribePoll()
    {
        if (_lastPollAt == DateTime.MinValue)
        {
            return "轮询：尚未开始（当前没有正在进行的倒计时）";
        }

        string time = _lastPollAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        string rejected = _rejectedCount > 0 ? $"· 已丢弃异常读数 {_rejectedCount} 次" : string.Empty;
        string speed = _secondsPerBeat is double perBeat
            ? $"· 表速度 {perBeat:F2} 秒/拍"
            : "· 表速度未知";
        string beat = _beatReading is int value ? $"· 节拍 {value}" : string.Empty;

        if (_lastReading is int seconds)
        {
            return $"轮询：最近读数 {seconds / 60}:{seconds % 60:D2}（{seconds} 秒）· {time} {speed}{beat} {rejected}";
        }

        return _lastPollError is null
            ? $"轮询：未识别到数字（{time}）{rejected}"
            : $"轮询：读表失败（{time}）· {_lastPollError}";
    }

    private sealed class Slot
    {
        public bool Running;
        public int Total;
        public int Remaining;

        /// <summary>启动那一刻的实测读数。</summary>
        public int? StartBaseline;

        /// <summary>等待对齐期间表已经走过的拍数，不计入这一路。</summary>
        public int SkippedBeats;

        /// <summary>等到这个时刻才真正开始（启动对齐用）。</summary>
        public DateTime? PendingStartAt;

        /// <summary>真实时钟兜底的结束时刻。</summary>
        public DateTime? EndsAt;

        /// <summary>这一路已经倒数到 0 自然结束（区别于从未启动）。</summary>
        public bool Finished;
    }
}
