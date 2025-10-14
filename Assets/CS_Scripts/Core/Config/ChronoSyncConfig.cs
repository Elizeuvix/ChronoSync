using System;

// Central config for developer-provided settings
namespace CS.Core.Config
{
    public static class ChronoSyncConfig
    {
        // Define your API key here in code so it is not stored in PlayerPrefs or requested from the player.
        // NOTE: Shipping a client with a hardcoded key is convenient but not secure for public releases.
    public const string API_KEY = "-AEWqRS0krRjIsg2Jvm_7S7R4n4fYtXVdjey4eVibqE"; // set to your developer key

        // Optional centralized endpoints (leave empty to keep per-component Inspector control)
        public const string API_BASE = "http://192.168.3.196:8100"; // HTTP base for Auth/Register/Login
        public const string WS_URL   = ""; // If empty, WebSocket URL will be derived from API_BASE by components when enabled

        // Default room settings (used when not overridden in the scene / inspector)
        public const string DEFAULT_ROOM_NAME = ""; // leave empty to let flow set/choose at runtime
        public const int DEFAULT_MAX_PLAYERS = 2;    // aligns with ChronoSyncCore default
    }
}