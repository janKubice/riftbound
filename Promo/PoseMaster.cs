using UnityEngine;

[ExecuteAlways]
public class PoseMaster : MonoBehaviour
{
    public Animator animator;
    public string stateName = "Attack_Overhead";
    [Range(0f, 1f)] public float playbackTime = 0.5f;
    public bool loop = false;

    private float _lastPlaybackTime;
    private string _lastStateName;
    private bool _wasLooping;

    void Update()
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;

        if (loop)
        {
            animator.speed = 1f;

            if (!animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))
            {
                animator.Play(stateName, 0, 0f);
            }

            if (!Application.isPlaying)
            {
                animator.Update(Time.deltaTime);
            }
        }
        else
        {
            animator.speed = 0f;

            if (playbackTime != _lastPlaybackTime || stateName != _lastStateName || loop != _wasLooping)
            {
                animator.Play(stateName, 0, playbackTime);
                animator.Update(0f);
                
                _lastPlaybackTime = playbackTime;
                _lastStateName = stateName;
            }
        }
        
        _wasLooping = loop;
    }
}