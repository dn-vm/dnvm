
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

    public static string GetLowerName(this Channel c) => c switch
    {
        Channel.Versioned v => v.ToString(),
        Channel.Lts => "lts",
        Channel.Sts => "sts",
        Channel.Latest => "latest",
        Channel.Preview => "preview",
    };
    public static string GetDisplayName(this Channel c) => c switch
    {
        Channel.Versioned v => v.ToString(),
        Channel.Lts => "LTS",
        Channel.Sts => "STS",
        Channel.Latest => "Latest",
        Channel.Preview => "Preview",
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
    protected abstract void Serialize(ISerializer serializer);
    void ISerialize<Channel>.Serialize(Channel channel, ISerializer serializer) => Serialize(serializer);

    partial record Versioned
    {
        public override string ToString() => $"{Major}.{Minor}";

        protected override void Serialize(ISerializer serializer)
            => serializer.SerializeString(this.ToString());
    }
    partial record Lts : Channel
    {
        protected override void Serialize(ISerializer serializer)
            => serializer.SerializeString("lts");
    }
    partial record Sts : Channel
    {
        protected override void Serialize(ISerializer serializer)
            => serializer.SerializeString("sts");
    }
    partial record Latest : Channel
    {
        protected override void Serialize(ISerializer serializer)
            => serializer.SerializeString("latest");
    }
    partial record Preview : Channel
    {
        protected override void Serialize(ISerializer serializer)
            => serializer.SerializeString("preview");
    }
}

partial record Channel : IDeserialize<Channel>
{
    public static Channel Deserialize<D>(ref D deserializer)
        where D : IDeserializer
    {
        var str = StringWrap.Deserialize(ref deserializer);
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
