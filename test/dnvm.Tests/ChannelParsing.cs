using System;
using static Dnvm.Channel;
namespace Dnvm.Tests
{
	public class ChannelParsing
	{
		[Fact]
		public void CanParseValidChannels()
		{
			Assert.Equal(new Channel(ChannelKind.LTS), From("lts"));
			Assert.Equal(new Channel(ChannelKind.LTS), From("Lts"));
			Assert.Equal(new Channel(ChannelKind.Current), From("Current"));
			Assert.Equal(new Channel(ChannelKind.Current), From("current"));
			Assert.Equal(new Channel(ChannelKind.Preview), From("preview"));
			Assert.Equal(new Channel(ChannelKind.Preview), From("Preview"));
			Assert.Equal(new Channel(ChannelKind.Numbered, 5, 0), From("5.0"));
			Assert.Equal(new Channel(ChannelKind.Numbered, 5, 0, 3), From("5.0.3xx"));
		}

		[Fact]
		public void FailsToParseInvalidChannels()
		{
			Func<string, Action> From = (string s) => { return () => { _ = Channel.From(s); }; };
			Assert.Throws<FormatException>(From("hello"));
			Assert.Throws<FormatException>(From("1.2.3"));
			Assert.Throws<FormatException>(From("12.234.234"));
			Assert.Throws<FormatException>(From("12.234.23xx"));
			Assert.Throws<FormatException>(From("7.0.0-preview7"));
			Assert.Throws<FormatException>(From("5.0.3x"));
		}
	}
}