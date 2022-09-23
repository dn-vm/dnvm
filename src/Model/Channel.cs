using StaticCs;
using System;
using System.CommandLine.Parsing;
using System.Linq;
using static Dnvm.Channel;

namespace Dnvm;

public record Channel(ChannelKind Kind, int? Major = null, int? Minor = null, int? Patch = null)
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
			ChannelKind.LTS => "LTS",
			ChannelKind.Current => "Current",
			ChannelKind.Preview => "7.0",
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
				try
				{
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
								throw new FormatException($"Parse error: Cannot parse {token} as channel - 3 segment channel must be in format A.B.Cxx");
							patch = int.Parse(numbers[2][..^2]);
							return new Channel(ChannelKind.Numbered, major, minor, patch);
						default:
							throw new FormatException($"Parse error: Cannot parse {token} as channel");
					}
				}
				catch (FormatException)
				{
					throw new DnvmException($"Parse error: Cannot parse {token} as channel");
				}
		};
	}
	public static Channel Parse(ArgumentResult arg)
	{
		var tok = arg.Tokens.Single().Value;
		return From(tok);
	}
}