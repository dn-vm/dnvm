using StaticCs;
using System;
using System.CommandLine.Parsing;
using System.Linq;
using static Dnvm.Channel;

namespace Dnvm;



public record struct Channel(ChannelKind Kind, int? Major = null, int? Minor = null, int? Patch = null)
{
	[Closed]
	public enum ChannelKind
	{
		LTS,
		Current,
		Preview,
		Numbered
	}

	public override string ToString()
		=> Kind switch
		{
			ChannelKind.LTS => "lts",
			ChannelKind.Current => "current",
			ChannelKind.Preview => "preview",
			ChannelKind.Numbered => Patch switch
			{
				int => $"{Major!.Value}.{Minor!.Value}.{Patch!.Value}xx",
				null => $"{Major!.Value}.{Minor!.Value}",
			}
		};
	public static Channel From(string token)
	{
		switch (token.ToLower())
		{
			case "lts": return new Channel(ChannelKind.LTS);
			case "current": return new Channel(ChannelKind.Current);
			case "preview": return new Channel(ChannelKind.Preview);
			default:
				var numbers = token.Split('.');
				int major, minor, patch;
				switch (numbers.Length)
				{
					case 2:
						major = int.Parse(numbers[0]);
						minor = int.Parse(numbers[1]);
						return new Channel(ChannelKind.Numbered, major, minor);
					case 3:
						major = int.Parse(numbers[0]);
						minor = int.Parse(numbers[1]);
						if (numbers[2].Length != 3 || numbers[2][^2..] != "xx")
							throw new FormatException("");
						patch = int.Parse(numbers[2][..^2]);
						return new Channel(ChannelKind.Numbered, major, minor, patch);
					default:
						throw new FormatException($"Cannot parse {token} - it is not a valid channel string");
				}
		};
	}
	public static Channel Parse(ArgumentResult arg)
	{
		var tok = arg.Tokens.Single().Value;
		return From(tok);
	}
}
