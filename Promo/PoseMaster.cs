using UnityEngine;

[ExecuteAlways]
public class PoseMaster : MonoBehaviour
{
    public Animator animator;
    public string stateName = "Attack_Overhead"; // MUSÍ se shodovat s názvem v Animator okně (ne název souboru!)
    [Range(0f, 1f)] public float playbackTime = 0.5f; // 0% až 100%

    // Použijeme LateUpdate, protože ten běží AŽ PO interním update Animátoru.
    // Tím přepíšeme cokoliv, co se Animátor snažil udělat (reset do stání).
    void LateUpdate()
    {
        if (animator == null) return;

        // 1. Zastavíme interní hodiny animátoru
        animator.speed = 0f;

        // 2. Natvrdo vnutíme stav a čas
        if (Application.isPlaying)
        {
            // Ve hře musíme volat Play, aby přeskočil přechody (Transitions)
            animator.Play(stateName, 0, playbackTime);
        }
        else
        {
            // V editoru používáme toto pro preview
            animator.Play(stateName, 0, playbackTime);
            animator.Update(0);
        }
    }
}