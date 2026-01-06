using UnityEngine;

public class WobbleBone : MonoBehaviour
{
    [Header("Wobble Settings")]
    [SerializeField] private float _maxWobble = 5f; // Maximální úhel vychýlení
    [SerializeField] private float _wobbleSpeed = 10f; // Jak rychle se hýbe
    [SerializeField] private float _recovery = 5f; // Jak rychle se vrací zpět

    private Vector3 _wobbleRotation;
    private Vector3 _lastPosition;
    private Vector3 _velocity;

    private void Start()
    {
        _lastPosition = transform.position;
    }

    private void LateUpdate()
    {
        // 1. Zjistit pohyb od posledního framu
        Vector3 movement = transform.InverseTransformDirection(transform.position - _lastPosition);
        
        // 2. Vypočítat vychýlení (inverzní k pohybu)
        // Když jdu doleva (X < 0), chci rotaci doprava (Z < 0) - zjednodušeně
        float wobX = -movement.z * _maxWobble; // Předozadní pohyb -> rotace X
        float wobZ = movement.x * _maxWobble;  // Boční pohyb -> rotace Z

        // 3. Aplikovat sílu do wobble rotace
        _wobbleRotation.x += wobX;
        _wobbleRotation.z += wobZ;

        // 4. Lerp zpět do nuly (pružina)
        _wobbleRotation = Vector3.Lerp(_wobbleRotation, Vector3.zero, Time.deltaTime * _recovery);

        // 5. Omezit extrém
        _wobbleRotation.x = Mathf.Clamp(_wobbleRotation.x, -_maxWobble, _maxWobble);
        _wobbleRotation.z = Mathf.Clamp(_wobbleRotation.z, -_maxWobble, _maxWobble);

        // 6. Aplikovat na objekt (přičíst k existující animaci/rotaci)
        // Používáme Rotate, ne localRotation =, abychom zachovali animaci
        transform.Rotate(Vector3.right, _wobbleRotation.x * Time.deltaTime * _wobbleSpeed);
        transform.Rotate(Vector3.forward, _wobbleRotation.z * Time.deltaTime * _wobbleSpeed);

        _lastPosition = transform.position;
    }
}