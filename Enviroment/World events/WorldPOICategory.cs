using System;

[Flags]
public enum WorldPOICategory
{
    None = 0,
    Shrine = 1 << 0,
    Chest = 1 << 1,
    Forge = 1 << 2,
    Altar = 1 << 3,
    Obelisk = 1 << 4,
    Hazard = 1 << 5,
    Portal = 1 << 6,
    Any = ~0
}