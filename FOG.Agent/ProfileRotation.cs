using FOG.Protocol;

namespace FOG.Agent;

public sealed class ProfileRotation
{
    private readonly object _sync = new();
    private string? _nextProfileId;

    public IReadOnlyList<EngineProfile> Order(IReadOnlyList<EngineProfile> profiles)
    {
        if (profiles.Count < 2)
        {
            return profiles;
        }

        lock (_sync)
        {
            var start = _nextProfileId is null
                ? 0
                : profiles.ToList().FindIndex(profile => profile.Id == _nextProfileId);
            if (start < 0)
            {
                start = 0;
            }

            return Enumerable.Range(0, profiles.Count)
                .Select(offset => profiles[(start + offset) % profiles.Count])
                .ToArray();
        }
    }

    public void MovePast(string profileId, IReadOnlyList<EngineProfile> profiles)
    {
        if (profiles.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            var index = profiles.ToList().FindIndex(profile => profile.Id == profileId);
            _nextProfileId = profiles[(Math.Max(index, 0) + 1) % profiles.Count].Id;
        }
    }

    public void Prefer(string profileId)
    {
        lock (_sync)
        {
            _nextProfileId = profileId;
        }
    }
}
