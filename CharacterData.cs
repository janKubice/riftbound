using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "Riftbound/Character Data")]
public class CharacterData : ScriptableObject
{
    [Header("UI Info")]
    public string CharacterName;
    [TextArea] public string Description;
    public Sprite Icon;

    [Header("Visuals")]
    public GameObject LobbyModelPrefab; // Model pro zobrazení v lobby (bez složité logiky)
    
    [Header("Stats Display")]
    [Range(0, 10)] public int DamageRating;
    [Range(0, 10)] public int SpeedRating;
    [Range(0, 10)] public int DefenseRating;
}