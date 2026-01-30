using UnityEngine;

public class LobbyCharacterVisuals : MonoBehaviour
{
    [SerializeField] private Transform _spawnPoint;
    [SerializeField] private float _rotateSpeed = 20f;

    private GameObject _currentModel;

    public void ShowCharacter(CharacterData data)
    {
        // 1. Smazat starý model
        if (_currentModel != null) Destroy(_currentModel);

        if (data == null || data.LobbyModelPrefab == null) return;

        // 2. Spawnout nový
        _currentModel = Instantiate(data.LobbyModelPrefab, _spawnPoint);
        
        // 3. Reset transformace
        _currentModel.transform.localPosition = Vector3.zero;
        _currentModel.transform.localRotation = Quaternion.identity;

        // 4. Zajistit Idle animaci (pokud má Animator)
        var anim = _currentModel.GetComponent<Animator>();
        if (anim != null) anim.Play("Idle"); // Předpokládá se stav "Idle"
    }

    private void Update()
    {
        // Pomalá rotace pro efekt
        if (_currentModel != null)
        {
            _currentModel.transform.Rotate(Vector3.up, _rotateSpeed * Time.deltaTime);
        }
    }
}