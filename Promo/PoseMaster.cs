using UnityEngine;

[ExecuteAlways]
public class PoseMaster : MonoBehaviour
{
    public Animator animator;
    public string stateName = "Attack_Overhead";
    [Range(0f, 1f)] public float playbackTime = 0.5f;
    
    // Přepínač chování
    public bool loop = false; 

    void LateUpdate()
    {
        if (animator == null) return;

        if (loop)
        {
            // REŽIM SMYČKY
            animator.speed = 1f;

            // V Editor Mode (ne Play Mode) se animátor sám neaktualizuje, musíme ho posunout manuálně
            if (!Application.isPlaying)
            {
                animator.Update(Time.deltaTime);
            }
            // V Play Mode (Ingame) se o to Unity postará samo, když je speed > 0
        }
        else
        {
            // REŽIM ZMRAZENÍ (Původní logika)
            animator.speed = 0f;

            if (Application.isPlaying)
            {
                animator.Play(stateName, 0, playbackTime);
            }
            else
            {
                animator.Play(stateName, 0, playbackTime);
                animator.Update(0);
            }
        }
    }
}