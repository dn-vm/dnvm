using System;
using static Dnvm.Version;
using static Dnvm.Version.VersionKind;
namespace Dnvm.Tests
{
	public class VersionParsing
	{
		[Fact]
		public void CanParseValidVersions()
		{
			Assert.Equal(From("latest"), new Version(Latest));
			Assert.Equal(From("1.2.3"), new Version(Exact, 1, 2, 3));
			Assert.Equal(From("12.234.234"), new Version(Exact, 12, 234, 234));
			Assert.Equal(From("7.0.0-preview7"), new Version(Exact, 7, 0, 0, "preview7"));
		}

		[Fact]
		public void FailsToParseInvalidVersions()
		{
			Func<string, Action> From = (string s) => { return () => { _ = Version.From(s); }; };
			Assert.Throws<FormatException>(From("hello"));
			Assert.Throws<FormatException>(From("5.0"));
			Assert.Throws<FormatException>(From("5.0"));
			Assert.Throws<FormatException>(From("5.0.s"));
			Assert.Throws<FormatException>(From("one.0.3"));
			Assert.Throws<FormatException>(From("4.0.3-"));
			Assert.Throws<FormatException>(From(""));
		}
		// ToString tests
	}
}