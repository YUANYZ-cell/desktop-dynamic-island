namespace DesktopDynamicIsland.Services;

public sealed class FullscreenSuppressionGate
{
    private readonly int _requiredConsecutiveDetections;
    private int _consecutiveDetections;

    public FullscreenSuppressionGate(int requiredConsecutiveDetections = 2)
    {
        if (requiredConsecutiveDetections < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredConsecutiveDetections));
        }

        _requiredConsecutiveDetections = requiredConsecutiveDetections;
    }

    public bool Update(bool foregroundCoversTaskbar)
    {
        if (!foregroundCoversTaskbar)
        {
            _consecutiveDetections = 0;
            return false;
        }

        _consecutiveDetections = Math.Min(
            _consecutiveDetections + 1,
            _requiredConsecutiveDetections);
        return _consecutiveDetections >= _requiredConsecutiveDetections;
    }
}
