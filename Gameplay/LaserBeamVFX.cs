using UnityEngine;

public class LaserBeamVFX : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Válec (Cylinder) s novým materiálem.")]
    [SerializeField] private Transform _meshRoot;
    [SerializeField] private Renderer _renderer;

    [Header("Settings")]
    [Tooltip("Šířka paprsku.")]
    [SerializeField] private float _beamWidth = 0.5f;
    [Tooltip("Opakování textury na metr délky.")]
    [SerializeField] private float _textureTiling = 0.5f;

    private MaterialPropertyBlock _propBlock;
    private static readonly int MainTexSt = Shader.PropertyToID("_MainTex_ST");

    private void Awake()
    {
        _propBlock = new MaterialPropertyBlock();
        // Vypneme stíny, laser je světlo
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        StopBeam();
    }

    public void UpdateBeam(Vector3 start, Vector3 end, Vector3 hitNormal = default)
    {
        if (!_meshRoot.gameObject.activeSelf) _meshRoot.gameObject.SetActive(true);

        float distance = Vector3.Distance(start, end);
        
        // 1. Pozicování: Střed mezi body
        _meshRoot.position = (start + end) / 2f;
        
        // 2. Rotace: Natočení k cíli
        // Unity Cylinder je orientovaný nahoru (Y). Aby ležel mezi body, 
        // musíme ho natočit tak, aby jeho Y osa ("up") směřovala k cíli.
        _meshRoot.up = end - start;

        // 3. Škálování
        // X/Z je tloušťka, Y je délka (u Unity Cylinderu je výška 2 jednotky, proto dělíme 2)
        _meshRoot.localScale = new Vector3(_beamWidth, distance * 0.5f, _beamWidth);

        // 4. Korekce textury (UV Tiling)
        // Aby se textura nenatáhla, musíme upravit Tiling Y v shaderu
        _renderer.GetPropertyBlock(_propBlock);
        
        // Tiling.y se musí zvyšovat s délkou
        Vector4 st = new Vector4(1, distance * _textureTiling, 0, 0);
        _propBlock.SetVector(MainTexSt, st);
        
        _renderer.SetPropertyBlock(_propBlock);
    }

    public void StopBeam()
    {
        if (_meshRoot != null) _meshRoot.gameObject.SetActive(false);
    }
}