public enum UnitType
{
    Gatherer,
    Attacker,
    Queen,
    Child,

    // Appended rather than inserted: these values are serialized as ints on every unit prefab, so
    // reordering them would silently retype existing units.
    Builder
}
