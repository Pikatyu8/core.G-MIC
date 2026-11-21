using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class PlayerSwirlFilter : FilterBase
    {
        private static readonly string[] SwirlModeChoices =
        {
            "Classic (dist^2)",
            "Edge Vortex (dist^8 * 0.1)",
            "Custom"
        };

        private static readonly string[] CenterModeChoices =
        {
            "Player",
            "Screen Center"
        };

        private static readonly string[] BoundaryChoices =
        {
            "Transparent",
            "Nearest",
            "Periodic",
            "Mirror"
        };

        private int _mode = 0;
        private float _strength = 1.0f;
        private float _exponent = 2.0f;
        private int _centerMode = 0;
        private int _boundaryMode = 3;

        public override string Id => "fx_player_swirl";
        public override string DisplayName => "Player Swirl";
        public override string Description => "Rotates each pixel around the player by an angle proportional to distance^N.";
        public override FilterCategory Category => FilterCategory.Standard;

        public PlayerSwirlFilter()
        {
            AddParameter(FilterParameter.Choice(
                "mode",
                "Mode",
                "Angle formula preset (dist^2, dist^8 * 0.1, or custom exponent).",
                SwirlModeChoices,
                0,
                v => _mode = v));

            AddParameter(FilterParameter.Float(
                "strength",
                "Strength",
                "Rotation multiplier (negative reverses swirl direction).",
                -10f, 10f, 1.0f,
                v => _strength = v,
                step: 0.1f));

            AddParameter(FilterParameter.Float(
                "exponent",
                "Distance Power",
                "Exponent applied to distance from center.",
                0.25f, 12.0f, 2.0f,
                v => _exponent = v,
                isVisible: () => _mode == 2,
                step: 0.25f));

            AddParameter(FilterParameter.Choice(
                "center_mode",
                "Center",
                "Pivot point used for distance and rotation.",
                CenterModeChoices,
                0,
                v => _centerMode = v));

            AddParameter(FilterParameter.Choice(
                "boundary",
                "Boundary",
                "Sampling behavior when rotation reaches outside the frame.",
                BoundaryChoices,
                3,
                v => _boundaryMode = v));
        }

        public override void Initialize()
        {
            TryCreateMaterial("CoreGMIC/PlayerSwirl");
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            Vector2 centerUv = GetCenterUv();

            float effectiveStrength;
            float effectiveExponent;
            if (_mode == 0)
            {
                effectiveStrength = _strength;
                effectiveExponent = 2.0f;
            }
            else if (_mode == 1)
            {
                effectiveStrength = _strength * 0.1f;
                effectiveExponent = 8.0f;
            }
            else
            {
                effectiveStrength = _strength;
                effectiveExponent = _exponent;
            }

            Material.SetVector("_CenterUv", centerUv);
            Material.SetFloat("_Strength", effectiveStrength);
            Material.SetFloat("_Exponent", effectiveExponent);
            Material.SetInt("_BoundaryMode", _boundaryMode);

            Graphics.Blit(source, destination, Material);
        }

        private Vector2 GetCenterUv()
        {
            if (_centerMode == 1)
            {
                return new Vector2(0.5f, 0.5f);
            }

            HeroController hero = HeroController.UnsafeInstance;
            GameCameras cameras = GameCameras.instance;
            Camera cam = (cameras != null && cameras.mainCamera != null) ? cameras.mainCamera : Camera.main;

            if (hero == null || cam == null || !hero.gameObject.activeInHierarchy)
            {
                return new Vector2(0.5f, 0.5f);
            }

            Vector3 vp = cam.WorldToViewportPoint(hero.transform.position);
            if (vp.z <= 0f || vp.x < -2f || vp.x > 3f || vp.y < -2f || vp.y > 3f)
            {
                return new Vector2(0.5f, 0.5f);
            }

            return new Vector2(vp.x, vp.y);
        }
    }
}