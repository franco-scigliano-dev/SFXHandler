using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Audio;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace com.fscigliano.SFXHandler
{
    /// <summary>
    /// Optimized audio reference asset for Unity 6 with performance improvements.
    /// Supports AudioResource with efficient loading and caching.
    /// </summary>
    [CreateAssetMenu(fileName = nameof(AudioReferenceAsset), menuName = k_menuPath + nameof(AudioReferenceAsset))]
    public class AudioReferenceAsset : ScriptableObject
    {
        private const string k_menuPath = "Audio/";

        public enum SpamProtectionType
        {
            OVERRIDE,
            AVOID
        }

        [Header("Audio Resource")] [SerializeField]
        private AssetReferenceT<AudioResource> _audioResourceReference;

        [Header("Audio Settings")] [SerializeField, Range(0f, 1f)]
        public float volume = 1f;

        [SerializeField, Range(0.1f, 3f)] public float pitch = 1f;
        [SerializeField] public bool loop = false;
        [SerializeField] public SpamProtectionType spamProtection = SpamProtectionType.OVERRIDE;

        [Header("Metadata")] [SerializeField] public string[] labels = Array.Empty<string>();
        [SerializeField, TextArea(3, 5)] public string description = "";

        // Runtime state
        private bool _isLoaded = false;
        private bool _isLoading = false;
        private AudioResource _loadedResource;
        private AsyncOperationHandle<AudioResource> _loadHandle;
        private Task<AudioResource> _loadTask;

        // Cached properties for performance
        private bool? _cachedValidReference;
        private string _cachedAssetGuid;

        public bool IsLoaded => _isLoaded;
        public AudioResource LoadedResource => _loadedResource;

        /// <summary>
        /// Checks if the audio reference is valid and can be loaded (cached for performance)
        /// </summary>
        public bool HasValidReference()
        {
            if (_cachedValidReference.HasValue && _cachedAssetGuid == _audioResourceReference?.AssetGUID)
                return _cachedValidReference.Value;

            _cachedAssetGuid = _audioResourceReference?.AssetGUID;
            _cachedValidReference = _audioResourceReference != null && _audioResourceReference.RuntimeKeyIsValid();
            return _cachedValidReference.Value;
        }

        /// <summary>
        /// Fast non-allocating setup method for high-frequency audio
        /// </summary>
        public bool TrySetAudioResource(AudioSource audioSource, float volumeMultiplier = 1f,
            float pitchMultiplier = 1f)
        {
            if (!_isLoaded) return false;

            audioSource.clip = null;
            audioSource.resource = _loadedResource;
            var inVolume = volume * volumeMultiplier;
            // Skip property changes if they're already correct
            if (!Mathf.Approximately(audioSource.volume, inVolume)) audioSource.volume = inVolume;

            var inPitch = pitch * pitchMultiplier;
            if (!Mathf.Approximately(audioSource.pitch, inPitch)) audioSource.pitch = inPitch;

            if (audioSource.loop != loop) audioSource.loop = loop;

            return true;
        }

        /// <summary>
        /// Optimized async loading with task caching
        /// </summary>
        public async Task<AudioResource> LoadAsync()
        {
            // Return cached result if already loaded
            if (_isLoaded && _loadedResource != null)
                return _loadedResource;

            // Return existing load task if already loading
            if (_isLoading && _loadTask != null)
                return await _loadTask;

            if (!HasValidReference())
                return null;

            _loadTask = LoadInternalAsync();
            return await _loadTask;
        }

        private async Task<AudioResource> LoadInternalAsync()
        {
            _isLoading = true;

            try
            {
                if (_audioResourceReference.OperationHandle.IsValid())
                {
                    if (_audioResourceReference.OperationHandle.IsDone)
                    {
                        _loadedResource = _audioResourceReference.OperationHandle.Result as AudioResource;
                    }
                    else
                    {
                        await _audioResourceReference.OperationHandle.Task;
                        _loadedResource = _audioResourceReference.OperationHandle.Result as AudioResource;
                    }
                }
                else
                {
                    _loadHandle = _audioResourceReference.LoadAssetAsync<AudioResource>();
                    _loadedResource = await _loadHandle.Task;
                }

                _isLoaded = _loadedResource != null;
                return _loadedResource;
            }
            catch (Exception e)
            {
                Debug.LogError($"AudioReferenceAsset: Failed to load '{name}': {e.Message}");
                return null;
            }
            finally
            {
                _isLoading = false;
                _loadTask = null;
            }
        }

        /// <summary>
        /// Unloads the audio resource
        /// </summary>
        public void Unload()
        {
            if (_loadHandle.IsValid())
            {
                _audioResourceReference.ReleaseAsset();
                _loadHandle = default;
            }

            _loadedResource = null;
            _isLoaded = false;
            _isLoading = false;
            _loadTask = null;
        }

        /// <summary>
        /// Fast validation without logging for production builds
        /// </summary>
        public bool ValidateConfiguration()
        {
            if (!HasValidReference())
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"AudioReferenceAsset '{name}': No valid audio resource reference assigned");
#endif
                return false;
            }

            return volume >= 0f && volume <= 1f && pitch >= 0.1f && pitch <= 3f;
        }

        private void OnDestroy()
        {
            Unload();
        }

        private void OnValidate()
        {
            volume = Mathf.Clamp01(volume);
            pitch = Mathf.Clamp(pitch, 0.1f, 3f);

            // Clear cached validation when properties change
            _cachedValidReference = null;
        }
    }
}