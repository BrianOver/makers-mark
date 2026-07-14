namespace GameSim.Contracts;

/// <summary>Deterministic integer identity for a hero. Allocated by the kernel counter, never random.</summary>
public readonly record struct HeroId(int Value)
{
    public override string ToString() => $"H{Value}";
}

/// <summary>Deterministic integer identity for a crafted or vendor item. The maker's mark hangs off this.</summary>
public readonly record struct ItemId(int Value)
{
    public override string ToString() => $"I{Value}";
}

/// <summary>Deterministic integer identity for a posted bounty.</summary>
public readonly record struct BountyId(int Value)
{
    public override string ToString() => $"B{Value}";
}

/// <summary>Deterministic integer identity for a game event; gossip lines must reference one (R14).</summary>
public readonly record struct EventId(int Value)
{
    public override string ToString() => $"E{Value}";
}
