namespace DesktopDynamicIsland.Services;

public enum MetricPair
{
    CpuRam,
    GpuVram
}

public sealed class MetricPairController
{
    private MetricPair _selectedPair;
    private MetricPair? _overridePair;
    private DateTimeOffset _overrideUntil;

    public MetricPairController(MetricPair selectedPair)
    {
        _selectedPair = selectedPair;
    }

    public MetricPair SelectedPair => _selectedPair;

    public void Select(MetricPair pair)
    {
        _selectedPair = pair;
    }

    public void Override(MetricPair pair, DateTimeOffset now, TimeSpan duration)
    {
        _overridePair = pair;
        _overrideUntil = now + duration;
    }

    public MetricPair GetEffectivePair(DateTimeOffset now)
    {
        if (_overridePair is MetricPair pair && now < _overrideUntil)
        {
            return pair;
        }

        _overridePair = null;
        return _selectedPair;
    }
}
