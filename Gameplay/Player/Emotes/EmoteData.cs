using UnityEngine;

[CreateAssetMenu(fileName = "NewEmote", menuName = "Emotes/Emote Data")]
public class EmoteData : ScriptableObject
{
    [Header("Identifikace")]
    public string EmoteName = "Dance";
    
    [Header("Nastavení Animátoru")]
    [Tooltip("Název Triggeru v Animatoru (např. 'Emote_Dance')")]
    public string AnimatorTriggerName;

    [Header("Volitelné")]
    [Tooltip("Zvuk, který se přehraje při emotu (pokud nějaký je)")]
    public AudioClip EmoteSound;
    
    [Tooltip("Má se emote zrušit pohybem?")]
    public bool CancelOnMove = true;
}