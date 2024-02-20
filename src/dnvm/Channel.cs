
using System.Globalization;
using Semver;
using Serde;
using StaticCs;

namespace Dnvm;

public static class Channels
{
    public static string GetDesc(this Channel c) => c switch
    {
        Channel.Versioned v => $"The latest version in the {v} support channel",
        Channel.Lts => "The latest version in Long-Term support",
        Channel.Sts => "The latest version in Short-Term support",
        Channel.Latest => "The latest supported version from either the LTS or STS support channels.",
        Channel.Preview => "The latest preview version",
    };
}


[Closed]
public abstract partial record Channel
{
    private Channel() { }

    /// <summary>
    /// A major-minor versioned channel.
    /// </summary>
    public sealed partial record Versioned(int Major, int Minor) : Channel;
    /// <summary>
    /// Newest Long Term Support release.
    /// </summary>
    public sealed partial record Lts : Channel;
    /// <summary>
    /// Newest Short Term Support release.
    /// </summary>
    public sealed partial record Sts : Channel;
    /// <summary>
    /// Latest supported version from either the LTS or STS support channels.
    /// </summary>
    public sealed partial record Latest : Channel;
    /// <summary>
    /// Latest preview version.
    /// </summary>
    public sealed partial record Preview : Channel;
}

partial record Channel : ISerialize<Channel>, ISerialize
{
    public abstract string GetDisplayName();
    public sealed override string ToString() => GetDisplayName();
    public string GetLowerName() => GetDisplayName().ToLowerInvariant();
    void ISerialize<Channel>.Serialize(Channel channel, ISerializer serializer)
        => serializer.SerializeString(GetLowerName());

    partial record Versioned
    {
        public override string GetDisplayName() => $"{Major}.{Minor}";
    }
    partial record Lts : Channel
    {
        public override string GetDisplayName() => "LTS";
    }
    partial record Sts : Channel
    {
        public override string GetDisplayName() => "STS";
    }
    partial record Latest : Channel
    {
        public override string GetDisplayName() => "Latest";
    }
    partial record Preview : Channel
    {
        public override string GetDisplayName() => "Preview";
    }
}

partial record Channel : IDeserialize<Channel>
{
    public static Channel Deserialize(IDeserializer deserializer)
    {
        var str = StringWrap.Deserialize(deserializer);
        switch (str)
        {
            case "lts": return new Lts();
            case "sts": return new Sts();
            case "latest": return new Latest();
            case "preview": return new Preview();
            default:
                var components = str.Split('.');
                if (components.Length != 2)
                {
                    throw new InvalidDeserializeValueException($"Invalid channel version: {str}");
                }
                var major = int.Parse(components[0]);
                var minor = int.Parse(components[1]);
                return new Versioned(major, minor);
        }
    }
}
