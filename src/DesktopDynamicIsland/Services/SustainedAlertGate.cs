namespace DesktopDynamicIsland.Services;

public sealed class SustainedAlertGate
{
    private readonly TimeSpan _sustainedDuration;
    private readonly TimeSpan _urgentDuration;
    private readonly TimeSpan _recoveryDuration;
    private readonly TimeSpan _cooldown;
    private DateTimeOffset? _exceededSince;
    private DateTimeOffset? _urgentSince;
    private DateTimeOffset? _recoverySince;
    private DateTimeOffset _cooldownUntil;
    private bool _active;
    private bool _notified;

    public SustainedAlertGate(
        TimeSpan sustainedDuration,
        TimeSpan urgentDuration,
        TimeSpan recoveryDuration,
        TimeSpan cooldown)
    {
        _sustainedDuration = sustainedDuration;
        _urgentDuration = urgentDuration;
        _recoveryDuration = recoveryDuration;
        _cooldown = cooldown;
    }

    public void Update(bool exceeded, bool urgent, bool recovered, DateTimeOffset now)
    {
        if (_active)
        {
            if (!recovered)
            {
                _recoverySince = null;
                return;
            }

            _recoverySince ??= now;
            if (now - _recoverySince.Value >= _recoveryDuration)
            {
                _active = false;
                _notified = false;
                _cooldownUntil = now + _cooldown;
                ResetPending();
            }

            return;
        }

        if (now < _cooldownUntil || recovered)
        {
            ResetPending();
            return;
        }

        _exceededSince = exceeded ? _exceededSince ?? now : null;
        _urgentSince = urgent ? _urgentSince ?? now : null;

        if ((_urgentSince is not null && now - _urgentSince.Value >= _urgentDuration) ||
            (_exceededSince is not null && now - _exceededSince.Value >= _sustainedDuration))
        {
            _active = true;
            _notified = false;
            ResetPending();
        }
    }

    public bool TryTakeNotification()
    {
        if (!_active || _notified)
        {
            return false;
        }

        _notified = true;
        return true;
    }

    private void ResetPending()
    {
        _exceededSince = null;
        _urgentSince = null;
        _recoverySince = null;
    }
}
