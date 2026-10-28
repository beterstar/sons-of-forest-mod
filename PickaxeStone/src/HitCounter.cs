using System.Collections.Generic;

namespace PickaxeStone;

/// <summary>
/// Decides when a pickaxe hit on a rock should drop a stone. Several game callbacks
/// fire for one impact (effect, audio, impact), so hits are de-duplicated: at most one
/// per swing (when the swing id is known) and never closer together than the minimum
/// interval. Every <c>hitsPerDrop</c>-th counted hit on the same rock drops.
/// Pure logic so it can be tested without the game.
/// </summary>
internal sealed class HitCounter
{
    private const int MaxTrackedRocks = 256;

    private readonly int _hitsPerDrop;
    private readonly float _minInterval;
    private readonly Dictionary<int, int> _hitsByRock = new();
    private int _lastSwing = int.MinValue;
    private float _lastHitTime = float.NegativeInfinity;

    internal HitCounter(int hitsPerDrop, float minIntervalSeconds)
    {
        _hitsPerDrop = hitsPerDrop < 1 ? 1 : hitsPerDrop;
        _minInterval = minIntervalSeconds;
    }

    /// <param name="rockKey">Stable id of the rock collider.</param>
    /// <param name="swingId">Id of the current swing, or a negative value if unknown.</param>
    /// <param name="now">Current time in seconds.</param>
    /// <returns>true when this hit should drop a stone.</returns>
    internal bool RegisterHit(int rockKey, int swingId, float now)
    {
        if (swingId >= 0 && swingId == _lastSwing) return false;
        if (now - _lastHitTime < _minInterval) return false;
        _lastSwing = swingId;
        _lastHitTime = now;

        if (_hitsByRock.Count >= MaxTrackedRocks && !_hitsByRock.ContainsKey(rockKey))
            _hitsByRock.Clear();

        _hitsByRock.TryGetValue(rockKey, out var hits);
        hits++;
        if (hits >= _hitsPerDrop)
        {
            _hitsByRock.Remove(rockKey);
            return true;
        }
        _hitsByRock[rockKey] = hits;
        return false;
    }
}
