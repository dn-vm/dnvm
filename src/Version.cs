using Serde;
using System;
using System.CommandLine.Parsing;
using System.Linq;
using static Dnvm.Version;
namespace Dnvm;

public partial record struct Version(VersionKind Kind, int? Major = null, int? Minor = null, int? Patch = null, string? Suffix = null)
{
	public override string ToString()
		=> string.Concat($"{Major}.{Minor}.{Patch}", Suffix is not null ? $"-{Suffix}" : "");

	public string DownloadPath
	{
		get
		{
			if (Kind != VersionKind.Exact)
				throw new InvalidOperationException($"Cannot construct download URI from a version that is not Exact: {this}");
			return $"/Sdk/{this}/dotnet-sdk-{this}-{Utilities.CurrentRID}.{Utilities.ZipSuffix}";
		}
	}

	public static Option<Version> Option { get; }

	public static Version From(string token)
	{
		switch (token.ToLower())
		{
			case "latest":
				throw new NotImplementedException("TODO");
				return new Version(VersionKind.Latest);
			default:
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

				return new Version(VersionKind.Exact, major, minor, patch, suffix);
		}
	}

	public enum VersionKind
	{
		Latest,
		Exact
	}
	public static Version Parse(ArgumentResult arg)
	{
		var tok = arg.Tokens.Single().Value;
		return Version.From(tok);
	}
}
