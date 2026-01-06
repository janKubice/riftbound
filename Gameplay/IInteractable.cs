using Unity.Netcode;

public interface IInteractable
{
    // Text, který se zobrazí (např. "E - Sebrat Meč")
    string InteractionPrompt { get; }

    // Akce, která se provede na serveru
    // 'interactor' je hráč, který akci provedl
    void Interact(NetworkObject interactor);
}