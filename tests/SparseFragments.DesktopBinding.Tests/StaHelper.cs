using System.Runtime.ExceptionServices;

namespace SparseFragments.DesktopBinding.Tests;

/// <summary>
/// Runs desktop binding bodies on a dedicated STA thread. WPF and WinForms
/// controls require a single-threaded apartment, while the test runner uses
/// multithreaded apartments, so every control interaction is marshaled here.
/// </summary>
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
