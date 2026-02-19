using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "GlobalMusicData", menuName = "Audio/Global Music Data")]
public class GlobalMusicData : ScriptableObject
{
    [Header("System")]
    public AudioClip MenuTrack;

    [Header("Combat")]
    [Tooltip("Rychlejší akční smyčky.")]
    public List<AudioClip> ActionTracks;
    
    [Tooltip("Epická hudba pro bosse.")]
    public List<AudioClip> BossTracks;
}