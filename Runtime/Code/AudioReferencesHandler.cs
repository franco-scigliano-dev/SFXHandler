using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using System.Threading.Tasks;

namespace com.fscigliano.SFXHandler
{
    /// <summary>
    /// Modern audio system that uses AudioReferenceAsset for configuration.
    /// Supports both AudioClips and AudioResources (Audio Random Containers) with addressable loading.
    /// No string-based references - only type-safe AudioReferenceAsset.
    /// </summary>
    public class AudioReferencesHandler : MonoBehaviour
    {
        [Header("References")] [SerializeField]
        protected AudioMixerGroup _audioMixerGroup;

        [SerializeField] protected AudioReferenceAsset[] _preloadAudioAssets;
        [SerializeField] protected bool _unloadAllPreloaded = false;
        [SerializeField] protected AudioReferenceAsset[] _unloadAudioAssets;
        [SerializeField] protected List<AudioSource> _audioSources = new();
        [SerializeField] protected bool _autoStart = true;

        protected bool _isInitialized = false;

        protected virtual void Start()
        {
            if (_autoStart)
            {
                Init();
            }
        }

        public virtual async Task Init()
        {
            try
            {
                InitializeAudioSources();
                await InitializeAsync();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        }

        /// <summary>
        /// Initializes the pool of AudioSource components
        /// </summary>
        protected virtual void InitializeAudioSources()
        {
            for (int i = 0; i < _audioSources.Count; i++)
            {
                AudioSource audioSource = _audioSources[i];
                audioSource.playOnAwake = false;
                audioSource.outputAudioMixerGroup = _audioMixerGroup;
            }
        }

        /// <summary>
        /// Initializes the audio system asynchronously based on loading strategy
        /// </summary>
        public virtual async Task InitializeAsync()
        {
            if (_isInitialized) return;

            await PreloadAllAudio();

            _isInitialized = true;
        }

        /// <summary>
        /// Preloads all audio in the system
        /// </summary>
        public virtual async Task PreloadAllAudio()
        {
            var tasks = new List<Task>();
            for (var index = 0; index < _preloadAudioAssets.Length; index++)
            {
                var audioData = _preloadAudioAssets[index];
                tasks.Add(audioData.LoadAsync());
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Plays audio using an AudioReferenceAsset with optional volume and pitch override
        /// </summary>
        public virtual async Task<AudioSource> PlayAudioAsync(AudioReferenceAsset audioReference,
            float volumeMultiplier = 1f, float pitchMultiplier = 1f)
        {
            if (audioReference == null)
            {
                return null;
            }

            if (!audioReference.IsLoaded)
            {
                await audioReference.LoadAsync();
            }

            return PlayAudioInternal(audioReference, volumeMultiplier, pitchMultiplier);
        }

        private AudioSource PlayAudioInternal(AudioReferenceAsset audioReference, float volumeMultiplier,
            float pitchMultiplier)
        {
            if (audioReference == null)
            {
                Debug.LogError($"Audio reference is null.");
            }

            // Handle spam protection
            AudioSource playingSource = GetFirstPlaying(audioReference);
            if (playingSource != null)
            {
                switch (audioReference.spamProtection)
                {
                    case AudioReferenceAsset.SpamProtectionType.OVERRIDE:
                        playingSource.Stop();
                        break;
                    case AudioReferenceAsset.SpamProtectionType.AVOID:
                        return playingSource;
                }
            }

            // Get available audio source
            AudioSource freeSource = GetFreeAudioSource();
            if (freeSource == null)
            {
                return null;
            }

            // Use the fast setup for better performance
            bool success = audioReference.TrySetAudioResource(freeSource, volumeMultiplier, pitchMultiplier);
            if (!success)
            {
                return null;
            }

            freeSource.Play();

            return freeSource;
        }

        /// <summary>
        /// Synchronous version that only works if the audio is already loaded
        /// </summary>
        public virtual AudioSource PlayAudio(AudioReferenceAsset audioReference, float volumeMultiplier = 1f,
            float pitchMultiplier = 1f)
        {
            if (!audioReference.IsLoaded)
            {
                Debug.LogWarning($"Audio reference '{audioReference.name}' is not loaded.");
                return null;
            }

            return PlayAudioInternal(audioReference, volumeMultiplier, pitchMultiplier);
        }

        /// <summary>
        /// Plays audio at a specific world position (3D audio)
        /// </summary>
        public virtual async Task<AudioSource> PlayAudioAtPositionAsync(AudioReferenceAsset audioReference,
            Vector3 position, float volumeMultiplier = 1f, float pitchMultiplier = 1f)
        {
            AudioSource audioSource = await PlayAudioAsync(audioReference, volumeMultiplier, pitchMultiplier);
            if (audioSource != null)
            {
                audioSource.transform.position = position;
                audioSource.spatialBlend = 1f; // 3D
            }

            return audioSource;
        }

        /// <summary>
        /// Stops all instances of a specific audio
        /// </summary>
        public virtual void StopAudio(AudioReferenceAsset audioReference)
        {
            if (!audioReference.IsLoaded) return;

            for (var index = 0; index < _audioSources.Count; index++)
            {
                var audioSource = _audioSources[index];
                if (audioSource.isPlaying)
                {
                    // Check if this source is playing our audio resource
                    if (audioReference.LoadedResource != null && audioSource.resource == audioReference.LoadedResource)
                    {
                        audioSource.Stop();
                    }
                }
            }
        }

        /// <summary>
        /// Stops all currently playing audio
        /// </summary>
        public virtual void StopAllAudio()
        {
            foreach (var audioSource in _audioSources)
            {
                if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }
            }
        }

        protected virtual AudioSource GetFirstPlaying(AudioReferenceAsset audioData)
        {
            if (!audioData.IsLoaded) return null;

            for (var index = 0; index < _audioSources.Count; index++)
            {
                var audioSource = _audioSources[index];
                if (audioSource.isPlaying)
                {
                    // Check if this source is playing our audio resource
                    if (audioData.LoadedResource != null && audioSource.resource == audioData.LoadedResource)
                    {
                        return audioSource;
                    }
                }
            }

            return null;
        }

        protected virtual AudioSource GetFreeAudioSource()
        {
            for (var index = 0; index < _audioSources.Count; index++)
            {
                var audioSource = _audioSources[index];
                if (!audioSource.isPlaying)
                    return audioSource;
            }

            return null;
        }


        protected virtual void OnDestroy()
        {
            var toUnload = _unloadAllPreloaded ? _preloadAudioAssets : _unloadAudioAssets;

            foreach (var audioData in toUnload)
            {
                if (audioData != null && audioData.IsLoaded)
                {
                    audioData.Unload();
                }
            }

            // Stop all audio sources to prevent any lingering playback
            if (_audioSources != null)
            {
                foreach (var audioSource in _audioSources)
                {
                    try
                    {
                        if (audioSource != null && audioSource.isPlaying)
                        {
                            audioSource.Stop();
                            audioSource.clip = null;
                            audioSource.resource = null;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"AudioHandler: Error stopping audio source: {e.Message}");
                    }
                }
            }

            _isInitialized = false;
        }
    }
}