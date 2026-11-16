using System;
using UnityEngine;

namespace CoreGMIC.Core
{
    /// <summary>
    /// Attached to GameCameras.instance.mainCamera to intercept rendered frames via OnRenderImage.
    /// </summary>
    public class PostProcessBridge : MonoBehaviour
    {
        private string _lastFailedFilterId;

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            IFilter activeFilter = FilterManager.ActiveFilter;

            if (!FilterManager.IsEnabled || activeFilter == null || !activeFilter.IsAvailable)
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

                // Fallback to passthrough so the screen never turns black.
                Graphics.Blit(source, destination);
            }
        }
    }
}