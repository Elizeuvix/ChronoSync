using System;

// Central config for developer-provided settings
namespace CS.Core.Config
{
    public static class ChronoSyncConfig
    {
        // Define your API key here in code so it is not stored in PlayerPrefs or requested from the player.
        // NOTE: Shipping a client with a hardcoded key is convenient but not secure for public releases.
        public const string API_KEY = "CHRONOSYNC_DEV_KEY"; // TODO: set your real key here

        // Default room settings (used when not overridden in the scene / inspector)
        public const string DEFAULT_ROOM_NAME = ""; // leave empty to let flow set/choose at runtime
        public const int DEFAULT_MAX_PLAYERS = 2;    // aligns with ChronoSyncCore default
    }
}