// Runtime třída pro aktivní efekt
using UnityEngine;

[System.Serializable]
    public class ActiveEffect
    {
        public StatusEffectData Data;
        public float Timer;
        public float TickTimer;
        public int Stacks;
        public GameObject SpawnedVFX; // Reference na vizuál (pouze Client)

        public ActiveEffect(StatusEffectData data)
        {
            Data = data;
            Timer = data.Duration;
            TickTimer = 0f;
            Stacks = 1;
        }
    }
