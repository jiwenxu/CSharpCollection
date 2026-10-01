using System.Collections.Concurrent;

namespace WeChatWindowInspector;

/// <summary>会话状态（供后台按键线程与主循环共享）</summary>
internal sealed class SessionState
{
    public volatile bool Running = true;
}

/// <summary>控制台快捷键监听：把按键放入队列，由主循环消费。'q' 亦交由主循环处理。</summary>
internal static class ConsoleKeyWatcher
{
    public static ConcurrentQueue<char> Start(Func<bool> isRunning)
    {
        var keys = new ConcurrentQueue<char>();

        var thread = new Thread(() =>
        {
            try
            {
                while (isRunning())
                {
                    if (Console.KeyAvailable)
                    {
                        keys.Enqueue(char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar));
                    }

                    Thread.Sleep(50);
                }
            }
            catch
            {
                // 输入被重定向或无控制台时忽略，不影响主流程
            }
        })
        {
            IsBackground = true,
            Name = "key-listener",
        };

        thread.Start();
        return keys;
    }
}
