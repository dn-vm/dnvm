using System;
using System.CommandLine.Parsing;
using System.Linq;
namespace Dnvm;

public record class Version(int? Major = null, int? Minor = null, int? Patch = null, string? Suffix = null)
{
	public override string ToString()
		=> string.Concat($"{Major}.{Minor}.{Patch}", Suffix is not null ? $"-{Suffix}" : "");

	public string ToStringNoSuffix()
		=> $"{Major}.{Minor}.{Patch}";

	public string[] DownloadPaths
	{
		get
		{
			return new[] {
				$"/Sdk/{this}/dotnet-sdk-{this.ToStringNoSuffix()}-{Utilities.CurrentRID}.{Utilities.ZipSuffix}",
				$"/Sdk/{this}/dotnet-sdk-{this}-{Utilities.CurrentRID}.{Utilities.ZipSuffix}"
			};
		}
	}

	public static Version From(string token)
	{
		int dot1 = token.IndexOf('.');
		if (dot1 + 1 >= token.Length || dot1 == -1)
			throw new FormatException($"Cannot parse {token} as Version.");

		int dot2 = token[(dot1 + 1)..].IndexOf('.');
		if (dot2 + 1 >= token.Length || dot2 == -1)
			throw new FormatException($"Cannot parse {token} as Version.");
		dot2 += dot1 + 1;

		int dash = token[(dot2 + 1)..].IndexOf('-');
		if (dash + 1 == token[(dot2 + 1)..].Length)
			throw new FormatException($"Cannot parse {token} as Version.");

		int major = int.Parse(token[..dot1]);
		int minor = int.Parse(token[(dot1 + 1)..dot2]);
		int patch;
		string? suffix;
		if (dash == -1)
		{
			patch = int.Parse(token[(dot2 + 1)..]);
			suffix = null;
		}
		else
		{
			patch = int.Parse(token[(dot2 + 1)..(dot2 + 1 + dash)]);
			suffix = token[(dot2 + dash + 2)..];
		}

		return new Version(major, minor, patch, suffix);
	}

	public static Version Parse(ArgumentResult arg)
	{
		var tok = arg.Tokens.Single().Value;
		return Version.From(tok);
	}
}
