namespace MinecraftClone.Management
{
    public enum WorldGameMode
    {
        Legacy = 0,
        Classic024SurvivalTest = 1
    }

    /// <summary>Выбранный слот мира (1..5). Одно место хранения вместо PlayerPrefs.</summary>
    public static class WorldSlotSelection
    {
        public static int Slot = 1;
        public static WorldGameMode NewWorldMode = WorldGameMode.Classic024SurvivalTest;
    }
}
