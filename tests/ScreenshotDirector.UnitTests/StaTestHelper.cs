using System.Threading;

namespace ScreenshotDirector.UnitTests;

public static class StaTestHelper
{
    public static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA test execution failed", ex);
        }
    }

    public static async Task RunInStaAsync(Func<Task> action)
    {
        Exception? ex = null;
        var tcs = new TaskCompletionSource();

        var thread = new Thread(async () =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception e)
            {
                ex = e;
                tcs.SetException(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await tcs.Task;
    }
}
