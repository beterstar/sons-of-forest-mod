namespace KelvinTwo;

/// <summary>
/// Decides when Kelvin 2 may warp home. Fair-play rules: only while carrying something,
/// only when the drop-off is far, only once he is actually heading back (the distance has
/// been shrinking for a few seconds - not while he's still out collecting), and at most
/// once per cooldown. Pure logic so it can be tested without the game.
/// </summary>
internal sealed class WarpDecider
{
    private readonly float _minDistance;
    private readonly float _cooldown;
    private readonly float _headingWindow;
    private readonly float _minClosing;

    private float _sampleTime = float.NegativeInfinity;
    private float _sampleDistance;
    private float _lastWarp = float.NegativeInfinity;

    /// <param name="minDistance">Only warp when the drop-off is farther than this (m).</param>
    /// <param name="cooldownSeconds">Minimum time between warps.</param>
    /// <param name="headingWindowSeconds">How long he must have been closing in on the drop-off.</param>
    /// <param name="minClosingMeters">How much closer he must have got in that window.</param>
    internal WarpDecider(float minDistance, float cooldownSeconds, float headingWindowSeconds = 3f, float minClosingMeters = 2f)
    {
        _minDistance = minDistance;
        _cooldown = cooldownSeconds;
        _headingWindow = headingWindowSeconds;
        _minClosing = minClosingMeters;
    }

    /// <summary>Forget the heading sample (not carrying, busy with a player, no body...).</summary>
    internal void Reset() => _sampleTime = float.NegativeInfinity;

    /// <summary>Feed one observation; true means "warp now".</summary>
    internal bool Update(bool carrying, float distanceToDropOff, float now)
    {
        if (!carrying || distanceToDropOff <= _minDistance || now - _lastWarp < _cooldown)
        {
            Reset();
            return false;
        }

        if (float.IsNegativeInfinity(_sampleTime))
        {
            _sampleTime = now;
            _sampleDistance = distanceToDropOff;
            return false;
        }

        if (now - _sampleTime < _headingWindow) return false;

        bool headingBack = _sampleDistance - distanceToDropOff >= _minClosing;
        _sampleTime = now;
        _sampleDistance = distanceToDropOff;
        if (!headingBack) return false;

        _lastWarp = now;
        Reset();
        return true;
    }
}
