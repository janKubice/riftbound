public enum ArenaState
{
    Waiting,    // Čekání na hráče (méně než 2)
    Countdown,  // Odpočet (2+ hráči připraveni)
    Fighting,   // Boj probíhá
    Ending      // Konec boje, návrat do lobby
}