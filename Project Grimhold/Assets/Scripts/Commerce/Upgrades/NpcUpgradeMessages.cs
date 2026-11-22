/// <summary>Player-facing Spanish copy for blocked upgrade previews (Doc 23 §§9.4, 11).</summary>
public static class NpcUpgradeMessages
{
    public const string NpcLimit = "Límite de mejora del NPC alcanzado; grados superiores disponibles en Dungeon";
    public const string GradeLocked = "El NPC no modifica escalados de grado B, A o S; grados superiores disponibles en Dungeon";
    public const string SocketExists = "El arma ya tiene una ranura; el NPC solo crea la primera";
    public const string WrongMerchant = "Este mercader no mejora este objeto";
    public const string MissingMaterialsStash = "Faltan materiales en el Inventario; los del Stash no se usan para mejorar.";

    public static string For(NpcUpgradePreview preview)
    {
        if (preview == null) return string.Empty;
        switch (preview.BlockReason)
        {
            case NpcUpgradeBlockReason.None: return string.Empty;
            case NpcUpgradeBlockReason.LimitReached:
                return preview.Operation == NpcUpgradeOperation.ArmorLevel ? "Límite de mejora del NPC alcanzado (+3)" : NpcLimit;
            case NpcUpgradeBlockReason.GradeLocked: return GradeLocked;
            case NpcUpgradeBlockReason.SocketExists: return SocketExists;
            case NpcUpgradeBlockReason.WrongMerchant: return WrongMerchant;
            case NpcUpgradeBlockReason.InsufficientGold:
                return $"Oro insuficiente: se requieren {preview.GoldRequired} y tienes {preview.GoldAvailable}.";
            case NpcUpgradeBlockReason.MissingMaterials:
                foreach (var line in preview.Materials)
                    if (line.AvailableInStash) return MissingMaterialsStash;
                return "Faltan materiales en el Inventario.";
            default: return "Este objeto no admite esta mejora";
        }
    }
}
