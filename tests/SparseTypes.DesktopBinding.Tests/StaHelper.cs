using System.Runtime.ExceptionServices;

namespace SparseTypes.DesktopBinding.Tests;

/// <summary>Runs desktop binding bodies on a dedicated STA thread.</summary>
internal static class StaHelper
{
    internal static void Run(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
