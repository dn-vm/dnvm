using System;
using System.Threading.Tasks;
using static Dnvm.Version;
namespace Dnvm.Tests
{
	public class VersionParsing
	{
		[Fact]
		public void CanParseValidVersions()
		{
			Assert.Equal(From("1.2.3"), new Version(1, 2, 3));
			Assert.Equal(From("12.234.234"), new Version(12, 234, 234));
			Assert.Equal(From("7.0.0-preview7"), new Version(7, 0, 0, "preview7"));
		}

		[Fact]
		public void FailsToParseInvalidVersions()
		{
			Func<string, Action> From = (string s) => { return () => { _ = Version.From(s); }; };
			Assert.Throws<DnvmException>(From("hello"));
			Assert.Throws<DnvmException>(From("5.0"));
			Assert.Throws<DnvmException>(From("5.0"));
			Assert.Throws<DnvmException>(From("5.0.s"));
			Assert.Throws<DnvmException>(From("one.0.3"));
			Assert.Throws<DnvmException>(From("4.0.3-"));
			Assert.Throws<DnvmException>(From(""));
		}
		// ToString tests
		[Fact]
		public async Task ASDF()
		{
			await new DefaultClient().DownloadArchiveAndExtractAsync(new Uri("https://dotnetbuilds.azureedge.net/public/Sdk/7.0.100-rtm.22473.5/dotnet-sdk-7.0.100-rtm.22473.5-linux-x64.tar.gz"), "C:\\Users\\jschuster\\Downloads\\");
		}
	}
}