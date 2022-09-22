using System.Collections.Generic;
using Xunit.Abstractions;

namespace Dnvm.Tests
{
	internal class TestLogger : ILogger
	{
		ITestOutputHelper? _output = null;
		public TestLogger(ITestOutputHelper output)
		{
			_output = output;
		}
		public List<string> Messages = new();
		public LogLevel LogLevel { get; set; }

		public void Error(string message)
		{
			message = "Error: " + message;
			Messages.Add(message);
			_output?.WriteLine(message);
		}

		public void Info(string message)
		{
			message = "Info: " + message;
			Messages.Add(message);
			_output?.WriteLine(message);
		}

		public void Log(string message)
		{
			message = "Log: " + message;
			Messages.Add(message);
			_output?.WriteLine(message);
		}
	}
}
