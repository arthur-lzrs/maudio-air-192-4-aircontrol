namespace AirControl.Core;

public interface ISingleInstanceGuard
{
    bool TryAcquire();
}
