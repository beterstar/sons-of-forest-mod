namespace KelvinTwo;

/// <summary>
/// Notices Kelvin 2 standing still with a load far from the drop-off (the "stands there
/// holding sticks until you hit him" freeze). He must have stayed within a small circle for
/// the whole window; walking, or being at the drop-off, resets it. Pure logic so it can be
/// tested without the game.
/// </summary>
internal sealed class StuckDetector
{
    private readonly float _window;
    private readonly float _moveTolerance;
    private readonly float _nearDropOff;

    private bool _hasAnchor;
    private float _anchorX, _anchorZ, _anchorTime;

    /// <param name="windowSeconds">How long he must stand still before it counts as stuck.</param>
    /// <param name="moveToleranceMeters">Moving less than this (shuffling, turning) is still standing still.</param>
    /// <param name="nearDropOffMeters">Closer than this to the drop-off he is unloading, not stuck.</param>
    internal StuckDetector(float windowSeconds = 15f, float moveToleranceMeters = 1.5f, float nearDropOffMeters = 4f)
    {
        _window = windowSeconds;
        _moveTolerance = moveToleranceMeters;
        _nearDropOff = nearDropOffMeters;
    }

    internal void Reset() => _hasAnchor = false;

    /// <summary>Feed one observation; true means "stuck - give him a new route now".</summary>
    internal bool Update(bool carrying, float distanceToDropOff, float x, float z, float now)
    {
        if (!carrying || distanceToDropOff <= _nearDropOff)
        {
            Reset();
            return false;
        }

        float dx = x - _anchorX, dz = z - _anchorZ;
        if (!_hasAnchor || dx * dx + dz * dz > _moveTolerance * _moveTolerance)
        {
            _hasAnchor = true;
            _anchorX = x;
            _anchorZ = z;
            _anchorTime = now;
            return false;
        }

        if (now - _anchorTime < _window) return false;
        _anchorTime = now;   // next nudge only after another full window
        return true;
    }
}
