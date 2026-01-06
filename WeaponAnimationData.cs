using UnityEngine;

[CreateAssetMenu(fileName = "WeaponAnims", menuName = "Game/Weapon Animation Data")]
public class WeaponAnimationData : ScriptableObject
{
    // Zde jsou reference na animační klipy
    [Tooltip("Animace pro stav Idle")]
    public AnimationClip Idle;
    
    [Tooltip("Animace pro chůzi vpřed")]
    public AnimationClip Walk;

    [Tooltip("Animace pro základní útok")]
    public AnimationClip Attack1;
    
    // ... přidejte další podle potřeby (Attack2, Run, Block...)
}