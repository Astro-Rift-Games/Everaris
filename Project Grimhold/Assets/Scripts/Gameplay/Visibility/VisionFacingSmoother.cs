using UnityEngine;

namespace ProjectGrimhold.Gameplay.Visibility
{
    /// <summary>
    /// Smooths a viewer's facing once per frame so every consumer of that frame's vision query
    /// (entity visibility and the fog edge) uses the same direction. The first step snaps, and so does
    /// the first step after the viewer went unqueried for a frame or more (spectator switch, respawn).
    /// </summary>
    public sealed class VisionFacingSmoother
    {
        private Vector2 _facing;
        private int _frame;
        private bool _hasFacing;

        public Vector2 Step(Vector2 rawFacing, float sharpness, float deltaTime, int frame)
        {
            if (_hasFacing && frame == _frame)
            {
                return _facing;
            }

            bool continuous = _hasFacing && frame == _frame + 1;
            _facing = continuous ? VisionFogRules.SmoothFacing(_facing, rawFacing, sharpness, deltaTime) : rawFacing;
            _frame = frame;
            _hasFacing = true;
            return _facing;
        }

        /// <summary>Forgets the current facing; the next step snaps to the raw facing.</summary>
        public void Reset() => _hasFacing = false;
    }
}
