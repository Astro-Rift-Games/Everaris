using System;

namespace Grimhold.Backend
{
    [Serializable]
    public struct BackendValidationDetail
    {
        public string field;
        public string msg;
    }

    [Serializable]
    public struct LoginRequest
    {
        public string username;
        public string password;
    }

    [Serializable]
    public struct LoginResult
    {
        public string token;
        public int expiresIn;
        // Opaque, single-use refresh credential (FIX-34); absent from legacy backends.
        public string refreshToken;
        public int refreshExpiresIn;
    }

    /// <summary>Body of <c>POST /auth/refresh</c>.</summary>
    [Serializable]
    public struct RefreshRequest
    {
        public string refreshToken;
    }

    /// <summary>Rotated credentials returned by <c>POST /auth/refresh</c>; the old refresh token is spent.</summary>
    [Serializable]
    public struct RefreshResult
    {
        public string token;
        public int expiresIn;
        public string refreshToken;
        public int refreshExpiresIn;
    }

    [Serializable]
    public struct CharacterData
    {
        public string characterId;
        public string name;
    }

    [Serializable]
    public struct CharacterProfileData
    {
        public string characterId;
        public CharacterProfileValues profile;
    }

    [Serializable]
    public struct CharacterProfileValues
    {
        public string lastSeen;
        public string customNote;
    }

    [Serializable]
    public struct PatchProfileRequest
    {
        public string customNote;
    }

    [Serializable]
    public struct CreateCharacterRequest
    {
        public string name;
    }

    [Serializable]
    public struct CreateCharacterResult
    {
        public string characterId;
        public string name;
    }
}
