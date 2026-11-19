using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoreGMIC.Core
{
    /// <summary>
    /// Attached to GameCameras.instance.mainCamera to intercept rendered frames via OnRenderImage
    /// and re-render excluded entities cleanly on an overlay camera between mainCamera and hudCamera.
    /// </summary>
    public class PostProcessBridge : MonoBehaviour
    {
        private const int ExclusionLayer = 31; // Unused in GlobalEnums.PhysLayers (max is 30).

        private Camera _camera;
        private Camera _overlayCamera;
        private OverlayCameraHook _overlayHook;
        private Behaviour _fastNoise;
        private bool _fastNoiseSuppressed;
        private bool _savedFastNoiseEnabled;
        private string _lastFailedFilterId;

        private readonly List<Renderer> _hiddenThisFrame = new List<Renderer>();
        private readonly List<Renderer> _overlayRenderers = new List<Renderer>();

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _fastNoise = GetComponent("FastNoise") as Behaviour;
            CreateOverlayCamera();
        }

        private void OnEnable()
        {
            if (_overlayCamera != null)
            {
                _overlayCamera.enabled = true;
            }
        }

        private void OnDisable()
        {
            RestoreFastNoise();
            RestoreHiddenRenderers();
            _overlayRenderers.Clear();
            if (_overlayCamera != null)
            {
                _overlayCamera.enabled = false;
            }
        }

        private void OnDestroy()
        {
            RestoreFastNoise();
            RestoreHiddenRenderers();
            _overlayRenderers.Clear();
            if (_overlayCamera != null)
            {
                Destroy(_overlayCamera.gameObject);
                _overlayCamera = null;
                _overlayHook = null;
            }
        }

        private void OnPreCull()
        {
            RestoreHiddenRenderers();
            _overlayRenderers.Clear();

            IFilter activeFilter = FilterManager.ActiveFilter;
            bool filterActive = !ShouldBypassFilters() && FilterManager.IsEnabled && activeFilter != null && activeFilter.IsAvailable;
            bool suppressNoise = (CoreGMICMod.Settings != null && CoreGMICMod.Settings.NoFastNoise) ||
                                 (filterActive && activeFilter.SuppressFastNoise);
            UpdateFastNoiseSuppression(suppressNoise);

            if (!filterActive || !EntityExclusionManager.HasAnyExclusion)
            {
                return;
            }

            EntityExclusionManager.CollectActiveExcludedRenderers(_hiddenThisFrame);
            for (int i = 0; i < _hiddenThisFrame.Count; i++)
            {
                Renderer r = _hiddenThisFrame[i];
                if (r != null)
                {
                    r.forceRenderingOff = true;
                    _overlayRenderers.Add(r);
                }
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            IFilter activeFilter = FilterManager.ActiveFilter;

            if (ShouldBypassFilters() || !FilterManager.IsEnabled || activeFilter == null || !activeFilter.IsAvailable)
            {
                Graphics.Blit(source, destination);
                return;
            }

            try
            {
                activeFilter.Apply(source, destination);
                _lastFailedFilterId = null;
            }
            catch (Exception ex)
            {
                if (_lastFailedFilterId != activeFilter.Id)
                {
                    _lastFailedFilterId = activeFilter.Id;
                    CoreGMICMod.Instance?.LogError($"Error applying filter '{activeFilter.Id}': {ex}");
                }

                Graphics.Blit(source, destination);
            }
        }

        private void OnPostRender()
        {
            RestoreHiddenRenderers();
        }

        private static bool ShouldBypassFilters()
        {
            GameManager gm = GameManager.UnsafeInstance;
            return gm != null && gm.gameState == GlobalEnums.GameState.MAIN_MENU;
        }

        private void UpdateFastNoiseSuppression(bool shouldSuppress)
        {
            if (_fastNoise == null)
            {
                _fastNoise = GetComponent("FastNoise") as Behaviour;
                if (_fastNoise == null)
                {
                    return;
                }
            }

            if (shouldSuppress)
            {
                if (!_fastNoiseSuppressed)
                {
                    _savedFastNoiseEnabled = _fastNoise.enabled;
                    _fastNoiseSuppressed = true;
                }
                else if (_fastNoise.enabled)
                {
                    // CameraController.ApplyEffectConfiguration may re-enable FastNoise on scene load.
                    _savedFastNoiseEnabled = true;
                }

                if (_fastNoise.enabled)
                {
                    _fastNoise.enabled = false;
                }
            }
            else
            {
                RestoreFastNoise();
            }
        }

        private void RestoreFastNoise()
        {
            if (_fastNoiseSuppressed && _fastNoise != null)
            {
                _fastNoise.enabled = _savedFastNoiseEnabled;
            }

            _fastNoiseSuppressed = false;
        }

        private void CreateOverlayCamera()
        {
            if (_overlayCamera != null)
            {
                return;
            }

            GameObject camGo = new GameObject("CoreGMIC_ExclusionOverlayCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            camGo.transform.SetParent(transform, false);

            _overlayCamera = camGo.AddComponent<Camera>();
            _overlayCamera.clearFlags = CameraClearFlags.Nothing;
            _overlayCamera.cullingMask = 1 << ExclusionLayer;
            _overlayCamera.depthTextureMode = DepthTextureMode.None;
            _overlayCamera.enabled = enabled;

            _overlayHook = camGo.AddComponent<OverlayCameraHook>();
            _overlayHook.Init(_camera, _overlayCamera, _overlayRenderers);
        }

        private void RestoreHiddenRenderers()
        {
            for (int i = 0; i < _hiddenThisFrame.Count; i++)
            {
                Renderer r = _hiddenThisFrame[i];
                if (r != null)
                {
                    r.forceRenderingOff = false;
                }
            }

            _hiddenThisFrame.Clear();
        }

        private sealed class OverlayCameraHook : MonoBehaviour
        {
            private readonly struct SavedLayer
            {
                public readonly GameObject GameObject;
                public readonly int Layer;

                public SavedLayer(GameObject gameObject, int layer)
                {
                    GameObject = gameObject;
                    Layer = layer;
                }
            }

            private Camera _mainCam;
            private Camera _overlayCam;
            private List<Renderer> _renderers;
            private readonly List<SavedLayer> _savedLayers = new List<SavedLayer>();
            private readonly HashSet<GameObject> _seenObjects = new HashSet<GameObject>();

            public void Init(Camera mainCam, Camera overlayCam, List<Renderer> renderers)
            {
                _mainCam = mainCam;
                _overlayCam = overlayCam;
                _renderers = renderers;
            }

            private void OnDisable()
            {
                RestoreLayers();
            }

            private void OnDestroy()
            {
                RestoreLayers();
            }

            private void OnPreCull()
            {
                RestoreLayers();

                if (_mainCam == null || _overlayCam == null || _renderers == null || _renderers.Count == 0)
                {
                    if (_overlayCam != null)
                    {
                        _overlayCam.cullingMask = 0;
                    }
                    return;
                }

                Camera hudCam = GameCameras.instance != null ? GameCameras.instance.hudCamera : null;
                float targetDepth = (hudCam != null && hudCam.depth > _mainCam.depth)
                    ? 0.5f * (_mainCam.depth + hudCam.depth)
                    : (_mainCam.depth + 0.5f);

                _overlayCam.CopyFrom(_mainCam);
                _overlayCam.projectionMatrix = _mainCam.projectionMatrix;
                _overlayCam.clearFlags = CameraClearFlags.Nothing;
                _overlayCam.cullingMask = 1 << ExclusionLayer;
                _overlayCam.depthTextureMode = DepthTextureMode.None;
                _overlayCam.depth = targetDepth;
                _overlayCam.enabled = true;

                for (int i = 0; i < _renderers.Count; i++)
                {
                    Renderer r = _renderers[i];
                    if (r == null)
                    {
                        continue;
                    }

                    r.forceRenderingOff = false;
                    if (!r.enabled || !r.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    GameObject go = r.gameObject;
                    if (_seenObjects.Add(go))
                    {
                        _savedLayers.Add(new SavedLayer(go, go.layer));
                        go.layer = ExclusionLayer;
                    }
                }
            }

            private void OnPostRender()
            {
                RestoreLayers();
                _renderers?.Clear();
            }

            private void RestoreLayers()
            {
                for (int i = 0; i < _savedLayers.Count; i++)
                {
                    SavedLayer entry = _savedLayers[i];
                    if (entry.GameObject != null)
                    {
                        entry.GameObject.layer = entry.Layer;
                    }
                }

                _savedLayers.Clear();
                _seenObjects.Clear();
            }
        }
    }
}