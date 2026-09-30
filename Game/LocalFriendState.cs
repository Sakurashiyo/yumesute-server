sealed class LocalFriendState
{
    readonly Dictionary<string, object?[]> knownUsers = new(StringComparer.Ordinal);
    readonly HashSet<string> outgoingRequests = new(StringComparer.Ordinal);
    readonly HashSet<string> incomingRequests = new(StringComparer.Ordinal);
    readonly HashSet<string> friends = new(StringComparer.Ordinal);
    readonly HashSet<string> favoriteFriends = new(StringComparer.Ordinal);
    readonly HashSet<string> blockedUsers = new(StringComparer.Ordinal);
    readonly object sync = new();

    public LocalFriendState()
    {
        knownUsers["5600293911"] = BuildFriendResult(
            "5600293911",
            "Alkyne",
            "よろしくお願いします。",
            50,
            DateTime.UtcNow,
            110010,
            false,
            190001,
            false,
            BuildOfficialCharacterRanks());
    }

    public object?[] GetFriends()
    {
        lock (sync)
        {
            return Page(friends.Select(id => CloneWithFavorite(knownUsers[id], favoriteFriends.Contains(id))).ToArray(), friends.Count);
        }
    }

    public object?[] GetOutgoingRequests()
    {
        lock (sync)
        {
            return Page(outgoingRequests.Select(id => CloneWithRanks(knownUsers[id], Array.Empty<object?>())).ToArray(), outgoingRequests.Count);
        }
    }

    public object?[] GetIncomingRequests()
    {
        lock (sync)
        {
            return Page(incomingRequests.Select(id => CloneWithRanks(knownUsers[id], Array.Empty<object?>())).ToArray(), incomingRequests.Count);
        }
    }

    public object?[] GetBlockedUsers()
    {
        lock (sync)
        {
            return new object?[] { blockedUsers.Select(id => CloneWithRanks(knownUsers[id], Array.Empty<object?>())).ToArray() };
        }
    }

    public object?[] Search(string? targetUserId)
    {
        if (string.IsNullOrWhiteSpace(targetUserId)) return new object?[] { null, 3 };

        lock (sync)
        {
            return knownUsers.TryGetValue(targetUserId.Trim(), out var user)
                ? new object?[] { CloneWithFavorite(user, favoriteFriends.Contains(targetUserId.Trim())), 4 }
                : new object?[] { null, 3 };
        }
    }

    public object?[] SendRequest(string? targetUserId)
    {
        if (string.IsNullOrWhiteSpace(targetUserId)) return new object?[] { 3 };

        lock (sync)
        {
            var id = targetUserId.Trim();
            if (!knownUsers.ContainsKey(id)) return new object?[] { 3 };
            if (friends.Contains(id)) return new object?[] { 2 };
            outgoingRequests.Add(id);
            incomingRequests.Remove(id);
            return new object?[] { 1 };
        }
    }

    public object?[] AcceptRequest(string? fromUserId)
    {
        if (string.IsNullOrWhiteSpace(fromUserId)) return new object?[] { 3 };

        lock (sync)
        {
            var id = fromUserId.Trim();
            if (!knownUsers.ContainsKey(id)) return new object?[] { 3 };
            incomingRequests.Remove(id);
            outgoingRequests.Remove(id);
            friends.Add(id);
            return new object?[] { 1 };
        }
    }

    public object?[] CancelRequest(string? targetUserId)
    {
        if (!string.IsNullOrWhiteSpace(targetUserId))
        {
            lock (sync)
            {
                outgoingRequests.Remove(targetUserId.Trim());
            }
        }

        return new object?[] { true };
    }

    public object?[] DenyRequest(string? fromUserId)
    {
        if (!string.IsNullOrWhiteSpace(fromUserId))
        {
            lock (sync)
            {
                incomingRequests.Remove(fromUserId.Trim());
            }
        }

        return new object?[] { true };
    }

    public object?[] RemoveFriend(string? targetUserId)
    {
        if (!string.IsNullOrWhiteSpace(targetUserId))
        {
            lock (sync)
            {
                var id = targetUserId.Trim();
                friends.Remove(id);
                favoriteFriends.Remove(id);
            }
        }

        return new object?[] { true };
    }

    public object?[] BlockUser(string? targetUserId)
    {
        if (!string.IsNullOrWhiteSpace(targetUserId))
        {
            lock (sync)
            {
                var id = targetUserId.Trim();
                if (knownUsers.ContainsKey(id)) blockedUsers.Add(id);
                friends.Remove(id);
                outgoingRequests.Remove(id);
                incomingRequests.Remove(id);
            }
        }

        return new object?[] { true };
    }

    public object?[] RemoveBlockUser(string? targetUserId)
    {
        if (!string.IsNullOrWhiteSpace(targetUserId))
        {
            lock (sync)
            {
                blockedUsers.Remove(targetUserId.Trim());
            }
        }

        return new object?[] { true };
    }

    public object?[] SetFavorite(object? payload)
    {
        var values = payload as object?[];
        var targetUserId = values?.FirstOrDefault(v => v is string)?.ToString();
        var favorite = values?.OfType<bool>().FirstOrDefault() ?? true;

        if (!string.IsNullOrWhiteSpace(targetUserId))
        {
            lock (sync)
            {
                var id = targetUserId.Trim();
                if (favorite) favoriteFriends.Add(id);
                else favoriteFriends.Remove(id);
            }
        }

        return new object?[] { true };
    }

    public void AddIncomingSampleRequest()
    {
        lock (sync)
        {
            if (!friends.Contains("5600293911")) incomingRequests.Add("5600293911");
        }
    }

    static object?[] Page(object?[] users, int count)
    {
        return new object?[] { users, count };
    }

    static object?[] CloneWithFavorite(object?[] user, bool isFavorite)
    {
        var clone = (object?[])user.Clone();
        clone[18] = isFavorite;
        return clone;
    }

    static object?[] CloneWithRanks(object?[] user, object?[] ranks)
    {
        var clone = (object?[])user.Clone();
        clone[12] = ranks;
        return clone;
    }

    static object?[] BuildFriendResult(
        string publicId,
        string displayName,
        string introduction,
        int playerRank,
        DateTime lastLoggedInAt,
        long mainCharacterMasterId,
        bool displayAwakeningStatus,
        long iconFrameMasterId,
        bool isFavorite,
        object?[] characterRanks)
    {
        return new object?[]
        {
            publicId,
            null,
            playerRank,
            0L,
            0L,
            0L,
            introduction,
            lastLoggedInAt,
            displayName,
            null,
            false,
            1,
            characterRanks,
            true,
            null,
            mainCharacterMasterId,
            displayAwakeningStatus,
            iconFrameMasterId,
            isFavorite
        };
    }

    static object?[] BuildOfficialCharacterRanks()
    {
        return new object?[]
        {
            new object?[] { 101, 2 },
            new object?[] { 102, 3 },
            new object?[] { 103, 2 },
            new object?[] { 104, 5 },
            new object?[] { 105, 2 },
            new object?[] { 106, 3 },
            new object?[] { 201, 2 },
            new object?[] { 202, 2 },
            new object?[] { 203, 2 },
            new object?[] { 204, 3 },
            new object?[] { 205, 2 },
            new object?[] { 301, 3 },
            new object?[] { 302, 1 },
            new object?[] { 303, 1 },
            new object?[] { 304, 3 },
            new object?[] { 305, 2 },
            new object?[] { 401, 3 },
            new object?[] { 402, 2 },
            new object?[] { 403, 2 },
            new object?[] { 404, 3 },
            new object?[] { 405, 2 }
        };
    }
}
